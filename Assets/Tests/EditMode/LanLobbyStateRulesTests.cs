using NUnit.Framework;
using Worthless.Lobby;

namespace Worthless.Tests
{
    public class LanLobbyStateRulesTests
    {
        [TestCase(LanLobbyState.Idle, true)]
        [TestCase(LanLobbyState.Failed, true)]
        [TestCase(LanLobbyState.Stopping, false)]
        [TestCase(LanLobbyState.StartingHost, false)]
        [TestCase(LanLobbyState.Listening, false)]
        [TestCase(LanLobbyState.Connecting, false)]
        [TestCase(LanLobbyState.Joined, false)]
        [TestCase(LanLobbyState.HostWithClient, false)]
        public void CanStartHost_OnlyFromIdleOrFailed(LanLobbyState state, bool expected)
        {
            Assert.AreEqual(expected, LanLobbyStateRules.CanStartHost(state));
        }

        [TestCase(LanLobbyState.Idle, true)]
        [TestCase(LanLobbyState.Failed, true)]
        [TestCase(LanLobbyState.Stopping, false)]
        [TestCase(LanLobbyState.StartingHost, false)]
        [TestCase(LanLobbyState.Listening, false)]
        [TestCase(LanLobbyState.Connecting, false)]
        [TestCase(LanLobbyState.Joined, false)]
        [TestCase(LanLobbyState.HostWithClient, false)]
        public void CanJoin_OnlyFromIdleOrFailed(LanLobbyState state, bool expected)
        {
            Assert.AreEqual(expected, LanLobbyStateRules.CanJoin(state));
        }

        [TestCase(LanLobbyState.Idle, false)]
        [TestCase(LanLobbyState.Failed, false)]
        [TestCase(LanLobbyState.Stopping, false)]
        [TestCase(LanLobbyState.StartingHost, true)]
        [TestCase(LanLobbyState.Listening, true)]
        [TestCase(LanLobbyState.Connecting, true)]
        [TestCase(LanLobbyState.Joined, true)]
        [TestCase(LanLobbyState.HostWithClient, true)]
        public void CanShutdown_WhileActive(LanLobbyState state, bool expected)
        {
            Assert.AreEqual(expected, LanLobbyStateRules.CanShutdown(state));
        }

        [TestCase(LanLobbyState.StartingHost, true)]
        [TestCase(LanLobbyState.Connecting, true)]
        [TestCase(LanLobbyState.Stopping, true)]
        [TestCase(LanLobbyState.Listening, false)]
        [TestCase(LanLobbyState.Idle, false)]
        [TestCase(LanLobbyState.Failed, false)]
        public void IsBusy_TracksTransitions(LanLobbyState state, bool expected)
        {
            Assert.AreEqual(expected, LanLobbyStateRules.IsBusy(state));
        }

        [Test]
        public void Describe_CoversEveryState()
        {
            foreach (LanLobbyState state in System.Enum.GetValues(typeof(LanLobbyState)))
            {
                Assert.IsFalse(string.IsNullOrEmpty(LanLobbyStateRules.Describe(state)));
            }
        }
    }
}