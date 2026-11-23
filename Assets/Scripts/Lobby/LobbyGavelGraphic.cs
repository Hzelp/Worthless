using UnityEngine;
using UnityEngine.UI;

namespace Worthless
{
    /// <summary>A letterpress-style auction vignette drawn at the lobby canvas resolution.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class LobbyGavelGraphic : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            var area = rectTransform.rect;
            var scale = Mathf.Min(area.width / 320f, area.height / 120f);
            var origin = area.center;
            var brass = ArtTheme.Gold;
            var shadow = Color.Lerp(ArtTheme.Surface, ArtTheme.Gold, 0.38f);
            var ivory = ArtTheme.Text;
            var ink = ArtTheme.Ink;

            // Engraved rays converge on the striking block.
            for (var i = 0; i < 17; i++)
            {
                var angle = (13f + 9.6f * i) * Mathf.Deg2Rad;
                var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                var center = new Vector2(-18f, -48f);
                var inner = 58f + (i % 3) * 4f;
                var outer = inner + (i % 2 == 0 ? 15f : 7f);
                Stroke(center + direction * inner, center + direction * outer, 1.2f, shadow);
            }

            // A turned handle, stepped metal bands, and an offset highlight.
            var joint = new Vector2(-19f, 14f);
            var along = new Vector2(0.93358f, -0.35837f);
            var up = new Vector2(-along.y, along.x);
            Part(8, -3.7f, 80, 3.7f, brass);
            Part(53, -5, 79, 5, brass);
            Part(79, -6.5f, 87, 6.5f, ivory);
            Part(-10, -18, 10, 18, brass);
            Part(-13, 11, 13, 17, ivory);
            Part(-17, 18, 17, 25, brass);
            Part(-13, -17, 13, -11, ivory);
            Part(-17, -25, 17, -18, brass);
            Part(-6, -10, -4, 10, ink);
            Stroke(new Vector2(-59, -40), new Vector2(12, -40), 6, ivory);
            Stroke(new Vector2(-65, -47), new Vector2(18, -47), 6, brass);
            Stroke(new Vector2(-145, -53), new Vector2(-31, -53), 1.2f, shadow);
            Stroke(new Vector2(-18, -53), new Vector2(145, -53), 1.2f, shadow);

            void Part(float left, float bottom, float right, float top, Color tint)
            {
                Quad(joint + along * left + up * bottom, joint + along * left + up * top,
                    joint + along * right + up * top, joint + along * right + up * bottom, tint);
            }

            void Stroke(Vector2 from, Vector2 to, float width, Color tint)
            {
                var axis = (to - from).normalized;
                var normal = new Vector2(-axis.y, axis.x) * (width * 0.5f);
                Quad(from - normal, from + normal, to + normal, to - normal, tint);
            }

            void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color tint)
            {
                var index = mesh.currentVertCount;
                mesh.AddVert(origin + a * scale, tint, Vector2.zero);
                mesh.AddVert(origin + b * scale, tint, Vector2.zero);
                mesh.AddVert(origin + c * scale, tint, Vector2.zero);
                mesh.AddVert(origin + d * scale, tint, Vector2.zero);
                mesh.AddTriangle(index, index + 1, index + 2);
                mesh.AddTriangle(index, index + 2, index + 3);
            }
        }
    }
}
