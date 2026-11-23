using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Worthless.Lobby
{
    /// <summary>局域网 IPv4 地址枚举与过滤（纯逻辑部分可测试）。</summary>
    public static class LocalAddresses
    {
        public static List<string> CollectIPv4(bool includeLoopback)
        {
            var result = new List<string>();
            try
            {
                var nics = NetworkInterface.GetAllNetworkInterfaces();
                for (var i = 0; i < nics.Length; i++)
                {
                    var nic = nics[i];
                    if (nic == null) continue;
                    if (nic.OperationalStatus != OperationalStatus.Up) continue;
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                    IPInterfaceProperties props;
                    try
                    {
                        props = nic.GetIPProperties();
                    }
                    catch (NetworkInformationException)
                    {
                        continue;
                    }

                    if (props == null) continue;
                    var unicast = props.UnicastAddresses;
                    for (var j = 0; j < unicast.Count; j++)
                    {
                        var entry = unicast[j];
                        if (entry == null) continue;
                        if (entry.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        var text = entry.Address.ToString();
                        if (!result.Contains(text)) result.Add(text);
                    }
                }
            }
            catch (NetworkInformationException)
            {
            }
            catch (PlatformNotSupportedException)
            {
            }

            if (includeLoopback && !result.Contains(EndpointRules.LoopbackAddress))
            {
                result.Add(EndpointRules.LoopbackAddress);
            }

            return result;
        }

        /// <summary>把候选字符串过滤为去重后的 IPv4 列表，保持输入顺序。</summary>
        public static List<string> FilterIPv4Candidates(IEnumerable<string> raw)
        {
            var result = new List<string>();
            if (raw == null) return result;

            foreach (var item in raw)
            {
                if (string.IsNullOrEmpty(item)) continue;
                IPAddress parsed;
                if (!IPAddress.TryParse(item.Trim(), out parsed)) continue;
                if (parsed.AddressFamily != AddressFamily.InterNetwork) continue;
                var text = parsed.ToString();
                if (!result.Contains(text)) result.Add(text);
            }

            return result;
        }

        public static string DescribeLanAddresses()
        {
            var list = CollectIPv4(true);
            return "本机 IPv4: " + string.Join(" / ", list.ToArray());
        }
    }
}