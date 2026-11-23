using UnityEngine;
using UnityEngine.UI;

namespace Worthless
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class MenuEngraving : MaskableGraphic
    {
        private static readonly Color32 Patina = new Color32(79, 90, 70, 255);
        private static readonly Color32 Brass = new Color32(161, 135, 86, 255);
        private static readonly Color32 Highlight = new Color32(221, 191, 133, 255);
        public enum Pattern { Frame, Title, Balance }
        private Pattern pattern;

        public void Configure(Pattern selection)
        {
            pattern = selection;
            raycastTarget = false;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            var rect = rectTransform.rect;
            if (pattern == Pattern.Balance)
                DrawBalanceSurround(mesh);
            else if (pattern == Pattern.Title)
                DrawTitle(mesh, rect);
            else
                DrawFrame(mesh, rect);
        }

        private static void DrawBalanceSurround(VertexHelper mesh)
        {
            var etched = new Color32(57, 77, 63, 255);
            var gilded = new Color32(116, 105, 71, 255);
            for (int side = -1; side <= 1; side += 2)
            {
                Vector2 Dial(float angle, float radius)
                {
                    float a = angle * Mathf.Deg2Rad;
                    return new Vector2(side * radius * Mathf.Cos(a), 7 + radius * Mathf.Sin(a));
                }
                // Each quadrant ends before the beam and before the compass rose.
                for (int i = 0; i < 60; i++)
                {
                    float angle = 20 + i * 1.1f;
                    Line(mesh, Dial(angle, 287), Dial(angle + 1.1f, 287), 1.25f, etched);
                    if (i >= 12 && i < 51)
                        Line(mesh, Dial(angle, 280), Dial(angle + 1.1f, 280), 1, gilded);
                }
                for (int i = 0; i <= 14; i++)
                {
                    float angle = 27 + i * 3.8f;
                    Line(mesh, Dial(angle, 290), Dial(angle, i % 3 == 0 ? 300 : 294), 1.4f, i % 3 == 0 ? gilded : etched);
                }
                Diamond(mesh, Dial(17, 287), 2.5f, 2.5f, gilded);

                // Laurel branches follow the underside of the pans and end outside their swing.
                Vector2 Stem(float t) => new Vector2(side * (54 + 237 * t), -282 + 31 * t + 136 * t * t);
                for (int i = 0; i < 32; i++)
                    Line(mesh, Stem(i / 32f), Stem((i + 1) / 32f), 1.7f, gilded);
                for (int i = 0; i < 9; i++)
                {
                    float t = .12f + i * .097f;
                    var root = Stem(t);
                    var axis = (Stem(t + .01f) - root).normalized;
                    var normal = new Vector2(-side * axis.y, side * axis.x);
                    float length = 32 - i * 1.4f;
                    for (int bank = -1; bank <= 1; bank += 2)
                    {
                        var start = root + axis * (bank == 1 ? 4 : 0);
                        var tip = start + axis * length + normal * bank * (17 - i * .55f);
                        var rib = new Vector2(-(tip - start).y, (tip - start).x).normalized;
                        var belly = Vector2.Lerp(start, tip, .48f);
                        Quad(mesh, start, belly + rib * 3.6f, tip, belly - rib * 3.6f, etched);
                        Line(mesh, start, Vector2.Lerp(belly, tip, .42f), 1, gilded);
                    }
                }
            }
            var summit = new Vector2(0, 294);
            Diamond(mesh, summit, 4.2f, 21, Brass);
            Diamond(mesh, summit, 14, 3.2f, Brass);
            Diamond(mesh, summit, 3, 8, Highlight);
            Diamond(mesh, new Vector2(0, -281), 13, 3, gilded);
            Line(mesh, new Vector2(-52, -282), new Vector2(-20, -282), 1, etched);
            Line(mesh, new Vector2(20, -282), new Vector2(52, -282), 1, etched);
        }

        private static void DrawFrame(VertexHelper mesh, Rect rect)
        {
            const float inset = 17f;
            for (int sx = -1; sx <= 1; sx += 2)
            {
                for (int sy = -1; sy <= 1; sy += 2)
                {
                    var corner = rect.center + new Vector2(sx * (rect.width / 2 - inset), sy * (rect.height / 2 - inset));
                    Vector2 Inward(float x, float y) => corner - new Vector2(sx * x, sy * y);

                    Line(mesh, Inward(11, 0), Inward(rect.width / 2 - inset - 11, 0), 1, Patina);
                    Line(mesh, Inward(0, 11), Inward(0, rect.height / 2 - inset - 5), 1, Patina);
                    Line(mesh, Inward(0, 11), Inward(11, 0), 1.4f, Brass);
                    // Mirrored stepped brass inlays inside the clipped outer corners.
                    Line(mesh, Inward(7, 67), Inward(7, 23), 1.4f, Brass);
                    Line(mesh, Inward(7, 23), Inward(16, 23), 1.4f, Brass);
                    Line(mesh, Inward(16, 23), Inward(16, 16), 1.4f, Brass);
                    Line(mesh, Inward(16, 16), Inward(23, 16), 1.4f, Brass);
                    Line(mesh, Inward(23, 16), Inward(23, 7), 1.4f, Brass);
                    Line(mesh, Inward(23, 7), Inward(97, 7), 1.4f, Brass);
                    Diamond(mesh, Inward(8, 8), 2.7f, 2.7f, Highlight);
                }
            }
            Diamond(mesh, new Vector2(rect.center.x, rect.yMax - inset), 6, 2.6f, Brass);
            Diamond(mesh, new Vector2(rect.center.x, rect.yMin + inset), 6, 2.6f, Brass);
        }

        private static void DrawTitle(VertexHelper mesh, Rect rect)
        {
            float halfWidth = rect.width * .5f;
            var crown = new Vector2(rect.center.x, rect.yMax - 27);
            for (int i = 0; i <= 12; i++)
            {
                float angle = i * Mathf.PI / 12;
                var ray = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                float length = i % 2 == 0 ? 25 : 20;
                Line(mesh, crown + ray * 8, crown + ray * length, 1.4f, Brass);
            }
            Diamond(mesh, crown, 5, 5, Highlight);

            var divider = new Vector2(rect.center.x, rect.yMin + 9);
            for (int side = -1; side <= 1; side += 2)
            {
                var axis = Vector2.right * side;
                Line(mesh, crown + axis * 33, crown + axis * halfWidth, 1.3f, Brass);
                Line(mesh, crown + axis * 57 + Vector2.down * 4, crown + axis * (halfWidth - 13) + Vector2.down * 4, 1, Patina);
                Line(mesh, divider + axis * 64, divider + axis * halfWidth, 1, Patina);
                Diamond(mesh, divider + axis * (halfWidth - 3), 3, 2.3f, Brass);
            }
            // Two tapered, crossing curves echo the lathe engraving on auction stationery.
            for (int side = -1; side <= 1; side += 2)
            {
                var previous = divider + Vector2.left * 64;
                for (int i = 1; i <= 56; i++)
                {
                    float t = i / 56f;
                    float y = side * 7 * Mathf.Sin(t * Mathf.PI) * Mathf.Sin(t * 3 * Mathf.PI);
                    var next = divider + new Vector2(Mathf.Lerp(-64, 64, t), y);
                    Line(mesh, previous, next, 1.35f, Brass);
                    previous = next;
                }
            }
            Diamond(mesh, divider, 3, 3, Highlight);
        }

        private static void Line(VertexHelper mesh, Vector2 a, Vector2 b, float width, Color32 color)
        {
            var delta = (b - a).normalized;
            var normal = new Vector2(-delta.y, delta.x) * (width * .5f);
            Quad(mesh, a - normal, a + normal, b + normal, b - normal, color);
        }

        private static void Diamond(VertexHelper mesh, Vector2 center, float width, float height, Color32 color)
        {
            Quad(mesh, center + Vector2.left * width, center + Vector2.up * height,
                center + Vector2.right * width, center + Vector2.down * height, color);
        }

        private static void Quad(VertexHelper mesh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color32 color)
        {
            int start = mesh.currentVertCount;
            mesh.AddVert(a, color, Vector2.zero);
            mesh.AddVert(b, color, Vector2.zero);
            mesh.AddVert(c, color, Vector2.zero);
            mesh.AddVert(d, color, Vector2.zero);
            mesh.AddTriangle(start, start + 1, start + 2);
            mesh.AddTriangle(start, start + 2, start + 3);
        }
    }
}
