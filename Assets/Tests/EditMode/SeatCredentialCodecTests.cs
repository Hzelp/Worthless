using NUnit.Framework;
using Worthless.Lobby;

namespace Worthless.Tests
{
    public class SeatCredentialCodecTests
    {
        private static byte[] Token(byte seed)
        {
            var token = new byte[SeatCredentialCodec.TokenBytes];
            for (var i = 0; i < token.Length; i++) token[i] = (byte)(seed + i);
            return token;
        }

        [TestCase(3)]
        [TestCase(9)]
        public void EncodeDecode_RoundTrips(byte seat)
        {
            var original = new SeatCredential { Seat = seat, GameId = 0xDEADBEEF, Token = Token(10) };
            var payload = SeatCredentialCodec.Encode(original);
            Assert.AreEqual(SeatCredentialCodec.PayloadLength, payload.Length);

            SeatCredential decoded;
            Assert.IsTrue(SeatCredentialCodec.TryDecode(payload, out decoded));
            Assert.AreEqual(original.Seat, decoded.Seat);
            Assert.AreEqual(original.GameId, decoded.GameId);
            CollectionAssert.AreEqual(original.Token, decoded.Token);
        }

        [TestCase(new byte[0])]
        [TestCase(new byte[] { 0x57, 0x02, 0, 0, 0, 0, 0 })]
        [TestCase(new byte[] { 0x00, 0x01, 0, 0, 0, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 })]
        public void TryDecode_RejectsMalformed(byte[] payload)
        {
            SeatCredential decoded;
            Assert.IsFalse(SeatCredentialCodec.TryDecode(payload, out decoded));
        }

        [Test]
        public void Matches_RequiresGameSeatAndToken()
        {
            var credential = new SeatCredential { Seat = 2, GameId = 42, Token = Token(1) };
            Assert.IsTrue(SeatCredentialCodec.Matches(credential, 42, 2, Token(1)));
            Assert.IsFalse(SeatCredentialCodec.Matches(credential, 43, 2, Token(1)));
            Assert.IsFalse(SeatCredentialCodec.Matches(credential, 42, 3, Token(1)));
            Assert.IsFalse(SeatCredentialCodec.Matches(credential, 42, 2, Token(2)));
        }

        [Test]
        public void Matches_RejectsWrongTokenLength()
        {
            var credential = new SeatCredential { Seat = 0, GameId = 7, Token = new byte[4] };
            Assert.IsFalse(SeatCredentialCodec.Matches(credential, 7, 0, Token(1)));
        }
    }
}
