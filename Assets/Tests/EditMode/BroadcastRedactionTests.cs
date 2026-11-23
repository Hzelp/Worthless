using NUnit.Framework;
using System.Linq;
using Worthless.Lobby;

namespace Worthless.Tests
{
    public class BroadcastRedactionTests
    {
        private static BroadcastWorld World()
        {
            var world = new BroadcastWorld();
            world.Items.Add(new PublicItemRef { Seat = 0, InstanceId = 1, PlayerName = "小甲", Name = "链轮", Category = "机械", Value = 80 });
            world.Items.Add(new PublicItemRef { Seat = 1, InstanceId = 2, Name = "陶罐", Category = "手作", Value = 80 });
            world.Items.Add(new PublicItemRef { Seat = 2, InstanceId = 3, Name = "晶簇", Category = "晶矿", Value = 30 });
            return world;
        }

        [TestCase(1, 1, true)]
        [TestCase(2, 2, false)]
        [TestCase(3, 3, true)]
        public void ExactRank_UsesCompetitionRank(int instance, int rank, bool expected)
        {
            var template = new BroadcastTemplate
            {
                Type = (BroadcastTemplateType)8,
                InstanceA = instance,
                MaxRank = rank
            };
            bool truth;
            string error;
            Assert.IsTrue(BroadcastRules.TryEvaluate(World(), template, out truth, out error), error);
            Assert.AreEqual(expected, truth);
        }

        private sealed class FixedRandom : IRandomSource
        {
            private readonly int[] values;
            private int cursor;
            public FixedRandom(params int[] values) { this.values = values; }
            public int Next(int maxExclusive) { return values[cursor++ % values.Length] % maxExclusive; }
        }

        private static BroadcastRules.BroadcastItem Interval()
        {
            var parts = BroadcastRules.BuildFragments(World(), new BroadcastTemplate { Type = BroadcastTemplateType.Interval, InstanceA = 1, Min = 63, Max = 97 });
            return new BroadcastRules.BroadcastItem { Fragments = parts, Text = string.Concat(parts.Select(p => p.Text)), Truth = true };
        }

        [TestCase(0, 0)] // 2% no masking
        [TestCase(3, 2)] // 10% two slots for three-slot clues
        [TestCase(13, 1)]
        [TestCase(99, 1)]
        public void Interval_RedactionPreservesTrueCategory(int roll, int hidden)
        {
            var text = BroadcastRules.Redact(Interval(), new BroadcastRedactionOptions(), new FixedRandom(roll, 0, 0));
            StringAssert.Contains("机械", text);
            StringAssert.DoesNotContain("链轮", text);
            Assert.AreEqual(hidden, text.Split(new[] { BroadcastRules.RedactionToken }, System.StringSplitOptions.None).Length - 1);
            if (hidden > 0) StringAssert.DoesNotContain("小甲", text);
        }

        [Test]
        public void AllRedacted_ReplacesEntireSentenceWithoutCategory()
        {
            var text = BroadcastRules.Redact(Interval(), new BroadcastRedactionOptions(), new FixedRandom(2));
            Assert.AreEqual(BroadcastRules.FullyRedactedText, text);
            StringAssert.DoesNotContain("机械", text);
        }

        [Test]
        public void AllRedacted_IsFixedVisualMaskIndependentOfContentAndTruth()
        {
            var other = new BroadcastRules.BroadcastItem
            {
                Text = "长短不同的另一条秘密情报",
                Truth = false,
                Fragments = new[] { new BroadcastFragment("秘密内容", true) }
            };
            foreach (var item in new[] { Interval(), other })
            {
                var text = BroadcastRules.Redact(item, new BroadcastRedactionOptions(), new FixedRandom(2));
                Assert.AreEqual(new string('█', 20), text);
            }
        }

        [Test]
        public void AllRedacted_DisplayUsesOneContinuousBlackBar()
        {
            var text = BroadcastRules.Redact(Interval(), new BroadcastRedactionOptions(), new FixedRandom(2));
            Assert.AreEqual("<color=#020509>" + new string('█', 20) + "</color>", BroadcastRules.FormatForDisplay(text));
        }

        [TestCase(3)]
        [TestCase(12)]
        [TestCase(99)]
        public void TwoSlotClue_NeverPartiallyHidesBoth(int roll)
        {
            var parts = BroadcastRules.BuildFragments(World(), new BroadcastTemplate { Type = BroadcastTemplateType.ExactRank, InstanceA = 1, MaxRank = 1 });
            var text = BroadcastRules.Redact(new BroadcastRules.BroadcastItem { Fragments = parts }, new BroadcastRedactionOptions(), new FixedRandom(roll, 0));
            Assert.AreEqual(1, text.Split(new[] { BroadcastRules.RedactionToken }, System.StringSplitOptions.None).Length - 1);
        }

        [Test]
        public void SeatViews_CachePerSeatAndGateUnpublishedContent()
        {
            var batch = new BroadcastRules.BroadcastBatch(); batch.Items.Add(Interval()); batch.Items.Add(Interval());
            var views = new BroadcastSeatViews(batch, new[] { 0, 1 }, new BroadcastRedactionOptions(), new FixedRandom(99, 0, 99, 0, 99, 1, 99, 1));
            Assert.IsNull(views.GetPublished(0, 1, 1));
            Assert.IsNull(views.GetPublished(2, 0, 1));
            var first = views.GetPublished(0, 0, 1);
            Assert.AreEqual(first, views.GetPublished(0, 0, 2)); // review/reconnect
            Assert.AreNotEqual(first, views.GetPublished(1, 0, 1));
            StringAssert.DoesNotContain("小甲", first);
            StringAssert.DoesNotContain("63", views.GetPublished(1, 0, 1));
        }

        [TestCase(0, 0)] [TestCase(9, 0)] [TestCase(10, 1)] [TestCase(64, 1)] [TestCase(65, 2)] [TestCase(99, 2)]
        public void WeightedLieDraw_Boundaries(int roll, int expected)
        {
            Assert.AreEqual(expected, BroadcastRules.DrawLieCount(new BroadcastBatchOptions(), 4, new FixedRandom(roll)));
        }

        [TestCase(false)] [TestCase(true)]
        public void Generator_FillsSixEvenWhenCategoriesOrValuesAllMatch(bool sameValues)
        {
            var types = new System.Collections.Generic.HashSet<BroadcastTemplateType>();
            var counts = new int[3];
            for (var seed = 0; seed < 300; seed++)
            {
                var world = World();
                for (var i = 0; i < world.Items.Count; i++)
                { var a = world.Items[i]; a.Category = "机械"; if (sameValues) a.Value = 80; world.Items[i] = a; }
                var random = new SystemRandomSource(seed);
                var batch = BroadcastRules.BuildRedactedBatch(world, new BroadcastBatchOptions { ItemCount = 6 }, random);
                Assert.AreEqual(6, batch.Items.Count, "seed=" + seed);
                Assert.AreEqual(batch.Items.Count(i => !i.Truth), batch.FakeCount);
                Assert.AreEqual(6, batch.Items.Select(i => i.ThemeKey).Distinct().Count());
                counts[batch.FakeCount]++;
                foreach (var clue in batch.Items)
                {
                    bool truth; string error;
                    Assert.IsTrue(BroadcastRules.TryEvaluate(world, clue.Template, out truth, out error), error);
                    Assert.AreEqual(truth, clue.Truth);
                    types.Add(clue.Template.Type);
                }
            }
            Assert.That(counts[0], Is.LessThan(counts[1]));
            Assert.That(counts[0], Is.LessThan(counts[2]));
            CollectionAssert.IsSubsetOf(new[] { BroadcastTemplateType.Interval, BroadcastTemplateType.Comparison, BroadcastTemplateType.ExactRank }, types);
        }

        [Test]
        public void Generator_DefaultMakesExactRankAnOccasionalSingleClue()
        {
            var rankBatches = 0;
            const int samples = 1000;
            for (var seed = 0; seed < samples; seed++)
            {
                var world = World();
                world.Items.Add(new PublicItemRef { Seat = 3, InstanceId = 4, Category = "手作", Value = 53 });
                world.Items.Add(new PublicItemRef { Seat = 4, InstanceId = 5, Category = "机械", Value = 97 });
                var batch = BroadcastRules.BuildRedactedBatch(world, new BroadcastBatchOptions(), new SystemRandomSource(seed));
                var ranks = batch.Items.Count(clue => clue.Template.Type == BroadcastTemplateType.ExactRank);
                Assert.LessOrEqual(ranks, 1);
                Assert.AreEqual(4, batch.Items.Count);
                Assert.AreEqual(batch.FakeCount, batch.Items.Count(clue => !clue.Truth));
                rankBatches += ranks;
            }
            Assert.That(rankBatches, Is.InRange(samples / 20, samples / 4));
        }

        [Test]
        public void Rumor_ExactlyOneAndIncludedInTruthCount()
        {
            for (var seed = 0; seed < 80; seed++)
            {
                var batch = BroadcastRules.BuildRedactedBatch(World(), new BroadcastBatchOptions(), new SystemRandomSource(seed), new BroadcastRules.MarketRumorPlan { Has = true, Category = "机械", Direction = 1 });
                Assert.AreEqual(4, batch.Items.Count);
                Assert.AreEqual(1, batch.Items.Count(i => i.Template.Type == BroadcastTemplateType.MarketRumor));
                Assert.AreEqual(batch.Items.Count(i => !i.Truth), batch.FakeCount);
            }
        }

        [Test]
        public void Display_EscapesPlayerMarkupAndUsesFixedMask()
        {
            Assert.AreEqual("＜size=200＞甲＜/size＞<color=#020509>████</color>", BroadcastRules.FormatForDisplay("<size=200>甲</size>████"));
        }

        [Test]
        public void IntervalGeneration_TruthNotDistinguishableBySpan()
        {
            var trueSpans = new System.Collections.Generic.HashSet<long>();
            var falseSpans = new System.Collections.Generic.HashSet<long>();
            var nonCentered = false;
            for (var seed = 0; seed < 100; seed++)
                foreach (var fakeCount in new[] { 0, 2 })
                {
                    var world = World();
                    var batch = BroadcastRules.BuildRedactedBatch(world, new BroadcastBatchOptions { ItemCount = 6, FakeMin = fakeCount, FakeMax = fakeCount }, new SystemRandomSource(seed));
                    foreach (var clue in batch.Items.Where(i => i.Template.Type == BroadcastTemplateType.Interval))
                    {
                        var t = clue.Template;
                        (clue.Truth ? trueSpans : falseSpans).Add(t.Max - t.Min);
                        PublicItemRef a; world.TryItem(t.InstanceA, out a);
                        if (clue.Truth && t.Min + t.Max != 2 * a.Value) nonCentered = true;
                    }
                }
            Assert.IsTrue(trueSpans.Overlaps(falseSpans), "真假区间跨度不应完全分离");
            Assert.IsTrue(nonCentered, "真实价值不应总是区间中点");
        }

        [TestCase(0, 0, 0)]
        [TestCase(1, 0, 10)]
        [TestCase(7, 0, 10)]
        [TestCase(10, 10, 10)]
        [TestCase(11, 10, 20)]
        [TestCase(19, 10, 20)]
        [TestCase(20, 10, 30)]
        [TestCase(80, 40, 120)]
        [TestCase(83, 50, 120)]
        [TestCase(300, 150, 450)]
        [TestCase(100000, 50000, 150000)]
        [TestCase(-7, -10, 0)]
        [TestCase(-80, -120, -40)]
        [TestCase(-100000, -150000, -50000)]
        [TestCase(100000000000000L, 50000000000000L, 150000000000000L)]
        [TestCase(-100000000000000L, -150000000000000L, -50000000000000L)]
        public void IntervalEndpointsStayInsideHalfPriceBand(long value, long allowedMin, long allowedMax)
        {
            var found = 0;
            for (var seed = 0; seed < 100; seed++)
            {
                var world = World();
                for (var i = 0; i < world.Items.Count; i++)
                {
                    var item = world.Items[i];
                    item.Value = value;
                    world.Items[i] = item;
                }
                var batch = BroadcastRules.BuildRedactedBatch(world,
                    new BroadcastBatchOptions { ItemCount = 6 }, new SystemRandomSource(seed));
                foreach (var clue in batch.Items.Where(item => item.Template.Type == BroadcastTemplateType.Interval))
                {
                    found++;
                    var interval = clue.Template;
                    Assert.GreaterOrEqual(interval.Min, allowedMin);
                    Assert.LessOrEqual(interval.Max, allowedMax);
                    Assert.AreEqual(0, interval.Min % 10);
                    Assert.AreEqual(0, interval.Max % 10);
                    Assert.LessOrEqual(interval.Min, interval.Max);
                    Assert.AreEqual(clue.Truth, interval.Min <= value && value <= interval.Max);
                }
            }
            Assert.Greater(found, 0);
        }

        [Test]
        public void NegativePriceIntervalsMirrorPositiveIntervals()
        {
            var found = 0;
            for (var seed = 0; seed < 50; seed++)
            {
                var positive = World();
                var negative = World();
                for (var i = 0; i < positive.Items.Count; i++)
                {
                    var item = positive.Items[i];
                    item.Value = 83;
                    positive.Items[i] = item;
                    item.Value = -83;
                    negative.Items[i] = item;
                }
                var options = new BroadcastBatchOptions { ItemCount = 6 };
                var positiveIntervals = BroadcastRules.BuildRedactedBatch(positive, options, new SystemRandomSource(seed))
                    .Items.Where(item => item.Template.Type == BroadcastTemplateType.Interval).ToArray();
                var negativeIntervals = BroadcastRules.BuildRedactedBatch(negative, options, new SystemRandomSource(seed))
                    .Items.Where(item => item.Template.Type == BroadcastTemplateType.Interval).ToArray();
                Assert.AreEqual(positiveIntervals.Length, negativeIntervals.Length);
                for (var i = 0; i < positiveIntervals.Length; i++)
                {
                    found++;
                    Assert.AreEqual(-positiveIntervals[i].Template.Max, negativeIntervals[i].Template.Min);
                    Assert.AreEqual(-positiveIntervals[i].Template.Min, negativeIntervals[i].Template.Max);
                    Assert.AreEqual(positiveIntervals[i].Truth, negativeIntervals[i].Truth);
                }
            }
            Assert.Greater(found, 0);
        }
    }
}
