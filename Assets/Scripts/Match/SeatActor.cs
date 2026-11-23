using UnityEngine;

namespace Worthless
{
    /// <summary>一个席位的可见角色。服务端用物理体，客户端只按快照摆放。</summary>
    public sealed class SeatActor : MonoBehaviour
    {
        private static readonly Color SelfColor = new Color(0.35f, 0.85f, 0.45f, 1f);
        private static readonly Color OtherColor = new Color(0.42f, 0.62f, 0.95f, 1f);
        private static readonly Color NpcColor = new Color(0.62f, 0.62f, 0.66f, 1f);
        private static readonly Color OfflineColor = new Color(0.45f, 0.45f, 0.5f, 0.45f);

        [SerializeField] private SpriteRenderer body;
        [SerializeField] private TextMesh label;
        [SerializeField] private SeatAnimationSet animationSet;

        private SeatAnimation animation;
        private MatchAuthority authority;
        private bool initialised;
        private string labelBase = string.Empty;
        private string displayName = string.Empty;

        public Rigidbody2D Body { get; private set; }
        public string DisplayName { get { return string.IsNullOrEmpty(displayName) ? name : displayName; } }
        public string BaseLabelText { get { return label != null ? label.text : string.Empty; } }
        public bool IsBaseLabelVisible { get { if (label == null) return false; var r = label.GetComponent<Renderer>(); return r != null && r.enabled; } }
        public Collider2D BodyCollider { get; private set; }
        public SpriteRenderer VisualRenderer => body;

        private void Awake()
        {
            Body = GetComponent<Rigidbody2D>();
            BodyCollider = GetComponent<Collider2D>();
            if (body == null) body = GetComponentInChildren<SpriteRenderer>();
            if (label == null) label = GetComponentInChildren<TextMesh>();
            authority = GetComponentInParent<MatchAuthority>();
            if (authority == null) authority = FindObjectOfType<MatchAuthority>();
            animation = GetComponent<SeatAnimation>();
            if (animation == null) animation = gameObject.AddComponent<SeatAnimation>();
            animation.Bind(body, animationSet);
        }

        public void ConfigureAuthority(bool isServer)
        {
            initialised = true;
            if (Body != null) Body.simulated = isServer;
            if (BodyCollider != null) BodyCollider.enabled = isServer;
        }

        public void ApplyView(SeatSnapshot snapshot, bool isSelf, bool started, bool visible)
        {
            if (!initialised) ConfigureAuthority(false);
            gameObject.SetActive(visible);
            if (!visible) return;

            if (body != null)
            {
                Color color = snapshot.IsNpc ? NpcColor : (isSelf ? SelfColor : OtherColor);
                if (started && snapshot.IsReal && !snapshot.IsOnline) color = OfflineColor;
                body.color = color;
            }
            if (animation != null) animation.Apply(snapshot.IsRunning, new Vector2(snapshot.VX, snapshot.VY));

            if (label != null)
            {
                labelBase = snapshot.IsNpc ? ("NPC " + (snapshot.Seat + 1)) : (isSelf ? "你" : ("P" + (snapshot.Seat + 1)));
                if (authority != null && snapshot.IsReal) displayName = authority.PlayerNameForSeat(snapshot.Seat);
                else displayName = labelBase;
                label.text = string.Empty;
            }
        }

        public void SetBaseLabelVisible(bool visible)
        {
            if (label != null) { var r = label.GetComponent<Renderer>(); if (r != null) r.enabled = visible; }
        }

        public void SetAuthoritativePosition(Vector2 position)
        {
            transform.position = new Vector3(position.x, position.y, 0f);
        }
    }
}
