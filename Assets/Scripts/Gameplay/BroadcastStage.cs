using UnityEngine;
using UnityEngine.UI;

namespace Worthless
{
    /// <summary>场景内可编辑的大屏镜头锚点（广播构图）。</summary>
    public sealed class BroadcastStage : MonoBehaviour
    {
        [Tooltip("Camera framing offset from this stage, in world units.")]
        public Vector3 CameraPosition = new Vector3(0f, 0f, -10f);
        [Min(1f)] public float OrthographicSize = 6.3f;
        [Min(0f)] public float ReviewRange = 12f;
        public Text ScreenText;
        [SerializeField] private Sprite idleInsignia;
        [Header("成功交易弹幕")]
        [Min(12)] public int TradeTickerFontSize = 26;
        [Min(20f)] public float TradeTickerSpeed = 180f;
        [Header("情报黑条故障效果")]
        [Range(0f, 1f)] public float RedactionGlitchIntensity = .85f;
        [Tooltip("黑条切片的横向错位幅度，按黑条高度计算。")]
        [Range(0f, 1.2f)] public float RedactionSliceDisplacement = .95f;
        [Tooltip("故障期间边缘细短的阶梯像素毛刺数量。")]
        [Range(0, 4)] public int RedactionSpikeCount = 4;
        [Range(0f, .6f)] public float RedactionSpikeLength = .26f;
        [Tooltip("按字体指标对齐后额外上下偏移；负数下移。")]
        [Range(-.25f, .25f)] public float RedactionMaskVerticalOffset;
        [Tooltip("故障事件之间的随机静默间隔（秒）。")]
        [Min(.5f)] public float RedactionGlitchMinInterval = 2f;
        [Min(.5f)] public float RedactionGlitchMaxInterval = 4.5f;
        [Min(.05f)] public float RedactionGlitchMinDuration = .18f;
        [Min(.05f)] public float RedactionGlitchMaxDuration = .32f;
        [Tooltip("仅在短暂故障事件内刷新碎块，不决定闪动间隔。")]
        [Range(4f, 24f)] public float RedactionGlitchRate = 12f;
        [Range(0, 12)] public int RedactionParticleCount = 8;
        [Tooltip("大碎块尺寸，以黑条高度为单位。")]
        [Range(.25f, 3f)] public float RedactionShardSize = .9f;
        [Tooltip("碎块可飞出黑条四边的距离，以黑条高度为单位。")]
        [Range(0f, 3f)] public float RedactionShardReach = .85f;
        public Color RedactionGlitchColor = new Color32(184, 184, 184, 240);

        private Text heading;
        private Image idleSeal;
        private SecretChoiceHud choices;
        private ScreenHistoryReview historyReview;
        public bool ReviewCloseRequested => historyReview != null && historyReview.ConsumeClose();
        public void BeginReview(bool preferTrades) { if (historyReview != null) historyReview.Begin(preferTrades); }
        public void SetReviewContent(string intel, string total, string trades) { if (historyReview != null) historyReview.SetContent(intel, total, trades); }
        public Vector3 FocusPosition => transform.position + CameraPosition;

        public float FocusSize(Camera camera)
        {
            if (ScreenText == null || camera == null) return OrthographicSize;
            var screen = ScreenText.canvas.transform as RectTransform;
            if (screen == null) return OrthographicSize;
            var width = screen.rect.width * Mathf.Abs(screen.lossyScale.x);
            return Mathf.Max(OrthographicSize, width * 0.57f / Mathf.Max(0.1f, camera.aspect));
        }

        private void Awake()
        {
            if (ScreenText == null) return;
            var root = ScreenText.rectTransform.parent as RectTransform;
            var background = root.GetComponentInChildren<Image>();
            if (background != null)
                background.color = background.sprite == null ? ArtTheme.Ink : Color.white;
            var rect = ScreenText.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(92, 75);
            rect.offsetMax = new Vector2(-92, -195);
            ScreenText.font = ArtTheme.FontFor(string.Empty);
            ScreenText.fontSize = 49;
            ScreenText.resizeTextForBestFit = true;
            ScreenText.resizeTextMinSize = 25;
            ScreenText.resizeTextMaxSize = 49;
            ScreenText.alignment = TextAnchor.UpperLeft;
            ScreenText.horizontalOverflow = HorizontalWrapMode.Wrap;
            ScreenText.verticalOverflow = VerticalWrapMode.Truncate;
            ScreenText.color = ArtTheme.Text;
            ScreenText.raycastTarget = false;
            RedactionGlitchEffect.Attach(ScreenText, this);

            var header = new GameObject("Screen heading", typeof(RectTransform), typeof(Text));
            header.transform.SetParent(root, false);
            heading = header.GetComponent<Text>();
            heading.rectTransform.anchorMin = new Vector2(0f, 1f);
            heading.rectTransform.anchorMax = Vector2.one;
            heading.rectTransform.offsetMin = new Vector2(119f, -154f);
            heading.rectTransform.offsetMax = new Vector2(-390f, -52f);
            heading.font = ScreenText.font;
            heading.fontSize = 64;
            heading.alignment = TextAnchor.MiddleLeft;
            heading.color = ArtTheme.Gold;
            heading.raycastTarget = false;
            var topLeft = new Vector2(0f, 1f);
            ArtTheme.Block(root, "Brass title marker", ArtTheme.Gold,
                new Vector2(97f, -102f), new Vector2(8f, 52f), anchor: topLeft);
            var lineRect = ArtTheme.Block(root, "Engraved divider", ArtTheme.Rule, Vector2.zero, Vector2.zero);
            lineRect.anchorMin = new Vector2(0f, 1f);
            lineRect.anchorMax = Vector2.one;
            lineRect.offsetMin = new Vector2(92f, -168f);
            lineRect.offsetMax = new Vector2(-92f, -165f);
            ArtTheme.Block(root, "Brass divider inset", ArtTheme.Gold,
                new Vector2(150f, -166f), new Vector2(116f, 6f), anchor: topLeft);
            if (idleInsignia != null)
            {
                var seal = ArtTheme.Rect(root, "Exchange insignia", new Vector2(0f, -55f), new Vector2(690f, 460f));
                idleSeal = seal.gameObject.AddComponent<Image>();
                idleSeal.sprite = idleInsignia;
                idleSeal.raycastTarget = false;
            }
            var screen = ScreenText.canvas;
            screen.worldCamera = Camera.main;
            var raycaster = screen.GetComponent<GraphicRaycaster>();
            if (raycaster == null) raycaster = screen.gameObject.AddComponent<GraphicRaycaster>();
            raycaster.blockingObjects = GraphicRaycaster.BlockingObjects.None;
            choices = SecretChoiceHud.Create(screen.transform as RectTransform);
            TradeScreenTicker.Create(this, screen.transform as RectTransform);
            historyReview = ScreenHistoryReview.Create(screen.transform as RectTransform, this);
            Present("情报大屏", "");
        }

        public void Present(string label, string content, bool showChoices = false, bool showReview = false)
        {
            if (historyReview != null) historyReview.SetVisible(showReview);
            if (idleSeal != null) idleSeal.enabled = !showReview && !showChoices && string.IsNullOrEmpty(content);
            if (choices != null && choices.gameObject.activeSelf != showChoices) choices.gameObject.SetActive(showChoices);
            if (ScreenText != null) ScreenText.enabled = !showChoices && !showReview;
            if (ScreenText != null && ScreenText.canvas.worldCamera == null) ScreenText.canvas.worldCamera = Camera.main;
            if (heading != null && heading.text != label)
            {
                heading.font = ArtTheme.FontFor(label);
                heading.text = label;
            }
            if (ScreenText != null && ScreenText.text != content)
            {
                ScreenText.font = ArtTheme.FontFor(content);
                ScreenText.text = content;
            }
        }
    }
}
