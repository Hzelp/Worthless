using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Worthless.Lobby;

namespace Worthless
{
    public sealed class BroadcastHudView : MonoBehaviour
    {
        public static bool BlocksMovement { get; private set; }
        public static bool IsSpotlight { get; private set; }

        private readonly List<Canvas> hiddenCanvases = new List<Canvas>();
        private Canvas canvas;
        private string reviewLines = "";
        private string tradeLines = "";
        private int renderedTrades = -1;
        private uint renderedGame;
        private Button reviewButton;
        private Text reviewCaption;
        private Image clickShield;
        private CameraFollow2D follow;
        private MatchRoundDirector director;
        private MatchAuthority authority;
        private BroadcastStage stage;
        private bool reviewing;
        private bool reviewAllowed;
        private int closedReviewFrame = -1;
        private RoundPhase reviewPhase;
        private int renderedCount = -1;
        private int renderedRound = -1;
        private bool devCheck;
        private bool devReviewed;
        private float devRangeAt;

        public static BroadcastHudView Create(Transform parent)
        {
            var root = new GameObject("BroadcastHud");
            root.transform.SetParent(parent, false);
            var view = root.AddComponent<BroadcastHudView>();
            view.Build();
            return view;
        }

        private void Build()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            devCheck = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-worthless-check-broadcast-ui") >= 0;
#endif
            var root = new GameObject("BroadcastCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.transform.SetParent(transform, false);
            canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 40;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600f, 900f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            var shield = new GameObject("Broadcast input shield", typeof(RectTransform), typeof(Image));
            shield.transform.SetParent(root.transform, false);
            Stretch((RectTransform)shield.transform);
            clickShield = shield.GetComponent<Image>();
            shield.SetActive(false);

            reviewButton = ArtTheme.Button(root.transform, "回看大屏", new Vector2(-114, 42), new Vector2(188, 44), false);
            reviewCaption = reviewButton.GetComponentInChildren<Text>();
            reviewCaption.resizeTextForBestFit = true;
            reviewCaption.resizeTextMinSize = 14;
            reviewCaption.resizeTextMaxSize = 20;
            var buttonRect = (RectTransform)reviewButton.transform;
            buttonRect.anchorMin = buttonRect.anchorMax = new Vector2(1f, 0f);
            reviewButton.onClick.AddListener(OpenReview);
            reviewButton.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (director == null) director = FindObjectOfType<MatchRoundDirector>();
            if (authority == null) authority = FindObjectOfType<MatchAuthority>();
            if (stage == null) stage = FindObjectOfType<BroadcastStage>();
            if (director == null || authority == null || !authority.Started || stage == null)
            {
                reviewing = reviewAllowed = false;
                ApplyPresentation(false, false);
                if (stage != null) stage.Present("情报大屏", string.Empty);
                reviewButton.gameObject.SetActive(false);
                return;
            }

            var phase = director.Phase;
            var openingGoal = phase == RoundPhase.RoundStart && director.Round == 1;
            var broadcast = phase == RoundPhase.Broadcast;
            var reveal = phase == RoundPhase.LieReveal;
            var market = phase == RoundPhase.MarketUpdate;
            var marketReady = market && director.MarketVersion == director.Round && !string.IsNullOrEmpty(director.MarketBanner);
            var secret = RoundStateMachine.IsSecretPhase(phase);
            var spotlight = openingGoal || broadcast || reveal || marketReady || secret;
            var spectator = authority.IsLocalSpectator || authority.LocalSeat < 0;
            var sameRound = director.BroadcastRound == director.Round;
            var count = spectator || !sameRound ? 0 : director.BroadcastCount;
            var near = authority.TryGetLocalPlayerPosition(out var local) &&
                (local - (Vector2)stage.transform.position).sqrMagnitude <= stage.ReviewRange * stage.ReviewRange;
            var canReview = !spotlight && !market && near && (count > 0 || director.PublicTradeCount > 0);
            reviewAllowed = canReview;
            var forcedClose = reviewing && (!canReview || phase != reviewPhase || Input.GetKeyDown(KeyCode.Escape) || stage.ReviewCloseRequested);
            if (forcedClose) CloseReview();

            if (!forcedClose && canReview &&
                InputProbe.GetKeyDown(KeyBindings.Get(GameInputAction.ContextInteract)))
            {
                if (reviewing && InputProbe.Focused && !InputGate.IsTyping() && !KeyBindings.Capturing &&
                    !FrontEndOverlay.IsOpen && !TradeHudView.IsOpen && !TransitionOverlay.IsPlaying)
                    CloseReview();
                else if (!reviewing && !InputGate.Blocked && !ChatHudView.HasRoomInteraction) OpenReview();
            }

            if (renderedRound != director.Round || renderedCount != count)
            {
                renderedRound = director.Round;
                renderedCount = count;
                var published = new StringBuilder();
                for (var i = 0; i < count; i++)
                {
                    if (director.TryGetBroadcastLine(i, out var line))
                        published.Append(i + 1).Append(".  ").AppendLine(line);
                }
                reviewLines = Worthless.Lobby.BroadcastRules.FormatForDisplay(published.ToString().TrimEnd());
                if (devCheck && count > 0)
                    Debug.Log("[BroadcastUI] 大屏累计条数=" + count + " t=" + Time.unscaledTime.ToString("F2") + "\n" + reviewLines);
            }

            if (renderedTrades != director.PublicTradeCount || renderedGame != authority.GameId)
            {
                renderedTrades = director.PublicTradeCount; renderedGame = authority.GameId;
                var trades = new StringBuilder();
                for (var i = 0; i < director.PublicTradeCount; i++)
                    if (director.TryGetPublicTrade(i, out var record)) trades.AppendLine(director.DescribePublicTrade(record)).AppendLine();
                tradeLines = trades.ToString().TrimEnd();
            }

            var total = !spectator && sameRound && director.BroadcastTotal >= 0
                ? "本轮情报中有 " + director.BroadcastTotal + " 条谎言" : string.Empty;
            stage.SetReviewContent(reviewLines, total, tradeLines);
            if (reviewing) stage.Present("大屏回看", string.Empty, false, true);
            else if (openingGoal)
            {
                var message = !spectator && director.TryGetLocalGoal(out var group, out var rank)
                    ? OpeningGoalMessage(group, rank) : "正在同步你的目标…";
                stage.Present("你的本局目标", message);
            }
            else if (marketReady) stage.Present("市场变动", director.MarketBanner);
            else if (reveal) stage.Present("谎言揭晓", spectator ? "情报阶段结束" : (total.Length == 0 ? "统计中…" : total));
            else if (broadcast) stage.Present("情报广播", count > 0 ? reviewLines : "情报播放中…");
            else if (secret) stage.Present(phase == RoundPhase.TieBreak ? "并列加赛" : "秘密价值操作", string.Empty, !spectator);
            else stage.Present("情报大屏", reviewLines);

            ApplyPresentation(spotlight || reviewing, reviewing);
            reviewButton.gameObject.SetActive(canReview && !reviewing);
            if (canReview)
            {
                var caption = ChatHudView.HasRoomInteraction ? "回看大屏" : KeyBindings.Get(GameInputAction.ContextInteract) + "  回看大屏";
                if (reviewCaption.text != caption)
                {
                    reviewCaption.font = ArtTheme.FontFor(caption);
                    reviewCaption.text = caption;
                }
            }
            if (devCheck && canReview && !reviewing && !devReviewed)
            {
                devReviewed = true;
                reviewButton.onClick.Invoke();
                devRangeAt = Time.unscaledTime + 3f;
                Debug.Log("[BroadcastUI] 点击回看，公开条数=" + count);
            }
            if (devCheck && reviewing && devRangeAt > 0f && Time.unscaledTime >= devRangeAt)
            {
                stage.ReviewRange = 1f;
                devRangeAt = 0f;
            }
        }

        private static string OpeningGoalMessage(GoalGroup group, int rank)
        {
            var groupName = group == GoalGroup.High ? "高价组" : "低价组";
            var order = group == GoalGroup.High ? "从高到低" : "从低到高";
            return "你属于" + groupName + "\n\n本局结束时，按组内物品价值" + order + "排序。\n排第 " + rank + " 名即可获胜。";
        }

        private void OpenReview()
        {
            if (director == null || !reviewAllowed || stage == null) return;
            reviewing = true;
            reviewPhase = director.Phase;
            stage.BeginReview(director.BroadcastRound != director.Round || director.BroadcastCount == 0);
        }

        private void CloseReview() { reviewing = false; closedReviewFrame = Time.frameCount; }

        private void OnDisable() { reviewing = false; closedReviewFrame = -1; ApplyPresentation(false, false); }

        private void ApplyPresentation(bool spotlight, bool showReview)
        {
            if (spotlight != IsSpotlight)
            {
                IsSpotlight = spotlight;
                var camera = Camera.main;
                if (follow == null && camera != null) follow = camera.GetComponent<CameraFollow2D>();
                if (follow != null) follow.SetSpotlight(spotlight ? stage : null);
                if (spotlight)
                {
                    foreach (var other in FindObjectsOfType<Canvas>())
                    {
                        if (other == canvas || other.gameObject.scene != gameObject.scene ||
                            other.renderMode != RenderMode.ScreenSpaceOverlay || !other.enabled) continue;
                        hiddenCanvases.Add(other);
                        other.enabled = false;
                    }
                    if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
                }
                else
                {
                    foreach (var other in hiddenCanvases)
                        if (other != null) other.enabled = true;
                    hiddenCanvases.Clear();
                }
                if (devCheck) Debug.Log("[BroadcastUI] spotlight=" + spotlight + " phase=" + (director != null ? director.Phase.ToString() : "?") + " t=" + Time.unscaledTime.ToString("F2"));
            }
            // Keep the shared interaction key blocked for this frame after closing,
            // so a later room-control Update cannot also treat that F as a door lock.
            BlocksMovement = spotlight || showReview || closedReviewFrame == Time.frameCount;
            if (clickShield != null)
            {
                clickShield.color = Color.clear;
                clickShield.raycastTarget = !showReview && spotlight && (director == null || !RoundStateMachine.IsSecretPhase(director.Phase));
                clickShield.gameObject.SetActive(BlocksMovement);
            }
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}
