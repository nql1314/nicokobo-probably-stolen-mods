# 正式版解包与证据入口

2026-11-09（北京时间）核对 Steam 安装版、同 Build 解包与开发者文档。

## 当前来源

- 安装位于 `F:/SteamLibrary/steamapps/common/Probably Stolen`，Steam manifest 为 `F:/SteamLibrary/steamapps/appmanifest_4348910.acf`。App `4348910`，Build `26525552`，Unity `2021.3.45f2`、Mono。
- 使用对应 Build 快照之前，核对 `source.json` 与 Steam manifest。
- 游戏代码显示 `VERSION 100-Rev13`。`GameVersions` 的界面比较使用 `Application.version`（此快照 PlayerSettings 记录 `0.46D`）。发布兼容说明分别注明显示版本、Steam Build 和实际验证范围。
- 安装版 `Assembly-CSharp.dll` 为 4,820,224 字节，MVID `911508a6-e0eb-4cc0-91e8-e14b16ea28d7`。REA `inspect_managed_artifact` 完整读取元数据，证据 `ev_45ee9ad0307b91ea6cab3c2b19bdb43257467088344923498380ce8f3b2244f7`。

## 按任务读取

以下路径相对目标 Build 解包目录。

- `code/Assembly-CSharp/` 为 ILSpy 反编译文本。依赖查 `ModLoader.cs` 的 `OrderByPrerequisites`、`PrerequisiteAssemblyNames` 和 `ResolveModDependency`。能力与信任查 `ModScanner.cs`、`ModCapability.cs`、`ModTrustList.cs`、`ModSecurityDebug.cs`，Steam 边界查 `ModSteamRules.cs` 和 `ModSteamAuth.cs`。反射与跨 Mod API 查 `ModReflect.cs`、`ModHelper.cs`。钩子和周目时序查 `ModHook.cs`、`PlayerStore.cs` 和实际调用方。
- `localization/` 提供 `en`／`zh` 文本，`resources/monobehaviours/` 提供序列化资源。用该快照的 `resources/errors.json` 核对解析缺口，原始未完整解析对象保存在 `resources/unparsed/`。
- `items.json`、`items_full_list.csv`、`item_values.json` 提供静态物品注册、工厂来源和可确定的基础值，包含未用内容。可获得性、运行时倍率和实际成交价另查实际机制。
- `item_icons.csv` 和 `game-assets/manifest.csv` 提供物品与图片关联、图集和裁剪来源。

剧情整理和完整提取流程分别见游戏资料工作区 probably-stolen-tool 的 `docs/vanilla-story/README.md` 和 `docs/UNPACKING.md`。重新提取时显式使用版本目录与 `--locales en zh`。

## 开发者文档与验证边界

本轮同步采用开发者 2026-11-08 的 `ae16103` 提交。官方计划在 2026-12-10 的游戏更新中启用新拒绝规则。当前快照 `ModScanner.REFUSE_UNSCANNABLE_CAPABILITIES` 和 `ModTrustList.WORKSHOP_VERIFIED_ONLY_CAPABILITIES` 均为 `false`。参见[官方中文指南](https://github.com/dragonpan2/probably-stolen-modding/blob/ae161038c6cfed6725f66cfbc5d6b05406975068/ModdingGuide.zh-CN.md)、[API 变更](https://github.com/dragonpan2/probably-stolen-modding/blob/ae161038c6cfed6725f66cfbc5d6b05406975068/APIChanges.zh-CN.md)和[Steam Rev13 公告](https://steamcommunity.com/ogg/4348910/announcements/detail/676262871030563153)。

本轮证据为官方文档、安装版程序集元数据和已有解包的方法体。普通启用、实际游戏效果、保存重载及工坊分发需各自在目标 Build 验证。本地构建、安装与发布见[开发说明](../../../docs/DEVELOPMENT.md)和[Steam 发布说明](../../../docs/STEAM_PUBLISHING.md)。
