using System.Net;
using System.Net.Sockets;

namespace Worthless.Lobby
{
    /// <summary>
    /// 纯逻辑端点解析与校验，不依赖 UnityEngine，便于 EditMode 测试。
    /// </summary>
    public static class EndpointRules
    {
        public const ushort MinPort = 1;
        public const ushort MaxPort = 65535;
        public const string WildcardAddress = "0.0.0.0";
        public const string LoopbackAddress = "127.0.0.1";
        public const string EmptyAddressError = "地址不能为空";
        public const string InvalidAddressError = "请输入合法的 IPv4 地址，例如 192.168.1.10";
        public const string EmptyPortError = "端口不能为空";
        public const string InvalidPortError = "端口必须是 1-65535 的整数";
        public const string WildcardJoinError = "0.0.0.0 只能用于监听，不能作为加入目标";

        public static bool TryParseIPv4(string text, out IPAddress address)
        {
            string ignored;
            return TryParseIPv4(text, out address, out ignored);
        }

        public static bool TryParseIPv4(string text, out IPAddress address, out string error)
        {
            address = null;
            error = null;
            if (string.IsNullOrEmpty(text) || text.Trim().Length == 0)
            {
                error = EmptyAddressError;
                return false;
            }

            var trimmed = text.Trim();
            if (!IsStrictDottedQuad(trimmed))
            {
                error = InvalidAddressError;
                return false;
            }

            address = IPAddress.Parse(trimmed);
            return true;
        }

        public static bool TryParsePort(string text, out ushort port)
        {
            string ignored;
            return TryParsePort(text, out port, out ignored);
        }

        public static bool TryParsePort(string text, out ushort port, out string error)
        {
            port = 0;
            error = null;
            if (string.IsNullOrEmpty(text) || text.Trim().Length == 0)
            {
                error = EmptyPortError;
                return false;
            }

            ushort parsed;
            if (!ushort.TryParse(text.Trim(), out parsed) || parsed < MinPort)
            {
                error = InvalidPortError;
                return false;
            }

            port = parsed;
            return true;
        }

        /// <summary>校验客机加入目标，拒绝通配地址与非法端口。</summary>
        public static bool TryValidateJoinTarget(string addressText, string portText, out string address, out ushort port, out string error)
        {
            address = null;
            port = 0;
            error = null;

            IPAddress parsedAddress;
            if (!TryParseIPv4(addressText, out parsedAddress, out error))
            {
                return false;
            }

            if (parsedAddress.Equals(IPAddress.Any))
            {
                error = WildcardJoinError;
                return false;
            }

            if (!TryParsePort(portText, out port, out error))
            {
                return false;
            }

            address = parsedAddress.ToString();
            return true;
        }

        /// <summary>IPAddress.TryParse 会接受 192.168.1 这类简写，这里要求严格四段十进制。</summary>
        public static bool IsStrictDottedQuad(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            var parts = text.Split('.');
            if (parts.Length != 4) return false;

            for (var i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                if (part.Length == 0 || part.Length > 3) return false;
                if (part.Length > 1 && part[0] == '0') return false;

                var value = 0;
                for (var j = 0; j < part.Length; j++)
                {
                    var c = part[j];
                    if (c < '0' || c > '9') return false;
                    value = value * 10 + (c - '0');
                }

                if (value > 255) return false;
            }

            return true;
        }

        public static string Format(string address, ushort port)
        {
            return address + ":" + port;
        }

        public static bool TryGetDirectedBroadcast(IPAddress local, IPAddress mask, out IPAddress broadcast)
        {
            broadcast = null;
            if (local?.AddressFamily != AddressFamily.InterNetwork || mask?.AddressFamily != AddressFamily.InterNetwork
                || IsWildcard(local) || IsLoopback(local)) return false;

            var addressBytes = local.GetAddressBytes();
            var maskBytes = mask.GetAddressBytes();
            uint hostBits = 0;
            for (var i = 0; i < maskBytes.Length; i++) hostBits = (hostBits << 8) | (byte)~maskBytes[i];
            // /0, /31, /32 and non-contiguous masks have no usable subnet broadcast here.
            if (hostBits <= 1 || hostBits == uint.MaxValue || (hostBits & (hostBits + 1)) != 0) return false;

            for (var i = 0; i < addressBytes.Length; i++) addressBytes[i] |= (byte)~maskBytes[i];
            broadcast = new IPAddress(addressBytes);
            return true;
        }

        public static bool IsWildcard(IPAddress address)
        {
            return address != null && address.Equals(IPAddress.Any);
        }

        public static bool IsLoopback(IPAddress address)
        {
            return address != null && IPAddress.IsLoopback(address);
        }
    }
}
