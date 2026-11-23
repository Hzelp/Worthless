using System.Collections.Generic;
using NUnit.Framework;
using Worthless.Lobby;

namespace Worthless.Tests
{
    public class LocalAddressesTests
    {
        [Test]
        public void FilterIPv4Candidates_KeepsOrderDedupesAndDropsInvalid()
        {
            var input = new List<string> { "192.168.1.10", "10.0.0.2", "192.168.1.10", " 127.0.0.1 ", "not-an-ip", "::1", "" };
            var filtered = LocalAddresses.FilterIPv4Candidates(input);
            CollectionAssert.AreEqual(new[] { "192.168.1.10", "10.0.0.2", "127.0.0.1" }, filtered);
        }

        [Test]
        public void FilterIPv4Candidates_NullInput_ReturnsEmpty()
        {
            Assert.AreEqual(0, LocalAddresses.FilterIPv4Candidates(null).Count);
        }

        [Test]
        public void CollectIPv4_WithLoopback_ContainsLoopbackAddress()
        {
            var addresses = LocalAddresses.CollectIPv4(true);
            CollectionAssert.Contains(addresses, EndpointRules.LoopbackAddress);
            Assert.AreEqual(addresses.Count, LocalAddresses.FilterIPv4Candidates(addresses).Count);
        }
    }
}