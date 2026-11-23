using System;
using Unity.Netcode;

namespace Worthless
{
    /// <summary>公开席位快照：只含玩法可见信息（席位、在线、位置、速度），不含物品/价值。</summary>
    [Serializable]
    public struct SeatSnapshot : INetworkSerializable, IEquatable<SeatSnapshot>
    {
        public const byte FlagNpc = 1;
        public const byte FlagOnline = 2;
        public const byte FlagReal = 4;
        public const byte FlagRunning = 8;
        public const byte FlagEliminated = 16;

        public byte Seat;
        public byte Flags;
        public float X;
        public float Y;
        public float VX;
        public float VY;

        public bool IsNpc { get { return (Flags & FlagNpc) != 0; } }
        public bool IsOnline { get { return (Flags & FlagOnline) != 0; } }
        public bool IsReal { get { return (Flags & FlagReal) != 0; } }
        public bool IsRunning { get { return (Flags & FlagRunning) != 0; } }
        public bool IsEliminated { get { return (Flags & FlagEliminated) != 0; } }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Seat);
            serializer.SerializeValue(ref Flags);
            serializer.SerializeValue(ref X);
            serializer.SerializeValue(ref Y);
            serializer.SerializeValue(ref VX);
            serializer.SerializeValue(ref VY);
        }

        public bool Equals(SeatSnapshot other)
        {
            return Seat == other.Seat && Flags == other.Flags
                && X.Equals(other.X) && Y.Equals(other.Y)
                && VX.Equals(other.VX) && VY.Equals(other.VY);
        }

        public override bool Equals(object obj)
        {
            return obj is SeatSnapshot && Equals((SeatSnapshot)obj);
        }

        public override int GetHashCode()
        {
            return Seat.GetHashCode() ^ Flags.GetHashCode() ^ X.GetHashCode() ^ Y.GetHashCode();
        }
    }
}