using NUnit.Framework;
using Worthless.Lobby;

namespace Worthless.Tests
{
    public class SeatBindingRulesTests
    {
        [Test]
        public void UnboundSentinel_DoesNotCollideWithHostClientIdZero()
        {
            Assert.AreEqual(0UL, 0UL, "NGO ServerClientId 是 0");
            Assert.IsFalse(SeatBindingRules.IsBound(SeatBindingRules.Unbound));
            Assert.IsTrue(SeatBindingRules.IsBound(0UL), "房主 (ClientId 0) 必须是合法绑定值");
        }

        [Test]
        public void CanMove_HostClientIdZeroCanMoveOwnSeat()
        {
            Assert.IsTrue(SeatBindingRules.CanMove(true, true, 0UL, 0UL));
        }

        [Test]
        public void CanMove_RejectsUnboundSeat()
        {
            Assert.IsFalse(SeatBindingRules.CanMove(true, true, SeatBindingRules.Unbound, SeatBindingRules.Unbound));
            Assert.IsFalse(SeatBindingRules.CanMove(true, true, SeatBindingRules.Unbound, 0UL));
        }

        [Test]
        public void NeedsOldDisconnect_WhenSeatHeldByAnotherConnection()
        {
            Assert.IsTrue(SeatBindingRules.NeedsOldDisconnect(true, 7, 9));
            Assert.IsFalse(SeatBindingRules.NeedsOldDisconnect(true, 9, 9));
            Assert.IsFalse(SeatBindingRules.NeedsOldDisconnect(false, 7, 9));
            Assert.IsFalse(SeatBindingRules.NeedsOldDisconnect(true, SeatBindingRules.Unbound, 9));
        }

        [Test]
        public void ShouldClearOnDisconnect_OnlyForCurrentBinding()
        {
            Assert.IsTrue(SeatBindingRules.ShouldClearOnDisconnect(true, 0, 0));
            Assert.IsFalse(SeatBindingRules.ShouldClearOnDisconnect(true, 9, 7));
            Assert.IsFalse(SeatBindingRules.ShouldClearOnDisconnect(false, 7, 7));
        }

        [Test]
        public void LateOldDisconnect_DoesNotClearNewBinding()
        {
            ulong bound = 12;
            var cleared = SeatBindingRules.ShouldClearOnDisconnect(true, bound, 11);
            Assert.IsFalse(cleared);
            Assert.AreEqual(12UL, bound);
        }

        [TestCase(true, true, 0UL, 0UL, true)]
        [TestCase(true, true, 5UL, 6UL, false)]
        [TestCase(false, true, 5UL, 5UL, false)]
        [TestCase(true, false, 5UL, 5UL, false)]
        [TestCase(true, true, SeatBindingRules.Unbound, 0UL, false)]
        public void CanMove_RequiresStartedOnlineAndBinding(bool started, bool online, ulong bound, ulong sender, bool expected)
        {
            Assert.AreEqual(expected, SeatBindingRules.CanMove(started, online, bound, sender));
        }

        [TestCase(0, 5, true)]
        [TestCase(4, 5, true)]
        [TestCase(5, 5, false)]
        [TestCase(0, 0, false)]
        [TestCase(-1, 5, false)]
        public void IsSeatIndexActive_MatchesTargetTier(int index, int tier, bool expected)
        {
            Assert.AreEqual(expected, SeatBindingRules.IsSeatIndexActive(index, tier));
        }
    }
}