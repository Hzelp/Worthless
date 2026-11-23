using System;
using System.Collections.Generic;
using System.Text;

namespace Worthless.Lobby
{
    public struct BroadcastFragment
    {
        public string Text;
        public bool Redactable;
        public BroadcastFragment(string text, bool redactable = false) { Text = text; Redactable = redactable; }
    }

    public sealed class BroadcastBatchOptions
    {
        public const int DefaultExactRankPercent = 20;

        public int ItemCount = 4;
        public int FakeMin;
        public int FakeMax = 2;
        public int ZeroLieWeight = 10;
        public int OneLieWeight = 55;
        public int TwoLieWeight = 35;
        public int ExactRankPercent = DefaultExactRankPercent;
    }

    public sealed class BroadcastRedactionOptions
    {
        public int NoRedactionPercent = 2;
        public int DoubleRedactionPercent = 10;
        public int FullRedactionPercent = 1;
    }

    // Owned by the authority for one game/round. No original data is retained here.
    public sealed class BroadcastSeatViews
    {
        private readonly Dictionary<int, string[]> views = new Dictionary<int, string[]>();
        public BroadcastSeatViews(BroadcastRules.BroadcastBatch batch, IEnumerable<int> seats, BroadcastRedactionOptions options, IRandomSource random)
        {
            foreach (var seat in seats)
            {
                var texts = new string[batch.Items.Count];
                for (var i = 0; i < texts.Length; i++) texts[i] = BroadcastRules.Redact(batch.Items[i], options, random);
                views.Add(seat, texts);
            }
        }
        public string GetPublished(int seat, int order, int publishedCount)
        {
            string[] texts;
            return order >= 0 && order < publishedCount && views.TryGetValue(seat, out texts) && order < texts.Length ? texts[order] : null;
        }
    }

    public static partial class BroadcastRules
    {
        public const string RedactionToken = "████";
        public const string FullyRedactedText = "████████████████████";

        private static string PlayerLabel(PublicItemRef item)
        {
            return string.IsNullOrEmpty(item.PlayerName) ? "P" + (item.Seat + 1) : item.PlayerName + "（P" + (item.Seat + 1) + "）";
        }

        public static BroadcastFragment[] BuildFragments(BroadcastWorld world, BroadcastTemplate t)
        {
            PublicItemRef a, b;
            if (!world.TryItem(t.InstanceA, out a)) return null;
            switch (t.Type)
            {
                case BroadcastTemplateType.Interval:
                    return new[] { new BroadcastFragment(PlayerLabel(a), true), new BroadcastFragment("的「" + a.Category + "」类物品，价格区间在 "),
                        new BroadcastFragment(t.Min.ToString(), true), new BroadcastFragment(" 到 "), new BroadcastFragment(t.Max.ToString(), true), new BroadcastFragment(" 内。") };
                case BroadcastTemplateType.Comparison:
                    if (!world.TryItem(t.InstanceB, out b)) return null;
                    return new[] { new BroadcastFragment(PlayerLabel(a), true), new BroadcastFragment("的物品价值" + (t.Operator == 2 ? "大于" : "小于")),
                        new BroadcastFragment(PlayerLabel(b), true), new BroadcastFragment("的物品价值。") };
                case BroadcastTemplateType.ExactRank:
                    return new[] { new BroadcastFragment(PlayerLabel(a), true), new BroadcastFragment("目前是第 "),
                        new BroadcastFragment(t.MaxRank.ToString(), true), new BroadcastFragment(" 名。") };
                default: return null;
            }
        }

        public static string Redact(BroadcastItem item, BroadcastRedactionOptions options, IRandomSource random)
        {
            if (item == null) return FullyRedactedText;
            var fragments = item.Fragments;
            if (fragments == null) return item.Text;
            var slots = new List<int>();
            for (var i = 0; i < fragments.Length; i++) if (fragments[i].Redactable) slots.Add(i);
            if (slots.Count == 0) return item.Text;
            var roll = random.Next(100);
            var none = Math.Max(0, Math.Min(100, options.NoRedactionPercent));
            var full = Math.Max(0, Math.Min(100 - none, options.FullRedactionPercent));
            if (roll >= none && roll < none + full) return FullyRedactedText;
            var count = roll < none ? 0 : (slots.Count > 2 && roll < none + full + options.DoubleRedactionPercent ? 2 : 1);
            var hidden = new HashSet<int>();
            while (count-- > 0 && slots.Count > 0)
            {
                var index = random.Next(slots.Count);
                hidden.Add(slots[index]);
                slots.RemoveAt(index);
            }
            var text = new StringBuilder();
            for (var i = 0; i < fragments.Length; i++) text.Append(hidden.Contains(i) ? RedactionToken : fragments[i].Text);
            return text.ToString();
        }

        // Network strings remain plain text; only the UI adds a fixed color tag.
        public static string FormatForDisplay(string text)
        {
            var escaped = (text ?? "").Replace("<", "＜").Replace(">", "＞");
            return System.Text.RegularExpressions.Regex.Replace(escaped, "█+", "<color=#020509>$0</color>");
        }

        public static int DrawLieCount(BroadcastBatchOptions options, int wanted, IRandomSource random)
        {
            var lo = Math.Max(0, Math.Min(wanted, options.FakeMin));
            var hi = Math.Max(lo, Math.Min(wanted, options.FakeMax));
            var weights = new[] { Math.Max(0, options.ZeroLieWeight), Math.Max(0, options.OneLieWeight), Math.Max(0, options.TwoLieWeight) };
            var total = 0;
            for (var i = lo; i <= hi && i < weights.Length; i++) total += weights[i];
            if (total <= 0) return lo + random.Next(hi - lo + 1);
            var roll = random.Next(total);
            for (var i = lo; i <= hi && i < weights.Length; i++) { roll -= weights[i]; if (roll < 0) return i; }
            return lo;
        }

        public static BroadcastBatch BuildRedactedBatch(BroadcastWorld world, BroadcastBatchOptions options, IRandomSource random, MarketRumorPlan rumor = default)
        {
            var batch = new BroadcastBatch();
            if (world == null || world.Items.Count == 0) return batch;
            var pool = new List<BroadcastItem>();
            foreach (var a in world.Items)
            {
                AddPriceIntervals(world, pool, a, random);
                for (var rank = 1; rank <= world.Items.Count; rank++)
                    AddRedacted(world, pool, new BroadcastTemplate { Type = BroadcastTemplateType.ExactRank, InstanceA = a.InstanceId, MaxRank = rank }, "rank:" + a.InstanceId);
            }
            for (var i = 0; i < world.Items.Count; i++)
                for (var j = i + 1; j < world.Items.Count; j++)
                    for (var op = 2; op <= 3; op++)
                        AddRedacted(world, pool, new BroadcastTemplate { Type = BroadcastTemplateType.Comparison, InstanceA = world.Items[i].InstanceId, InstanceB = world.Items[j].InstanceId, Operator = op }, "cmp:" + i + ":" + j);
            var categories = new Dictionary<string, int>();
            foreach (var a in world.Items) if (!string.IsNullOrEmpty(a.Category))
            { categories.TryGetValue(a.Category, out var count); categories[a.Category] = count + 1; }
            var band = (world.Items.Count + 2) / 3;
            foreach (var category in categories)
                if (category.Value <= world.Items.Count - band)
                    for (var op = 0; op <= 1; op++)
                        Add(world, pool, new BroadcastTemplate { Type = BroadcastTemplateType.GroupBand, Category = category.Key, MaxRank = band, Operator = op }, "group:" + category.Key + ":" + op);
            var wanted = Math.Max(1, Math.Min(6, options.ItemCount));
            var fake = DrawLieCount(options, wanted, random);
            if (rumor.Has && !string.IsNullOrEmpty(rumor.Category))
            {
                world.MarketDirections[rumor.Category] = rumor.Direction;
                var makeFalse = fake > 0 && (fake == wanted || random.Next(2) == 0);
                Add(world, batch.Items, new BroadcastTemplate { Type = BroadcastTemplateType.MarketRumor, Category = rumor.Category, Direction = makeFalse ? -rumor.Direction : rumor.Direction }, "rumor:planned");
            }
            var usedThemes = new HashSet<string>();
            var usedTypes = new HashSet<BroadcastTemplateType>();
            var selectedFake = 0;
            foreach (var item in batch.Items) if (!item.Truth) selectedFake++;
            var allowExactRank = random.Next(100) < options.ExactRankPercent;
            // Fill false quota first, then truths; both share theme keys to avoid contradictory duplicate subjects.
            TakeDiverse(pool, false, fake - selectedFake, usedThemes, usedTypes, random, batch.Items, ref allowExactRank);
            TakeDiverse(pool, true, wanted - batch.Items.Count, usedThemes, usedTypes, random, batch.Items, ref allowExactRank);
            for (var i = batch.Items.Count - 1; i > 0; i--)
            { var j = random.Next(i + 1); var tmp = batch.Items[i]; batch.Items[i] = batch.Items[j]; batch.Items[j] = tmp; }
            for (var i = 0; i < batch.Items.Count; i++) { batch.Items[i].Order = i; if (!batch.Items[i].Truth) batch.FakeCount++; }
            return batch;
        }

        private static void AddPriceIntervals(BroadcastWorld world, List<BroadcastItem> pool, PublicItemRef item, IRandomSource random)
        {
            // Work in magnitudes so negative prices use exactly the mirrored rules.
            var value = Math.Abs(item.Value);
            var floorPrice = value / 10 * 10;
            var ceilingPrice = floorPrice + (value % 10 == 0 ? 0 : 10);
            var lowerLimit = (value / 20 + (value % 20 == 0 ? 0 : 1)) * 10;
            var upperLimit = (value + value / 2) / 10 * 10;

            // If the ten-unit grid has no endpoint on that side, round outward just
            // enough to contain the price. For example, 7 uses the band [0, 10].
            if (lowerLimit > floorPrice) lowerLimit = floorPrice;
            if (upperLimit < ceilingPrice) upperLimit = ceilingPrice;

            var min = DrawIntervalEndpoint(lowerLimit, floorPrice, random);
            var max = DrawIntervalEndpoint(ceilingPrice, upperLimit, random);
            AddSignedInterval(world, pool, item, min, max);

            var belowPrice = floorPrice - (value % 10 == 0 ? 10 : 0);
            var abovePrice = ceilingPrice + (value % 10 == 0 ? 10 : 0);
            var hasBelow = belowPrice >= lowerLimit;
            var hasAbove = abovePrice <= upperLimit;
            if (!hasBelow && !hasAbove) return;

            var useBelow = hasBelow && (!hasAbove || random.Next(2) == 0);
            var sideMin = useBelow ? lowerLimit : abovePrice;
            var sideMax = useBelow ? belowPrice : upperLimit;
            var first = DrawIntervalEndpoint(sideMin, sideMax, random);
            var second = DrawIntervalEndpoint(sideMin, sideMax, random);
            AddSignedInterval(world, pool, item, Math.Min(first, second), Math.Max(first, second));
        }

        private static long DrawIntervalEndpoint(long min, long max, IRandomSource random)
        {
            var choices = (max - min) / 10 + 1;
            if (choices <= int.MaxValue) return min + 10L * random.Next((int)choices);

            // Validated economy limits can exceed an int's endpoint count.
            // Rejection sampling keeps every ten-unit endpoint equally likely.
            const int partRange = 1 << 30;
            const long sampleRange = 1L << 60;
            var limit = sampleRange - sampleRange % choices;
            long sample;
            do { sample = (long)random.Next(partRange) * partRange + random.Next(partRange); }
            while (sample >= limit);
            return min + 10L * (sample % choices);
        }

        private static void AddSignedInterval(BroadcastWorld world, List<BroadcastItem> pool, PublicItemRef item, long min, long max)
        {
            var template = new BroadcastTemplate
            {
                Type = BroadcastTemplateType.Interval,
                InstanceA = item.InstanceId,
                Min = item.Value < 0 ? -max : min,
                Max = item.Value < 0 ? -min : max
            };
            AddRedacted(world, pool, template, "interval:" + item.InstanceId);
        }

        private static void AddRedacted(BroadcastWorld world, List<BroadcastItem> pool, BroadcastTemplate template, string theme)
        {
            bool truth; string error;
            if (!TryEvaluate(world, template, out truth, out error)) return;
            var parts = BuildFragments(world, template);
            var text = new StringBuilder();
            foreach (var part in parts) text.Append(part.Text);
            pool.Add(new BroadcastItem { Template = template, Truth = truth, Text = text.ToString(), ThemeKey = theme, Fragments = parts });
        }

        private static void TakeDiverse(List<BroadcastItem> pool, bool truth, int count, HashSet<string> themes, HashSet<BroadcastTemplateType> types, IRandomSource random, List<BroadcastItem> target, ref bool allowExactRank)
        {
            while (count-- > 0)
            {
                var families = new List<BroadcastTemplateType>();
                foreach (var item in pool)
                    if (item.Truth == truth && !themes.Contains(item.ThemeKey) && !families.Contains(item.Template.Type)) families.Add(item.Template.Type);
                if (families.Count == 0) return;
                // Preserve the truth quota if exact ranks are the only candidates left.
                if (!allowExactRank && families.Count > 1) families.Remove(BroadcastTemplateType.ExactRank);
                var unused = families.FindAll(type => !types.Contains(type));
                if (unused.Count > 0) families = unused;
                var family = families[random.Next(families.Count)];
                var candidates = pool.FindAll(item => item.Truth == truth && item.Template.Type == family && !themes.Contains(item.ThemeKey));
                var chosen = candidates[random.Next(candidates.Count)];
                if (family == BroadcastTemplateType.ExactRank) allowExactRank = false;
                themes.Add(chosen.ThemeKey); types.Add(family); target.Add(chosen);
            }
        }
    }
}
