using UnityEngine;
using UnityEngine.UI;

namespace Worthless
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class AuctionIris : MaskableGraphic
    {
        private const int Blades = 12;
        private float closure;
        private float handAngle;

        public void Pose(float amount, float clockAngle)
        {
            closure = Mathf.Clamp01(amount);
            handAngle = clockAngle;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            if (closure <= 0f) return;
            var rect = rectTransform.rect;
            var center = rect.center;
            var outer = rect.size.magnitude * .53f + 4f;
            var opening = outer * (1f - closure);
            var twist = 22f * closure;
            var seam = Color.Lerp(ArtTheme.Surface, ArtTheme.Gold, .28f);
            for (int i = 0; i < Blades; i++)
            {
                float angle = i * (360f / Blades) + 7f;
                var a = center + Direction(angle + twist) * opening;
                var b = center + Direction(angle + 30f + twist) * opening;
                var c = center + Direction(angle + 30f) * outer;
                var d = center + Direction(angle) * outer;
                Quad(mesh, a, b, c, d, ArtTheme.Panel, ArtTheme.Ink);
                Stroke(mesh, a, d, 1.3f, seam);
                Stroke(mesh, a, b, 2.2f, ArtTheme.Gold);
            }

            // The seal arrives just as the last opening becomes smaller than the dial.
            var stamp = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.86f, .99f, closure));
            if (stamp <= 0f) return;
            float radius = 65f * stamp;
            for (int i = 0; i < 60; i++)
            {
                var a = Direction(i * 6f);
                var b = Direction((i + 1) * 6f);
                Quad(mesh, center, center, center + a * radius, center + b * radius, ArtTheme.Ink, ArtTheme.Panel);
                Stroke(mesh, center + a * radius, center + b * radius, 2f * stamp, ArtTheme.Gold);
                Stroke(mesh, center + a * (radius - 4f * stamp), center + b * (radius - 4f * stamp), stamp, seam);
                bool hour = i % 5 == 0;
                Stroke(mesh, center + a * radius * (hour ? .75f : .85f), center + a * radius * .9f,
                    (hour ? 2.5f : 1.1f) * stamp, hour ? ArtTheme.Gold : seam);
            }
            var minute = Direction(90f - handAngle + 74f * (1f - stamp));
            var hourHand = Direction(155f - handAngle / 12f);
            Stroke(mesh, center - minute * radius * .12f, center + minute * radius * .7f, 2.4f * stamp, ArtTheme.Gold);
            Stroke(mesh, center, center + hourHand * radius * .46f, 4f * stamp, ArtTheme.Text);
            var pin = 4f * stamp;
            Quad(mesh, center + Vector2.left * pin, center + Vector2.up * pin, center + Vector2.right * pin,
                center + Vector2.down * pin, ArtTheme.Gold, ArtTheme.Gold);
        }

        private static Vector2 Direction(float degrees)
        {
            float angle = degrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        }

        private static void Stroke(VertexHelper mesh, Vector2 a, Vector2 b, float width, Color color)
        {
            var axis = (b - a).normalized;
            var normal = new Vector2(-axis.y, axis.x) * (width * .5f);
            Quad(mesh, a - normal, a + normal, b + normal, b - normal, color, color);
        }

        private static void Quad(VertexHelper mesh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color inner, Color outer)
        {
            int first = mesh.currentVertCount;
            mesh.AddVert(a, inner, Vector2.zero);
            mesh.AddVert(b, inner, Vector2.zero);
            mesh.AddVert(c, outer, Vector2.zero);
            mesh.AddVert(d, outer, Vector2.zero);
            mesh.AddTriangle(first, first + 1, first + 2);
            mesh.AddTriangle(first, first + 2, first + 3);
        }
    }
}
