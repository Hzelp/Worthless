using System;

namespace Worthless.Lobby
{
    [Serializable]
    public struct ItemDefinition
    {
        public string Id;
        public string DisplayName;
        public string Category;
        public string Glyph;
        public int PaletteIndex;

        public ItemDefinition(string id, string displayName, string category, string glyph, int paletteIndex)
        {
            Id = id;
            DisplayName = displayName;
            Category = category;
            Glyph = glyph;
            PaletteIndex = paletteIndex;
        }
    }

    /// <summary>ITEM_DESIGN 首测 9 例；名称/外观公开，类别用于市场事件。</summary>
    [Serializable]
    public sealed class ItemCatalogData
    {
        public ItemDefinition[] Definitions = DefaultDefinitions();

        public static ItemDefinition[] DefaultDefinitions()
        {
            return new[]
            {
                new ItemDefinition("M01", "上弦钥匙", "机械", "K", 0),
                new ItemDefinition("M02", "旧怀表", "机械", "W", 0),
                new ItemDefinition("M03", "折叠罗盘", "机械", "C", 0),
                new ItemDefinition("H01", "纸灯", "手作", "L", 1),
                new ItemDefinition("H02", "漆木面具", "手作", "M", 1),
                new ItemDefinition("H03", "彩线挂毯", "手作", "T", 1),
                new ItemDefinition("C01", "斑纹矿石", "晶矿", "O", 2),
                new ItemDefinition("C02", "琥珀坠", "晶矿", "A", 2),
                new ItemDefinition("C03", "紫晶簇", "晶矿", "X", 2)
            };
        }

        public bool Validate(out string error)
        {
            error = null;
            if (Definitions == null || Definitions.Length < 9)
            {
                error = "物品定义至少需要 9 个";
                return false;
            }

            for (var i = 0; i < Definitions.Length; i++)
            {
                if (string.IsNullOrEmpty(Definitions[i].Id) || string.IsNullOrEmpty(Definitions[i].DisplayName))
                {
                    error = "物品定义缺少 ID/名称";
                    return false;
                }
            }

            return true;
        }
    }
}