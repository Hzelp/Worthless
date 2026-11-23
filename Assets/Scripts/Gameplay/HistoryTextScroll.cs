using UnityEngine;
using UnityEngine.UI;

namespace Worthless
{
    /// <summary>A clipped text region inside its owner's canvas, with no extra HUD or popup.</summary>
    internal sealed class HistoryTextScroll
    {
        public ScrollRect Scroll { get; private set; }
        public Text Text { get; private set; }
        public static HistoryTextScroll Create(Transform parent, string name, Vector2 position, Vector2 size, int fontSize)
        {
            var root = ArtTheme.Rect(parent, name, position, size);
            var viewport = ArtTheme.Block(root, "Viewport", Color.clear, Vector2.zero, Vector2.zero, true);
            ArtTheme.Stretch(viewport); viewport.offsetMax = new Vector2(-12, 0);
            viewport.gameObject.AddComponent<RectMask2D>();
            var text = ArtTheme.Label(viewport, "Entries", "", fontSize, Vector2.zero, Vector2.zero, ArtTheme.Muted, TextAnchor.UpperLeft);
            text.supportRichText = false; text.verticalOverflow = VerticalWrapMode.Overflow;
            var content = text.rectTransform; content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one; content.pivot = new Vector2(.5f, 1);
            var scroll = root.gameObject.AddComponent<ScrollRect>(); scroll.viewport = viewport; scroll.content = content;
            scroll.horizontal = false; scroll.vertical = true; scroll.scrollSensitivity = 40; scroll.movementType = ScrollRect.MovementType.Clamped;
            var rail = ArtTheme.Block(root, "Scrollbar", ArtTheme.Surface, Vector2.zero, Vector2.zero, true);
            rail.anchorMin = new Vector2(1, 0); rail.anchorMax = Vector2.one; rail.offsetMin = new Vector2(-7, 0); rail.offsetMax = Vector2.zero;
            var handle = ArtTheme.Block(rail, "Handle", ArtTheme.Teal, Vector2.zero, Vector2.zero, true); ArtTheme.Stretch(handle);
            var bar = rail.gameObject.AddComponent<Scrollbar>(); bar.handleRect = handle; bar.targetGraphic = handle.GetComponent<Image>(); bar.direction = Scrollbar.Direction.BottomToTop;
            scroll.verticalScrollbar = bar; scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            return new HistoryTextScroll { Scroll = scroll, Text = text };
        }
        public void SetContent(string content, bool reset = false, bool richText = false)
        {
            if (Text.text == content && Text.supportRichText == richText && !reset) return;
            var wasShort = Scroll.content.rect.height <= Scroll.viewport.rect.height + 1f;
            var position = reset || wasShort ? 1f : Scroll.verticalNormalizedPosition;
            Text.supportRichText = richText;
            Text.font = ArtTheme.FontFor(content); Text.text = content;
            Text.rectTransform.sizeDelta = new Vector2(0, Mathf.Max(Scroll.viewport.rect.height, Text.preferredHeight + 3));
            Canvas.ForceUpdateCanvases(); Scroll.verticalNormalizedPosition = position;
        }
    }
}
