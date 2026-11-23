using System.Net;
using NUnit.Framework;
using Worthless.Lobby;

namespace Worthless.Tests
{
    public class EndpointRulesTests
    {
        [TestCase("127.0.0.1", true)]
        [TestCase("192.168.1.10", true)]
        [TestCase("0.0.0.0", true)]
        [TestCase(" 10.0.0.5 ", true)]
        [TestCase("192.168.1", false)]
        [TestCase("host.local", false)]
        [TestCase("", false)]
        [TestCase("999.1.1.1", false)]
        [TestCase("::1", false)]
        public void TryParseIPv4_Validates(string input, bool expected)
        {
            IPAddress address;
            var ok = EndpointRules.TryParseIPv4(input, out address);
            Assert.AreEqual(expected, ok);
            if (expected)
            {
                Assert.IsNotNull(address);
                Assert.AreEqual(System.Net.Sockets.AddressFamily.InterNetwork, address.AddressFamily);
            }
        }

        [TestCase("7777", true, 7777)]
        [TestCase(" 1024 ", true, 1024)]
        [TestCase("1", true, 1)]
        [TestCase("65535", true, 65535)]
        [TestCase("0", false, 0)]
        [TestCase("65536", false, 0)]
        [TestCase("-1", false, 0)]
        [TestCase("abc", false, 0)]
        [TestCase("", false, 0)]
        public void TryParsePort_Validates(string input, bool expected, int expectedPort)
        {
            ushort port;
            var ok = EndpointRules.TryParsePort(input, out port);
            Assert.AreEqual(expected, ok);
            Assert.AreEqual((ushort)expectedPort, port);
        }

        [TestCase("192.168.0.20", "7777", true)]
        [TestCase("127.0.0.1", "7777", true)]
        [TestCase("0.0.0.0", "7777", false)]
        [TestCase("192.168.0.20", "0", false)]
        [TestCase("", "7777", false)]
        public void TryValidateJoinTarget_RejectsWildcardAndBadInput(string address, string port, bool expected)
        {
            string normalizedAddress;
            ushort normalizedPort;
            string error;
            var ok = EndpointRules.TryValidateJoinTarget(address, port, out normalizedAddress, out normalizedPort, out error);
            Assert.AreEqual(expected, ok);
            if (expected)
            {
                Assert.IsNull(error);
                Assert.AreEqual(address.Trim(), normalizedAddress);
                Assert.AreEqual(ushort.Parse(port), normalizedPort);
            }
            else
            {
                Assert.IsFalse(string.IsNullOrEmpty(error));
            }
        }

        [Test]
        public void Format_ProducesAddressColonPort()
        {
            Assert.AreEqual("192.168.1.5:7777", EndpointRules.Format("192.168.1.5", 7777));
            Assert.AreEqual("127.0.0.1:1", EndpointRules.Format("127.0.0.1", 1));
        }

        [Test]
        public void IsWildcardAndLoopback_DetectAddressKinds()
        {
            Assert.IsTrue(EndpointRules.IsWildcard(IPAddress.Parse("0.0.0.0")));
            Assert.IsFalse(EndpointRules.IsWildcard(IPAddress.Parse("127.0.0.1")));
            Assert.IsTrue(EndpointRules.IsLoopback(IPAddress.Parse("127.0.0.1")));
            Assert.IsFalse(EndpointRules.IsLoopback(IPAddress.Parse("192.168.1.5")));
        }

        [TestCase("192.168.31.72", "255.255.255.0", "192.168.31.255")]
        [TestCase("10.18.158.45", "255.255.0.0", "10.18.255.255")]
        [TestCase("192.168.31.72", "255.255.255.192", "192.168.31.127")]
        [TestCase("192.168.31.72", "255.255.255.252", "192.168.31.75")]
        [TestCase("192.168.31.72", "255.255.255.254", null)]
        [TestCase("192.168.31.72", "255.255.255.255", null)]
        [TestCase("192.168.31.72", "255.0.255.0", null)]
        [TestCase("192.168.31.72", "0.0.0.0", null)]
        [TestCase("127.0.0.1", "255.0.0.0", null)]
        [TestCase("0.0.0.0", "255.255.255.0", null)]
        [TestCase("::1", "255.255.255.0", null)]
        public void DirectedBroadcast_UsesTheInterfaceSubnet(string local, string mask, string expected)
        {
            var ok = EndpointRules.TryGetDirectedBroadcast(IPAddress.Parse(local), IPAddress.Parse(mask), out var target);
            Assert.AreEqual(expected != null, ok);
            Assert.AreEqual(expected, target?.ToString());
        }
    }
}
