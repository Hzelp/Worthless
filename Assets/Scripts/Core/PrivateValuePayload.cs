using System;

namespace Worthless.Lobby
{
    /// <summary>私有价值载荷（仅目标客户端；字段校验版本/局/轮/实例/席位）。</summary>
    [Serializable]
    public struct PrivateValuePayload
    {
        public uint GameId;
        public int Round;
        public int ItemInstanceId;
        public int OwnershipSeat;
        public long Value;
        public int ValueVersion;
        public bool Eliminated;

        public bool IsValidFor(uint gameId, int round, int instanceId, int seat)
        {
            return GameId == gameId && Round == round && ItemInstanceId == instanceId
                && OwnershipSeat == seat && ValueVersion > 0;
        }
    }

    /// <summary>公开淘汰快照（公开价在淘汰时固定）。</summary>
    [Serializable]
    public struct EliminationRecord
    {
        public int Round;
        public int Seat;
        public int ItemInstanceId;
        public string ItemDefinitionId;
        public long PublicValue;

        public EliminationRecord(int round, int seat, int itemInstanceId, string defId, long publicValue)
        {
            Round = round;
            Seat = seat;
            ItemInstanceId = itemInstanceId;
            ItemDefinitionId = defId;
            PublicValue = publicValue;
        }
    }
}