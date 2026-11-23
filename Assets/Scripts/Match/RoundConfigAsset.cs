using UnityEngine;
using Worthless.Lobby;

namespace Worthless
{
    [CreateAssetMenu(menuName = "Worthless/RoundConfig", fileName = "RoundConfig")]
    public sealed class RoundConfigAsset : ScriptableObject
    {
        public RoundConfigData Data = new RoundConfigData();
    }
}