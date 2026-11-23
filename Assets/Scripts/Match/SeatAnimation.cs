using UnityEngine;
namespace Worthless
{
    public sealed class SeatAnimation : MonoBehaviour
    {
        private SpriteRenderer rendererRef;
        private SeatAnimationSet set;
        private Vector2 facing = Vector2.down;
        private float timer;
        private int frame;

        public void Bind(SpriteRenderer target, SeatAnimationSet animationSet)
        {
            rendererRef = target;
            set = animationSet;
            Apply(false, Vector2.zero);
        }

        public void Apply(bool running, Vector2 velocity)
        {
            if (set == null || rendererRef == null) return;
            if (velocity.sqrMagnitude > 0.0001f) facing = velocity.normalized;
            var row = Row(facing);
            rendererRef.flipX = facing.x < -0.05f;
            var bank = running ? set.Run(row) : set.Walk(row);
            if (bank == null || bank.Length == 0) return;
            if (velocity.sqrMagnitude <= 0.0001f)
            {
                frame = 0; timer = 0f;
                rendererRef.sprite = bank[0];
                return;
            }
            timer += Time.deltaTime;
            var duration = running ? 0.075f : 0.12f;
            while (timer >= duration) { timer -= duration; frame = (frame + 1) % bank.Length; }
            rendererRef.sprite = bank[frame % bank.Length];
        }

        private static int Row(Vector2 d)
        {
            if (Mathf.Abs(d.y) > Mathf.Abs(d.x) * 1.6f) return d.y >= 0f ? 4 : 0;
            if (d.y > 0.25f) return 3;
            if (d.y < -0.25f) return 1;
            return 2;
        }
    }
}
