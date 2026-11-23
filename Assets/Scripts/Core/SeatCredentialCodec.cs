using System;

namespace Worthless.Lobby
{
    /// <summary>局内席位凭证的编码/校验（与 NGO ClientId 无关）。</summary>
    public struct SeatCredential
    {
        public byte Seat;
        public uint GameId;
        public byte[] Token;
    }

    public static class SeatCredentialCodec
    {
        public const byte Magic = 0x57;
        public const byte Version = 1;
        public const int TokenBytes = 16;
        public const int PayloadLength = 23;

        public static byte[] Encode(SeatCredential credential)
        {
            if (credential.Token == null || credential.Token.Length != TokenBytes) return null;
            if (credential.Seat >= LobbySeatingRules.MaxRealPlayers) return null;

            var payload = new byte[PayloadLength];
            payload[0] = Magic;
            payload[1] = Version;
            payload[2] = credential.Seat;
            payload[3] = (byte)((credential.GameId >> 24) & 0xFF);
            payload[4] = (byte)((credential.GameId >> 16) & 0xFF);
            payload[5] = (byte)((credential.GameId >> 8) & 0xFF);
            payload[6] = (byte)(credential.GameId & 0xFF);
            Buffer.BlockCopy(credential.Token, 0, payload, 7, TokenBytes);
            return payload;
        }

        public static bool TryDecode(byte[] payload, out SeatCredential credential)
        {
            credential = default(SeatCredential);
            if (payload == null || payload.Length != PayloadLength) return false;
            if (payload[0] != Magic || payload[1] != Version) return false;
            if (payload[2] >= LobbySeatingRules.MaxRealPlayers) return false;

            var token = new byte[TokenBytes];
            Buffer.BlockCopy(payload, 7, token, 0, TokenBytes);
            credential.Seat = payload[2];
            credential.GameId = ((uint)payload[3] << 24) | ((uint)payload[4] << 16) | ((uint)payload[5] << 8) | payload[6];
            credential.Token = token;
            return true;
        }

        /// <summary>凭证必须同时匹配当前局、席位与令牌，且长度正确。</summary>
        public static bool Matches(SeatCredential credential, uint gameId, byte seat, byte[] expectedToken)
        {
            if (credential.Token == null || expectedToken == null) return false;
            if (credential.Token.Length != TokenBytes || expectedToken.Length != TokenBytes) return false;
            if (credential.GameId != gameId || credential.Seat != seat) return false;

            var diff = 0;
            for (var i = 0; i < TokenBytes; i++)
            {
                diff |= credential.Token[i] ^ expectedToken[i];
            }

            return diff == 0;
        }

        public static string ToHex(byte[] bytes)
        {
            if (bytes == null) return string.Empty;
            var chars = new char[bytes.Length * 2];
            const string digits = "0123456789abcdef";
            for (var i = 0; i < bytes.Length; i++)
            {
                chars[i * 2] = digits[bytes[i] >> 4];
                chars[i * 2 + 1] = digits[bytes[i] & 0xF];
            }

            return new string(chars);
        }
    }
}
