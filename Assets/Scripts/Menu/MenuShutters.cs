using UnityEngine;
using UnityEngine.UI;

namespace Worthless
{
    public sealed class MenuShutters : MonoBehaviour
    {
        private RectTransform frame;
        private RectTransform upper, lower, sweep, seal;
        private Image sweepImage;
        private CanvasGroup sealGroup;

        private void Awake()
        {
            frame = (RectTransform)transform;
            upper = CreateShutter("Upper shutter", ArtTheme.Panel, ArtTheme.Gold, false);
            lower = CreateShutter("Lower shutter", ArtTheme.Ink, ArtTheme.Teal, true);
            sweep = ArtTheme.Block(frame, "Light sweep", ArtTheme.Gold, Vector2.zero, new Vector2(18, 1200));
            sweep.localRotation = Quaternion.Euler(0, 0, -21f);
            sweepImage = sweep.GetComponent<Image>();
            seal = ArtTheme.Block(frame, "Exchange mark", ArtTheme.Gold, Vector2.zero, new Vector2(74, 74));
            seal.localRotation = Quaternion.Euler(0, 0, 45);
            sealGroup = seal.gameObject.AddComponent<CanvasGroup>();
            var fill = ArtTheme.Block(seal, "Inset", ArtTheme.Ink, Vector2.zero, new Vector2(62, 62));
            ArtTheme.Block(fill, "Center", ArtTheme.Teal, Vector2.zero, new Vector2(19, 19));
        }

        private RectTransform CreateShutter(string label, Color color, Color edgeColor, bool edgeAtTop)
        {
            var shutter = ArtTheme.Block(frame, label, color, Vector2.zero, Vector2.zero);
            float y = edgeAtTop ? 1f : 0f;
            var edge = ArtTheme.Block(shutter, "Edge", edgeColor, Vector2.zero, new Vector2(0f, 3f));
            edge.anchorMin = new Vector2(0f, y);
            edge.anchorMax = new Vector2(1f, y);
            var inlay = ArtTheme.Block(shutter, "Inlay", ArtTheme.Rule,
                new Vector2(0f, edgeAtTop ? -13f : 13f), new Vector2(0f, 1f));
            inlay.anchorMin = edge.anchorMin;
            inlay.anchorMax = edge.anchorMax;
            return shutter;
        }

        public void Pose(float closed, float progress)
        {
            float width = Mathf.Max(1f, frame.rect.width);
            float height = Mathf.Max(1f, frame.rect.height);
            float half = height * .5f;
            upper.sizeDelta = lower.sizeDelta = new Vector2(width + 8f, half + 5f);
            float position = height * .25f + (1f - closed) * (half + 8f);
            upper.anchoredPosition = new Vector2(0f, position);
            lower.anchoredPosition = new Vector2(0f, -position);
            sealGroup.alpha = Mathf.InverseLerp(.73f, 1f, closed);
            seal.localScale = Vector3.one * Mathf.Lerp(.84f, 1f, closed);
            sweep.sizeDelta = new Vector2(14f, height * 1.28f);
            sweep.anchoredPosition = new Vector2(Mathf.Lerp(-width * .75f, width * .75f, progress), 0f);
            sweepImage.color = new Color(.98f, .80f, .48f, .30f * Mathf.Sin(Mathf.PI * progress) * closed);
        }
    }
}
