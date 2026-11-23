using Unity.Netcode;
using UnityEngine;
namespace Worthless
{
    public sealed partial class MatchRoundDirector
    {
        [SerializeField] private Vector2[] roomCenters = { new Vector2(-14f, 10f), new Vector2(14f, 10f), new Vector2(0f, -10f) };
        [SerializeField] private Vector2[] roomHalfExtents = { new Vector2(4f, 3f), new Vector2(4f, 3f), new Vector2(4f, 3f) };
        private readonly NetworkVariable<bool> room0Locked = new NetworkVariable<bool>(false);
        private readonly NetworkVariable<bool> room1Locked = new NetworkVariable<bool>(false);
        private readonly NetworkVariable<bool> room2Locked = new NetworkVariable<bool>(false);
        private readonly NetworkList<int> seatRoom = new NetworkList<int>();
        private readonly int[] lastRoom = new int[MatchAuthority.SeatCount];
        private readonly int[] lastAuthorizedRoom = new int[MatchAuthority.SeatCount];
        private readonly Vector2[] lastRoomPos = new Vector2[MatchAuthority.SeatCount];
        private readonly bool[] hasRoomPos = new bool[MatchAuthority.SeatCount];
        private readonly float[] blockedLogAt = new float[MatchAuthority.SeatCount];

        public const int RoomCount = 3;
        public int LocalRoomId
        {
            get
            {
                if (!IsSpawned || authority == null || authority.LocalSeat < 0 || authority.LocalSeat >= seatRoom.Count) return -1;
                return seatRoom[authority.LocalSeat];
            }
        }
        public int AuthoritativeRoomAt(Vector2 position) { return RoomAt(position); }

        public bool TryGetRoomGeometry(int room, out Vector2 center, out Vector2 half)
        {
            center = Vector2.zero; half = Vector2.zero;
            if (room < 0 || room >= RoomCount || roomCenters == null || roomHalfExtents == null
                || room >= roomCenters.Length || room >= roomHalfExtents.Length) return false;
            center = roomCenters[room]; half = roomHalfExtents[room];
            return half.x > 0.01f && half.y > 0.01f;
        }

        public bool IsRoomLocked(int room) { return room == 0 ? room0Locked.Value : room == 1 ? room1Locked.Value : room == 2 && room2Locked.Value; }
        public int RoomOfSeat(int seat) { return seat >= 0 && seat < seatRoom.Count ? seatRoom[seat] : -1; }

        private void InitializeRoomState()
        {
            seatRoom.Clear();
            for (var i = 0; i < MatchAuthority.SeatCount; i++)
            {
                seatRoom.Add(-1); lastRoom[i] = -1; lastAuthorizedRoom[i] = -1; hasRoomPos[i] = false;
            }
            room0Locked.Value = false; room1Locked.Value = false; room2Locked.Value = false;
        }

        private int ActiveRoomLimit
        {
            get
            {
                var count = authority != null ? authority.RoomCount : 0;
                return Mathf.Clamp(count, 0, RoomCount);
            }
        }

        private int RoomAt(Vector2 p)
        {
            for (var i = 0; i < ActiveRoomLimit; i++)
            {
                if (roomCenters == null || roomHalfExtents == null || i >= roomCenters.Length || i >= roomHalfExtents.Length) continue;
                var h = roomHalfExtents[i];
                if (Mathf.Abs(p.x - roomCenters[i].x) <= h.x && Mathf.Abs(p.y - roomCenters[i].y) <= h.y) return i;
            }
            return -1;
        }

        public void TickRoomsForAuthority()
        {
            if (!IsServer || !initialized || authority == null || machine == null) return;
            var active = ActiveRoomLimit;
            for (var i = 0; i < MatchAuthority.SeatCount; i++)
            {
                if (i >= tier) { SetSeatRoom(i, -1); lastAuthorizedRoom[i] = -1; continue; }
                bool isNpc; bool isOnline; ulong bound;
                if (!authority.TryGetSeatInfo(i, out isNpc, out isOnline, out bound) || isNpc || !isOnline || !alive[i] || authority.IsSpectator(i))
                {
                    SetSeatRoom(i, -1); lastRoom[i] = -1; continue;
                }
                if (!authority.TryGetSeatPosition(i, out var pos)) { SetSeatRoom(i, -1); continue; }
                if (!hasRoomPos[i]) { lastRoomPos[i] = pos; hasRoomPos[i] = true; }
                var candidate = pos;
                var room = RoomAt(candidate);
                if (room >= 0 && room < active && lastRoom[i] < 0 && IsRoomLocked(room)
                    && lastAuthorizedRoom[i] == room && RoomAt(lastRoomPos[i]) == room)
                {
                    lastRoomPos[i] = pos; SetSeatRoom(i, room); continue;
                }
                if (room >= 0 && room < active && lastRoom[i] != room && IsRoomLocked(room))
                {
                    candidate = lastRoomPos[i];
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    if (Time.time >= blockedLogAt[i] + 1f)
                    {
                        blockedLogAt[i] = Time.time;
                        Debug.Log("[Room] blocked seat=" + (i + 1) + " candidate=" + pos + " corrected=" + candidate + " room=" + (room + 1));
                    }
#endif
                    var actor = authority.GetSeatActor(i);
                    if (actor != null)
                    {
                        if (actor.Body != null) { actor.Body.position = candidate; actor.Body.velocity = Vector2.zero; }
                        actor.transform.position = candidate;
                    }
                    room = RoomAt(candidate);
                }
                lastRoomPos[i] = candidate;
                SetSeatRoom(i, room);
                if (room >= 0) lastAuthorizedRoom[i] = room;
                else lastAuthorizedRoom[i] = -1;
            }
            for (var r = 0; r < RoomCount; r++)
            {
                if (r >= active || !IsRoomLocked(r)) continue;
                var occupied = false;
                for (var i = 0; i < tier; i++)
                    if (RoomOfSeat(i) == r) { occupied = true; break; }
                if (!occupied) SetRoomLocked(r, false);
            }
        }

        private void SetSeatRoom(int seat, int room)
        {
            if (lastRoom[seat] != room) { lastRoom[seat] = room; if (seat < seatRoom.Count) seatRoom[seat] = room; }
        }

        public bool TryResolveRoomNow(int seat, out int room)
        {
            room = -1;
            if (authority == null || seat < 0 || seat >= tier || !alive[seat] || authority.IsSpectator(seat)) return false;
            bool isNpc; bool isOnline; ulong bound;
            if (!authority.TryGetSeatInfo(seat, out isNpc, out isOnline, out bound) || isNpc || !isOnline) return false;
            if (!authority.TryGetSeatPosition(seat, out var pos)) return false;
            room = RoomAt(pos);
            if (room < 0 || room >= ActiveRoomLimit) return false;
            if (IsRoomLocked(room) && RoomOfSeat(seat) != room) return false;
            return true;
        }

        private void SetRoomLocked(int room, bool locked)
        {
            if (room == 0) room0Locked.Value = locked;
            else if (room == 1) room1Locked.Value = locked;
            else if (room == 2) room2Locked.Value = locked;
        }

        public void ToggleRoomLock()
        {
            if (!IsSpawned || authority == null || authority.LocalSeat < 0) return;
            var room = LocalRoomId;
            if (room < 0) return;
            RequestRoomLockServerRpc(authority.GameId, room, !IsRoomLocked(room));
        }

        [ServerRpc(RequireOwnership = false)]
        private void RequestRoomLockServerRpc(uint gameId, int room, bool locked, ServerRpcParams rpcParams = default)
        {
            if (!IsServer || authority == null || machine == null || !initialized || !authority.Started) return;
            if (gameId != authority.GameId || room < 0 || room >= RoomCount) return;
            var seat = SeatForClient(rpcParams.Receive.SenderClientId);
            int nowRoom;
            if (!TryResolveRoomNow(seat, out nowRoom) || nowRoom != room)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.Log("[Room] reject lock seat=" + (seat + 1) + " room=" + (room + 1));
#endif
                return;
            }
            if (seat < seatRoom.Count && seatRoom[seat] != nowRoom) seatRoom[seat] = nowRoom;
            lastRoom[seat] = nowRoom;
            SetRoomLocked(room, locked);
            Debug.Log("[Room] lock room=" + (room + 1) + " locked=" + locked + " seat=" + (seat + 1));
        }
    }
}