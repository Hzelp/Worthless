using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Worthless
{
    /// <summary>Decorates already-masked glyphs. Hidden words never enter this component.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Text))]
    public sealed class RedactionGlitchEffect : BaseMeshEffect
    {
        private struct MaskRun { public Rect Bounds; public UIVertex Sample; }
        private readonly List<MaskRun> runs = new List<MaskRun>(32);
        private readonly UIVertex[] glyph = new UIVertex[4];
        private readonly UIVertex[] pixel = new UIVertex[4];
        private BroadcastStage settings;
        private Text label;
        private int lastTick = -1;
        private bool wasVisible, burstActive;
        private float nextBurstAt, burstStartedAt, burstUntil;
        private uint seed, sequence, burstSeed;
        private Font metricFont;
        private int metricSize;
        private FontStyle metricStyle;
        private float heightRatio = .9f, bottomRatio;

        protected override void OnDisable()
        {
            wasVisible = false;
            burstActive = false;
            lastTick = -1;
            base.OnDisable();
        }

        public static void Attach(Text text, BroadcastStage stage)
        {
            var effect = text.GetComponent<RedactionGlitchEffect>();
            if (effect == null) effect = text.gameObject.AddComponent<RedactionGlitchEffect>();
            effect.settings = stage;
            effect.label = text;
            effect.seed = unchecked((uint)effect.GetInstanceID() * 2654435761U);
            effect.PrepareMetrics();
            text.SetVerticesDirty();
        }

        private void PrepareMetrics()
        {
            if (label == null || label.font == null) return;
            var size = label.resizeTextForBestFit ? label.cachedTextGenerator.fontSizeUsedForBestFit : label.fontSize;
            if (size <= 0) size = label.fontSize;
            if (metricFont == label.font && metricSize == size && metricStyle == label.fontStyle) return;
            metricFont = label.font; metricSize = size; metricStyle = label.fontStyle;
            // Request outside ModifyMesh: rebuilding a font atlas during a mesh rebuild is unsafe.
            metricFont.RequestCharactersInTexture("国M█", size, metricStyle);
            CharacterInfo normal, mask;
            var reference = metricFont.HasCharacter('国') ? '国' : 'M';
            if (metricFont.GetCharacterInfo(reference, out normal, size, metricStyle) &&
                metricFont.GetCharacterInfo('█', out mask, size, metricStyle) && mask.maxY > mask.minY)
            {
                var height = (float)(mask.maxY - mask.minY);
                heightRatio = (normal.maxY - normal.minY) / height;
                bottomRatio = (normal.minY - mask.minY) / height;
            }
        }

        private void Update()
        {
            if (label != null && label.isActiveAndEnabled) PrepareMetrics();
            var visible = label != null && label.isActiveAndEnabled && settings != null &&
                settings.RedactionGlitchIntensity > 0 && label.text.IndexOf('█') >= 0;
            if (!visible)
            {
                wasVisible = false;
                if (burstActive) { burstActive = false; if (label != null) label.SetVerticesDirty(); }
                return;
            }
            var now = Time.unscaledTime;
            if (!wasVisible) { wasVisible = true; ScheduleNext(now); }
            if (!burstActive && now >= nextBurstAt)
            {
                burstSeed = unchecked(seed + ++sequence * 2246822519U);
                burstStartedAt = now;
                var min = Mathf.Max(.05f, settings.RedactionGlitchMinDuration);
                burstUntil = now + Mathf.Lerp(min, Mathf.Max(min, settings.RedactionGlitchMaxDuration), Noise(burstSeed));
                burstActive = true;
                lastTick = -1;
            }
            if (!burstActive) return;
            if (now >= burstUntil)
            {
                burstActive = false;
                ScheduleNext(now);
                label.SetVerticesDirty();
                return;
            }
            var tick = Mathf.FloorToInt((now - burstStartedAt) * Mathf.Clamp(settings.RedactionGlitchRate, 4, 24));
            if (tick != lastTick) { lastTick = tick; label.SetVerticesDirty(); }
        }

        private void ScheduleNext(float now)
        {
            var min = Mathf.Max(.5f, settings.RedactionGlitchMinInterval);
            var max = Mathf.Max(min, settings.RedactionGlitchMaxInterval);
            nextBurstAt = now + Mathf.Lerp(min, max, Noise(unchecked(seed + ++sequence * 3266489917U)));
        }

        public override void ModifyMesh(VertexHelper mesh)
        {
            if (!IsActive() || settings == null) return;
            runs.Clear();
            var originalCount = mesh.currentVertCount;
            var open = false;
            var run = new MaskRun();
            for (var i = 0; i + 3 < originalCount; i += 4)
            {
                for (var j = 0; j < 4; j++) mesh.PopulateUIVertex(ref glyph[j], i + j);
                var color = glyph[0].color;
                // FormatForDisplay marks mask glyphs with this exact color.
                if (color.r != 2 || color.g != 5 || color.b != 9 || color.a == 0)
                {
                    if (open) { runs.Add(run); open = false; }
                    continue;
                }
                var bounds = Rect.MinMaxRect(glyph[0].position.x, glyph[0].position.y,
                    glyph[0].position.x, glyph[0].position.y);
                for (var j = 1; j < 4; j++)
                {
                    bounds.xMin = Mathf.Min(bounds.xMin, glyph[j].position.x);
                    bounds.xMax = Mathf.Max(bounds.xMax, glyph[j].position.x);
                    bounds.yMin = Mathf.Min(bounds.yMin, glyph[j].position.y);
                    bounds.yMax = Mathf.Max(bounds.yMax, glyph[j].position.y);
                }
                if (bounds.width <= 0 || bounds.height <= 0) continue;
                var rawHeight = bounds.height;
                bounds.y = bounds.yMin + rawHeight * (bottomRatio + settings.RedactionMaskVerticalOffset);
                bounds.height = rawHeight * heightRatio;
                // The font's full-block ascender is taller than the Chinese glyphs. Replace
                // only these mask quads; readable glyph geometry is never shifted or recolored.
                for (var j = 0; j < 4; j++)
                {
                    var hidden = glyph[j]; hidden.color.a = 0;
                    mesh.SetUIVertex(hidden, i + j);
                }
                if (open && (Mathf.Abs(bounds.center.y - run.Bounds.center.y) > bounds.height * .25f ||
                    bounds.xMin - run.Bounds.xMax > bounds.height * .4f))
                { runs.Add(run); open = false; }
                if (!open)
                {
                    var sample = glyph[0];
                    sample.uv0 = (glyph[0].uv0 + glyph[2].uv0) * .5f;
                    run = new MaskRun { Bounds = bounds, Sample = sample };
                    open = true;
                }
                else run.Bounds = Rect.MinMaxRect(run.Bounds.xMin, Mathf.Min(run.Bounds.yMin, bounds.yMin),
                    bounds.xMax, Mathf.Max(run.Bounds.yMax, bounds.yMax));
            }
            if (open) runs.Add(run);
            if (runs.Count == 0) return;
            var tick = Mathf.FloorToInt((Time.unscaledTime - burstStartedAt) * Mathf.Clamp(settings.RedactionGlitchRate, 4, 24));
            var selected = (int)(burstSeed % (uint)runs.Count);
            for (var i = 0; i < runs.Count; i++)
                if (burstActive && settings.RedactionGlitchIntensity > 0 && i == selected)
                    DrawRun(mesh, runs[i], tick, i);
                else DrawPixel(mesh, runs[i].Sample, runs[i].Bounds, new Color32(2, 5, 9, 255));
        }

        private void DrawRun(VertexHelper mesh, MaskRun run, int tick, int index)
        {
            var r = run.Bounds;
            var unit = Mathf.Max(1, Mathf.Round(r.height / 8));
            var strength = Mathf.Clamp01(settings.RedactionGlitchIntensity);
            var localSeed = unchecked(burstSeed + (uint)(index * 179 + tick * 613));
            var shade = settings.RedactionGlitchColor.grayscale;
            var opacity = settings.RedactionGlitchColor.a * strength;
            // Replace the whole silhouette with offset bands, rather than sprinkling
            // tiny particles on an unchanged bar. Gaps show only the screen background:
            // the source text contains mask blocks, never the hidden sentence.
            const int slices = 4;
            var bandHeight = r.height / slices;
            var displacement = Mathf.Min(r.width * .28f, r.height * settings.RedactionSliceDisplacement) * strength;
            var bodyJitter = (tick % 2 == 0 ? -1 : 1) * r.height * .18f * strength;
            for (var p = 0; p < slices; p++)
            {
                var n = unchecked(localSeed + (uint)(p * 97));
                var shift = (p % 2 == 0 ? -1 : 1) * displacement * (.5f + .5f * Noise(n + 1));
                var trim = r.width * .1f * Noise(n + 2);
                var x = r.xMin + bodyJitter + shift + (p % 2 == 0 ? trim : 0);
                var y = r.yMin + p * bandHeight;
                var band = new Rect(Snap(x, unit), y, r.width - trim,
                    bandHeight - Mathf.Min(unit * .5f, bandHeight * .2f));
                // A brief neutral-gray signal lift makes the actual displaced body visible,
                // rather than leaving all motion hidden against the dark screen background.
                var luma = (byte)(40 + 32 * Noise(n + 6));
                DrawPixel(mesh, run.Sample, band, new Color32(luma, luma, luma, 255));
                if (p < Mathf.Clamp(settings.RedactionSpikeCount, 0, slices) && settings.RedactionSpikeLength > 0)
                {
                    var length = Mathf.Max(unit, Snap(r.height * settings.RedactionSpikeLength *
                        (.65f + .6f * Noise(n + 7)), unit));
                    var thin = Mathf.Max(1, Mathf.Round(unit * .4f));
                    var left = shift < 0;
                    var sx = left ? band.xMin - length : band.xMax;
                    var sy = Snap(band.center.y, thin);
                    var spikeGray = shade * (.55f + .25f * Noise(n + 8));
                    var spikeColor = new Color(spikeGray, spikeGray, spikeGray, opacity * .9f);
                    DrawPixel(mesh, run.Sample, new Rect(sx, sy, length, thin), spikeColor);
                    // One stepped tooth keeps the edge digital, with just a few fine burrs.
                    DrawPixel(mesh, run.Sample, new Rect(left ? sx : sx + length - thin * 2,
                        sy + (p % 2 == 0 ? thin : -thin), thin * 2, thin), spikeColor);
                }
                if (p % 2 == 0)
                {
                    var gray = shade * (.7f + .3f * Noise(n + 3));
                    var patchWidth = Mathf.Max(unit * 3, r.width * (.1f + .22f * Noise(n + 4)));
                    DrawPixel(mesh, run.Sample, new Rect(x + (r.width - patchWidth) * Noise(n + 5),
                        y, patchWidth, bandHeight * .65f), new Color(gray, gray, gray, opacity));
                }
            }
            var count = Mathf.Clamp(settings.RedactionParticleCount, 0, 12);
            for (var p = 0; p < count; p++)
            {
                var particleSeed = unchecked(localSeed + (uint)(p * 67));
                var life = Mathf.Clamp01((Time.unscaledTime - burstStartedAt) / Mathf.Max(.01f, burstUntil - burstStartedAt));
                var scale = r.height * Mathf.Clamp(settings.RedactionShardSize, .25f, 3f);
                var dominant = p < 4;
                var width = scale * (dominant ? .8f + Noise(particleSeed + 1) : .3f + .6f * Noise(particleSeed + 1));
                var height = scale * (dominant ? .4f + .5f * Noise(particleSeed + 2) : .15f + .3f * Noise(particleSeed + 2));
                width = Mathf.Max(unit * 2, Snap(width, unit));
                height = Mathf.Max(unit * 2, Snap(height, unit));
                var distance = r.height * Mathf.Clamp(settings.RedactionShardReach, 0, 3) *
                    (.15f + .65f * life) * (.5f + .5f * Noise(particleSeed + 3));
                var x = r.xMin + Noise(particleSeed + 4) * r.width;
                var y = r.yMin + Noise(particleSeed + 5) * r.height;
                switch ((p + (int)(burstSeed % 4)) % 4)
                {
                    case 0: x = r.xMin - distance; break;
                    case 1: x = r.xMax + distance; break;
                    case 2: y = r.yMax + distance; break;
                    default: y = r.yMin - distance; break;
                }
                var bounds = new Rect(Snap(x - width * .5f, unit), Snap(y - height * .5f, unit), width, height);
                var gray = shade * (.65f + .35f * Noise(particleSeed + 6));
                DrawPixel(mesh, run.Sample, bounds, new Color(gray, gray, gray, opacity * (1 - life * .45f)));
                // Large broken slabs, rather than a cloud of tiny dots.
                if (dominant)
                    DrawPixel(mesh, run.Sample, new Rect(bounds.xMin + unit, bounds.yMin,
                        Mathf.Max(unit, Snap(width * .4f, unit)), Mathf.Max(unit, Snap(height * .4f, unit))),
                        new Color32(2, 5, 9, 255));
            }
        }

        private void DrawPixel(VertexHelper mesh, UIVertex sample, Rect rect, Color color)
        {
            if (color.a <= 0) return;
            for (var i = 0; i < 4; i++) { pixel[i] = sample; pixel[i].color = color; }
            pixel[0].position = new Vector3(rect.xMin, rect.yMax, sample.position.z);
            pixel[1].position = new Vector3(rect.xMax, rect.yMax, sample.position.z);
            pixel[2].position = new Vector3(rect.xMax, rect.yMin, sample.position.z);
            pixel[3].position = new Vector3(rect.xMin, rect.yMin, sample.position.z);
            mesh.AddUIVertexQuad(pixel);
        }

        private static float Snap(float value, float unit) => Mathf.Round(value / unit) * unit;
        private static float Noise(uint seed)
        {
            // Local visual noise must not consume the gameplay random sequence.
            seed ^= seed >> 16; seed = unchecked(seed * 0x7feb352dU);
            seed ^= seed >> 15; seed = unchecked(seed * 0x846ca68bU);
            seed ^= seed >> 16;
            return (seed & 0x00ffffff) / 16777216f;
        }
    }
}
