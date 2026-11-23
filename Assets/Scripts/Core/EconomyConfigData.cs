using System;
using System.Collections.Generic;

namespace Worthless.Lobby
{
    [Serializable]
    public struct MarketEventDefinition
    {
        public string Id;
        public string DisplayName;
        public string[] Categories;
        public int[] BasisPoints;

        public MarketEventDefinition(string id, string displayName, string[] categories, int[] basisPoints)
        {
            Id = id;
            DisplayName = displayName;
            Categories = categories;
            BasisPoints = basisPoints;
        }

        public int BasisPointsFor(string category)
        {
            if (Categories == null || BasisPoints == null) return 0;
            for (var i = 0; i < Categories.Length && i < BasisPoints.Length; i++)
            {
                if (Categories[i] == category) return BasisPoints[i];
            }

            return 0;
        }
    }

    [Serializable]
    public sealed class EconomyConfigData
    {
        private const int MaxMarketBasisPoints = 9000;
        public const int BasisPointsPerPercent = 100;

        public long MinValue = -100000;
        public long MaxValue = 100000;
        public int InitialMin = 30;
        public int InitialMax = 90;
        public int SwingVarianceBasisPoints = 2500;
        public MarketEventDefinition[] Events = DefaultEvents();

        public static MarketEventDefinition[] DefaultEvents()
        {
            var mech = new[] { "机械", "手作", "晶矿" };
            return new[]
            {
                new MarketEventDefinition("E01", "机械热销", mech, new[] { 4200, -2600, 500 }),
                new MarketEventDefinition("E02", "手作坊市集反转", mech, new[] { -4200, 4800, 0 }),
                new MarketEventDefinition("E03", "晶矿走强", mech, new[] { -1800, -3000, 5500 }),
                new MarketEventDefinition("E04", "供应链震荡", mech, new[] { 2400, 1200, -3800 })
            };
        }

        public static int Utf8Length(string text)
        {
            return string.IsNullOrEmpty(text) ? 0 : System.Text.Encoding.UTF8.GetByteCount(text);
        }

        public MarketEventDefinition RollMarketEvent(int eventIndex, IRandomSource random)
        {
            var template = Events[eventIndex];
            var rates = new int[template.BasisPoints.Length];
            for (var i = 0; i < rates.Length; i++)
            {
                var baseline = template.BasisPoints[i];
                var min = Math.Max(-MaxMarketBasisPoints, baseline - SwingVarianceBasisPoints);
                var max = Math.Min(MaxMarketBasisPoints, baseline + SwingVarianceBasisPoints);
                var minPercent = (int)Math.Ceiling((double)min / BasisPointsPerPercent);
                var maxPercent = (int)Math.Floor((double)max / BasisPointsPerPercent);
                // Narrow legacy settings can leave no whole percentage inside the interval.
                var percent = minPercent > maxPercent
                    ? (int)Math.Round((double)baseline / BasisPointsPerPercent, MidpointRounding.AwayFromZero)
                    : minPercent + (minPercent == maxPercent ? 0 : random.Next(maxPercent - minPercent + 1));
                rates[i] = percent * BasisPointsPerPercent;
            }

            return new MarketEventDefinition(template.Id, template.DisplayName, template.Categories, rates);
        }

        public bool Validate(out string error)
        {
            error = null;
            if (MaxValue <= MinValue) { error = "价值上下限非法"; return false; }
            var maxSafe = (long.MaxValue - 5000L) / 19000L;
            if (MaxValue > maxSafe || MinValue < -maxSafe) { error = "价值范围过大，可能溢出"; return false; }
            if (InitialMin < 1 || InitialMax < InitialMin) { error = "初始价值范围非法"; return false; }
            if (InitialMin < MinValue || InitialMax > MaxValue) { error = "初始价值超出上下限"; return false; }
            if (SwingVarianceBasisPoints < 0 || SwingVarianceBasisPoints > MaxMarketBasisPoints) { error = "市场随机幅度超出 0～90 个百分点"; return false; }
            if (Events == null || Events.Length == 0) { error = "市场事件为空"; return false; }
            var ids = new HashSet<string>();
            foreach (var e in Events)
            {
                if (string.IsNullOrEmpty(e.Id) || string.IsNullOrEmpty(e.DisplayName)) { error = "市场事件缺少 ID/名称"; return false; }
                if (Utf8Length(e.Id) > 8 || Utf8Length(e.DisplayName) > 40) { error = "市场事件 ID/名称过长：" + e.Id; return false; }
                if (!ids.Add(e.Id)) { error = "市场事件 ID 重复：" + e.Id; return false; }
                if (e.Categories == null || e.BasisPoints == null || e.Categories.Length == 0 || e.Categories.Length != e.BasisPoints.Length)
                {
                    error = "市场事件类别/倍率配置不完整：" + e.Id;
                    return false;
                }

                var seen = new HashSet<string>();
                for (var i = 0; i < e.Categories.Length; i++)
                {
                    if (string.IsNullOrEmpty(e.Categories[i])) { error = "市场事件类别为空：" + e.Id; return false; }
                    if (!seen.Add(e.Categories[i])) { error = "市场事件类别重复：" + e.Id + "/" + e.Categories[i]; return false; }
                    if (Utf8Length(e.Categories[i]) > 16) { error = "市场事件类别过长：" + e.Id; return false; }
                    if (e.BasisPoints[i] > MaxMarketBasisPoints || e.BasisPoints[i] < -MaxMarketBasisPoints) { error = "市场事件倍率超出 ±90%：" + e.Id; return false; }
                }
            }

            return true;
        }

        /// <summary>与物品目录交叉校验：每个事件必须恰好覆盖目录中的全部类别（可零倍率）。</summary>
        public bool ValidateForCatalog(ItemCatalogData catalog, out string error)
        {
            if (!Validate(out error)) return false;
            if (catalog == null || catalog.Definitions == null) { error = "物品目录为空"; return false; }
            var known = new HashSet<string>();
            foreach (var def in catalog.Definitions)
            {
                if (Utf8Length(def.DisplayName) > 40) { error = "物品名称过长，可能超出公告容量"; return false; }
                if (Utf8Length(def.Category) > 16) { error = "物品类别过长，可能超出公告容量"; return false; }
                known.Add(def.Category);
            }
            foreach (var e in Events)
            {
                var worst = 24 + Utf8Length(e.DisplayName);
                foreach (var category in e.Categories) worst += Utf8Length(category) + 11;
                if (worst > 470) { error = "市场公告可能超长：" + e.Id; return false; }
            }
            foreach (var e in Events)
            {
                var seen = new HashSet<string>();
                foreach (var category in e.Categories)
                {
                    if (!known.Contains(category)) { error = "市场事件类别不在物品目录：" + e.Id + "/" + category; return false; }
                    seen.Add(category);
                }

                if (seen.Count != known.Count) { error = "市场事件未覆盖全部类别：" + e.Id; return false; }
            }

            return true;
        }
    }

    public static class EconomyRules
    {
        public static int PickMarketEvent(int eventCount, int previousEvent, IRandomSource random)
        {
            if (eventCount <= 1) return 0;
            if (previousEvent < 0 || previousEvent >= eventCount) return random.Next(eventCount);
            var pick = random.Next(eventCount - 1);
            return pick >= previousEvent ? pick + 1 : pick;
        }

        public static long ApplyBasisPoints(long value, int basisPoints, long minValue, long maxValue)
        {
            var bound = (long.MaxValue - 5000L) / 19000L;
            if (value > bound) value = bound;
            if (value < -bound) value = -bound;
            var numerator = value * (10000L + basisPoints);
            var result = (numerator + (numerator < 0 ? -5000L : 5000L)) / 10000L;
            if (result < minValue) result = minValue;
            if (result > maxValue) result = maxValue;
            return result;
        }

        public static long ApplyDelta(long value, int delta, long minValue, long maxValue)
        {
            // Validated economy limits leave room for the entire batch of seat deltas.
            var result = value + delta;
            return Math.Max(minValue, Math.Min(maxValue, result));
        }

        public static int Utf8Length(string text)
        {
            return string.IsNullOrEmpty(text) ? 0 : System.Text.Encoding.UTF8.GetByteCount(text);
        }

        public static long InitialValue(int seed, int instanceId, int minValue, int maxValue)
        {
            var span = maxValue - minValue + 1;
            var mixed = unchecked((uint)(seed * 73856093) ^ (uint)(instanceId * 19349663));
            mixed ^= mixed >> 13;
            mixed *= 0x5bd1e995u;
            mixed ^= mixed >> 15;
            return minValue + (int)(mixed % (uint)span);
        }
    }
}
