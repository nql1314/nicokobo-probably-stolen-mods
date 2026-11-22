---
name: probably-stolen-official-mods
description: 为 Probably Stolen 正式版内置 ModLoader 开发、修复、迁移和发布 C# Mod，按官方中文指南处理内容、UI、资源、配置、存档与 Steam 工坊。适用于旧 Demo／Nicokobo Forge 迁移；百科、视频、第三方 Mod 资料同步和明确针对 Demo 的维护使用各自流程。
---

# Probably Stolen 正式版 Mod 开发与发布

默认目标是正式版 Unity Mono 与游戏内置加载器。不要给正式版自动安装 MelonLoader，不要使用 Demo 的 Il2CppAssemblies、GameAssembly 偏移或旧 Forge DLL。用户明确维护旧版时，以旧版工程为准，不擅自迁移。

## 依据与入口

- 本机正式版位于 `F:\SteamLibrary\steamapps\common\Probably Stolen`。本轮对照 `100-Rev13`（Steam Build `26525552`）与 2026-11-08 的开发者文档。按目标 Build 选解包快照，先核对 `source.json` 与 Steam manifest。路径、版本口径和核对范围见[检查基线](references/release-evidence.md)。
- 优先查[官方中文指南](https://github.com/dragonpan2/probably-stolen-modding/blob/main/ModdingGuide.zh-CN.md)，必要时对照[英文指南](https://github.com/dragonpan2/probably-stolen-modding/blob/main/ModdingGuide.md)。本地实际接口以 `Probably Stolen_Data/Managed/Assembly-CSharp.dll` 为准；文档与实现不一致时分别说明。更新游戏后按需查[中文 API 变更](https://github.com/dragonpan2/probably-stolen-modding/blob/main/APIChanges.zh-CN.md)。
- 新建／修复先读 [官方开发流程](references/official-workflow.md)。迁移、依赖设计或旧档兼容再读 [迁移方案](references/release-migration.md)。需要复核证据和已知限制时读 [检查基线](references/release-evidence.md)。不自动读取旧版场景指南。
- 可复制 [最小工程](assets/official-starter/OfficialStarter.csproj) 到目标源码目录，修改项目名、程序集名、manifest ID 和入口；它没有安装或发布构建步骤。按需添加游戏自带程序集引用，全部 `Private=false`。

## 按任务读取

以下参考是中文指南的任务摘要；保留官方章节链接，不把完整手册一次载入，也不把示例中的所有功能加入目标 Mod。

| 当前任务 | 读取 |
| --- | --- |
| 工程、manifest、生命周期、前置依赖与跨 Mod 服务 | [官方开发流程](references/official-workflow.md) |
| 能力声明、12 月安全更新、`ModReflect`、Harmony、后台任务与加载诊断 | [扫描与安全适配](references/official-security.md) |
| 日志、F8/F9、钩子选择、物品／顾客来源、资源、本地化、配置和周目数据 | [资源与钩子](references/official-resources-and-hooks.md) |
| 掉落、人脉升级、开局、天赋、难度、剧情顾客、夜间地点／事件、鉴定物品 | [内容注册 API](references/official-content-apis.md) |
| 自定义窗口、纸质手册、机器窗口、游戏选项与按键绑定 | [UI 与纸质手册](references/official-ui-and-books.md) |
| 上传入口、首次发布、更新已有工坊条目、封面／详情与可见性 | [Steam 工坊发布](references/official-workshop.md) |
| Demo／Forge 迁移、共享库、旧 ID 与旧存档 | [迁移方案](references/release-migration.md) |

## 工作流程

1. 读目标仓库约定和现有修改，核对游戏路径、构建与真实依赖。新项目默认 `netstandard2.1`，直接引用正式版 `Managed`，不用 net6 Melon 插件模板。
2. 先找官方钩子和 `ModHelper`／专用注册 API。物品用 `OnModItemDirectoryInit`，资源用 `OnModAssetsLoaded`，周目数据在游戏载入后读写。官方不能覆盖的部分才使用精确的游戏代码 Harmony 补丁，并标明升级维护风险。
3. 一个包采用一个明确的 `IMod` 入口。加载器会在 `Init` 之前自动对入口程序集 `PatchAll`，不要重复安装这些补丁。无 `IMod` 的依赖库不会自动 PatchAll；其必要补丁须由入口显式管理安装、失败撤销和退出清理，或编入入口程序集。`Init` 建配置，`OnEnable` 订阅事件；`OnDisable` 对称退订和清理自身资源。普通启停需要重启，不承诺热卸载。
4. `manifest.xml` 必须包含有效 `ID`、非空 `Assemblies` 和 `Capabilities`（无额外能力时用空元素）。列全自带 DLL，游戏、Unity、Harmony 和 Newtonsoft 使用安装版副本。Rev13 可通过 `Prerequisites` 引用前置 Mod 的 DLL，编译引用设 `Private=false`，由加载器从已运行的前置包解析。
5. 保持物品、配方、开局、owner 与存档身份稳定。区分全局配置、周目字符串数据和物品标签；写入内存不等于存盘。库存移动、付款和奖励仍须按真实返回值／写后状态判断成功。
6. 仅执行本次范围的验证。普通代码修改以相关项目编译通过为默认完成条件；编译不证明游戏扫描、加载、效果或保存重载。打包、安装、游戏执行与线上发布按用户请求分别处理。官方 `create-mod.bat` 带自动安装步骤，未授权安装时不要直接执行。
7. 用户要求发布本 Mod 时，先核对新建／已有条目 ID、包、版本和详情；本地仓库已有 `Publish-Steam.cmd`／`Publish-Steam.ps1`，按[工坊参考](references/official-workshop.md)使用。询问上传入口或整理技能不执行登录、上传或公开；已明确授权的发布直接在范围内完成。

## 重要决策

- `OnEnable` 时本 Mod 还可能不在 `ModLoader.Mods` 中，贴图／本地化／声音也未加载。不要在这里查找自己的 `LoadedMod` 或操作玩家库存；订阅具名事件，在资源回调中按 `Manifest.ID` 筛选自身。
- `GameVersions` 填目标游戏的 `Application.version`。未知时开发模板留空，发布前核实。Steam Build 与实际兼容验证单独记录。
- 按真实用途声明能力。新开发与发布准备按官方 2026-12-10 更新计划移除 `reflection-external`、`native`、`dynamic-code`、`threads`。涉及 `files` 或 `network` 的工坊包另有开发者验证要求，见[扫描与安全适配](references/official-security.md)。
- 发布验收使用普通玩家的扫描与启用流程。保留现有信任记录和开发者模式设置，加载器安全判定由游戏处理。
- 只有用户明确要求执行原生测试时，读取 `%USERPROFILE%/.codex/skills/probably-stolen-native-test/SKILL.md`。仅分析、整理技能和讨论测试不启动游戏。

交付用简洁中文说明改动、实际验证和未验证部分。不要把新技能的安装当作已有 Mod 已完成迁移。
