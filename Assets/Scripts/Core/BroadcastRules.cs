using System;
using System.Collections.Generic;

namespace Worthless.Lobby
{
    public enum BroadcastTemplateType { Absolute = 0, Interval = 1, Category = 2, Rank = 3, Comparison = 4, History = 5, GroupBand = 6, MarketRumor = 7, ExactRank = 8 }

    public struct PublicItemRef
    {
        public int Seat;
        public int InstanceId;
        public string Name;
        public string PlayerName;
        public string Category;
        public long Value; // server-only world; never serialized
    }

    public struct PublicEliminationRef
    {
        public int Round;
        public int Seat;
        public int InstanceId;
        public string Name;
        public long PublicValue;
    }

    public sealed class BroadcastWorld
    {
        public readonly List<PublicItemRef> Items = new List<PublicItemRef>();
        public readonly List<PublicEliminationRef> History = new List<PublicEliminationRef>();
        public readonly Dictionary<string,int> MarketDirections = new Dictionary<string,int>();

        public bool TryItem(int instanceId, out PublicItemRef item)
        {
            for (var i = 0; i < Items.Count; i++) if (Items[i].InstanceId == instanceId) { item = Items[i]; return true; }
            item = default;
            return false;
        }

        public int RankOf(int instanceId)
        {
            PublicItemRef self;
            if (!TryItem(instanceId, out self)) return -1;
            var greater = 0;
            for (var i = 0; i < Items.Count; i++) if (Items[i].Value > self.Value) greater++;
            return 1 + greater;
        }

        public bool TryHistory(int round, int seat, int instanceId, out PublicEliminationRef record)
        {
            for (var i = 0; i < History.Count; i++)
            {
                if (History[i].Round == round && History[i].Seat == seat && History[i].InstanceId == instanceId)
                { record = History[i]; return true; }
            }
            record = default;
            return false;
        }
    }

    public struct BroadcastTemplate
    {
        public BroadcastTemplateType Type;
        public int InstanceA;
        public int InstanceB;
        public long Threshold;
        public long Min;
        public long Max;
        public string Category;
        public int MaxRank;
        public int HistoryRound;
        public int HistorySeat;
        public int HistoryInstance;
        public int Operator; // 0 >=, 1 <=, 2 >, 3 <, 4 ==
        public int Direction;
    }

    public static partial class BroadcastRules
    {
        public const int ItemIntervalSeconds = 3;
        public const int LastHoldSeconds = 3;
        public const int LieRevealSeconds = 5;

        public static int BatchDurationSeconds(int itemCount)
        {
            if (itemCount <= 0) return 0;
            return (itemCount - 1) * ItemIntervalSeconds + LastHoldSeconds;
        }

        public static bool TryEvaluate(BroadcastWorld world, BroadcastTemplate t, out bool truth, out string error)
        {
            truth = false;
            error = null;
            if (world == null) { error = "world"; return false; }
            PublicItemRef a;
            switch (t.Type)
            {
                case BroadcastTemplateType.Absolute:
                    if (!world.TryItem(t.InstanceA, out a)) { error = "instance"; return false; }
                    if (t.Operator < 0 || t.Operator > 4) { error = "operator"; return false; }
                    truth = Compare(a.Value, t.Threshold, t.Operator);
                    return true;
                case BroadcastTemplateType.Interval:
                    if (!world.TryItem(t.InstanceA, out a)) { error = "instance"; return false; }
                    if (t.Min > t.Max) { error = "interval"; return false; }
                    truth = a.Value >= t.Min && a.Value <= t.Max;
                    return true;
                case BroadcastTemplateType.Category:
                    if (!world.TryItem(t.InstanceA, out a)) { error = "instance"; return false; }
                    if (string.IsNullOrEmpty(t.Category)) { error = "category"; return false; }
                    truth = a.Category == t.Category;
                    return true;
                case BroadcastTemplateType.Rank:
                    var rank = world.RankOf(t.InstanceA);
                    if (rank < 0) { error = "instance"; return false; }
                    if (t.MaxRank < 1 || t.MaxRank >= world.Items.Count) { error = "rank"; return false; }
                    truth = rank <= t.MaxRank;
                    return true;
                case BroadcastTemplateType.ExactRank:
                    var exactRank = world.RankOf(t.InstanceA);
                    if (exactRank < 0) { error = "instance"; return false; }
                    if (t.MaxRank < 1 || t.MaxRank > world.Items.Count) { error = "rank"; return false; }
                    truth = exactRank == t.MaxRank;
                    return true;
                case BroadcastTemplateType.Comparison:
                    PublicItemRef b;
                    if (!world.TryItem(t.InstanceA, out a) || !world.TryItem(t.InstanceB, out b)) { error = "instance"; return false; }
                    if (t.InstanceA == t.InstanceB) { error = "same"; return false; }
                    if (t.Operator < 0 || t.Operator > 4) { error = "operator"; return false; }
                    truth = Compare(a.Value, b.Value, t.Operator);
                    return true;
                case BroadcastTemplateType.History:
                    PublicEliminationRef h;
                    if (!world.TryHistory(t.HistoryRound, t.HistorySeat, t.HistoryInstance, out h)) { error = "history"; return false; }
                    if (t.Operator < 0 || t.Operator > 4) { error = "operator"; return false; }
                    truth = Compare(h.PublicValue, t.Threshold, t.Operator);
                    return true;
                case BroadcastTemplateType.GroupBand:
                    if (string.IsNullOrEmpty(t.Category)) { error = "category"; return false; }
                    if (t.Operator != 0 && t.Operator != 1) { error = "operator"; return false; }
                    if (t.MaxRank < 1 || t.MaxRank >= world.Items.Count) { error = "rank"; return false; }
                    foreach (var item in world.Items)
                    {
                        if (item.Category != t.Category) continue;
                        var position = 1;
                        foreach (var other in world.Items)
                        {
                            if (t.Operator == 0 ? other.Value > item.Value : other.Value < item.Value) position++;
                        }
                        if (position > t.MaxRank) continue;
                        truth = true;
                        break;
                    }
                    return true;
                case BroadcastTemplateType.MarketRumor:
                    if (string.IsNullOrEmpty(t.Category)) { error = "category"; return false; }
                    if (t.Direction != 1 && t.Direction != -1) { error = "direction"; return false; }
                    int dir;
                    if (!world.MarketDirections.TryGetValue(t.Category, out dir)) { error = "market"; return false; }
                    truth = dir == t.Direction;
                    return true;
                default:
                    error = "type";
                    return false;
            }
        }

        private static bool Compare(long a, long b, int op)
        {
            switch (op)
            {
                case 0: return a >= b;
                case 1: return a <= b;
                case 2: return a > b;
                case 3: return a < b;
                case 4: return a == b;
                default: return false;
            }
        }

        public static bool TryFormat(BroadcastWorld world, BroadcastTemplate t, out string text)
        {
            text = null;
            PublicItemRef a;
            PublicItemRef b;
            switch (t.Type)
            {
                case BroadcastTemplateType.Absolute:
                    if (!world.TryItem(t.InstanceA, out a)) return false;
                    text = "P" + (a.Seat + 1) + " 的「" + a.Name + "」价值" + OpText(t.Operator) + " " + t.Threshold;
                    return true;
                case BroadcastTemplateType.Interval:
                    if (!world.TryItem(t.InstanceA, out a)) return false;
                    text = "P" + (a.Seat + 1) + " 的「" + a.Name + "」价值落在 [" + t.Min + ", " + t.Max + "]";
                    return true;
                case BroadcastTemplateType.Category:
                    if (!world.TryItem(t.InstanceA, out a)) return false;
                    text = "P" + (a.Seat + 1) + " 的「" + a.Name + "」属于「" + t.Category + "」类别";
                    return true;
                case BroadcastTemplateType.Rank:
                    if (!world.TryItem(t.InstanceA, out a) || t.MaxRank < 1) return false;
                    text = "P" + (a.Seat + 1) + " 的「" + a.Name + "」价值排名不低于第 " + t.MaxRank + " 名";
                    return true;
                case BroadcastTemplateType.ExactRank:
                    if (!world.TryItem(t.InstanceA, out a)) return false;
                    text = PlayerLabel(a) + "目前是第 " + t.MaxRank + " 名。";
                    return true;
                case BroadcastTemplateType.Comparison:
                    if (!world.TryItem(t.InstanceA, out a) || !world.TryItem(t.InstanceB, out b)) return false;
                    text = "P" + (a.Seat + 1) + " 的「" + a.Name + "」" + OpText(t.Operator) + " P" + (b.Seat + 1) + " 的「" + b.Name + "」价值";
                    return true;
                case BroadcastTemplateType.GroupBand:
                    if (string.IsNullOrEmpty(t.Category) || t.MaxRank < 1 || t.Operator < 0 || t.Operator > 1) return false;
                    var band = t.MaxRank == 1
                        ? (t.Operator == 0 ? "全场最高" : "全场最低")
                        : "排在全场" + (t.Operator == 0 ? "前 " : "倒数 ") + t.MaxRank + " 名";
                    text = "场上至少有一件「" + t.Category + "」类物品的估价" + band + "。";
                    return true;
                case BroadcastTemplateType.MarketRumor:
                    if (string.IsNullOrEmpty(t.Category) || (t.Direction != 1 && t.Direction != -1)) return false;
                    text = "有风声说，下一次市场变化中「" + t.Category + "」行情可能" + (t.Direction > 0 ? "偏暖" : "偏冷");
                    return true;
                case BroadcastTemplateType.History:
                    PublicEliminationRef h;
                    if (!world.TryHistory(t.HistoryRound, t.HistorySeat, t.HistoryInstance, out h)) return false;
                    text = "第 " + h.Round + " 轮淘汰的 P" + (h.Seat + 1) + "「" + h.Name + "」公开结算价" + OpText(t.Operator) + " " + t.Threshold;
                    return true;
            }
            return false;
        }

        private static string OpText(int op)
        {
            switch (op)
            {
                case 0: return "至少";
                case 1: return "至多";
                case 2: return "高于";
                case 3: return "低于";
                case 4: return "等于";
                default: return "?";
            }
        }

        public sealed class BroadcastItem
        {
            public int Order;
            public string Text;
            public bool Truth; // server-only
            public BroadcastTemplate Template;
            public string ThemeKey;
            public BroadcastFragment[] Fragments; // server-only original parts
        }

        public struct MarketRumorPlan
        {
            public bool Has;
            public string Category;
            public int Direction;
        }

        public static bool TryBuildMarketRumor(string[] categories, int[] basisPoints, bool finalRound, float chance, IRandomSource random, out MarketRumorPlan plan)
        {
            plan = default;
            if (finalRound || chance <= 0f || categories == null || basisPoints == null) return false;
            var p = chance < 0f ? 0f : (chance > 1f ? 1f : chance);
            if (random.Next(10000) >= (int)(p * 10000f)) return false;
            var eligible = new List<int>();
            for (var i = 0; i < categories.Length && i < basisPoints.Length; i++)
                if (!string.IsNullOrEmpty(categories[i]) && basisPoints[i] != 0) eligible.Add(i);
            if (eligible.Count == 0) return false;
            var pick = eligible[random.Next(eligible.Count)];
            plan.Has = true;
            plan.Category = categories[pick];
            plan.Direction = basisPoints[pick] > 0 ? 1 : -1;
            return true;
        }

        public sealed class BroadcastBatch
        {
            public readonly List<BroadcastItem> Items = new List<BroadcastItem>();
            public int FakeCount;
        }

        public static BroadcastBatch BuildBatch(BroadcastWorld world, int wantedItems, int fakeMin, int fakeMax, IRandomSource random, bool useAbsolute = false, bool useCategory = false)
        {
            var pool = new List<BroadcastItem>();
            if (world == null || world.Items.Count == 0) return new BroadcastBatch();
            var thresholds = new long[] { 20, 30, 40, 50, 60, 70, 80, 90, 100, 120 };
            foreach (var item in world.Items)
            {
                if (useAbsolute)
                    Add(world, pool, new BroadcastTemplate { Type = BroadcastTemplateType.Absolute, InstanceA = item.InstanceId, Threshold = thresholds[random.Next(thresholds.Length)], Operator = random.Next(4) }, "abs:" + item.InstanceId);
                var t = thresholds[random.Next(thresholds.Length)];
                Add(world, pool, new BroadcastTemplate { Type = BroadcastTemplateType.Interval, InstanceA = item.InstanceId, Min = Math.Max(0, t - 10), Max = t + 10 }, "interval:" + item.InstanceId);
                if (useCategory)
                    Add(world, pool, new BroadcastTemplate { Type = BroadcastTemplateType.Category, InstanceA = item.InstanceId, Category = item.Category }, "cat:" + item.InstanceId);
                for (var k = 1; k < world.Items.Count; k++)
                    Add(world, pool, new BroadcastTemplate { Type = BroadcastTemplateType.Rank, InstanceA = item.InstanceId, MaxRank = k }, "rank:" + item.InstanceId);
            }
            for (var i = 0; i < world.Items.Count; i++)
            {
                for (var j = i + 1; j < world.Items.Count; j++)
                {
                    Add(world, pool, new BroadcastTemplate { Type = BroadcastTemplateType.Comparison, InstanceA = world.Items[i].InstanceId, InstanceB = world.Items[j].InstanceId, Operator = 2 + random.Next(2) }, "cmp:" + i + ":" + j);
                }
            }
            foreach (var h in world.History)
            {
                Add(world, pool, new BroadcastTemplate { Type = BroadcastTemplateType.History, HistoryRound = h.Round, HistorySeat = h.Seat, HistoryInstance = h.InstanceId, Threshold = h.PublicValue, Operator = 4 }, "hist:" + h.Round + ":" + h.Seat + ":" + h.InstanceId);
            }

            if (pool.Count == 0) return new BroadcastBatch();
            var truePool = new List<BroadcastItem>();
            var falsePool = new List<BroadcastItem>();
            var usedText = new HashSet<string>();
            foreach (var item in pool)
            {
                if (item.Text == null || !usedText.Add(item.Text)) continue;
                if (item.Truth) truePool.Add(item); else falsePool.Add(item);
            }
            var wanted = Math.Min(wantedItems, truePool.Count + falsePool.Count);
            var lo = Math.Max(fakeMin, 0);
            var hi = Math.Max(fakeMax, lo);
            var targetFake = lo + random.Next(hi - lo + 1);
            targetFake = Math.Min(targetFake, Math.Min(falsePool.Count, wanted));
            var targetTrue = Math.Min(truePool.Count, wanted - targetFake);
            var usedThemes = new HashSet<string>();
            var batch = new BroadcastBatch();
            TakeUnique(falsePool, usedThemes, targetFake, random, batch.Items);
            TakeUnique(truePool, usedThemes, targetTrue, random, batch.Items);
            // 权威随机洗牌：真假顺序不得由总数/生成顺序推导。
            for (var i = batch.Items.Count - 1; i > 0; i--)
            {
                var j = random.Next(i + 1);
                var tmp = batch.Items[i]; batch.Items[i] = batch.Items[j]; batch.Items[j] = tmp;
            }
            for (var i = 0; i < batch.Items.Count; i++) batch.Items[i].Order = i;
            batch.FakeCount = targetFake;
            return batch;
        }

        private static void Add(BroadcastWorld world, List<BroadcastItem> pool, BroadcastTemplate t, string themeKey)
        {
            bool truth;
            string error;
            if (!TryEvaluate(world, t, out truth, out error)) return;
            string text;
            if (!TryFormat(world, t, out text)) return;
            pool.Add(new BroadcastItem { Text = text, Truth = truth, Template = t, ThemeKey = themeKey });
        }


        public static BroadcastBatch BuildVagueBatch(BroadcastWorld world, int wantedItems, int fakeMin, int fakeMax, IRandomSource random, MarketRumorPlan rumor = default)
        {
            var pool = new List<BroadcastItem>();
            if (world == null || world.Items.Count == 0) return new BroadcastBatch();
            var categories = new Dictionary<string, int>();
            foreach (var item in world.Items)
            {
                if (string.IsNullOrEmpty(item.Category)) continue;
                categories.TryGetValue(item.Category, out var count);
                categories[item.Category] = count + 1;
            }
            var rankBand = (world.Items.Count + 2) / 3;
            foreach (var category in categories)
            {
                if (category.Value > world.Items.Count - rankBand) continue;
                Add(world, pool, new BroadcastTemplate { Type = BroadcastTemplateType.GroupBand, Category = category.Key, MaxRank = rankBand, Operator = 0 }, "group:" + category.Key + ":high");
                Add(world, pool, new BroadcastTemplate { Type = BroadcastTemplateType.GroupBand, Category = category.Key, MaxRank = rankBand, Operator = 1 }, "group:" + category.Key + ":low");
            }
            if (pool.Count == 0) return new BroadcastBatch();
            var truePool = new List<BroadcastItem>();
            var falsePool = new List<BroadcastItem>();
            var usedText = new HashSet<string>();
            foreach (var item in pool)
            {
                if (item.Text == null || !usedText.Add(item.Text)) continue;
                if (item.Truth) truePool.Add(item); else falsePool.Add(item);
            }
            var wanted = Math.Min(wantedItems, truePool.Count + falsePool.Count);
            var lo = Math.Max(fakeMin, 0);
            var hi = Math.Max(fakeMax, lo);
            var targetFake = lo + random.Next(hi - lo + 1);
            targetFake = Math.Min(targetFake, Math.Min(falsePool.Count, wanted));
            var targetTrue = Math.Min(truePool.Count, wanted - targetFake);
            var usedThemes = new HashSet<string>();
            var batch = new BroadcastBatch();
            BroadcastItem plannedRumor = null;
            if (rumor.Has && !string.IsNullOrEmpty(rumor.Category) && wanted > 0 && (targetFake > 0 || targetTrue > 0))
            {
                world.MarketDirections[rumor.Category] = rumor.Direction;
                var makeFalse = targetFake > 0 && (targetTrue <= 0 || random.Next(2) == 0);
                var rumorPool = new List<BroadcastItem>();
                Add(world, rumorPool, new BroadcastTemplate { Type = BroadcastTemplateType.MarketRumor, Category = rumor.Category, Direction = makeFalse ? -rumor.Direction : rumor.Direction }, "rumor:planned");
                if (rumorPool.Count > 0)
                {
                    plannedRumor = rumorPool[0];
                    wanted--;
                    if (plannedRumor.Truth) targetTrue--; else targetFake--;
                }
            }
            targetFake = Math.Max(0, Math.Min(targetFake, wanted));
            targetTrue = Math.Max(0, Math.Min(targetTrue, wanted - targetFake));
            TakeUnique(falsePool, usedThemes, targetFake, random, batch.Items);
            TakeUnique(truePool, usedThemes, targetTrue, random, batch.Items);
            if (plannedRumor != null) batch.Items.Add(plannedRumor);
            for (var i = batch.Items.Count - 1; i > 0; i--)
            {
                var j = random.Next(i + 1);
                var tmp = batch.Items[i]; batch.Items[i] = batch.Items[j]; batch.Items[j] = tmp;
            }
            for (var i = 0; i < batch.Items.Count; i++) batch.Items[i].Order = i;
            batch.FakeCount = 0;
            foreach (var item in batch.Items) if (!item.Truth) batch.FakeCount++;
            return batch;
        }

        private static void TakeUnique(List<BroadcastItem> source, HashSet<string> usedThemes, int count,
            IRandomSource random, List<BroadcastItem> target)
        {
            for (var guard = 0; guard < 200 && count > 0 && source.Count > 0; guard++)
            {
                var idx = random.Next(source.Count);
                var item = source[idx];
                source.RemoveAt(idx);
                if (item.ThemeKey != null && !usedThemes.Add(item.ThemeKey)) continue;
                target.Add(item);
                count--;
            }
        }
    }
}
