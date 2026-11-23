using UnityEngine;
namespace Worthless
{
    [CreateAssetMenu(menuName = "Worthless/Seat Animation Set")]
    public sealed class SeatAnimationSet : ScriptableObject
    {
        public Sprite[] walkDown, walkDownRight, walkRight, walkUpRight, walkUp;
        public Sprite[] runDown, runDownRight, runRight, runUpRight, runUp;
        public Sprite[] Walk(int row) { return row == 0 ? walkDown : row == 1 ? walkDownRight : row == 2 ? walkRight : row == 3 ? walkUpRight : walkUp; }
        public Sprite[] Run(int row) { return row == 0 ? runDown : row == 1 ? runDownRight : row == 2 ? runRight : row == 3 ? runUpRight : runUp; }
    }
}