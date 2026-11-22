# 正式版扫描与安全适配

以 `100-Rev13`（Steam Build `26525552`）为当前基线。退役能力和工坊验证由游戏构建控制。Rev13 已提供迁移 API 和预警，官方计划在 **2026-12-10 的游戏更新**中启用拒绝规则。换版先核对目标构建。参见[官方 API 变更](https://github.com/dragonpan2/probably-stolen-modding/blob/ae161038c6cfed6725f66cfbc5d6b05406975068/APIChanges.zh-CN.md#2026年11月8日)和[检查基线](release-evidence.md)。

## 能力与生效阶段

| 项目 | Rev13 | 12 月更新计划 |
| --- | --- | --- |
| `reflection-external`、`native`、`dynamic-code`、`threads` | `DEPRECATED`／菜单预警。新增检测有 `Pending` 记录，旧检测仍按当前规则计入能力 | 使用或声明任一项均触发拒绝 |
| 工坊包声明 `files` 或 `network` | 无开发者验证时显示预警，`mod-scan` 提示 `VERIFICATION` | 按 manifest ID 与版本指纹核对签名名单的 `verified`，缺少验证则拒绝启用和加载 |
| `[ModRestricted]` 的新增限制 | `SaveFiles`、系统外壳辅助入口等出现在 `Pending` | 调用或修补 `SaveFiles` 等计入 `files`。`PaperEditorFiles.OpenFile/OpenFolder` 和 `ModUIManager.OpenModFolder` 按启动外部程序拒绝 |

长期使用的能力按实际调用声明。`files` 覆盖直接访问系统文件的路径，`network` 覆盖联网 API，`process` 只用于 `Application.OpenURL` 的字面 `http://` 或 `https://` 链接。`steam` 用于允许的 Steamworks 调用，例如成就、覆盖界面、工坊查询。`steam-auth` 是独立的服务端票证能力。官方配置、包内读取、ModData 和 `LoadModAssetBundle` 可处理相应需求而无须声明 `files`。参见[能力表](https://github.com/dragonpan2/probably-stolen-modding/blob/ae161038c6cfed6725f66cfbc5d6b05406975068/ModdingGuide.zh-CN.md#能力声明与扫描器)。

Rev13 已会硬拒绝以下行为。任何能力声明均不能放行这些行为。

- 启动外部程序、打开文件路径或非网页协议，以及 Steam 工坊写入／订阅／下载、Steam 云写入、直接请求 Steam 票证、账户敏感操作与聊天。发布由作者通过 SteamCMD 操作。
- 写加载器或信任状态，例如 `SetModEnabled`、`ReorderMods`、`ApproveAndEnable`、`ModTrust.Approve/Revoke`、`ModWorkshop.Unsubscribe`。`ModLoader.Mods` 可读取，返回列表副本。对加载器、信任核心、`PaperBookRegistry`、`ModReflect` 等保护类型的补丁或反射也会被拒绝。
- 用全局或游戏自有 owner 撤补丁。`UnpatchAll()` 的默认 `null`、`Unpatch` 的默认 `"*"` 和 `dse.` 开头的 owner 都会触发拒绝。用 `harmony.Unpatch(original, patchType, harmony.Id)` 指定自己的 owner。
- 包内未列出的 DLL、游戏或框架同名副本、未获准的程序集引用、不可解析的 IL 或补丁特性、隐匿载荷。扫描覆盖清单中全部 DLL（含捆绑库）和资源目录。发布包只保留实际资源，清理压缩包、脚本、安装程序和美术源文件。

## 反射与 Harmony

- 已知目标优先用显式游戏类型和字面成员名，例如 `AccessTools.Method(typeof(PlayerStore), "ModCash")`。固定的类型局部变量和字面 `Assembly.GetType` 也可被扫描器识别。`obj.GetType()`、运行时拼接的名字、`dynamic` 和 COM 激活属于需替换的反射路径。
- 运行时才知道类型或成员时，用 `ModReflect.FindType/Types/Has/Get/Set/Call/New` 或静态版本。热路径可缓存 `Method<TDelegate>`／`StaticMethod<TDelegate>` 的绑定结果，动态选择 Harmony 目标可用 `Target`。游戏检查成员声明类型，允许游戏类型、自有程序集、运行中的已声明前置程序集及 `UnityEngine.UI`、`TMPro`、`UnityEngine.EventSystems`。受保护类型、Easy Save 以及这三个命名空间以外的引擎和系统成员仍受限。被拒绝的查找抛 `ModReflectAccessException`，用 `mod-reflect` 定位。参见[官方反射章节](https://github.com/dragonpan2/probably-stolen-modding/blob/ae161038c6cfed6725f66cfbc5d6b05406975068/ModdingGuide.zh-CN.md#反射)。
- 字面引擎／系统类型加字面成员的常规查找按直接调用规则分类。针对外部引擎／系统方法的 Harmony 补丁仍计入 `reflection-external`，上列三个 UI 命名空间例外。Transpiler 的 `CodeInstruction` 成员查找也会扫描，常规 `OpCode`、`Label`、`ILGenerator` 用法本身不计 `dynamic-code`。运行时防护会把改写方法中的调用归到 Transpiler 所属 Mod。
- 用普通托管调用或现有官方 API 替换 `DllImport`、指针、动态加载、`Reflection.Emit` 和 `Expression.Compile`。持久化可用 `JsonUtility` 或游戏自带的 Newtonsoft.Json，保持 `TypeNameHandling.None`（包括特性配置）。核对捆绑 DLL 的内部用法，先替换触发项再移除退役声明。

## 后台工作

- 将自建 `Thread`、计时器、`Task.Run/Delay/ContinueWith`、`Parallel.*` 及借用游戏组件启动的协程替换为 `ModHelper.StartCoroutine/StopCoroutine/StopAllCoroutines`、`Delay` 或 `RunInBackground`。这些方法接收本 Mod 的 ID。自有 `MonoBehaviour` 可在自身启动协程或使用 `Invoke`。
- `RunInBackground(modId, work, onFinished)` 向工作传入取消令牌。循环和可取消的调用应检查令牌，Unity 对象更新放在主线程的完成回调或 `RunOnMainThread`。停止 Mod 时加载器发出取消信号并丢弃待执行回调。参见[官方后台工作章节](https://github.com/dragonpan2/probably-stolen-modding/blob/ae161038c6cfed6725f66cfbc5d6b05406975068/ModdingGuide.zh-CN.md#后台工作)。
- 加载器停止 Mod 时清理所属 Harmony、`ModHook` 订阅、自有组件、托管协程和暴露的服务／监听。Mod 仍负责清理自行赋给游戏对象的委托和自行注册的外部状态。获准做停止回归时，可在测试周目的 `devMode` 中用 `mod-stop <id>` 复现停止流程。

## 工坊验证与票证

- 按 12 月规则准备工坊包。确实需要 `files` 或 `network` 时，在发布前通过官方模组频道提交条目、用途与扫描结果。验证可以只覆盖 `mod-scan` 的 `fingerprint` 对应版本，也可以由开发者按 ID 用 `"*"` 覆盖全部版本。已验证版本跳过能力确认框。本地 `Mods/` 文件夹免于该工坊验证门槛。参见[官方工坊验证章节](https://github.com/dragonpan2/probably-stolen-modding/blob/ae161038c6cfed6725f66cfbc5d6b05406975068/ModdingGuide.zh-CN.md#使用文件或互联网的创意工坊模组需要验证)。
- 仅在玩家使用某功能期间收集或传输该功能必需的数据。参见[官方行为准则](https://github.com/dragonpan2/probably-stolen-modding/blob/ae161038c6cfed6725f66cfbc5d6b05406975068/CodeOfConduct.zh-CN.md)。
- 自有服务器需要验证玩家资格时，声明 `steam-auth`，在主线程调用 `ModHelper.RequestSteamAuthTicket(serverIdentity, onTicket)`。身份为服务器的 `steamid:<id>` 或 `ip:<地址>:<端口>`。返回 `true` 表示已受理，票证在回调取得，失败时回调可收到 `null`。每个 Mod 同时一张票证，断线用 `CancelSteamAuthTicket()`，服务器用 `BeginAuthSession`／`EndAuthSession` 管理验证会话。联网发送仍需 `network`。参见[官方票证章节](https://github.com/dragonpan2/probably-stolen-modding/blob/ae161038c6cfed6725f66cfbc5d6b05406975068/ModdingGuide.zh-CN.md#向你的服务器证明玩家的-steam-身份)。

## 扫描与诊断

1. 在获准的游戏验证中，按[开发工具入口](official-resources-and-hooks.md#日志与开发入口)使用 F8，执行 `mod-scan <ID 或包目录>`。检查 `ERROR`、硬拒绝项、`declared` 与 `detected`。为 12 月版本提前处理 `DEPRECATED` 和 `Pending`，遇到 `VERIFICATION` 核对目标版本的开发者验证。
2. 核对普通启用与重启后的加载结果。能力增加、扫描规则变化或包内容更新可能触发重新扫描或审核。批准覆盖包内文件，单换资源也会触发文件夹复核。版本指纹仍基于清单与 DLL，区别于文件夹哈希。开发者模式适用于本机 Mod 的开发迭代，发布验收按普通启用规则执行。
3. `mod-trust [id]` 和 `mod-trust-list` 可看本地批准和远程名单状态。运行时停止或加载拒绝核对存档目录旁的 `mod-audit.log` 与 `Player.log`。F8/F9 开发工具与每 Mod 的 Developer mode 是两个设置。按任务授权使用，并保留玩家的信任记录。
