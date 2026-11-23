using NUnit.Framework;
using Worthless;
using Worthless.Lobby;

namespace Worthless.Tests
{
    public class BroadcastVagueTests
    {
        private static BroadcastWorld World()
        {
            var w = new BroadcastWorld();
            w.Items.Add(new PublicItemRef { Seat = 0, InstanceId = 1, Name = "链轮", Category = "机械", Value = 80 });
            w.Items.Add(new PublicItemRef { Seat = 1, InstanceId = 2, Name = "陶罐", Category = "手作", Value = 55 });
            w.Items.Add(new PublicItemRef { Seat = 2, InstanceId = 3, Name = "晶簇", Category = "晶矿", Value = 30 });
            return w;
        }

        [Test]
        public void GroupBand_IsVagueAndEvaluable()
        {
            var w = World();
            bool truth; string error;
            var t = new BroadcastTemplate { Type = BroadcastTemplateType.GroupBand, Category = "机械", MaxRank = 1, Operator = 0 };
            Assert.IsTrue(BroadcastRules.TryEvaluate(w, t, out truth, out error), error);
            Assert.IsTrue(truth);
            string text;
            Assert.IsTrue(BroadcastRules.TryFormat(w, t, out text));
            Assert.AreEqual("场上至少有一件「机械」类物品的估价全场最高。", text);
            t.Operator = 1;
            Assert.IsTrue(BroadcastRules.TryEvaluate(w, t, out truth, out error), error);
            Assert.IsFalse(truth);
            Assert.IsTrue(BroadcastRules.TryFormat(w, t, out text));
            Assert.AreEqual("场上至少有一件「机械」类物品的估价全场最低。", text);
        }

        [Test]
        public void MarketRumor_TrueAndFalseAndNoFutureNumbers()
        {
            var w = World();
            w.MarketDirections["手作"] = 1;
            bool truth; string error;
            var live = new BroadcastTemplate { Type = BroadcastTemplateType.MarketRumor, Category = "手作", Direction = 1 };
            var fake = new BroadcastTemplate { Type = BroadcastTemplateType.MarketRumor, Category = "手作", Direction = -1 };
            Assert.IsTrue(BroadcastRules.TryEvaluate(w, live, out truth, out error), error);
            Assert.IsTrue(truth);
            Assert.IsTrue(BroadcastRules.TryEvaluate(w, fake, out truth, out error), error);
            Assert.IsFalse(truth);
            string text;
            Assert.IsTrue(BroadcastRules.TryFormat(w, live, out text));
            Assert.IsTrue(text.Contains("偏暖"));
            Assert.IsFalse(text.Contains("%"));
            Assert.IsFalse(text.Contains("事件"));
        }

        [Test]
        public void VagueBatch_MixesTruthAndKeepsNoExactPrice()
        {
            var w = World();
            var plan = new BroadcastRules.MarketRumorPlan { Has = true, Category = "手作", Direction = -1 };
            var batch = BroadcastRules.BuildVagueBatch(w, 4, 1, 1, new SystemRandomSource(7), plan);
            Assert.Greater(batch.Items.Count, 0);
            var rumors = 0;
            var actualFake = 0;
            var joined = "";
            foreach (var item in batch.Items)
            {
                joined += item.Text + "|";
                Assert.IsFalse(item.Text.Contains("["));
                if (item.Template.Type == BroadcastTemplateType.MarketRumor) rumors++;
                if (item.Template.Type == BroadcastTemplateType.GroupBand)
                    Assert.IsFalse(string.IsNullOrEmpty(item.Template.Category));
                if (!item.Truth) actualFake++;
            }
            Assert.LessOrEqual(rumors, 1);
            Assert.AreEqual(actualFake, batch.FakeCount);
            Assert.IsFalse(joined.Contains("价值至少"));
            Assert.IsFalse(joined.Contains("排名不低于"));
            Assert.IsFalse(joined.Contains("这批物品"));
        }

        [Test]
        public void VagueBatch_SkipsCategoryCluesGuaranteedByTheVisibleLineup()
        {
            var world = World();
            for (var i = 0; i < world.Items.Count; i++)
            {
                var item = world.Items[i];
                item.Category = "机械";
                world.Items[i] = item;
            }

            var batch = BroadcastRules.BuildVagueBatch(world, 4, 0, 2, new SystemRandomSource(3));
            Assert.IsEmpty(batch.Items);
        }

        [Test]
        public void MarketRumorGate_ZeroOneFinalAndAtMostOne()
        {
            var cats = new[] { "机械", "手作" };
            var bps = new[] { -200, 300 };
            BroadcastRules.MarketRumorPlan plan;
            Assert.IsFalse(BroadcastRules.TryBuildMarketRumor(cats, bps, false, 0f, new SystemRandomSource(1), out plan));
            Assert.IsTrue(BroadcastRules.TryBuildMarketRumor(cats, bps, false, 1f, new SystemRandomSource(1), out plan));
            Assert.IsTrue(plan.Has);
            Assert.IsFalse(BroadcastRules.TryBuildMarketRumor(cats, bps, true, 1f, new SystemRandomSource(1), out plan));
            var batch = BroadcastRules.BuildVagueBatch(World(), 4, 1, 1, new SystemRandomSource(3), new BroadcastRules.MarketRumorPlan { Has = true, Category = "手作", Direction = 1 });
            var count = 0;
            foreach (var item in batch.Items) if (item.Template.Type == BroadcastTemplateType.MarketRumor) count++;
            Assert.AreEqual(1, count);
        }

    }
}
