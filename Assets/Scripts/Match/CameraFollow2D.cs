using UnityEngine;
namespace Worthless
{
    [RequireComponent(typeof(Camera))]
    public sealed class CameraFollow2D : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField, Min(0.1f)] private float smooth = 8f;
        [SerializeField] private Vector3 offset = new Vector3(0f, 0f, -10f);
        [SerializeField, Range(0.4f, 1.2f)] private float roomZoom = 0.72f;
        [SerializeField, Range(0f, 0.95f)] private float roomDarkness = 0.68f;

        private BroadcastStage spotlight;
        private Camera cam;
        private float baseSize;
        private readonly SpriteRenderer[] mask = new SpriteRenderer[4];
        private Texture2D maskTexture;
        private Sprite maskSprite;
        private float maskOpacity;
        private Vector2 maskCenter;
        private Vector2 maskHalfExtents;
        private MatchRoundDirector director;

        private void Awake()
        {
            cam = GetComponent<Camera>();
            if (cam != null) { cam.orthographic = true; baseSize = cam.orthographicSize; }
            BuildMask();
        }

        public void SetTarget(Transform value)
        {
            target = value;
        }

        public void SetSpotlight(BroadcastStage stage)
        {
            spotlight = stage;
        }

        private void LateUpdate()
        {
            var desired = target != null ? target.position + offset : transform.position;
            var desiredSize = baseSize;
            var desiredOpacity = 0f;
            if (director == null) director = FindObjectOfType<MatchRoundDirector>();
            if (spotlight != null)
            {
                desired = spotlight.FocusPosition;
                desiredSize = spotlight.FocusSize(cam);
            }
            else if (director != null && target != null && target.gameObject.activeInHierarchy &&
                director.IsRoomLocked(director.LocalRoomId) &&
                director.TryGetRoomGeometry(director.LocalRoomId, out var center, out var half))
            {
                maskCenter = center;
                maskHalfExtents = half;
                desired = new Vector3(center.x, center.y, offset.z);
                desiredSize = Mathf.Max(baseSize * roomZoom, half.y + 0.7f, (half.x + 0.7f) / cam.aspect);
                desiredOpacity = roomDarkness;
            }
            var t = 1f - Mathf.Exp(-smooth * Time.unscaledDeltaTime);
            transform.position = Vector3.Lerp(transform.position, desired, t);
            if (cam != null) cam.orthographicSize = Mathf.Lerp(cam.orthographicSize, desiredSize, t);
            maskOpacity = Mathf.MoveTowards(maskOpacity, desiredOpacity, Time.unscaledDeltaTime * 3f);
            if (maskOpacity > 0.001f) ShowMask(maskCenter, maskHalfExtents);
            else SetMask(false);
        }

        private void BuildMask()
        {
            maskTexture = new Texture2D(1, 1);
            maskTexture.SetPixel(0, 0, Color.white);
            maskTexture.Apply();
            maskSprite = Sprite.Create(maskTexture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
            for (var i = 0; i < 4; i++)
            {
                var go = new GameObject("RoomMask" + i);
                go.transform.SetParent(transform, false);
                mask[i] = go.AddComponent<SpriteRenderer>();
                mask[i].sprite = maskSprite;
                mask[i].sortingOrder = short.MaxValue;
                go.SetActive(false);
            }
        }

        private void ShowMask(Vector2 center, Vector2 half)
        {
            if (mask[0] == null) return;
            var h = cam.orthographicSize + Mathf.Abs(transform.position.y - center.y) + half.y + 1f;
            var w = cam.orthographicSize * cam.aspect + Mathf.Abs(transform.position.x - center.x) + half.x + 1f;
            mask[0].transform.position = new Vector3(center.x - half.x - w * 0.5f, center.y, 0f);
            mask[1].transform.position = new Vector3(center.x + half.x + w * 0.5f, center.y, 0f);
            mask[2].transform.position = new Vector3(center.x, center.y + half.y + h * 0.5f, 0f);
            mask[3].transform.position = new Vector3(center.x, center.y - half.y - h * 0.5f, 0f);
            for (var i = 0; i < 4; i++)
            {
                mask[i].transform.localScale = new Vector3(i < 2 ? w : half.x * 2f, i < 2 ? (half.y + h) * 2f : h, 1f);
                mask[i].color = new Color(0.018f, 0.026f, 0.045f, maskOpacity);
                mask[i].gameObject.SetActive(true);
            }
        }

        private void OnDestroy()
        {
            Destroy(maskSprite);
            Destroy(maskTexture);
        }

        private void SetMask(bool visible)
        {
            if (mask[0] == null) return;
            for (var i = 0; i < 4; i++) if (mask[i] != null) mask[i].gameObject.SetActive(visible);
        }
    }
}
