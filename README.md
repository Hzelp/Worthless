# Worthless

**游趣无限2026/VGG2026作品**

[下载 Worthless 宣传片（MP4）](https://github.com/Hzelp/Worthless/releases/download/pv-2026/WorthlessPV.mp4)

<img src="Assets/Art/AuctionFrontispiece.png" alt="深绿丝绒上的金色旧怀表与琥珀" width="620">

**每段交易只能成交一次。你现在就换，还是等大屏再说一句？**

《Worthless》是一款围绕估值、谈判和物品交易展开的社交推理派对游戏。每位玩家开局持有一件物品，只有持有者能直接查看它的当前价值。你可以在公共频道开价，也可以把对手叫进私人房间谈条件。大屏逐条播报估值线索，里面可能混着假情报。

## 玩法

- **交换物品**。双方确认后才成交，每位玩家每段最多成交一次。
- **读懂市场**。留意公开情报和交易记录。最后一段交易开始前，系统会公布假情报的总数。
- **争取秘密名次**。根据随机分配的高价组或低价组目标争取指定排名。每轮还可秘密调整另一位玩家物品的价值。
- **单人或局域网联机**。每局支持 5～10 个席位，空位由 NPC 补齐。

完整流程见[电子版游戏规则](Docs/WORTHLESS_GAME_RULES.md)。

## 开发和运行

使用 Unity `2022.3.62f2c1`、C#、URP 2D 和 Netcode for GameObjects。由 Unity Hub 打开工程根目录，待依赖导入后，打开 [MainMenu](Assets/Scenes/MainMenu.unity) 并进入 Play Mode。

在 Windows 上，通过 Unity 菜单 **Worthless → LAN Test → Build Release and Run** 构建并启动发行版。输出位于 `Builds/LanTest/Worthless.exe`。联机玩家需连接同一局域网，由一人创建房间，其他人加入。

| 路径 | 内容 |
| --- | --- |
| [`Assets/Scripts/`](Assets/Scripts/) | 主菜单、局域网大厅、对局流程、交易和 NPC 逻辑 |
| [`Assets/Scenes/`](Assets/Scenes/) | 主菜单、联机大厅和对局场景 |
| [`Assets/ScriptableObjects/`](Assets/ScriptableObjects/) | 物品、市场、情报与回合配置 |
| [`Assets/Tests/EditMode/`](Assets/Tests/EditMode/) | Unity Edit Mode 测试 |
| [`Packages/`](Packages/) · [`ProjectSettings/`](ProjectSettings/) | 依赖清单和 Unity 工程设置 |

## 第三方素材

- 字体使用 Fusion Pixel，随项目附带 [SIL Open Font License](Assets/StreamingAssets/ThirdParty/FusionPixel-OFL.txt)。
- 部分地形使用 [Kenney Pixel Platformer](https://kenney.nl/assets/pixel-platformer) 系列素材，包内附许可文件。
- 公开版教程使用文字按键提示。键帽图标可从 [Vryell 的原始素材页](https://vryell.itch.io/controller-keyboard-icons) 获取。
