using NUnit.Framework;
using Worthless.Lobby;

namespace Worthless.Tests
{
    public class LobbySeatingRulesTests
    {
        [TestCase(1, 5)]
        [TestCase(2, 5)]
        [TestCase(5, 5)]
        [TestCase(6, 6)]
        [TestCase(7, 7)]
        [TestCase(8, 8)]
        [TestCase(9, 9)]
        [TestCase(10, 10)]
        public void DefaultTier_SmallestFittingTier(int realCount, int expectedTier)
        {
            Assert.AreEqual(expectedTier, LobbySeatingRules.DefaultTier(realCount));
        }

        [TestCase(0)]
        [TestCase(11)]
        [TestCase(-1)]
        public void DefaultTier_InvalidCounts(int realCount)
        {
            Assert.AreEqual(0, LobbySeatingRules.DefaultTier(realCount));
        }

        [TestCase(5, 1, 4)]
        [TestCase(5, 5, 0)]
        [TestCase(7, 2, 5)]
        [TestCase(9, 3, 6)]
        [TestCase(9, 9, 0)]
        [TestCase(10, 3, 7)]
        public void NpcPreview_FillsEmptySeats(int tier, int realCount, int expectedNpc)
        {
            Assert.AreEqual(expectedNpc, LobbySeatingRules.NpcPreview(tier, realCount));
        }

        [TestCase(5, true)]
        [TestCase(7, true)]
        [TestCase(9, true)]
        [TestCase(4, false)]
        [TestCase(6, true)]
        [TestCase(8, true)]
        [TestCase(0, false)]
        public void IsValidTier_AcceptsEveryIntegerFromFiveToTen(int tier, bool expected)
        {
            Assert.AreEqual(expected, LobbySeatingRules.IsValidTier(tier));
        }

        [TestCase(5, 1, true)]
        [TestCase(5, 5, true)]
        [TestCase(7, 6, true)]
        [TestCase(9, 9, true)]
        [TestCase(5, 6, false)]
        [TestCase(7, 8, false)]
        [TestCase(5, 0, false)]
        [TestCase(8, 3, true)]
        [TestCase(10, 3, true)]
        public void CanStart_ValidatesTierAndRealCount(int tier, int realCount, bool expected)
        {
            string reason;
            Assert.AreEqual(expected, LobbySeatingRules.CanStart(tier, realCount, out reason));
            if (!expected) Assert.IsFalse(string.IsNullOrEmpty(reason));
        }

        [TestCase(8, true)]
        [TestCase(9, true)]
        [TestCase(10, false)]
        public void CanAcceptJoin_CapsAtTen(int realCount, bool expected)
        {
            Assert.AreEqual(expected, LobbySeatingRules.CanAcceptJoin(realCount));
        }

        [Test]
        public void CanStart_InsufficientTierExplainsReason()
        {
            string reason;
            Assert.IsFalse(LobbySeatingRules.CanStart(5, 6, out reason));
            StringAssert.Contains("更大的档位", reason);
        }
    }
}