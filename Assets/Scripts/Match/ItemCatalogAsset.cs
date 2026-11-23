using UnityEngine;
using Worthless.Lobby;

namespace Worthless
{
    [CreateAssetMenu(menuName = "Worthless/ItemCatalog", fileName = "ItemCatalog")]
    public sealed class ItemCatalogAsset : ScriptableObject
    {
        public ItemCatalogData Data = new ItemCatalogData();
    }
}