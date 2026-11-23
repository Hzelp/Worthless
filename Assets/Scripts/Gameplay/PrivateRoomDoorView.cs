using UnityEngine;
using UnityEngine.Tilemaps;

namespace Worthless
{
    /// <summary>
    /// 私人房门：跟随房主权威锁状态填一格门砖，并只在房主启用该门砖的 TilemapCollider2D。
    /// 客户端只显示门砖，不做本地物理；实体阻挡由房主权威碰撞与位置拦截共同保证。
    /// </summary>
    public sealed class PrivateRoomDoorView : MonoBehaviour
    {
        [SerializeField, Range(0, 2)] private int roomId;
        [SerializeField] private Tilemap doorTilemap;
        [SerializeField] private Vector3Int doorCell = new Vector3Int(0, -3, 0);
        [SerializeField] private TileBase closedTile;

        private MatchRoundDirector director;
        private MatchAuthority authority;
        private bool applied;
        private bool closed;
        private TilemapCollider2D doorCollider;

        public int RoomId { get { return roomId; } }
        public bool DoorClosed { get { return closed; } }
        public bool DoorBlocksMovement { get { return doorCollider != null && doorCollider.enabled; } }

        private void Update()
        {
            if (director == null) director = FindObjectOfType<MatchRoundDirector>();
            if (authority == null) authority = FindObjectOfType<MatchAuthority>();
            EnsureDoorCollider();
            if (doorTilemap == null)
            {
                Apply(false, false);
                return;
            }
            if (director == null || authority == null)
            {
                Apply(false, false);
                return;
            }

            var activeRoom = roomId >= 0 && roomId < authority.RoomCount;
            var inFormalMatch = authority.Started && !director.MatchOver;
            var shouldClose = activeRoom && inFormalMatch && director.IsRoomLocked(roomId);
            var shouldBlock = shouldClose && authority.IsServer;
            Apply(shouldClose, shouldBlock);
        }

        private void EnsureDoorCollider()
        {
            if (doorCollider != null || doorTilemap == null) return;
            doorCollider = doorTilemap.GetComponent<TilemapCollider2D>();
            if (doorCollider == null) doorCollider = doorTilemap.GetComponentInChildren<TilemapCollider2D>(true);
        }

        private void Apply(bool shouldClose, bool shouldBlock)
        {
            if (doorTilemap == null)
            {
                closed = false;
                if (doorCollider != null) doorCollider.enabled = false;
                return;
            }

            if (!applied || closed != shouldClose)
            {
                closed = shouldClose;
                applied = true;
                doorTilemap.SetTile(doorCell, shouldClose ? closedTile : null);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.Log("[RoomDoor] room=" + (roomId + 1) + " closed=" + closed + " block=" + shouldBlock + " server=" + (authority != null && authority.IsServer));
#endif
            }
            if (doorCollider != null && doorCollider.enabled != shouldBlock)
                doorCollider.enabled = shouldBlock;
        }

        private void OnDisable()
        {
            if (doorTilemap != null) doorTilemap.SetTile(doorCell, null);
            if (doorCollider != null) doorCollider.enabled = false;
            applied = false;
            closed = false;
        }
    }
}
