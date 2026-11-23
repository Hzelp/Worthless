using UnityEngine;

namespace Worthless
{
    /// <summary>
    /// Gameplay 场景的显式绑定表：把地图/席位/出生点/房间/相机交给权威逻辑使用。
    /// 只在 Gameplay 场景里存在，LanLobby 不持有任何地图对象引用。
    /// </summary>
    public sealed class GameplayBinder : MonoBehaviour
    {
        [SerializeField] private SeatActor[] seatActors = new SeatActor[MatchAuthority.SeatCount];
        [SerializeField] private Transform[] spawnPoints = new Transform[MatchAuthority.SeatCount];
        [SerializeField] private GameObject[] privateRooms = new GameObject[3];
        [SerializeField] private CameraFollow2D cameraFollow;

        public SeatActor[] SeatActors { get { return seatActors; } }
        public Transform[] SpawnPoints { get { return spawnPoints; } }
        public GameObject[] PrivateRooms { get { return privateRooms; } }
        public CameraFollow2D CameraFollow { get { return cameraFollow; } }

        public void ApplyRoomVisibility(int rooms)
        {
            for (var i = 0; i < privateRooms.Length; i++)
            {
                if (privateRooms[i] != null) privateRooms[i].SetActive(rooms > i);
            }
        }

        public void HideAllSeats()
        {
            for (var i = 0; i < seatActors.Length; i++)
            {
                if (seatActors[i] != null) seatActors[i].gameObject.SetActive(false);
            }
        }
    }
}