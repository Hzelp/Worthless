using NUnit.Framework;
using Worthless.Lobby;
using System.Collections.Generic;

namespace Worthless.Tests
{
    public class BroadcastRulesTests
    {
        private static BroadcastWorld World()
        {
            var w = new BroadcastWorld();
            w.Items.Add(new PublicItemRef { Seat = 0, InstanceId = 1, Name = "A", Category = "机械", Value = 60 });
            w.Items.Add(new PublicItemRef { Seat = 1, InstanceId = 2, Name = "B", Category = "手作", Value = 60 });
            w.Items.Add(new PublicItemRef { Seat = 2, InstanceId = 3, Name = "C", Category = "晶矿", Value = 50 });
            w.History.Add(new PublicEliminationRef { Round = 1, Seat = 4, InstanceId = 9, Name = "旧物", PublicValue = 30 });
            return w;
        }

        private static bool Eval(BroadcastWorld w, BroadcastTemplate t) { bool v; string e; Assert.IsTrue(BroadcastRules.TryEvaluate(w, t, out v, out e), e); return v; }

        [Test]
        public void Absolute_EndpointsAndOperators()
        {
            var w = World();
            Assert.IsTrue(Eval(w, new BroadcastTemplate { Type = BroadcastTemplateType.Absolute, InstanceA = 1, Threshold = 60, Operator = 0 }));
            Assert.IsTrue(Eval(w, new BroadcastTemplate { Type = BroadcastTemplateType.Absolute, InstanceA = 1, Threshold = 60, Operator = 1 }));
            Assert.IsFalse(Eval(w, new BroadcastTemplate { Type = BroadcastTemplateType.Absolute, InstanceA = 1, Threshold = 60, Operator = 2 }));
        }

        [Test]
        public void Interval_InclusiveEndpoints()
        {
            var w = World();
            Assert.IsTrue(Eval(w, new BroadcastTemplate { Type = BroadcastTemplateType.Interval, InstanceA = 1, Min = 60, Max = 60 }));
            Assert.IsTrue(Eval(w, new BroadcastTemplate { Type = BroadcastTemplateType.Interval, InstanceA = 3, Min = 40, Max = 50 }));
            Assert.IsFalse(Eval(w, new BroadcastTemplate { Type = BroadcastTemplateType.Interval, InstanceA = 3, Min = 51, Max = 60 }));
        }

        [Test]
        public void Category_AndRankTie()
        {
            var w = World();
            Assert.IsTrue(Eval(w, new BroadcastTemplate { Type = BroadcastTemplateType.Category, InstanceA = 1, Category = "机械" }));
            Assert.IsFalse(Eval(w, new BroadcastTemplate { Type = BroadcastTemplateType.Category, InstanceA = 1, Category = "晶矿" }));
            Assert.AreEqual(1, w.RankOf(1));
            Assert.AreEqual(1, w.RankOf(2));
            Assert.AreEqual(3, w.RankOf(3));
            Assert.IsTrue(Eval(w, new BroadcastTemplate { Type = BroadcastTemplateType.Rank, InstanceA = 1, MaxRank = 1 }));
            Assert.IsFalse(Eval(w, new BroadcastTemplate { Type = BroadcastTemplateType.Rank, InstanceA = 3, MaxRank = 2 }));
        }

        [Test]
        public void Comparison_AndSameInstanceRejected()
        {
            var w = World();
            Assert.IsTrue(Eval(w, new BroadcastTemplate { Type = BroadcastTemplateType.Comparison, InstanceA = 1, InstanceB = 2, Operator = 4 }));
            Assert.IsFalse(Eval(w, new BroadcastTemplate { Type = BroadcastTemplateType.Comparison, InstanceA = 1, InstanceB = 3, Operator = 3 }));
            string error; bool truth;
            Assert.IsFalse(BroadcastRules.TryEvaluate(w, new BroadcastTemplate { Type = BroadcastTemplateType.Comparison, InstanceA = 1, InstanceB = 1, Operator = 4 }, out truth, out error));
        }

        [Test]
        public void History_RequiresExistingRecord()
        {
            var w = World();
            Assert.IsTrue(Eval(w, new BroadcastTemplate { Type = BroadcastTemplateType.History, HistoryRound = 1, HistorySeat = 4, HistoryInstance = 9, Threshold = 30, Operator = 0 }));
            string error; bool truth;
            Assert.IsFalse(BroadcastRules.TryEvaluate(w, new BroadcastTemplate { Type = BroadcastTemplateType.History, HistoryRound = 2, HistorySeat = 4, HistoryInstance = 9, Threshold = 30, Operator = 0 }, out truth, out error));
        }

        [Test]
        public void Batch_ExactFakeCountAndDuration()
        {
            var w = World();
            var batch = BroadcastRules.BuildBatch(w, 4, 1, 1, new SystemRandomSource(3));
            Assert.Greater(batch.Items.Count, 0);
            Assert.LessOrEqual(batch.Items.Count, 4);
            Assert.AreEqual(1, batch.FakeCount);
            Assert.AreEqual(BroadcastRules.BatchDurationSeconds(batch.Items.Count), (batch.Items.Count - 1) * 3 + 3);
            foreach (var item in batch.Items) Assert.IsNotNull(item.Text);
        }

        [Test]
        public void Batch_InsufficientWorldReturnsActualAndNoFakeOverflow()
        {
            var w = new BroadcastWorld();
            w.Items.Add(new PublicItemRef { Seat = 0, InstanceId = 1, Name = "A", Category = "机械", Value = 60 });
            var batch = BroadcastRules.BuildBatch(w, 4, 2, 2, new SystemRandomSource(1));
            Assert.LessOrEqual(batch.FakeCount, batch.Items.Count);
            Assert.LessOrEqual(batch.Items.Count, 4);
        }
    }
}