using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using UnityEngine;
using Worthless.Lobby;
namespace Worthless
{
    [Serializable]
    public sealed class LanRoomInfo
    {
        public int v; public string n; public ushort p; public int t; public int r; public int s;
        public string sid; public string nonce; public string Address; public long SeenTicks;
    }

    public sealed class LanDiscoveryService : MonoBehaviour
    {
        public const int DiscoveryPort = 47777;
        private const int Protocol = 1;
        private const string QueryPrefix = "WORTHLESS_DISCOVER_V1:";
        private const int PacketBudget = 32;
        private const long RoomLifetimeTicks = 5 * TimeSpan.TicksPerSecond;
        private UdpClient hostSocket;
        private UdpClient browseSocket;
        private readonly List<(UdpClient Socket, IPAddress Broadcast)> subnetSockets = new List<(UdpClient, IPAddress)>();
        private float nextSearch;
        private string sessionId = string.Empty;
        private string queryNonce = string.Empty;
        private readonly Dictionary<string, LanRoomInfo> results = new Dictionary<string, LanRoomInfo>();
        private readonly List<string> expired = new List<string>();
        public event Action Changed;
        public bool Browsing { get; private set; }
        public IReadOnlyCollection<LanRoomInfo> Results { get { return results.Values; } }
        public string HostName = "Worthless 房间";
        public ushort GamePort = 7777;
        public int Tier = 5;
        public int Real = 1;
        public bool Started;

        public bool StartHost(string roomName, ushort gamePort)
        {
            HostName = roomName;
            GamePort = gamePort;
            StopHost();
            try
            {
                hostSocket = new UdpClient();
                hostSocket.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                hostSocket.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
                hostSocket.Client.Blocking = false;
                sessionId = Guid.NewGuid().ToString("N");
                Debug.Log("[Discovery] host listen " + DiscoveryPort + " room=" + roomName + " game=" + gamePort);
                return true;
            }
            catch (SocketException e)
            {
                StopHost();
                Debug.LogWarning("[Discovery] host bind failed: " + e.SocketErrorCode);
                return false;
            }
        }

        public void StopHost() { if (hostSocket != null) { hostSocket.Close(); hostSocket = null; } }

        public void StartBrowse()
        {
            StopBrowse();
            results.Clear();
            try
            {
                browseSocket = OpenBrowseSocket(IPAddress.Any);
            }
            catch (SocketException e)
            {
                Debug.LogWarning("[Discovery] browse bind failed: " + e.SocketErrorCode);
                return;
            }

            OpenSubnetSockets();
            Browsing = true;
            queryNonce = Guid.NewGuid().ToString("N");
            nextSearch = 0f;
        }

        public void StopBrowse()
        {
            Browsing = false;
            browseSocket?.Close();
            browseSocket = null;
            foreach (var subnet in subnetSockets) subnet.Socket.Close();
            subnetSockets.Clear();
        }
        public void Clear() { results.Clear(); if (Changed != null) Changed(); }

        private static UdpClient OpenBrowseSocket(IPAddress address)
        {
            var socket = new UdpClient(new IPEndPoint(address, 0));
            try
            {
                socket.Client.Blocking = false;
                socket.EnableBroadcast = true;
                return socket;
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        private void OpenSubnetSockets()
        {
            try
            {
                var sources = new HashSet<IPAddress>();
                foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (adapter.OperationalStatus != OperationalStatus.Up || !adapter.Supports(NetworkInterfaceComponent.IPv4)) continue;
                    foreach (var ip in adapter.GetIPProperties().UnicastAddresses)
                    {
                        if (ip.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        if (!EndpointRules.TryGetDirectedBroadcast(ip.Address, ip.IPv4Mask, out var target) || !sources.Add(ip.Address)) continue;
                        try
                        {
                            subnetSockets.Add((OpenBrowseSocket(ip.Address), target));
                            Debug.Log("[Discovery] browse subnet " + ip.Address + " -> " + target + ":" + DiscoveryPort);
                        }
                        catch (SocketException e)
                        {
                            Debug.LogWarning("[Discovery] subnet bind failed: " + ip.Address + " " + e.SocketErrorCode);
                        }
                    }
                }
            }
            catch (NetworkInformationException e)
            {
                Debug.LogWarning("[Discovery] IPv4 interface enumeration failed: " + e.Message);
            }
        }

        private void Update()
        {
            if (hostSocket != null) ReplyToQueries();
            if (!Browsing || browseSocket == null) return;
            if (Time.unscaledTime >= nextSearch)
            {
                nextSearch = Time.unscaledTime + 1f;
                var query = Encoding.UTF8.GetBytes(QueryPrefix + queryNonce);
                foreach (var subnet in subnetSockets) TrySend(subnet.Socket, query, new IPEndPoint(subnet.Broadcast, DiscoveryPort));
                TrySend(browseSocket, query, new IPEndPoint(IPAddress.Broadcast, DiscoveryPort));
                TrySend(browseSocket, query, new IPEndPoint(IPAddress.Loopback, DiscoveryPort));
            }

            var changed = false;
            foreach (var subnet in subnetSockets) changed |= ReadRooms(subnet.Socket);
            changed |= ReadRooms(browseSocket);
            expired.Clear();
            var now = DateTime.UtcNow.Ticks;
            foreach (var pair in results)
                if (now - pair.Value.SeenTicks > RoomLifetimeTicks) expired.Add(pair.Key);
            foreach (var key in expired) results.Remove(key);
            if (changed || expired.Count > 0) Changed?.Invoke();
        }

        private void ReplyToQueries()
        {
            for (var i = 0; i < PacketBudget && TryReceive(hostSocket, out var bytes, out var from); i++)
            {
                if (bytes.Length > 256) continue;
                var query = Encoding.UTF8.GetString(bytes);
                if (!query.StartsWith(QueryPrefix, StringComparison.Ordinal)) continue;
                var nonce = query.Substring(QueryPrefix.Length);
                if (!Guid.TryParseExact(nonce, "N", out _)) continue;
                var packet = new LanRoomInfo
                {
                    v = Protocol, n = HostName, p = GamePort, t = Tier, r = Real,
                    s = Started ? 1 : 0, sid = sessionId, nonce = nonce
                };
                TrySend(hostSocket, Encoding.UTF8.GetBytes(JsonUtility.ToJson(packet)), from);
            }
        }

        private bool ReadRooms(UdpClient socket)
        {
            var changed = false;
            for (var i = 0; i < PacketBudget && TryReceive(socket, out var bytes, out var from); i++)
            {
                if (bytes.Length > 512) continue;
                LanRoomInfo info;
                try { info = JsonUtility.FromJson<LanRoomInfo>(Encoding.UTF8.GetString(bytes)); }
                catch (ArgumentException) { continue; }
                if (info == null || info.v != Protocol || string.IsNullOrEmpty(info.n) || info.p == 0 || info.nonce != queryNonce) continue;
                info.Address = from.Address.ToString();
                info.SeenTicks = DateTime.UtcNow.Ticks;
                var key = string.IsNullOrEmpty(info.sid) ? info.Address + ":" + info.p : info.sid;
                if (results.TryGetValue(key, out var current))
                {
                    // Keep the selected endpoint stable across replies from multiple interfaces.
                    var differentEndpoint = current.Address != info.Address;
                    var preferNew = (current.Address == EndpointRules.LoopbackAddress && !IPAddress.IsLoopback(from.Address))
                        || info.SeenTicks - current.SeenTicks > RoomLifetimeTicks;
                    if (differentEndpoint && !preferNew)
                    {
                        info.Address = current.Address;
                        info.SeenTicks = current.SeenTicks;
                    }
                    changed |= current.Address != info.Address || current.n != info.n || current.p != info.p
                        || current.t != info.t || current.r != info.r || current.s != info.s;
                }
                else
                {
                    if (results.Count >= PacketBudget) continue;
                    changed = true;
                    Debug.Log("[Discovery] found " + info.Address + ":" + info.p + " via " + socket.Client.LocalEndPoint);
                }
                results[key] = info;
            }
            return changed;
        }

        private static bool TryReceive(UdpClient socket, out byte[] bytes, out IPEndPoint from)
        {
            bytes = null;
            from = null;
            try
            {
                if (socket.Available == 0) return false;
                bytes = socket.Receive(ref from);
                return true;
            }
            catch (SocketException) { return false; }
        }

        private static void TrySend(UdpClient socket, byte[] bytes, IPEndPoint target)
        {
            try { socket.Send(bytes, bytes.Length, target); }
            catch (SocketException) { }
        }

        private void OnDestroy() { StopHost(); StopBrowse(); }
    }
}
