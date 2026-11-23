using NUnit.Framework;
using Worthless.Lobby;

namespace Worthless.Tests
{
    public sealed class MatchHistoryTests
    {
        [Test]
        public void HistoryPersistsAcrossRoundsAndFreezesPreviouslyKnownValues()
        {
            var ledger = new ValueHistoryLedger(5);
            ledger.Record(0, 1, RoundPhase.RoundStart, ValueHistoryReason.Initial, 1000, 2, 40);
            ledger.Record(0, 1, RoundPhase.SecondTrade, ValueHistoryReason.Trade, 1001, 3, -20, 1);
            var settled = EconomyRules.ApplyDelta(EconomyRules.ApplyBasisPoints(-20, 5000, -100000, 100000), 5, -100000, 100000);
            ledger.RecordSettlement(0, 1, 3, false, 1001, 3, settled);
            ledger.Record(0, 2, RoundPhase.FirstTrade, ValueHistoryReason.Trade, 1002, 4, 90000, 1);
            Assert.AreEqual(4, ledger.ForSeat(0).Count);
            Assert.AreEqual(40, ledger.ForSeat(0)[0].ValueAtAcquisition);
            Assert.AreEqual(-20, ledger.ForSeat(0)[1].ValueAtAcquisition);
            Assert.AreEqual(-25, ledger.ForSeat(0)[2].ValueAtAcquisition);
            var opening = ledger.ForSeat(0)[2];
            Assert.AreEqual(2, opening.Round);
            Assert.AreEqual(ValueHistoryReason.RoundOpening, opening.Reason);
            Assert.IsFalse(opening.PrecedesMarket(1), "上一轮市场已计入下一轮开场价");
            Assert.IsTrue(opening.PrecedesMarket(2), "若之后转手，本轮市场仍影响该开场价的估算");
            Assert.AreEqual(2, ledger.ForSeat(0)[3].Round);
            Assert.IsEmpty(ledger.ForSeat(1));
        }

        [TestCase(1)]
        [TestCase(10)]
        public void FinalSettlementAndTieBreakStayInTheLastRound(int rounds)
        {
            var ledger = new ValueHistoryLedger(5);
            ledger.RecordSettlement(0, rounds, rounds, false, 1000, 2, 45);
            ledger.RecordSettlement(0, rounds, rounds, true, 1000, 2, 50);
            var history = ledger.ForSeat(0);
            Assert.AreEqual(2, history.Count);
            Assert.AreEqual(rounds, history[0].Round);
            Assert.AreEqual(rounds, history[1].Round);
            Assert.AreEqual(ValueHistoryReason.Settlement, history[0].Reason);
            Assert.AreEqual((int)RoundPhase.FinalSettlement, history[0].Phase);
            Assert.AreEqual(ValueHistoryReason.TieBreak, history[1].Reason);
            Assert.IsFalse(history[0].PrecedesMarket(rounds));
            Assert.IsFalse(history[1].PrecedesMarket(rounds));
        }

        [Test]
        public void TenRoundsDoNotDropEarlierRecordsAndDuplicateEventsAreIdempotent()
        {
            var ledger = new ValueHistoryLedger(10);
            for (var round = 1; round <= 10; round++)
            {
                Assert.IsTrue(ledger.Record(9, round, RoundPhase.FirstTrade, ValueHistoryReason.Trade, 1000 + round, round, round * -1000, round));
                Assert.IsFalse(ledger.Record(9, round, RoundPhase.FirstTrade, ValueHistoryReason.Trade, 1000 + round, round, 999, round));
                ledger.Record(9, round, RoundPhase.MarketUpdate, ValueHistoryReason.Settlement, 1000 + round, round, round * -1200);
            }
            Assert.AreEqual(20, ledger.ForSeat(9).Count);
            Assert.AreEqual(-1000, ledger.ForSeat(9)[0].ValueAtAcquisition);
            Assert.AreEqual(-12000, ledger.ForSeat(9)[19].ValueAtAcquisition);
        }

        [Test]
        public void TradeSequenceIsGlobalAndOrdinalResetsPerRoundAndTradeStage()
        {
            var journal = new TradeJournalIndex();
            int sequence, ordinal;
            Assert.IsTrue(journal.TryRecord(1, 1, RoundPhase.FirstTrade, out sequence, out ordinal));
            Assert.AreEqual(1, sequence); Assert.AreEqual(1, ordinal);
            Assert.IsFalse(journal.TryRecord(1, 1, RoundPhase.FirstTrade, out sequence, out ordinal));
            Assert.IsTrue(journal.TryRecord(2, 1, RoundPhase.FirstTrade, out sequence, out ordinal));
            Assert.AreEqual(2, sequence); Assert.AreEqual(2, ordinal);
            Assert.IsTrue(journal.TryRecord(3, 1, RoundPhase.ThirdTrade, out sequence, out ordinal));
            Assert.AreEqual(3, sequence); Assert.AreEqual(1, ordinal);
            Assert.IsTrue(journal.TryRecord(4, 2, RoundPhase.FirstTrade, out sequence, out ordinal));
            Assert.AreEqual(4, sequence); Assert.AreEqual(1, ordinal);
            Assert.IsFalse(journal.TryRecord(5, 2, RoundPhase.SecretChoice, out sequence, out ordinal));
            Assert.IsFalse(journal.TryRecord(6, 0, RoundPhase.FirstTrade, out sequence, out ordinal));
        }

        [Test]
        public void SnapshotPolicyAllowsEarlierRoundCatchupButRejectsWrongIdentityAndRegression()
        {
            Assert.IsTrue(HistorySnapshotRules.CanApply(8, 2, 1, 10, 8, 2, 3, 5));
            Assert.IsFalse(HistorySnapshotRules.CanApply(7, 2, 3, 20, 8, 2, 3, 5));
            Assert.IsFalse(HistorySnapshotRules.CanApply(8, 1, 3, 20, 8, 2, 3, 5));
            Assert.IsFalse(HistorySnapshotRules.CanApply(8, 2, 4, 20, 8, 2, 3, 5));
            Assert.IsFalse(HistorySnapshotRules.CanApply(8, 2, 2, 4, 8, 2, 3, 5));
        }

        [Test]
        public void PublicMarketTrendUsesCompoundedPricesAndHandlesNegativePrices()
        {
            var estimate = new KnownPriceEstimate(100);
            estimate.ApplyMarket(10000, -100000, 100000);
            estimate.ApplyMarket(-6000, -100000, 100000);
            Assert.AreEqual(80, estimate.MarketOnlyValue);
            Assert.AreEqual("↓", estimate.Arrow);
            Assert.AreEqual(100, estimate.LastKnownValue);
            var negative = new KnownPriceEstimate(-100);
            negative.ApplyMarket(5000, -100000, 100000);
            Assert.AreEqual("↓", negative.Arrow);
            var unchanged = new KnownPriceEstimate(0);
            unchanged.ApplyMarket(15000, -100000, 100000);
            Assert.AreEqual("", unchanged.Arrow);
        }
    }
}
