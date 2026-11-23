using System.Collections;
using UnityEngine;

namespace Worthless
{
    // The binding stays still while the page opens into its final reading position.
    [RequireComponent(typeof(RectTransform), typeof(CanvasGroup))]
    public sealed class CatalogueLeaf : MonoBehaviour
    {
        private RectTransform page;
        private CanvasGroup paper;
        private Vector2 rest;

        private void Awake()
        {
            page = (RectTransform)transform;
            paper = GetComponent<CanvasGroup>();
            rest = page.anchoredPosition;
        }

        private void OnEnable() => StartCoroutine(Open());

        private IEnumerator Open()
        {
            float hinge = page.rect.width * .5f;
            for (float elapsed = 0; elapsed < .25f; elapsed += Time.unscaledDeltaTime)
            {
                float t = elapsed / .25f;
                float fold = (1f - t) * (1f - t) * (1f - t);
                float width = 1f - .065f * fold;
                page.localScale = new Vector3(width, 1f, 1f);
                page.anchoredPosition = rest + new Vector2(-hinge * (1f - width), -9f * fold);
                paper.alpha = 1f - .8f * fold;
                yield return null;
            }
            page.localScale = Vector3.one;
            page.anchoredPosition = rest;
            paper.alpha = 1f;
        }
    }
}
