using System;
using Unity.Netcode;

namespace Worthless
{
    public struct PublicItemState : INetworkSerializable, IEquatable<PublicItemState>
    {
        public int Seat;
        public int InstanceId;
        public int DefIndex;
        public int IsNpc;
        public int ValueVersion;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Seat);
            serializer.SerializeValue(ref InstanceId);
            serializer.SerializeValue(ref DefIndex);
            serializer.SerializeValue(ref IsNpc);
            serializer.SerializeValue(ref ValueVersion);
        }

        public bool Equals(PublicItemState other)
        {
            return Seat == other.Seat && InstanceId == other.InstanceId && DefIndex == other.DefIndex
                && IsNpc == other.IsNpc
                && ValueVersion == other.ValueVersion;
        }
    }

    public struct TradePublicState : INetworkSerializable, IEquatable<TradePublicState>
    {
        public int Seat;
        public int Exhausted;
        public int Tradable;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Seat);
            serializer.SerializeValue(ref Exhausted);
            serializer.SerializeValue(ref Tradable);
        }

        public bool Equals(TradePublicState other) { return Seat == other.Seat && Exhausted == other.Exhausted && Tradable == other.Tradable; }
    }

}