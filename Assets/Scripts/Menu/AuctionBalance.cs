using UnityEngine;
using UnityEngine.UI;

namespace Worthless
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class AuctionBalance : MaskableGraphic
    {
        private static readonly Color Metal = new Color32(105, 134, 113, 255);
        private static readonly Color Brass = new Color32(177, 132, 70, 255);
        private static readonly Color Light = new Color32(251, 225, 160, 255);
        private static readonly Color Lacquer = new Color32(172, 76, 61, 255);
        private static readonly Color Ruby = new Color32(226, 115, 84, 255);
        private const float Cycle = 7.8f;
        private float elapsed, lastDrawn;
        private VertexHelper mesh;

        private void Update()
        {
            if (FrontEndOverlay.IsOpen || TransitionOverlay.IsScreenCovered) return;
            elapsed += Time.unscaledDeltaTime;
            if (elapsed - lastDrawn < 1f / 30f) return;
            lastDrawn = elapsed;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vertices)
        {
            vertices.Clear();
            mesh = vertices;
            var t = elapsed % Cycle;
            // Signed loads drive the beam and the needle from the same two impacts.
            float keyLoad = Load(t, .84f, 5.58f);
            float gemLoad = Load(t, 2.76f, 6.58f);
            float tilt = (keyLoad * 11f - gemLoad * 18f +
                Recoil(t - .84f) * 4f - Recoil(t - 2.76f) * 6f) * Mathf.Deg2Rad;
            var pivot = new Vector2(0, 71);
            var arm = new Vector2(Mathf.Cos(tilt), Mathf.Sin(tilt)) * 193f;
            DrawGauge();
            Stand(pivot);
            var left = pivot - arm;
            var right = pivot + arm;
            Pan(left, t, true);
            Pan(right, t, false);

            BeamHalf(pivot, left);
            BeamHalf(pivot, right);
            float needle = Mathf.PI * .5f + tilt * 2.25f;
            var axis = new Vector2(Mathf.Cos(needle), Mathf.Sin(needle));
            var cross = new Vector2(-axis.y, axis.x);
            Triangle(pivot + cross * 6f, pivot - cross * 6f, pivot + axis * 124f, Lacquer);
            Line(pivot + axis * 19, pivot + axis * 104, 1.5f, Ruby);
            Disc(pivot, 15, 15, ArtTheme.Gold);
            Disc(pivot, 9, 9, ArtTheme.Panel);
            for (int i = 0; i < 8; i++)
            {
                var ray = Polar(i * 45f);
                Line(pivot + ray * 11, pivot + ray * 14, 1.5f, Brass);
            }
            Jewel(pivot, 5, 7);
            mesh = null;
        }

        private static float Load(float t, float impact, float lift)
        {
            return Mathf.SmoothStep(0, 1, (t - impact) / .26f) *
                (1f - Mathf.SmoothStep(0, 1, (t - lift) / .35f));
        }

        private static float Recoil(float age)
        {
            return age <= 0 ? 0 : Mathf.Sin(age * 11f) * Mathf.Exp(-age * 2.8f);
        }

        private void DrawGauge()
        {
            var center = new Vector2(0, 71);
            Arc(center, 147, 147, 42, 138, 36, 2.5f, Brass);
            for (int i = 0; i <= 32; i++)
            {
                var dir = Polar(42 + 3f * i);
                bool major = i % 4 == 0;
                Line(center + dir * (major ? 129 : 138), center + dir * 146,
                    major ? 2.8f : 1.3f, i == 16 ? Ruby : Metal);
            }
            Disc(new Vector2(0, -231), 151, 7.5f, new Color32(9, 18, 16, 255));
        }

        private void Stand(Vector2 pivot)
        {
            Trapezoid(-228, -217, 119, 119, Metal);
            Trapezoid(-217, -202, 111, 94, Brass);
            Trapezoid(-205, -195, 94, 91, Metal);
            Trapezoid(-195, -185, 76, 45, ArtTheme.Surface);
            Trapezoid(-185, -166, 45, 20, Brass);
            Trapezoid(-166, 53, 17, 8, Metal);
            Quad(new Vector2(-11, -164), new Vector2(-5, 52), new Vector2(-1, 52), new Vector2(-3, -164), Brass);
            Line(new Vector2(8, -160), new Vector2(3, 42), 1.4f, ArtTheme.Rule);
            // Paired cast scrolls and a shell-shaped foot give the silhouette depth.
            for (int side = -1; side <= 1; side += 2)
            {
                Vector2 Point(float x, float y) => new Vector2(side * x, y);
                Line(Point(6, 17), Point(19, -17), 2.3f, Brass);
                Line(Point(19, -17), Point(8, -76), 2.3f, Brass);
                Line(Point(8, -27), Point(19, -17), 1.4f, Metal);
                Line(Point(11, -151), Point(34, -175), 2, ArtTheme.Gold);
                for (int i = 0; i < 5; i++)
                    Line(Point(5 + i * 5, -187), Point(8 + i * 14, -201), 1.3f, Metal);
                for (int i = 0; i < 7; i++)
                {
                    float x = 8 + i * 13;
                    Line(Point(x, -215), Point(x + 6, -207), 1.6f, ArtTheme.Panel);
                    Line(Point(x + 6, -207), Point(x + 12, -215), 1.6f, ArtTheme.Panel);
                }
            }
            for (int i = 0; i < 3; i++)
                Trapezoid(-168 + i * 6, -165 + i * 6, 23 - i * 2, 23 - i * 2, Light);
            Disc(pivot, 21, 21, Metal);
            Jewel(new Vector2(0, -18), 6, 15);
            Jewel(new Vector2(0, -209), 9, 5);
            Line(new Vector2(-108, -218), new Vector2(108, -218), 2f, Light);
        }

        private void BeamHalf(Vector2 pivot, Vector2 end)
        {
            var perp = new Vector2(0, 1);
            // Openwork arches are attached to the beam at both ends as it rocks.
            Vector2 Rib(float t) => Vector2.Lerp(pivot, end, t) - perp * (4 + 21 * Mathf.Sin(t * Mathf.PI));
            for (int i = 0; i < 20; i++)
                Line(Rib(i / 20f), Rib((i + 1) / 20f), 2.5f, Brass);
            for (int i = 1; i <= 4; i++)
            {
                float t = i * .19f;
                Line(Rib(t), Vector2.Lerp(pivot, end, t - .075f), 1.5f, Metal);
                Line(Rib(t), Vector2.Lerp(pivot, end, t + .075f), 1.5f, Metal);
            }
            var neck = Vector2.Lerp(pivot, end, .81f);
            Quad(pivot - perp * 9, pivot + perp * 9, neck + perp * 5, neck - perp * 4, Metal);
            Quad(neck - perp * 4, neck + perp * 5, end + perp * 13, end + perp * 2, Metal);
            Line(pivot + perp * 5, neck + perp * 2, 2f, Light);
            Line(neck + perp * 2, end + perp * 9, 2f, Light);
            Disc(end + perp * 6, 7, 7, Brass);
            Disc(end + perp * 6, 3, 3, ArtTheme.Ink);
        }

        private void Pan(Vector2 hook, float t, bool key)
        {
            float impact = key ? .84f : 2.76f;
            float lift = key ? 5.58f : 6.58f;
            float sway = Recoil(t - impact) * (key ? 8f : -8f);
            var pan = hook + new Vector2(sway, -145f);
            Line(hook, pan + new Vector2(-64, 0), 2.3f, Metal);
            Line(hook, pan + new Vector2(64, 0), 2.3f, Metal);
            Line(hook, pan + new Vector2(0, -4), 1f, Brass);
            // The object falls with acceleration and leaves on a softer upward curve.
            float drop = Mathf.Clamp01((t - impact + .65f) / .65f);
            float rise = Mathf.SmoothStep(0, 1, (t - lift) / .66f);
            float opacity = Mathf.Clamp01(drop * 4) * (1f - rise);
            if (opacity > .001f)
            {
                float air = (1f - drop * drop + rise) * 98f;
                var item = pan + new Vector2(0, air + (key ? 24f : 23f));
                if (key) Key(item, drop, opacity);
                else Gem(item, opacity);
            }
            // A curved brass bowl, with a thin bright rim and a dark underside.
            for (int i = 0; i < 24; i++)
            {
                float x0 = (i - 12) / 12f, x1 = (i - 11) / 12f;
                float h0 = -19f * (1f - x0 * x0), h1 = -19f * (1f - x1 * x1);
                Quad(pan + new Vector2(66*x0, h0), pan + new Vector2(66*x0, 0),
                    pan + new Vector2(66*x1, 0), pan + new Vector2(66*x1, h1), Brass);
                Line(pan + new Vector2(66*x0, h0), pan + new Vector2(66*x1, h1), 2.4f, ArtTheme.Ink);
            }
            Line(pan + Vector2.left * 67, pan + Vector2.right * 67, 4f, ArtTheme.Gold);
            Line(pan + new Vector2(-61, -3), pan + new Vector2(61, -3), 1.6f, Light);
            for (int flute = -4; flute <= 4; flute++)
            {
                float x = flute * 12;
                float depth = 15 * (1 - x * x / (66 * 66));
                Line(pan + new Vector2(x, -5), pan + new Vector2(x * .75f, -depth), 1.5f, ArtTheme.Surface);
            }
            Jewel(pan + new Vector2(0, -11), 4, 5);
        }

        private void Jewel(Vector2 center, float width, float height)
        {
            var top = center + Vector2.up * height;
            var bottom = center + Vector2.down * height;
            Triangle(center + Vector2.left * width, top, bottom, ArtTheme.Gold);
            Triangle(top, center + Vector2.right * width, bottom, Lacquer);
        }

        private void Key(Vector2 center, float drop, float alpha)
        {
            var axis = Polar(-22f + (1f - drop) * 27f);
            var up = new Vector2(-axis.y, axis.x);
            var bow = center - axis * 22;
            var keyBrass = Fade(ArtTheme.Gold, alpha);
            var glint = Fade(Light, alpha);
            Arc(bow, 11, 11, 0, 360, 28, 6f, keyBrass);
            Arc(bow, 11, 11, 20, 220, 16, 2f, glint);
            Line(bow + axis * 10, bow + axis * 49, 6f, keyBrass);
            Line(bow + axis * 13 + up, bow + axis * 46 + up, 1.4f, glint);
            Line(bow + axis * 40, bow + axis * 40 - up * 8, 4f, keyBrass);
            Line(bow + axis * 48, bow + axis * 48 - up * 11, 5f, keyBrass);
        }

        private void Gem(Vector2 c, float alpha)
        {
            var left = c + new Vector2(-26, 5);
            var right = c + new Vector2(26, 5);
            var peak = c + new Vector2(0, 24);
            var foot = c + new Vector2(0, -22);
            var facet = c + new Vector2(-6, 6);
            Triangle(left, c + new Vector2(-15, 22), facet, Fade(Brass, alpha));
            Triangle(c + new Vector2(-15, 22), peak, facet, Fade(Light, alpha));
            Triangle(facet, peak, right, Fade(Ruby, alpha));
            Triangle(left, facet, foot, Fade(Ruby, alpha));
            Triangle(facet, right, foot, Fade(Lacquer, alpha));
        }

        private static Color Fade(Color c, float alpha) { c.a *= alpha; return c; }
        private static Vector2 Polar(float degrees)
        {
            float a = degrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(a), Mathf.Sin(a));
        }

        private void Trapezoid(float y0, float y1, float lower, float upper, Color c) =>
            Quad(new Vector2(-lower,y0), new Vector2(-upper,y1), new Vector2(upper,y1), new Vector2(lower,y0), c);

        private void Disc(Vector2 center, float rx, float ry, Color c)
        {
            int count = rx > 100 ? 96 : 32;
            int first = mesh.currentVertCount;
            mesh.AddVert(center, c, Vector2.zero);
            for (int i = 0; i <= count; i++)
            {
                var p = Polar(i * 360f / count);
                mesh.AddVert(center + new Vector2(rx*p.x, ry*p.y), c, Vector2.zero);
                if (i > 0) mesh.AddTriangle(first, first+i+1, first+i);
            }
        }

        private void Arc(Vector2 center, float rx, float ry, float start, float end, int steps, float width, Color c)
        {
            var scale = new Vector2(rx, ry);
            var previous = center + Vector2.Scale(Polar(start), scale);
            for (int i = 1; i <= steps; i++)
            {
                var next = center + Vector2.Scale(Polar(Mathf.Lerp(start, end, (float)i / steps)), scale);
                Line(previous, next, width, c);
                previous = next;
            }
        }

        private void Line(Vector2 a, Vector2 b, float width, Color c)
        {
            var axis = (b-a).normalized;
            var normal = new Vector2(-axis.y, axis.x) * (.5f * width);
            Quad(a-normal, a+normal, b+normal, b-normal, c);
        }

        private void Triangle(Vector2 a, Vector2 b, Vector2 c, Color color)
        {
            int i = mesh.currentVertCount;
            mesh.AddVert(a, color, Vector2.zero);
            mesh.AddVert(b, color, Vector2.zero);
            mesh.AddVert(c, color, Vector2.zero);
            mesh.AddTriangle(i, i+1, i+2);
        }

        private void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color)
        {
            int i = mesh.currentVertCount;
            mesh.AddVert(a, color, Vector2.zero);
            mesh.AddVert(b, color, Vector2.zero);
            mesh.AddVert(c, color, Vector2.zero);
            mesh.AddVert(d, color, Vector2.zero);
            mesh.AddTriangle(i, i+1, i+2);
            mesh.AddTriangle(i, i+2, i+3);
        }
    }
}
