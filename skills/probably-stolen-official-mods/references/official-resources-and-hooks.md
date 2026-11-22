# 正式版资源、钩子与配置

按任务选择资源或钩子，签名以目标构建为准。参见[官方中文指南](https://github.com/dragonpan2/probably-stolen-modding/blob/ae161038c6cfed6725f66cfbc5d6b05406975068/ModdingGuide.zh-CN.md)、[检查基线](release-evidence.md)和[开发流程](official-workflow.md)。

## 日志与开发入口

- 用 `new ModLog(manifest)` 保存日志实例，再调用 `Log/Warning/Error`，前缀来自 manifest 名称。不要把实例用法误写成任意静态调用。
- 正式版 F8/F9 默认关闭。获准使用游戏开发工具时，在 `Probably Stolen.exe` 旁放空的 `dev.txt` 后重启。按键无响应先查 `Player.log` 的 `[DevTools]` 行。发布包不包含此标记。参见[官方开发工具章节](https://github.com/dragonpan2/probably-stolen-modding/blob/ae161038c6cfed6725f66cfbc5d6b05406975068/ModdingGuide.zh-CN.md#开发工具)。
- F8 是 Quantum Console，可用 `user-commands` 和 `man <命令>` 查看命令。载入周目后按 F9 打开开发菜单，从 Command > Toggle Verbose Log 开启原版信息日志，从 Special > Modding Panel 进入模组工具。
- 注册自有调试按钮用 `ModHelper.AddDebugFunction(modId, category, name, action, tall)`，关闭时 `RemoveDebugFunctions(modId)`。`DEBUG_CATEGORY_STORE/COMMAND/SPAWN/SPECIAL` 对应 F9 四列，`MODDING` 对应独立面板。`MARKETING` 为兼容项，模组按钮归到 Special。可能耗时的操作可用 `StallWatchdog.Crumb` 留一条简短的卡顿诊断记录。
- 下文控制台／编辑器操作仅供用户要求相应游戏操作时使用；整理资料与编译本身不启动游戏。

## 钩子选择

在 `OnEnable` 订阅具名处理器，在 `OnDisable` 退订。`VeryEarly/Early/Normal/Late/VeryLate` 表示层级，不保证任意管理器在每个时刻都可用；准确调用位置按目标构建核对。处理器异常会被隔离记录，仍须修复。

| 需求 | `ModHook` 事件 | 参数／约束 |
| --- | --- | --- |
| 注册物品 | `OnModItemDirectoryInit` | `ModItemDirectory`；`Add(id, factory)`，保留稳定物品 ID |
| 顾客生成 | `OnGenerateCustomerVeryEarly/Early/Normal/Late/VeryLate` | `StoreClientManager`；不是立绘／阵营注册 |
| 读档与新局载入 | `OnGameLoadedInit/Early/Normal/Late` | 无参；需要周目数据时从已载入阶段读取 |
| 仅新局奖励 | `OnNewGameStarted` | `string scenarioId`；方案初始物品和天赋物品放置之后，读档不触发 |
| 开关门、睡醒、出入商店 | `OnShutterOpened/Closed*`、`OnGoingSleep*`、`OnWakingUp*`、`OnLeavingStore*`、`OnReturningStore*` | `*` 为 Early/Late，无参；睡眠钩子不自动等于存盘成功 |
| 夜间维护 | `OnHandlingNightlyServicesEarly/Late` | 无参 |
| 发明家售卖货品 | `OnPlaceInventorInventoryItemEarly/Late` | `List<GameItem>` 玩家物品；用 `PlayerStore.Instance.AddDirectSellingItemToTable(itemId)` 加入货品 |
| 普通／开发提示框 | `OnCreateTooltipEarly/Late`、`OnCreateDevTooltipEarly/Late` | `RichTextBuilder, GameItem` |
| 本 Mod 的资源完成 | `OnModAssetsLoaded` | `LoadedMod`；按 `Manifest.ID` 过滤并检查所需资源键 |
| 最终掉落表／一次掷取 | `OnLootTablesLoaded`、`OnLootRolled` | 无参／`LootRollContext`；详见内容参考 |
| 配置应用 | `OnModConfigApplied` | `string modId`；只处理自己的 ID |
| 人脉购买完成 | `OnNetworkUpgradeUnlocked` | `string upgradeId`；效果执行之后 |
| 应用难度 | `OnDifficultyApplied` | `string difficultyId`；新局与读档均可能发生 |
| 夜间到达／离开 | `OnAfterhourArriveEarly/Late`、`OnAfterhourLeaveEarly/Late` | `string locationId` |
| 夜间事件开始／结束 | `OnAfterhourEventStarted/Ended` | `string locationId, string eventId` |
| 手册面板构建／重建 | `OnPaperOpened` | `string fullId, PaperUI ui`；此处绑定动作，详见 UI 参考 |

## 物品与顾客归属

`SpawnItem(id)` 返回对象，放置另用 `MoveItemToInventory/FrontCounter/BackCounter/DisplayCase/Trash/Afterhours`。这些操作需要已载入的周目；放置后检查实际容器状态。`SetItemOwnerShip(item, isOwn)` 设置玩家／顾客所有权，不等于来源标签。

目录工厂通常自动识别物品来源；工厂位于共享库时显式 `SetSource(item, modId, modName)`，来源标签为 `MOD_ID_TAG/MOD_NAME_TAG`。在顾客生成钩子里用 `manager.AddClient(client)` 加入的顾客自动带来源；其他加入路径在其抵达柜台前调用 `SetClientSource(client, modId)`。这些标签不能代替稳定物品或存档 ID。

## 资源与本地化

- `Init/OnEnable` 早于图片、本地化与声音加载。启动时依赖这些资源的逻辑等 `OnModAssetsLoaded`；不要在 `OnEnable` 通过 `ModLoader.Mods` 查找自己。
- 游戏按资源类型读取指定目录的直接子文件。`SetSpriteAndShapeFromMod(manifestId, fileStem)` 使用 `Textures/Items` 的图，透明区域参与物品占格。目录名、文件名键与 manifest ID 各有用途。安全扫描另会检查包内文件，见[扫描与安全适配](official-security.md)。
- `Textures/Paper` 用于平滑 UI 精灵图，布局常以原图两倍显示。通用 `Textures` 不会自动显示在 UI；引用方式按使用它的 API 核对。
- 声音通过 `PlaySound/PlaySound2/PlaySound3(modId, soundKey)` 接游戏音频通道；`GetSoundIdentifier` 得到 `modId:soundKey`。同一通道的新声音会切断旧声音，需要重叠时选择不同通道，不另建音源绕过玩家设置。
- 本地化文件名使用 `en/fr/de/ru/zh/ja/es/ptBR/ko/it/pl.csv`，中文是 `zh.csv`。UTF-8，无表头；首个逗号分开键和值，支持 `#` 注释、`""` 引号转义和字面 `\n`，不支持跨物理行的值。
- `GetLocalized` 按当前语言 → `en` → 原键解析，占位用 `{0}` 等 `string.Format` 格式。界面标签用延迟函数读取，不在启动时固化翻译。

## 物品透明边缘的拖拽

Build 26525552 中，单件拖拽通过 `RenderHandler.RaycastElement<GameItemElement>` 找到鼠标下的物品。`GameItemElement` 的底图默认按贴图 alpha 判定点击，完全透明的像素会点穿。框选直接检查 `modifiedShape` 的占用格，因此即使四格物品的透明边缘无法单独拖动，框选仍能选中它。将底图的 `alphaHitTestMinimumThreshold` 设为 `0f` 会让整个贴图矩形响应点击。[Unity Image API](https://docs.unity3d.com/2019.1/Documentation/ScriptReference/UI.Image-alphaHitTestMinimumThreshold.html)

NicoKobo Forge 从 1.0.10 起，在物品刷新时为使用 Mod 精灵图或可识别 Mod 来源的物品添加透明命中区域。命中按当前 `modifiedShape` 的占用格计算，随物品旋转、翻转，沿用游戏的物品输入处理。带缺口的异形物品只在实际占用格内获得额外命中。

内容工厂按设计注册物品、设置精灵图和占格。用共享库里的工厂时，可通过 `ModHelper.SetSource` 显式标注来源。启用含此处理的 Forge 即可让这些物品的占用格透明边缘支持拖动。验收时从 2×2 物品的透明角起拖，并检查旋转后异形物品的缺口仍可点到后方的格子。

## 模组配置与周目状态

| 需要 | 使用 | 生效与持久化 |
| --- | --- | --- |
| 跨存档玩家设置 | `GetConfig(modId)`、`Define`、`DefineEnum<T>` | `ModConfigs/<id>.json`，定义返回时已装入保存值 |
| 单局进度／收据 | `Set/GetModData` 及 Int/Float/Bool 版本 | 字符串存储随游戏保存；从 `OnGameLoadedInit` 读取，写内存不等于写盘 |
| 单件物品状态 | `ModifyTag/GetTagReadonly` | 跟随具体物品 |
| 包内只读资料 | `ReadModFileText/Bytes`、`ModFileExists`、`LoadModAssetBundle` | 包内相对路径，无须 `files` |
| 不随存档槽的独立文件 | `Read/WriteModDataFileText/Bytes`、`ModDataFileExists`、`DeleteModDataFile`、`ListModDataFiles` | `ModData/<id>` 内相对路径，无须 `files` |

独立文件写入和删除使用自身 manifest ID。其他已安装 Mod 的数据目录只读，跨 Mod 写删会抛 `UnauthorizedAccessException`。通过 `LoadModAssetBundle(modId, relativePath)` 加载包内资源包，保留返回的实例并在用完后 `Unload`。参见[官方文件辅助方法](https://github.com/dragonpan2/probably-stolen-modding/blob/ae161038c6cfed6725f66cfbc5d6b05406975068/ModdingGuide.zh-CN.md#不声明能力也能读写文件)。

`Define` 的 bool 对应开关；带范围数字对应滑块；无范围数字对应输入；带 `choices` 的 string／枚举对应下拉。`OnChanged` 在应用实际变化、`entry.Set` 或配置重置时触发，载入不触发；因此初始化应直接读取 `Value`。需一次性刷新时过滤 `OnModConfigApplied(modId)`，启动时才读取的参数标记 `requiresRestart`。

配置面板的“恢复默认”先改变控件，按“应用”才生效。只有已激活并执行初始化的 Mod 才有配置项。周目存储使用 `modId:key` 命名空间，禁用不会自动清除；不在启动或读档时无条件 `ClearModData`，结构化数据的 schema 与迁移见[迁移方案](release-migration.md)。
