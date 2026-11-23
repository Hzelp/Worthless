using UnityEngine;
using UnityEngine.UI;

namespace Worthless
{
    internal sealed class ScreenHistoryReview
    {
        private RectTransform root;
        private Button intelTab, tradesTab;
        private HistoryTextScroll entries;
        private Text footer;
        private bool trades, closeRequested;
        private string intel = "", total = "", transactions = "";
        public bool Visible => root.gameObject.activeSelf;
        public static ScreenHistoryReview Create(RectTransform screen, BroadcastStage stage)
        {
            var view = new ScreenHistoryReview();
            view.root = ArtTheme.Rect(screen, "Screen history review", Vector2.zero, new Vector2(1600, 900));
            view.root.localScale = new Vector3(screen.rect.width / 1600f, screen.rect.height / 900f, 1);
            view.intelTab = ArtTheme.Button(view.root, "本轮情报", new Vector2(-510, 215), new Vector2(360, 68), false);
            view.tradesTab = ArtTheme.Button(view.root, "全部交易", new Vector2(-110, 215), new Vector2(360, 68), false);
            var close = ArtTheme.Button(view.root, "关闭回看", new Vector2(560, 215), new Vector2(270, 68), false);
            foreach (var button in new[] { view.intelTab, view.tradesTab, close }) button.GetComponentInChildren<Text>().fontSize = 34;
            view.intelTab.onClick.AddListener(() => view.Select(false)); view.tradesTab.onClick.AddListener(() => view.Select(true));
            close.onClick.AddListener(() => view.closeRequested = true);
            var page = ArtTheme.Block(view.root, "Archive page", ArtTheme.Ink, new Vector2(0, -76), new Vector2(1408, 478));
            ArtTheme.Block(page, "Archive binding", ArtTheme.Gold, new Vector2(-701, 0), new Vector2(6, 478));
            view.entries = HistoryTextScroll.Create(page, "Public screen records", Vector2.zero, new Vector2(1344, 432), 34);
            view.entries.Text.color = ArtTheme.Text;
            RedactionGlitchEffect.Attach(view.entries.Text, stage);
            view.footer = ArtTheme.Label(view.root, "Review footer", "", 28, new Vector2(0, -350), new Vector2(1408, 42), ArtTheme.Teal, TextAnchor.MiddleLeft);
            view.root.gameObject.SetActive(false);
            return view;
        }
        public void Begin(bool preferTrades)
        {
            closeRequested = false; trades = preferTrades; root.gameObject.SetActive(true); Refresh(true);
        }
        public bool ConsumeClose()
        {
            var result = closeRequested; closeRequested = false; return result;
        }
        public void SetVisible(bool value)
        {
            if (root.gameObject.activeSelf != value) root.gameObject.SetActive(value);
        }
        public void SetContent(string intelText, string totalText, string tradeText)
        {
            intel = intelText; total = totalText; transactions = tradeText;
            if (Visible) Refresh(false);
        }
        private void Select(bool tradePage) { trades = tradePage; Refresh(true); }
        private void Refresh(bool reset)
        {
            intelTab.image.color = trades ? ArtTheme.Surface : ArtTheme.Teal;
            tradesTab.image.color = trades ? ArtTheme.Teal : ArtTheme.Surface;
            intelTab.GetComponentInChildren<Text>().color = trades ? ArtTheme.Text : ArtTheme.Ink;
            tradesTab.GetComponentInChildren<Text>().color = trades ? ArtTheme.Ink : ArtTheme.Text;
            // Intel has already escaped player markup and added the mask's color tags.
            entries.SetContent(trades ? (transactions.Length == 0 ? "本局尚无成功交易。" : transactions) : (intel.Length == 0 ? "本轮尚无已公开情报。" : intel), reset, richText: !trades);
            var text = trades ? "本局全部成功交易" : total;
            footer.font = ArtTheme.FontFor(text); footer.text = text;
        }
    }
}
