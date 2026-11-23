using UnityEngine;
using Worthless.Lobby;

namespace Worthless
{
    [CreateAssetMenu(menuName = "Worthless/EconomyConfig", fileName = "EconomyConfig")]
    public sealed class EconomyConfigAsset : ScriptableObject
    {
        public EconomyConfigData Data = new EconomyConfigData();
    }
}