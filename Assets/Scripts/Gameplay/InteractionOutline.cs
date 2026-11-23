using UnityEngine;

namespace Worthless
{
    /// <summary>One sprite texel of yellow silhouette around the current local trade target.</summary>
    [DefaultExecutionOrder(100)]
    public sealed class InteractionOutline : MonoBehaviour
    {
        private static readonly Color OutlineColor = new Color32(255, 217, 58, 255);
        private static readonly Vector2[] Offsets =
        {
            new Vector2(-1, -1), new Vector2(0, -1), new Vector2(1, -1),
            new Vector2(-1, 0), new Vector2(1, 0),
            new Vector2(-1, 1), new Vector2(0, 1), new Vector2(1, 1)
        };

        private SpriteRenderer source;
        private GameObject outlineRoot;
        private SpriteRenderer[] silhouettes;
        private Material material;

        public bool Visible => outlineRoot != null && outlineRoot.activeInHierarchy;

        public void SetTarget(SeatActor actor)
        {
            var next = actor != null ? actor.VisualRenderer : null;
            if (source == next) return;
            source = next;
            if (outlineRoot != null) outlineRoot.SetActive(false);
        }

        private void LateUpdate()
        {
            if (source == null || !source.enabled || source.sprite == null || !source.gameObject.activeInHierarchy)
            {
                if (outlineRoot != null) outlineRoot.SetActive(false);
                return;
            }
            if (material == null)
            {
                var shader = Resources.Load<Shader>("InteractionOutline");
                if (shader == null) return;
                material = new Material(shader);
            }
            if (outlineRoot == null)
            {
                outlineRoot = new GameObject("Interaction pixel outline");
                silhouettes = new SpriteRenderer[Offsets.Length];
                for (var i = 0; i < silhouettes.Length; i++)
                {
                    var pixel = new GameObject("Silhouette " + i);
                    pixel.transform.SetParent(outlineRoot.transform, false);
                    silhouettes[i] = pixel.AddComponent<SpriteRenderer>();
                    silhouettes[i].sharedMaterial = material;
                    silhouettes[i].color = OutlineColor;
                }
            }
            outlineRoot.transform.SetParent(source.transform, false);
            outlineRoot.transform.localPosition = Vector3.zero;
            outlineRoot.transform.localRotation = Quaternion.identity;
            outlineRoot.transform.localScale = Vector3.one;
            var texel = 1f / source.sprite.pixelsPerUnit;
            for (var i = 0; i < silhouettes.Length; i++)
            {
                var silhouette = silhouettes[i];
                silhouette.sprite = source.sprite;
                silhouette.flipX = source.flipX;
                silhouette.flipY = source.flipY;
                silhouette.sortingLayerID = source.sortingLayerID;
                silhouette.sortingOrder = source.sortingOrder - 1;
                silhouette.maskInteraction = source.maskInteraction;
                silhouette.transform.localPosition = Offsets[i] * texel;
            }
            outlineRoot.SetActive(true);
        }

        private void OnDisable()
        {
            source = null;
            if (outlineRoot != null) outlineRoot.SetActive(false);
        }

        private void OnDestroy()
        {
            if (outlineRoot != null) Destroy(outlineRoot);
            if (material != null) Destroy(material);
        }
    }
}
