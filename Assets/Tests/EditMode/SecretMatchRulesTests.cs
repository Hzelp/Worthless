using System;
using System.Linq;
using NUnit.Framework;
using Worthless.Lobby;

namespace Worthless.Tests
{
    public sealed class SecretMatchRulesTests
    {
        [TestCase(5)] [TestCase(6)] [TestCase(7)] [TestCase(8)] [TestCase(9)] [TestCase(10)]
        public void RolesAreBalancedAndAllFourRankPairsOccur(int seats)
        {
            var pairs = new System.Collections.Generic.HashSet<(int, int)>();
            var firstSeat = new System.Collections.Generic.HashSet<GoalGroup>();
            for (var seed = 0; seed < 100; seed++)
            {
                var goals = new SecretGoals(seats, new SystemRandomSource(seed));
                var highs = Enumerable.Range(0, seats).Count(i => goals.GroupOf(i) == GoalGroup.High);
                Assert.LessOrEqual(Math.Abs(2 * highs - seats), 1);
                pairs.Add((goals.HighRank, goals.LowRank));
                firstSeat.Add(goals.GroupOf(0));
            }
            Assert.AreEqual(4, pairs.Count);
            Assert.AreEqual(2, firstSeat.Count);
        }

        [Test]
        public void MixedRanksAreEvaluatedInsideTheirOwnGroupsAndTiesShareTarget()
        {
            var goals = Enumerable.Range(0, 100).Select(seed => new SecretGoals(6, new SystemRandomSource(seed)))
                .First(g => g.HighRank == 1 && g.LowRank == 2);
            var high = Enumerable.Range(0, 6).Where(i => goals.GroupOf(i) == GoalGroup.High).ToArray();
            var low = Enumerable.Range(0, 6).Where(i => goals.GroupOf(i) == GoalGroup.Low).ToArray();
            var values = new long[6];
            values[high[0]] = 5; values[high[1]] = 10; values[high[2]] = 7;
            values[low[0]] = -10; values[low[1]] = -5; values[low[2]] = 90;
            CollectionAssert.AreEqual(new[] { high[1] }, goals.Winners(values, GoalGroup.High));
            CollectionAssert.AreEqual(new[] { low[1] }, goals.Winners(values, GoalGroup.Low));
            values[low[2]] = -5;
            CollectionAssert.AreEquivalent(new[] { low[1], low[2] }, goals.Winners(values, GoalGroup.Low));
            values[low[1]] = -10;
            CollectionAssert.AreEquivalent(new[] { low[0], low[1] }, goals.Winners(values, GoalGroup.Low));
        }

        [Test]
        public void SecretTimeoutFillsMissingChoicesAndRejectsSelfDuplicateLateAndInvalid()
        {
            var choices = new SecretSelections(5, 30);
            Assert.IsFalse(choices.TrySubmit(0, 0, 5, 1));
            Assert.IsFalse(choices.TrySubmit(0, 5, 5, 1));
            Assert.IsFalse(choices.TrySubmit(0, 1, 4, 1));
            Assert.IsTrue(choices.TrySubmit(0, 2, -5, 1));
            Assert.IsFalse(choices.TrySubmit(0, 1, 5, 2));
            Assert.IsFalse(choices.TrySubmit(1, 2, 5, 30));
            Assert.IsFalse(choices.Complete);
            var deltas = choices.LockAndSum(new SystemRandomSource(17));
            Assert.IsTrue(choices.Complete);
            for (var i = 0; i < 5; i++)
            {
                Assert.IsTrue(choices.TryGet(i, out var target, out var change));
                Assert.AreNotEqual(i, target);
                Assert.AreEqual(5, Math.Abs(change));
            }
            Assert.IsTrue(choices.TryGet(0, out var originalTarget, out var originalChange));
            Assert.AreEqual(2, originalTarget);
            Assert.AreEqual(-5, originalChange);
            Assert.AreNotEqual(0, deltas.Sum(), "奇数个 ±5 的总和不会为零");
            Assert.Throws<InvalidOperationException>(() => choices.LockAndSum(new SystemRandomSource(1)));
        }

        [Test]
        public void AllSubmittedCancelsOppositeDeltasAndMarketPrecedesRemainingDelta()
        {
            var choices = new SecretSelections(5, 30);
            Assert.IsTrue(choices.TrySubmit(0, 4, 5, 1));
            Assert.IsTrue(choices.TrySubmit(1, 4, -5, 1));
            Assert.IsTrue(choices.TrySubmit(2, 0, -5, 1));
            Assert.IsTrue(choices.TrySubmit(3, 1, 5, 1));
            Assert.IsTrue(choices.TrySubmit(4, 1, 5, 1));
            Assert.IsTrue(choices.Complete);
            var deltas = choices.LockAndSum(new SystemRandomSource(1));
            CollectionAssert.AreEqual(new[] { -5, 10, 0, 0, 0 }, deltas);
            long market = EconomyRules.ApplyBasisPoints(20, 5000, -100000, 100000);
            Assert.AreEqual(25, EconomyRules.ApplyDelta(market, deltas[0], -100000, 100000));
            Assert.AreEqual(-2, EconomyRules.ApplyBasisPoints(-1, 5000, -100000, 100000));
            Assert.AreEqual(-4, EconomyRules.ApplyDelta(1, -5, -100000, 100000));
        }

        [Test]
        public void TieBreakRunsOnceWithNoExtraMarketOrNormalRound()
        {
            var machine = new RoundStateMachine(new RoundConfigData());
            machine.Start(0, 3);
            var trace = new System.Collections.Generic.List<(RoundPhase, int)>();
            while (machine.Active)
                machine.Advance(machine.PhaseEndsAt, (phase, round) =>
                {
                    trace.Add((phase, round));
                    if (phase == RoundPhase.FinalSettlement) machine.RequireTieBreak();
                    if (phase == RoundPhase.TieBreak)
                    {
                        Assert.AreEqual(30, machine.RemainingSeconds(machine.PhaseEndsAt - 30));
                        machine.RequireTieBreak();
                    }
                });
            Assert.AreEqual(2, trace.Count(x => x.Item1 == RoundPhase.MarketUpdate));
            Assert.AreEqual(3, trace.Count(x => x.Item1 == RoundPhase.SecretChoice));
            CollectionAssert.AreEqual(new[] { (RoundPhase.FinalSettlement, 3), (RoundPhase.TieBreak, 3), (RoundPhase.GameOver, 3) }, trace.Skip(trace.Count - 3));
        }
    }
}
