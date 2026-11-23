using System.Collections.Generic;
using NUnit.Framework;
using Worthless.Lobby;

namespace Worthless.Tests
{
    public class RoundRulesTests
    {
        private static RoundConfigData ShortConfig()
        {
            return new RoundConfigData
            {
                RoundStartSeconds = 0.5f,
                OpeningGoalSeconds = 0.5f,
                FirstTradeSeconds = 1f,
                BroadcastSeconds = 0.5f,
                SecondTradeSeconds = 1f,
                LieRevealSeconds = 0.5f,
                ThirdTradeSeconds = 1f,
                MarketDisplaySeconds = 0.5f
            };
        }

        [Test]
        public void OpeningGoalWindowPrecedesTradingAndDoesNotRepeat()
        {
            var config = ShortConfig();
            config.OpeningGoalSeconds = 6f;
            var machine = new RoundStateMachine(config);
            machine.Start(100, 2);
            machine.Advance(105.99);
            Assert.AreEqual(RoundPhase.RoundStart, machine.Phase);
            machine.Advance(106);
            Assert.AreEqual(RoundPhase.FirstTrade, machine.Phase);

            while (machine.Round == 1) machine.Advance(machine.PhaseEndsAt);
            Assert.AreEqual(RoundPhase.RoundStart, machine.Phase);
            Assert.AreEqual(config.RoundStartSeconds, machine.RemainingSeconds(machine.PhaseEndsAt - config.RoundStartSeconds));
        }

        [Test]
        public void PhaseSequence_ReachesGameOver_WithTwoRounds()
        {
            var machine = new RoundStateMachine(ShortConfig());
            machine.Start(0, 2);
            Assert.AreEqual(RoundPhase.RoundStart, machine.Phase);
            var trace = new List<RoundPhase>();
            var now = 0.0;
            for (var step = 0; step < 200 && machine.Active; step++)
            {
                now += 0.5;
                machine.Advance(now, (p, r) => trace.Add(p));
            }
            CollectionAssert.Contains(trace, RoundPhase.FirstTrade);
            CollectionAssert.Contains(trace, RoundPhase.SecretChoice);
            CollectionAssert.Contains(trace, RoundPhase.MarketUpdate);
            Assert.AreEqual(RoundPhase.GameOver, machine.Phase);
            Assert.IsFalse(machine.Active, "GameOver 后不再计时");
        }

        [Test]
        public void Advance_IsBoundedAndMonotonic()
        {
            var machine = new RoundStateMachine(ShortConfig());
            machine.Start(0, 2);
            var transitions = machine.Advance(1000);
            Assert.LessOrEqual(transitions, RoundStateMachine.MaxTransitionsPerAdvance);
            // 从起局大跳先停在 Broadcast，给完整广播窗口
            Assert.AreEqual(RoundPhase.Broadcast, machine.Phase);
            var config = ShortConfig();
            Assert.GreaterOrEqual(machine.RemainingSeconds(1000), config.BroadcastSeconds - 0.001);
            machine.Advance(1000 + config.BroadcastSeconds + 0.1);
            Assert.AreEqual(RoundPhase.SecondTrade, machine.Phase);
            machine.Advance(1000 + config.BroadcastSeconds + config.SecondTradeSeconds + 0.2);
            Assert.AreEqual(RoundPhase.LieReveal, machine.Phase);
            Assert.GreaterOrEqual(machine.RemainingSeconds(1000 + config.BroadcastSeconds + config.SecondTradeSeconds + 0.2), 4.999);
            var now = 1000.0;
            for (var i = 0; i < 200 && machine.Active; i++)
            {
                now += 1.0;
                machine.Advance(now);
            }
            Assert.AreEqual(RoundPhase.GameOver, machine.Phase);
            Assert.AreEqual(0, machine.RemainingSeconds(now));
        }

        [Test]
        public void EarlyTrade_ShortensTrades()
        {
            var config = ShortConfig();
            var machine = new RoundStateMachine(config);
            machine.Start(0, 2, true);
            machine.Advance(machine.PhaseEndsAt + 0.01);
            Assert.AreEqual(RoundPhase.FirstTrade, machine.Phase);
            Assert.LessOrEqual(machine.RemainingSeconds(machine.PhaseEndsAt - 0.01), 1.0);
        }

        [Test]
        public void Restore_PreservesPhaseAndDeadline()
        {
            var machine = new RoundStateMachine(ShortConfig());
            machine.Restore(2, RoundPhase.SecretChoice, 42.5, false, 2);
            Assert.AreEqual(2, machine.Round);
            Assert.AreEqual(RoundPhase.SecretChoice, machine.Phase);
            Assert.AreEqual(42.5, machine.PhaseEndsAt, 0.0001);
            Assert.AreEqual(2.5, machine.RemainingSeconds(40), 0.0001);
        }

        [Test]
        public void ConfigValidation_RejectsInvalid()
        {
            string error;
            var invalid = new RoundConfigData { FirstTradeSeconds = 0f };
            Assert.IsFalse(invalid.Validate(out error));
            Assert.IsNotNull(error);
            Assert.IsTrue(ShortConfig().Validate(out error));
        }

        [Test]
        public void ConfiguredRounds_ControlSecretPhasesAndMarkets()
        {
            foreach (var expected in new[] { 1, RoundConfigData.DefaultRounds, RoundConfigData.MaxRounds })
            {
                var machine = new RoundStateMachine(ShortConfig());
                machine.Start(0, expected);
                var markets = 0;
                var settlements = 0;
                var now = 0.0;
                for (var step = 0; step < 4000 && machine.Active; step++)
                {
                    now += 0.25;
                    machine.Advance(now, (p, r) =>
                    {
                        if (p == RoundPhase.MarketUpdate) markets++;
                        if (p == RoundPhase.SecretChoice) settlements++;
                    });
                }
                Assert.AreEqual(expected, settlements, "秘密选择次数");
                Assert.AreEqual(expected - 1, markets, "末轮不触发市场");
                Assert.AreEqual(RoundPhase.GameOver, machine.Phase);
            }
        }

        [Test]
        public void BigJump_StillGivesFullSecretAndMarketWindows()
        {
            var config = ShortConfig();
            var machine = new RoundStateMachine(config);
            machine.Start(0, 2);
            var now = 1000.0; // 大时间跳跃：Broadcast 先完整，再揭晓 5 秒，再到结算/市场
            machine.Advance(now);
            Assert.AreEqual(RoundPhase.Broadcast, machine.Phase);
            Assert.GreaterOrEqual(machine.RemainingSeconds(now), config.BroadcastSeconds - 0.001);
            now += config.BroadcastSeconds + 0.1;
            machine.Advance(now);
            Assert.AreEqual(RoundPhase.SecondTrade, machine.Phase);
            now += config.SecondTradeSeconds + 0.1;
            machine.Advance(now);
            Assert.AreEqual(RoundPhase.LieReveal, machine.Phase);
            Assert.GreaterOrEqual(machine.RemainingSeconds(now), 4.999);
            now += 5.1;
            machine.Advance(now);
            Assert.AreEqual(RoundPhase.ThirdTrade, machine.Phase);
            now += config.ThirdTradeSeconds + 0.1;
            machine.Advance(now);
            Assert.AreEqual(RoundPhase.SecretChoice, machine.Phase);
            Assert.GreaterOrEqual(machine.RemainingSeconds(now), SecretSelections.Seconds - 0.001);
            now += SecretSelections.Seconds + 5;
            machine.Advance(now);
            Assert.AreEqual(RoundPhase.MarketUpdate, machine.Phase);
            Assert.GreaterOrEqual(machine.RemainingSeconds(now), config.MarketDisplaySeconds - 0.001);
        }

        [Test]
        public void RequestEarlyEnd_ShortensCurrentPhase()
        {
            var machine = new RoundStateMachine(ShortConfig());
            machine.Start(0, 1);
            machine.Advance(1.0);
            var before = machine.PhaseEndsAt;
            machine.RequestEarlyEnd(1.0, 0.2f);
            Assert.Less(machine.PhaseEndsAt, before);
            machine.Advance(1.3);
            Assert.AreEqual(RoundPhase.Broadcast, machine.Phase);
        }
    }

    public class EliminationRulesTests
    {
        private static List<RankEntry> Entries(params long[] values)
        {
            var list = new List<RankEntry>();
            for (var i = 0; i < values.Length; i++) list.Add(new RankEntry(i, values[i]));
            return list;
        }

        [Test]
        public void LowestThreeTie_PicksTwoBoundary()
        {
            var result = EliminationRules.Select(Entries(30, 30, 30, 30, 50), 2, new SystemRandomSource(1));
            Assert.AreEqual(2, result.Length);
            foreach (var seat in result) Assert.LessOrEqual(seat, 3);
        }

        [Test]
        public void UniqueLowest_PlusBoundaryTie()
        {
            var result = EliminationRules.Select(Entries(10, 20, 20, 30), 2, new SystemRandomSource(7));
            Assert.AreEqual(2, result.Length);
            Assert.Contains(0, result);
            Assert.IsTrue(System.Array.IndexOf(result, 1) >= 0 || System.Array.IndexOf(result, 2) >= 0);
        }

        [Test]
        public void AllEqual_PicksExactlyTwo()
        {
            var result = EliminationRules.Select(Entries(40, 40, 40, 40, 40), 2, new SystemRandomSource(3));
            Assert.AreEqual(2, result.Length);
            Assert.AreNotEqual(result[0], result[1]);
        }

        [Test]
        public void FewerAliveThanSlots_EliminatesAll()
        {
            var result = EliminationRules.Select(Entries(10), 2, new SystemRandomSource(1));
            Assert.AreEqual(1, result.Length);
        }
    }

    public class TradeRulesTests
    {
        [Test]
        public void Window_IsStrictlyWithinPhaseAndEligibility()
        {
            Assert.IsTrue(TradeRules.IsWindowOpen(true, true, 0.1f));
            Assert.IsFalse(TradeRules.IsWindowOpen(true, true, 0f));
            Assert.IsFalse(TradeRules.IsWindowOpen(false, true, 5f));
            Assert.IsFalse(TradeRules.IsWindowOpen(true, false, 5f));
            Assert.IsTrue(TradeRules.CanUseEligibility(0, false));
            Assert.IsTrue(TradeRules.CanUseEligibility(1, false));
            Assert.IsFalse(TradeRules.CanUseEligibility(2, false));
            Assert.IsFalse(TradeRules.CanUseEligibility(0, true));
            Assert.IsTrue(TradeRules.CanInitiate(true, true, 1f, true, false, 0, false, 1, 0, true));
            Assert.IsFalse(TradeRules.CanInitiate(true, true, 1f, true, true, 0, false, 1, 0, true));
            Assert.IsTrue(TradeRules.CanInitiate(true, true, 1f, true, false, 1, false, 1, 0, true));
            Assert.IsFalse(TradeRules.CanInitiate(true, true, 1f, true, false, 2, false, 1, 0, true));
            Assert.IsFalse(TradeRules.CanInitiate(true, true, 1f, true, false, 0, false, 0, 0, true));
        }

        [Test]
        public void NpcProbability_IsDeterministicAtBounds()
        {
            Assert.IsFalse(TradeRules.NpcAccepts(0f, 0));
            Assert.IsTrue(TradeRules.NpcAccepts(1f, 9999));
            Assert.IsTrue(TradeRules.NpcAccepts(0.5f, 4999));
            Assert.IsFalse(TradeRules.NpcAccepts(0.5f, 5000));
        }

        [Test]
        public void ReturnTrade_AndStaleVersion_Detected()
        {
            Assert.IsTrue(TradeRules.IsReturnTradeBlocked(true));
            Assert.IsFalse(TradeRules.IsReturnTradeBlocked(false));
            Assert.IsTrue(TradeRules.IsStaleVersion(1, 2, 1, 1));
            Assert.IsTrue(TradeRules.IsStaleVersion(1, 1, 1, 2));
            Assert.IsFalse(TradeRules.IsStaleVersion(1, 1, 1, 1));
        }

        [Test]
        public void TradeConfigProbability_Validation()
        {
            Assert.IsTrue(TradeRules.IsValidProbability(0f));
            Assert.IsTrue(TradeRules.IsValidProbability(0.5f));
            Assert.IsTrue(TradeRules.IsValidProbability(1f));
            Assert.IsFalse(TradeRules.IsValidProbability(-0.1f));
            Assert.IsFalse(TradeRules.IsValidProbability(1.1f));
            Assert.IsFalse(TradeRules.IsValidProbability(float.NaN));
        }
    }

    public class PrivateValueRulesTests
    {
        [Test]
        public void SwappedItemPrivatePacketArrivingBeforeNewPublicIdentityMustWait()
        {
            Assert.AreEqual(PrivateMergeDecision.Stage,
                PrivateValueRules.Decide(7, 9, 1, 1001, 18, 7, 1, 9, 1000, false, 17, 17, 9));
            Assert.AreEqual(PrivateMergeDecision.Apply,
                PrivateValueRules.Decide(7, 9, 1, 1001, 18, 7, 1, 9, 1001, false, 18, 17, 9));
            Assert.AreEqual(PrivateMergeDecision.Discard,
                PrivateValueRules.Decide(7, 9, 1, 1000, 17, 7, 1, 9, 1001, false, 18, 18, 9));
        }

        [Test]
        public void Alignment_AppliesOnlyWhenPublicVersionMatches()
        {
            var apply = PrivateValueRules.Decide(7, 1, 1, 1001, 1, 7, 1, 1, 1001, false, 1, 0, 0);
            Assert.AreEqual(PrivateMergeDecision.Apply, apply);
            var future = PrivateValueRules.Decide(7, 1, 1, 1001, 2, 7, 1, 1, 1001, false, 1, 1, 1);
            Assert.AreEqual(PrivateMergeDecision.Stage, future);
            var old = PrivateValueRules.Decide(7, 1, 1, 1001, 1, 7, 1, 1, 1001, false, 2, 2, 1);
            Assert.AreEqual(PrivateMergeDecision.Discard, old);
            var wrongSeat = PrivateValueRules.Decide(7, 1, 2, 1001, 1, 7, 1, 1, 1001, false, 1, 0, 0);
            Assert.AreEqual(PrivateMergeDecision.Discard, wrongSeat);
            var wrongInstance = PrivateValueRules.Decide(7, 1, 1, 1002, 1, 7, 1, 1, 1001, false, 1, 0, 0);
            Assert.AreEqual(PrivateMergeDecision.Stage, wrongInstance);
            var eliminated = PrivateValueRules.Decide(7, 1, 1, 1001, 1, 7, 1, 1, 1001, true, 1, 0, 0);
            Assert.AreEqual(PrivateMergeDecision.Discard, eliminated);
            var duplicate = PrivateValueRules.Decide(7, 1, 1, 1001, 1, 7, 1, 1, 1001, false, 1, 1, 1);
            Assert.AreEqual(PrivateMergeDecision.Discard, duplicate);
        }

        [Test]
        public void MissingPrerequisites_StageInsteadOfShowing()
        {
            var noSeat = PrivateValueRules.Decide(7, 1, 1, 1001, 1, 7, -1, 1, 1001, false, 0, 0, 0);
            Assert.AreEqual(PrivateMergeDecision.Stage, noSeat);
            var newInstance = PrivateValueRules.Decide(7, 2, 1, 2001, 1, 7, 1, 2, 1001, false, 1, 0, 0);
            Assert.AreEqual(PrivateMergeDecision.Stage, newInstance);
            var newInstanceOldVersion = PrivateValueRules.Decide(7, 2, 1, 2001, 1, 7, 1, 2, 1001, false, 2, 0, 0);
            Assert.AreEqual(PrivateMergeDecision.Discard, newInstanceOldVersion);
            var noRound = PrivateValueRules.Decide(7, 1, 1, 1001, 1, 7, 1, 0, 1001, false, 0, 0, 0);
            Assert.AreEqual(PrivateMergeDecision.Stage, noRound);
            var futureRound = PrivateValueRules.Decide(7, 2, 1, 1001, 1, 7, 1, 1, 1001, false, 1, 0, 0);
            Assert.AreEqual(PrivateMergeDecision.Stage, futureRound);
        }
    }

    public class EconomyRulesTests
    {
        [Test]
        public void BasisPoints_RoundsAwayFromZero_AndClamps()
        {
            Assert.AreEqual(112L, EconomyRules.ApplyBasisPoints(100, 1200, 10, 150));
            Assert.AreEqual(10L, EconomyRules.ApplyBasisPoints(10, -9000, 10, 150));
            Assert.AreEqual(150L, EconomyRules.ApplyBasisPoints(140, 5000, 10, 150));
            Assert.AreEqual(11L, EconomyRules.ApplyBasisPoints(10, 500, 10, 150));
        }

        [Test]
        public void InitialValue_StaysInConfiguredRange_AndIsDeterministic()
        {
            for (var i = 0; i < 100; i++)
            {
                var value = EconomyRules.InitialValue(12345, i, 30, 90);
                Assert.GreaterOrEqual(value, 30);
                Assert.LessOrEqual(value, 90);
                Assert.AreEqual(value, EconomyRules.InitialValue(12345, i, 30, 90));
            }
        }

        [Test]
        public void EconomyConfig_RejectsInvalidEvents()
        {
            string error;
            var bad = new EconomyConfigData();
            bad.Events[0].BasisPoints[0] = 20000;
            Assert.IsFalse(bad.Validate(out error));
            Assert.IsTrue(new EconomyConfigData().Validate(out error));
            var duplicated = new EconomyConfigData();
            duplicated.Events[1].Id = duplicated.Events[0].Id;
            Assert.IsFalse(duplicated.Validate(out error));
            var missing = new EconomyConfigData();
            missing.Events[0].Categories = new[] { "机械", "手作" };
            missing.Events[0].BasisPoints = new[] { 100, 100 };
            Assert.IsFalse(missing.ValidateForCatalog(new ItemCatalogData(), out error));
        }

        [Test]
        public void EconomyConfig_RejectsOverflowAndLongBanners()
        {
            string error;
            var huge = new EconomyConfigData();
            huge.MaxValue = long.MaxValue / 2;
            Assert.IsFalse(huge.Validate(out error));
            var minBp = new EconomyConfigData();
            minBp.Events[0].BasisPoints[0] = int.MinValue;
            Assert.IsFalse(minBp.Validate(out error));
            var longName = new EconomyConfigData();
            longName.Events[0].DisplayName = new string('市', 30);
            Assert.IsFalse(longName.Validate(out error));
            var catalog = new ItemCatalogData();
            catalog.Definitions[0].DisplayName = new string('钥', 30);
            Assert.IsFalse(new EconomyConfigData().ValidateForCatalog(catalog, out error));
            Assert.AreEqual(150L, EconomyRules.ApplyBasisPoints(long.MaxValue / 2, 9000, 10, 150));
        }

        [Test]
        public void WorkshopMarket_Reversal_AndZeroCategory()
        {
            var events = EconomyConfigData.DefaultEvents();
            MarketEventDefinition workshop = default;
            foreach (var item in events) if (item.Id == "E02") workshop = item;
            Assert.AreEqual(-4200, workshop.BasisPointsFor("机械"));
            Assert.AreEqual(4800, workshop.BasisPointsFor("手作"));
            Assert.AreEqual(0, workshop.BasisPointsFor("晶矿"));
            Assert.AreEqual(35L, EconomyRules.ApplyBasisPoints(60, workshop.BasisPointsFor("机械"), 10, 150));
            Assert.AreEqual(74L, EconomyRules.ApplyBasisPoints(50, workshop.BasisPointsFor("手作"), 10, 150));
            Assert.AreEqual(75L, EconomyRules.ApplyBasisPoints(75, workshop.BasisPointsFor("晶矿"), 10, 150));
            string error;
            Assert.IsTrue(new EconomyConfigData().ValidateForCatalog(new ItemCatalogData(), out error), error);
        }

        [Test]
        public void MarketRoll_VariesTheSameEventWithoutLosingStrongRankReversals()
        {
            var config = new EconomyConfigData();
            var lower = config.RollMarketEvent(1, new MarketRollRandom(false));
            var upper = config.RollMarketEvent(1, new MarketRollRandom(true));
            Assert.AreEqual("E02", lower.Id);
            CollectionAssert.AreEqual(new[] { -6700, 2300, -2500 }, lower.BasisPoints);
            CollectionAssert.AreEqual(new[] { -1700, 7300, 2500 }, upper.BasisPoints);
            CollectionAssert.AreEqual(new[] { -4200, 4800, 0 }, config.Events[1].BasisPoints);
            var mechanical = EconomyRules.ApplyBasisPoints(60, upper.BasisPointsFor("机械"), config.MinValue, config.MaxValue);
            var handmade = EconomyRules.ApplyBasisPoints(50, lower.BasisPointsFor("手作"), config.MinValue, config.MaxValue);
            Assert.Less(mechanical, handmade, "The ranking reverses even under the weakest divergence.");
        }

        [Test]
        public void MarketRoll_RespectsBoundsAndAllowsFixedRates()
        {
            var config = new EconomyConfigData();
            config.Events[0].BasisPoints = new[] { -9000, 8950, -8950 };
            CollectionAssert.AreEqual(new[] { -9000, 6500, -9000 }, config.RollMarketEvent(0, new MarketRollRandom(false)).BasisPoints);
            CollectionAssert.AreEqual(new[] { -6500, 9000, -6500 }, config.RollMarketEvent(0, new MarketRollRandom(true)).BasisPoints);
            CollectionAssert.AreEqual(new[] { -8900, 6600, -8900 }, config.RollMarketEvent(0, new MarketRollRandom(false, 1)).BasisPoints);
            config.SwingVarianceBasisPoints = 0;
            CollectionAssert.AreEqual(new[] { -9000, 9000, -9000 }, config.RollMarketEvent(0, new MarketRollRandom(false)).BasisPoints);
            config.SwingVarianceBasisPoints = -1;
            Assert.IsFalse(config.Validate(out _));
        }

        [Test]
        public void MarketSelection_SkipsThePreviousEventWhenPossible()
        {
            Assert.AreEqual(1, EconomyRules.PickMarketEvent(4, 0, new MarketRollRandom(false)));
            Assert.AreEqual(2, EconomyRules.PickMarketEvent(4, 3, new MarketRollRandom(true)));
            Assert.AreEqual(3, EconomyRules.PickMarketEvent(4, -1, new MarketRollRandom(true)));
            Assert.AreEqual(0, EconomyRules.PickMarketEvent(1, 0, new MarketRollRandom(false)));
        }

        private sealed class MarketRollRandom : IRandomSource
        {
            private readonly bool highest;
            private readonly int lowerOffset;
            public MarketRollRandom(bool highest, int lowerOffset = 0)
            {
                this.highest = highest;
                this.lowerOffset = lowerOffset;
            }
            public int Next(int maxExclusive) => highest ? maxExclusive - 1 : lowerOffset;
        }

        [Test]
        public void ItemCatalog_HasNineDefinedItems()
        {
            var catalog = new ItemCatalogData();
            string error;
            Assert.IsTrue(catalog.Validate(out error));
            Assert.AreEqual(9, catalog.Definitions.Length);
            Assert.Contains("机械", new List<string> { catalog.Definitions[0].Category });
        }

        [Test]
        public void PrivatePayload_ValidatesIdentityAndVersion()
        {
            var payload = new PrivateValuePayload
            {
                GameId = 7, Round = 2, ItemInstanceId = 1001, OwnershipSeat = 1, Value = 55, ValueVersion = 3
            };
            Assert.IsTrue(payload.IsValidFor(7, 2, 1001, 1));
            Assert.IsFalse(payload.IsValidFor(8, 2, 1001, 1));
            Assert.IsFalse(payload.IsValidFor(7, 3, 1001, 1));
            Assert.IsFalse(payload.IsValidFor(7, 2, 1002, 1));
            Assert.IsFalse(payload.IsValidFor(7, 2, 1001, 0));
            payload.ValueVersion = 0;
            Assert.IsFalse(payload.IsValidFor(7, 2, 1001, 1));
        }
    }
}
