using UnityEngine;
using UnityEngine.EventSystems;

namespace Worthless
{
    public sealed class MenuFocusMotion : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
        ISelectHandler, IDeselectHandler
    {
        private RectTransform arrow;
        private CanvasGroup opacity;
        private Vector2 rest;
        private bool hovered;
        private bool selected;
        private float progress;
        private Coroutine movement;

        public void Configure(RectTransform marker)
        {
            arrow = marker;
            rest = marker.anchoredPosition;
            opacity = marker.gameObject.AddComponent<CanvasGroup>();
            opacity.alpha = 0.55f;
        }

        public void OnPointerEnter(PointerEventData data) { hovered = true; Wake(); }
        public void OnPointerExit(PointerEventData data) { hovered = false; Wake(); }
        public void OnSelect(BaseEventData data) { selected = true; Wake(); }
        public void OnDeselect(BaseEventData data) { selected = false; Wake(); }

        private void Wake()
        {
            if (arrow != null && movement == null)
                movement = StartCoroutine(Animate());
        }

        private System.Collections.IEnumerator Animate()
        {
            do
            {
                progress = Mathf.MoveTowards(progress, hovered || selected ? 1f : 0f, Time.unscaledDeltaTime * 7f);
                arrow.anchoredPosition = rest + Vector2.right * (8f * Mathf.SmoothStep(0f, 1f, progress));
                opacity.alpha = Mathf.Lerp(0.55f, 1f, progress);
                yield return null;
            } while (progress != (hovered || selected ? 1f : 0f));
            movement = null;
        }
    }
}
