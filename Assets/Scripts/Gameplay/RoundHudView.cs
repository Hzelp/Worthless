using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Worthless.Lobby;

namespace Worthless
{
    public sealed class RoundHudView : MonoBehaviour
    {
        private static readonly Color Paper = new Color32(223, 213, 180, 255);
        private static readonly Color PrintedInk = new Color32(39, 49, 40, 255);
        private static readonly Color Urgent = new Color32(247, 131, 99, 255);
        private MatchRoundDirector director;
        private MatchAuthority authority;
        private Text phaseText;
        private Text roundText;
        private Text clockText;
        private Text skipText;
        private RectTransform phaseCard;
        private RectTransform phaseLeaf;
        private Image phasePaper;
        private RectTransform valueStamp;
        private string previousPhaseLabel = string.Empty;
        private RectTransform timerFill;
        private Image timerInk;
        private RectTransform valueCard;
        private Text itemTitle;
        private Text valueText;
        private HistoryTextScroll valueHistory;
        private Text resultText;
        private RectTransform results;
        private Text goalText;
        private bool hasShownValue;
        private long shownValue;
        private long previousValue;
        private float valueChangedAt;
        private int shownInstance;
        private RoundPhase timedPhase;
        private float phaseDuration;
        private float phaseChangedAt;
        private int shownRound = -1;

        public static RoundHudView Create(Transform parent)
        {
            var root = new GameObject("RoundHud");
            root.transform.SetParent(parent, false);
            var view = root.AddComponent<RoundHudView>();
            view.Build();
            return view;
        }

        private void Build()
        {
            if (FindObjectOfType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

            var root = new GameObject("RoundCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.transform.SetParent(transform, false);
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 11;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600f, 900f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            phaseCard = ArtTheme.Block(root.transform, "Phase", ArtTheme.Panel, new Vector2(0, -52), new Vector2(478, 64), anchor: new Vector2(0.5f, 1f));
            var roundTab = ArtTheme.Block(phaseCard, "Round tab", ArtTheme.Gold, new Vector2(-190, 0), new Vector2(98, 64));
            Label(roundTab, "Round heading", "轮次", 14, new Vector2(0, 15), new Vector2(88, 19), ArtTheme.Ink);
            roundText = Label(roundTab, "Round count", "", 24, new Vector2(0, -9), new Vector2(90, 32), ArtTheme.Ink);
            phaseLeaf = ArtTheme.Block(phaseCard, "Phase leaf", ArtTheme.Panel, new Vector2(-2, 0), new Vector2(238, 32));
            phasePaper = phaseLeaf.GetComponent<Image>();
            phaseText = Label(phaseLeaf, "Phase text", "", 24, Vector2.zero, new Vector2(238, 32), ArtTheme.Text, TextAnchor.MiddleLeft);
            skipText = Label(phaseCard, "Skip vote", "", 14, new Vector2(-2, -14), new Vector2(238, 20), ArtTheme.Teal, TextAnchor.MiddleLeft);
            ArtTheme.Block(phaseCard, "Clock divider", ArtTheme.Surface, new Vector2(125, 0), new Vector2(2, 36));
            clockText = Label(phaseCard, "Clock", "", 27, new Vector2(180, 0), new Vector2(99, 43), ArtTheme.Gold);
            var rail = ArtTheme.Block(phaseCard, "Timer rail", ArtTheme.Surface, new Vector2(49, -30), new Vector2(380, 4));
            timerFill = ArtTheme.Block(rail, "Time remaining", ArtTheme.Teal, Vector2.zero, Vector2.zero);
            ArtTheme.Stretch(timerFill);
            timerInk = timerFill.GetComponent<Image>();

            valueCard = ArtTheme.Block(root.transform, "Private value", ArtTheme.Panel, new Vector2(20, -20), new Vector2(318, 236), anchor: new Vector2(0, 1));
            valueCard.pivot = new Vector2(0, 1);
            var appraisal = ArtTheme.Block(valueCard, "Valuation slip", Paper, new Vector2(0, 68), new Vector2(318, 100));
            var stamp = ArtTheme.Block(appraisal, "Private valuation stamp", PrintedInk, new Vector2(-109, 25), new Vector2(72, 22));
            valueStamp = stamp;
            Label(stamp, "Privacy", "私有估值", 14, Vector2.zero, new Vector2(68, 20), Paper);
            itemTitle = Label(appraisal, "Held item", "当前物品", 19, new Vector2(39, 25), new Vector2(206, 28), PrintedInk, TextAnchor.MiddleRight);
            ArtTheme.Block(appraisal, "Print rule", new Color32(153, 150, 121, 255), new Vector2(0, 7), new Vector2(286, 1));
            valueText = Label(appraisal, "Current value", "—", 39, new Vector2(0, -20), new Vector2(286, 48), PrintedInk, TextAnchor.MiddleRight);
            goalText = Label(valueCard, "Secret goal", "目标同步中", 17, new Vector2(0, -1), new Vector2(286, 26), ArtTheme.Gold, TextAnchor.MiddleLeft);
            ArtTheme.Block(valueCard, "Divider", ArtTheme.Surface, new Vector2(0, -19), new Vector2(286, 2));
            Label(valueCard, "Value history title", "价值日志", 15, new Vector2(0, -34), new Vector2(286, 22), ArtTheme.Teal, TextAnchor.MiddleLeft);
            valueHistory = HistoryTextScroll.Create(valueCard, "Value history", new Vector2(0, -78), new Vector2(286, 62), 16);
            results = ArtTheme.Block(root.transform, "Match results", Paper, new Vector2(0, 52), new Vector2(900, 424));
            var masthead = ArtTheme.Block(results, "Results masthead", ArtTheme.Panel, new Vector2(0, 164), new Vector2(900, 96));
            Label(masthead, "Results title", "最终结果", 40, Vector2.zero, new Vector2(830, 70), ArtTheme.Text, TextAnchor.MiddleLeft);
            ArtTheme.Block(masthead, "Brass binding", ArtTheme.Gold, new Vector2(0, -46), new Vector2(900, 4));
            resultText = Label(results, "Winners", "", 30, new Vector2(0, -47), new Vector2(830, 284), PrintedInk, TextAnchor.UpperLeft);
            resultText.resizeTextMinSize = 18;
            resultText.supportRichText = false;
            results.gameObject.SetActive(false);
        }

        private static Text Label(Transform parent, string name, string content, int size, Vector2 position, Vector2 area, Color color,
            TextAnchor alignment = TextAnchor.MiddleCenter)
        {
            var label = ArtTheme.Label(parent, name, content, size, position, area, color, alignment);
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = Mathf.Max(12, size - 6);
            label.resizeTextMaxSize = size;
            return label;
        }

        private static void SetText(Text label, string content)
        {
            if (label.text == content) return;
            label.font = ArtTheme.FontFor(content);
            label.text = content;
        }

        private void Update()
        {
            if (director == null) director = FindObjectOfType<MatchRoundDirector>();
            if (authority == null) authority = FindObjectOfType<MatchAuthority>();
            if (director == null || authority == null) return;

            var phase = director.Phase;
            var remaining = director.Remaining;
            if (phase != timedPhase || director.Round != shownRound)
            {
                previousPhaseLabel = phaseText.text;
                timedPhase = phase;
                shownRound = director.Round;
                phaseDuration = remaining;
                phaseChangedAt = Time.unscaledTime;
            }
            phaseDuration = Mathf.Max(remaining, phaseDuration);

            var spectator = authority.IsLocalSpectator;
            var canVote = RoundStateMachine.IsTradePhase(phase) && !spectator;
            float turn = Mathf.Clamp01((Time.unscaledTime - phaseChangedAt) / .36f);
            bool outgoing = turn < .38f && previousPhaseLabel.Length != 0;
            float face = outgoing ? 1f - turn / .38f : Mathf.InverseLerp(.38f, 1f, turn);
            if (previousPhaseLabel.Length == 0) face = turn;
            float height = Mathf.Sin(face * Mathf.PI * .5f);
            phaseLeaf.localScale = new Vector3(1f, Mathf.Max(.055f, height), 1f);
            phaseLeaf.anchoredPosition = new Vector2(-2, (canVote ? 10f : 0f) + (outgoing ? -1f : 1f) * (1f - height) * 7f);
            phasePaper.color = Color.Lerp(ArtTheme.Gold, ArtTheme.Panel, height);
            SetText(roundText, Mathf.Max(1, director.Round) + " / " + director.TotalRounds);
            SetText(phaseText, outgoing ? previousPhaseLabel : PhaseName(phase));
            SetText(skipText, canVote ? director.LocalSkipVoted ? "Ctrl  撤票" : "Ctrl  跳过" : string.Empty);
            SetText(clockText, phase == RoundPhase.GameOver ? "—" : Mathf.Max(0, Mathf.CeilToInt(remaining)) + " 秒");
            var urgent = phase != RoundPhase.GameOver && remaining > 0 && remaining <= 5;
            clockText.color = urgent ? Urgent : ArtTheme.Gold;
            timerInk.color = urgent ? Urgent : ArtTheme.Teal;
            timerFill.anchorMax = new Vector2(phaseDuration > 0f ? Mathf.Clamp01(remaining / phaseDuration) : 0f, 1f);
            SetText(resultText, director.MatchOver ? director.ResultBanner : string.Empty);
            results.gameObject.SetActive(director.MatchOver && !string.IsNullOrEmpty(director.ResultBanner));

            var hasItem = director.TryGetPublicItem(authority.LocalSeat, out var item);
            valueCard.gameObject.SetActive(!spectator && hasItem);
            if (spectator || !hasItem) return;

            SetText(goalText, director.TryGetLocalGoal(out var group, out var rank) ? "你的目标 · " + DescribeGoal(group, rank) : "目标同步中");
            SetText(itemTitle, director.GetItemName(item.DefIndex));
            var history = director.LocalHeldHistory;
            var log = new StringBuilder();
            for (var i = history.Count - 1; i >= 0; i--)
            {
                var entry = history[i];
                if (log.Length > 0) log.Append('\n');
                log.Append("第").Append(entry.Round).Append("轮 ").Append(director.GetItemName(entry.DefIndex)).Append("  ").Append(director.HistoryPriceText(entry));
            }
            valueHistory.SetContent(log.Length > 0 ? log.ToString() : "等待本人记录同步…");
            var hasValue = director.HasPrivate && !director.IsPrivateStale;
            if (!hasValue)
            {
                SetText(valueText, "同步中");
                return;
            }

            var value = director.PrivateValue;
            if (!hasShownValue || shownValue != value || shownInstance != item.InstanceId)
            {
                previousValue = hasShownValue ? shownValue : value;
                shownValue = value;
                shownInstance = item.InstanceId;
                hasShownValue = true;
                valueChangedAt = Time.unscaledTime;
            }
            var fraction = Mathf.SmoothStep(0, 1, Mathf.Clamp01((Time.unscaledTime - valueChangedAt) / 0.32f));
            var display = fraction >= 1f ? shownValue : System.Convert.ToInt64(previousValue + ((double)shownValue - previousValue) * fraction);
            SetText(valueText, display.ToString("N0"));
            valueText.color = Color.Lerp(new Color32(132, 65, 35, 255), PrintedInk, fraction);
            // Ink touches the paper as the new price settles. The printed number stays level.
            var landing = Mathf.Clamp01((Time.unscaledTime - valueChangedAt - .07f) / .25f);
            var lift = (1f - landing) * (1f - landing);
            var impact = Mathf.Sin(landing * Mathf.PI * 2f) * Mathf.Sin(landing * Mathf.PI);
            valueStamp.anchoredPosition = new Vector2(-109f, 25f + 10f * lift);
            valueStamp.localRotation = Quaternion.Euler(0f, 0f, -9f * lift);
            valueStamp.localScale = Vector3.one * (1f + .18f * lift - .045f * impact);
        }

        private static string PhaseName(RoundPhase phase)
        {
            switch (phase)
            {
                case RoundPhase.RoundStart: return "回合开始";
                case RoundPhase.FirstTrade: return "第一次交易";
                case RoundPhase.Broadcast: return "情报广播";
                case RoundPhase.SecondTrade: return "第二次交易";
                case RoundPhase.LieReveal: return "谎言揭晓";
                case RoundPhase.ThirdTrade: return "最后交易";
                case RoundPhase.SecretChoice: return "秘密价值操作";
                case RoundPhase.TieBreak: return "并列加赛";
                case RoundPhase.MarketUpdate: return "市场变动";
                case RoundPhase.GameOver: return "对局结束";
                default: return "准备中";
            }
        }

        internal static string DescribeGoal(GoalGroup group, int rank) => group == GoalGroup.High
            ? "高价组 · 第 " + rank + " 高" : "低价组 · 第 " + rank + " 低";
    }
}
