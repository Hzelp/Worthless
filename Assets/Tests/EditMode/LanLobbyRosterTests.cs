using NUnit.Framework;
using Worthless.Lobby;

namespace Worthless.Tests
{
    public class LanLobbyRosterTests
    {
        [Test]
        public void Add_IncrementsOncePerClient()
        {
            var roster = new LanLobbyRoster();
            Assert.IsTrue(roster.Add(1));
            Assert.IsFalse(roster.Add(1));
            Assert.AreEqual(1, roster.Count);
        }

        [Test]
        public void Remove_UnknownClient_DoesNotDecrement()
        {
            var roster = new LanLobbyRoster();
            roster.Add(2);
            Assert.IsFalse(roster.Remove(99), "未完成握手的连接不应被计入扣减");
            Assert.AreEqual(1, roster.Count);
        }

        [Test]
        public void Remove_KnownClient_Decrements()
        {
            var roster = new LanLobbyRoster();
            roster.Add(1);
            roster.Add(2);
            Assert.IsTrue(roster.Remove(1));
            Assert.AreEqual(1, roster.Count);
            Assert.IsFalse(roster.Contains(1));
        }

        [Test]
        public void Clear_ResetsWithoutStaleCount()
        {
            var roster = new LanLobbyRoster();
            roster.Add(1);
            roster.Add(2);
            roster.Clear();
            Assert.AreEqual(0, roster.Count);
            Assert.AreEqual(0, roster.Ids.Count);
        }

        [Test]
        public void AddAfterRemove_NewRoundCountsFresh()
        {
            var roster = new LanLobbyRoster();
            roster.Add(1);
            roster.Remove(1);
            Assert.IsTrue(roster.Add(1), "重开后同一 ClientId 应可再次登记");
            Assert.AreEqual(1, roster.Count);
        }
    }
}