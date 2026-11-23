using UnityEngine;
using UnityEngine.UI;
using Worthless.Lobby;

namespace Worthless
{
    public sealed class SecretChoiceHud : MonoBehaviour
    {
        private readonly Button[] seatButtons = new Button[MatchAuthority.SeatCount - 1];
        private readonly Text[] seatNames = new Text[MatchAuthority.SeatCount - 1];
        private readonly Text[] seatNumbers = new Text[MatchAuthority.SeatCount - 1];
        private readonly int[] targets = new int[MatchAuthority.SeatCount - 1];
        private Text clock;
        private Text status;
        private Text goal;
        private Button add;
        private Button subtract;
        private Button submit;
        private MatchAuthority authority;
        private MatchRoundDirector director;
        private int selected = -1;
        private int direction;
        private int lastRound;
        private RoundPhase lastPhase;
        private uint lastGame;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private float devSubmitAt;
        private int devSkipRound;
        private bool devDrive;
        private bool devSent;
        private bool devAcknowledged;
#endif

        public static SecretChoiceHud Create(RectTransform screen)
        {
            var root = ArtTheme.Rect(screen, "Secret choice controls", Vector2.zero, new Vector2(1600, 900));
            root.localScale = new Vector3(screen.rect.width / 1600f, screen.rect.height / 900f, 1f);
            var view = root.gameObject.AddComponent<SecretChoiceHud>();
            view.Build();
            root.gameObject.SetActive(false);
            return view;
        }

        private void Build()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var args = System.Environment.GetCommandLineArgs();
            devDrive = System.Array.IndexOf(args, "-worthless-check-secret-choice") >= 0;
            var skip = System.Array.IndexOf(args, "-worthless-secret-timeout-round");
            if (skip >= 0 && skip + 1 < args.Length) int.TryParse(args[skip + 1], out devSkipRound);
#endif
            clock = ArtTheme.Label(transform, "Time remaining", "", 48, new Vector2(530, 344), new Vector2(352, 90), ArtTheme.Gold, TextAnchor.MiddleRight);
            ArtTheme.Label(transform, "Target instruction", "选择要影响的玩家", 37, new Vector2(-415, 230), new Vector2(570, 62), ArtTheme.Text, TextAnchor.MiddleLeft);
            goal = ArtTheme.Label(transform, "Private goal", "", 32, new Vector2(310, 230), new Vector2(790, 62), ArtTheme.Gold, TextAnchor.MiddleRight);
            for (var i = 0; i < seatButtons.Length; i++)
            {
                var slot = i;
                var button = MakeButton(transform, "Target " + i, new Vector2(-480 + i % 3 * 480, 146 - i / 3 * 102), new Vector2(448, 86), () => selected = targets[slot]);
                seatButtons[i] = button;
                var name = button.GetComponentInChildren<Text>();
                name.rectTransform.anchoredPosition = new Vector2(45, 0);
                name.rectTransform.sizeDelta = new Vector2(325, 78);
                name.alignment = TextAnchor.MiddleLeft;
                seatNames[i] = name;
                var stub = ArtTheme.Block(button.transform, "Seat stub", ArtTheme.Ink, new Vector2(-174, 0), new Vector2(76, 62));
                seatNumbers[i] = ArtTheme.Label(stub, "Seat number", "", 31, Vector2.zero, new Vector2(74, 58), ArtTheme.Gold, TextAnchor.MiddleCenter);
            }
            ArtTheme.Block(transform, "Action divider", ArtTheme.Surface, new Vector2(0, -130), new Vector2(1408, 3));
            subtract = MakeButton(transform, "价值 -5", new Vector2(-545, -197), new Vector2(318, 80), () => direction = -SecretSelections.Change);
            add = MakeButton(transform, "价值 +5", new Vector2(-201, -197), new Vector2(318, 80), () => direction = SecretSelections.Change);
            submit = MakeButton(transform, "确认操作", new Vector2(493, -197), new Vector2(422, 80), () => director.RequestSecretChoice(selected, direction));
            status = ArtTheme.Label(transform, "Private selection status", "", 34, new Vector2(0, -313), new Vector2(1408, 96), ArtTheme.Text, TextAnchor.UpperLeft);
            status.supportRichText = false;
            status.resizeTextForBestFit = true;
            status.resizeTextMinSize = 23;
            status.resizeTextMaxSize = 34;
            ArtTheme.Block(transform, "Footer rule", ArtTheme.Surface, new Vector2(0, -252), new Vector2(1408, 2));
        }

        private static Button MakeButton(Transform parent, string title, Vector2 position, Vector2 size, UnityEngine.Events.UnityAction action)
        {
            var rect = ArtTheme.Block(parent, title, ArtTheme.Surface, position, size, raycast: true);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = rect.GetComponent<Image>();
            var colors = button.colors;
            colors.disabledColor = new Color(.78f, .78f, .78f, 1f);
            button.colors = colors;
            var text = ArtTheme.Label(rect, "Label", title, 40, Vector2.zero, size - new Vector2(30, 8), ArtTheme.Text, TextAnchor.MiddleCenter);
            text.supportRichText = false;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 23;
            text.resizeTextMaxSize = 40;
            button.onClick.AddListener(action);
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            return button;
        }

        private static void PaintAction(Button button, bool selected, Color accent)
        {
            var color = selected ? accent : ArtTheme.Surface;
            if (button.image.color == color) return;
            button.image.color = color;
            button.GetComponentInChildren<Text>().color = selected ? ArtTheme.Ink : ArtTheme.Text;
        }

        private void Update()
        {
            if (authority == null) authority = FindObjectOfType<MatchAuthority>();
            if (director == null) director = FindObjectOfType<MatchRoundDirector>();
            var currentPhase = director != null ? director.Phase : RoundPhase.Idle;
            var visible = authority != null && authority.Started && authority.IsLocalPlayerSeated && director != null && RoundStateMachine.IsSecretPhase(currentPhase);
            if (!visible) return;

            if (lastGame != authority.GameId || lastRound != director.Round || lastPhase != currentPhase)
            {
                lastGame = authority.GameId;
                lastRound = director.Round;
                lastPhase = currentPhase;
                selected = -1;
                direction = 0;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                devSubmitAt = Time.unscaledTime + 2f;
                devSent = false;
                devAcknowledged = false;
                if (devDrive) Debug.Log("[SecretCheck] opened round=" + lastRound + " phase=" + lastPhase + " remaining=" + director.Remaining.ToString("F1") + " seats=" + authority.TargetSeats + " totalRounds=" + director.TotalRounds);
#endif
            }
            clock.text = Mathf.Max(0, Mathf.CeilToInt(director.Remaining)) + " 秒";
            clock.color = director.Remaining <= 5 ? new Color32(247, 131, 99, 255) : ArtTheme.Gold;
            goal.text = director.TryGetLocalGoal(out var group, out var rank) ? "你的目标 · " + RoundHudView.DescribeGoal(group, rank) : "";
            var accepted = director.TryGetLocalChoice(out var target, out var change);
            var slot = 0;
            for (var seat = 0; seat < authority.TargetSeats && slot < seatButtons.Length; seat++)
            {
                if (seat == authority.LocalSeat) continue;
                targets[slot] = seat;
                var button = seatButtons[slot];
                var label = seatNames[slot];
                seatNumbers[slot].text = "P" + (seat + 1);
                slot++;
                button.gameObject.SetActive(true);
                label.text = authority.PlayerNameForSeat(seat);
                label.font = ArtTheme.FontFor(label.text);
                button.interactable = !accepted;
                var chosen = (accepted ? target : selected) == seat;
                button.image.color = chosen ? ArtTheme.Teal : ArtTheme.Surface;
                label.color = chosen ? ArtTheme.Ink : ArtTheme.Text;
            }
            while (slot < seatButtons.Length) seatButtons[slot++].gameObject.SetActive(false);
            add.interactable = subtract.interactable = !accepted;
            var adjustment = accepted ? change : direction;
            PaintAction(add, adjustment > 0, ArtTheme.Teal);
            PaintAction(subtract, adjustment < 0, new Color32(233, 157, 126, 255));
            submit.interactable = !accepted && selected >= 0 && direction != 0 && director.Remaining > 0f;
            PaintAction(submit, submit.interactable, ArtTheme.Gold);
            status.color = accepted ? ArtTheme.Teal : ArtTheme.Text;
            if (accepted)
                status.text = "已锁定  ·  P" + (target + 1) + " " + authority.PlayerNameForSeat(target) + "  " + (change > 0 ? "+5" : "-5");
            else if (selected < 0)
                status.text = "请选择一名玩家。";
            else if (direction == 0)
                status.text = "已选 P" + (selected + 1) + " " + authority.PlayerNameForSeat(selected) + "，请选择加减方向。";
            else
                status.text = "P" + (selected + 1) + " " + authority.PlayerNameForSeat(selected) + "  " + (direction > 0 ? "+5" : "-5") + "，点击确认锁定。";
            status.font = ArtTheme.FontFor(status.text);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (devDrive && accepted && !devAcknowledged)
            {
                devAcknowledged = true;
                Debug.Log("[SecretCheck] acknowledged round=" + lastRound + " phase=" + lastPhase);
            }
            if (devDrive && !devSent && !accepted && slot > 0 && Time.unscaledTime >= devSubmitAt && (lastRound != devSkipRound || lastPhase == RoundPhase.TieBreak))
            {
                devSent = true;
                seatButtons[0].onClick.Invoke();
                add.onClick.Invoke();
                submit.onClick.Invoke();
            }
#endif
        }
    }
}
