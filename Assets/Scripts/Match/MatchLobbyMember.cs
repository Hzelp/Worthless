using System;
using Unity.Collections;
using Unity.Netcode;
namespace Worthless
{
    public struct MatchLobbyMember : INetworkSerializable, IEquatable<MatchLobbyMember>
    {
        public ulong ClientId;
        public FixedString64Bytes Name;
        public bool IsHost;
        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref ClientId); s.SerializeValue(ref Name); s.SerializeValue(ref IsHost);
        }
        public bool Equals(MatchLobbyMember o) { return ClientId == o.ClientId && Name.Equals(o.Name) && IsHost == o.IsHost; }
        public override bool Equals(object o) { return o is MatchLobbyMember && Equals((MatchLobbyMember)o); }
        public override int GetHashCode() { return ClientId.GetHashCode() ^ Name.GetHashCode(); }
    }
}