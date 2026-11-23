using UnityEngine;
using UnityEngine.UI;
using Worthless.Lobby;

namespace Worthless
{
    /// <summary>对局 HUD：席位/在线、提示、结束或退出对局、断线重连与返回大厅。</summary>
    public sealed class MatchHudView : MonoBehaviour
    {
        private Text seatText;
        private Text noticeText;
        private RectTransform seatPlate;
        private RectTransform noticePlate;
        private Button actionButton;
        private Text actionLabel;
        private Button reconnectButton;
        private Button backButton;
        private Canvas canvas;
        private MatchAuthority authority;
        private MatchRoundDirector director;
        private int lastVotePhase = -1;
        private float noticeUntil;

        public Button ActionButton { get { return actionButton; } }
        public Button ReconnectButton { get { return reconnectButton; } }
        public Button BackButton { get { return backButton; } }
        public System.Action ActionRequested;
        public System.Action ReconnectRequested;
        public System.Action BackToLobbyRequested;

        public static MatchHudView Create(Transform parent)
        {
            var go = new GameObject("MatchHud");
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<MatchHudView>();
            view.Build();
            return view;
        }

        public void SetVisible(bool visible)
        {
            if (canvas != null) canvas.gameObject.SetActive(visible);
        }

        public void SetSeatLine(string text)
        {
            if (seatText == null || seatText.text == text) return;
            seatText.font = ArtTheme.FontFor(text);
            seatText.text = text;
        }

        public void SetNotice(string text)
        {
            if (noticeText == null || noticeText.text == text) return;
            noticeText.font = ArtTheme.FontFor(text ?? string.Empty);
            noticeText.text = text;
            noticeUntil = Time.unscaledTime + 4f;
        }

        public void SetActions(string label, bool actionVisible, bool reconnectVisible)
        {
            if (actionButton != null)
            {
                actionButton.gameObject.SetActive(actionVisible);
                if (actionLabel != null && actionLabel.text != label)
                {
                    actionLabel.font = ArtTheme.FontFor(label);
                    actionLabel.text = label;
                }
            }

            if (reconnectButton != null) reconnectButton.gameObject.SetActive(reconnectVisible);
            if (backButton != null) backButton.gameObject.SetActive(reconnectVisible);
        }

        private void Update()
        {
            if (authority == null) authority = FindObjectOfType<MatchAuthority>();
            if (director == null) director = FindObjectOfType<MatchRoundDirector>();
            if (director != null && lastVotePhase != (int)director.Phase) { lastVotePhase = (int)director.Phase; director.ResetLocalSkipOnPhase(); }
            if (authority != null && !authority.IsLocalSpectator && director != null && RoundStateMachine.IsTradePhase(director.Phase)
                && !InputGate.Blocked && (InputProbe.GetKeyDown(KeyCode.LeftControl) || InputProbe.GetKeyDown(KeyCode.RightControl)))
                director.ToggleSkipVote();
            noticePlate.gameObject.SetActive(noticeText.text.Length > 0 && Time.unscaledTime < noticeUntil);
            var spectator = authority != null && authority.IsLocalSpectator;
            seatPlate.gameObject.SetActive(spectator);
            noticePlate.anchoredPosition = new Vector2(-165, spectator ? -102 : -66);
            if (!spectator) return;
            if (InputGate.Blocked) return;
            if (InputProbe.GetKeyDown(KeyBindings.Get(GameInputAction.MoveLeft)) || InputProbe.GetKeyDown(KeyCode.LeftArrow)) authority.CycleSpectatorTarget(-1);
            if (InputProbe.GetKeyDown(KeyBindings.Get(GameInputAction.MoveRight)) || InputProbe.GetKeyDown(KeyCode.RightArrow)) authority.CycleSpectatorTarget(1);
            SetSeatLine("观战 · " + authority.SpectatorTargetName + "（A/D 或 ←/→ 切换）");
        }

        private void Build()
        {
            if (FindObjectOfType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem), typeof(UnityEngine.EventSystems.StandaloneInputModule));
            }

            var canvasObject = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600f, 900f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            var panel = ArtTheme.Rect(canvasObject.transform, "Session actions", new Vector2(-20, -20), Vector2.zero, Vector2.one);
            seatPlate = ArtTheme.Block(panel, "Spectator", ArtTheme.Panel, new Vector2(-165, -63), new Vector2(330, 34));
            seatText = ArtTheme.Label(seatPlate, "Seat", "", 16, Vector2.zero, new Vector2(306, 28), ArtTheme.Teal, TextAnchor.MiddleRight);
            seatText.resizeTextForBestFit = true;
            seatText.resizeTextMinSize = 12;
            seatText.resizeTextMaxSize = 16;
            seatText.supportRichText = false;
            noticePlate = ArtTheme.Block(panel, "Session notice", ArtTheme.Panel, new Vector2(-165, -102), new Vector2(330, 40));
            ArtTheme.Block(noticePlate, "Notice edge", ArtTheme.Gold, new Vector2(163, 0), new Vector2(4, 40));
            noticeText = ArtTheme.Label(noticePlate, "Notice", "", 16, Vector2.zero, new Vector2(306, 36), ArtTheme.Text, TextAnchor.MiddleRight);
            noticeText.resizeTextForBestFit = true;
            noticeText.resizeTextMinSize = 12;
            noticeText.resizeTextMaxSize = 16;
            noticeText.supportRichText = false;

            actionButton = ArtTheme.Button(panel, "结束对局", new Vector2(-72, -20), new Vector2(144, 40), false);
            reconnectButton = ArtTheme.Button(panel, "重连原席位", new Vector2(-241, -180), new Vector2(152, 40), true);
            backButton = ArtTheme.Button(panel, "返回大厅", new Vector2(-78, -180), new Vector2(152, 40), false);
            actionLabel = actionButton.GetComponentInChildren<Text>();

            actionButton.onClick.AddListener(OnAction);
            reconnectButton.onClick.AddListener(OnReconnect);
            backButton.onClick.AddListener(OnBack);
            SetActions("结束对局", true, false);
        }

        private void OnAction()
        {
            if (ActionRequested != null) ActionRequested();
        }

        private void OnReconnect()
        {
            if (ReconnectRequested != null) ReconnectRequested();
        }

        private void OnBack()
        {
            if (BackToLobbyRequested != null) BackToLobbyRequested();
        }
    }
}
