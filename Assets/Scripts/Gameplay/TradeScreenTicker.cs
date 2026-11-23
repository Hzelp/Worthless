using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Worthless
{
    public sealed class TradeScreenTicker : MonoBehaviour
    {
        private BroadcastStage stage;
        private RectTransform viewport;
        private Text line;
        private MatchRoundDirector director;
        private readonly Queue<string> pending = new Queue<string>();
        private bool running;
        private uint game;
        public static void Create(BroadcastStage stage, RectTransform screen)
        {
            var rect = ArtTheme.Rect(screen, "Live trade ticker", Vector2.zero, Vector2.zero);
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.offsetMin = new Vector2(92f, 30f);
            rect.offsetMax = new Vector2(-92f, 74f);
            rect.gameObject.AddComponent<RectMask2D>();
            var ticker = rect.gameObject.AddComponent<TradeScreenTicker>();
            ticker.stage = stage; ticker.viewport = rect;
            ticker.line = ArtTheme.Label(rect, "Trade crawl", "", stage.TradeTickerFontSize, Vector2.zero, new Vector2(2000, 46), ArtTheme.Muted, TextAnchor.MiddleLeft);
            ticker.line.supportRichText = false; ticker.line.horizontalOverflow = HorizontalWrapMode.Overflow;
            ticker.line.rectTransform.anchorMin = ticker.line.rectTransform.anchorMax = new Vector2(0, .5f);
            ticker.line.rectTransform.pivot = new Vector2(0, .5f);
        }
        private void Update()
        {
            if (director == null)
            {
                director = FindObjectOfType<MatchRoundDirector>();
                if (director != null) director.TradeCommitted += OnTrade;
            }
            var authority = director != null ? director.GetComponent<MatchAuthority>() : null;
            if (authority == null || !authority.Started) return;
            if (game != authority.GameId)
            {
                game = authority.GameId; pending.Clear(); running = false; line.text = "";
            }
            if (!running && pending.Count > 0)
            {
                line.text = pending.Dequeue(); line.font = ArtTheme.FontFor(line.text);
                line.fontSize = Mathf.Max(12, stage.TradeTickerFontSize);
                line.rectTransform.sizeDelta = new Vector2(line.preferredWidth + 20, viewport.rect.height);
                line.rectTransform.anchoredPosition = new Vector2(viewport.rect.width, 0);
                running = true;
            }
            if (!running) return;
            var rect = line.rectTransform;
            rect.anchoredPosition += Vector2.left * (Mathf.Max(20, stage.TradeTickerSpeed) * Time.unscaledDeltaTime);
            if (rect.anchoredPosition.x + rect.rect.width < 0) { running = false; line.text = ""; }
        }
        private void OnTrade(PublicTradeRecord record)
        {
            // This event is sent only at commit time. NetworkList catch-up never invokes it.
            pending.Enqueue(director.DescribePublicTrade(record));
        }
        private void OnDestroy()
        {
            if (director != null) director.TradeCommitted -= OnTrade;
        }
    }
}
