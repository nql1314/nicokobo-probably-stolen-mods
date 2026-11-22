# Steam 创意工坊发布

正式版发布入口为根目录 `Publish-Steam.cmd`（双击选择 Mod）或 `Publish-Steam.ps1`。流程为：单个正式版 Mod Release 编译、校验、生成目录包和 ZIP、上传到 Steam 创意工坊。发布不安装到游戏，也不启动游戏。

Forge 与内容包分别发布。`-Mod nicokobo-forge` 只处理前置；选择内容包不会自动打包或上传 Forge，玩家仍需单独订阅并启用所需前置。

## 一次性准备

- 准备[仓库要求的构建环境](../README.md#编译)，并保留正式版游戏 Managed 程序集。
- 从 [Valve 下载 SteamCMD](https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip)，解压到 `.local/steamcmd/`。也可通过 `-SteamCmdPath '其他位置\steamcmd.exe'` 指定，或将其加入 PATH。
- 使用拥有游戏和相应工坊条目权限的 Steam 账号。脚本仅保存登录名，密码和 Steam Guard 交给 SteamCMD 交互输入；首次运行可能需要更新 SteamCMD。需要验证时不能完全无人值守。

## 发布与更新

在仓库根目录的 PowerShell 7 中执行：

```powershell
# 首次创建：默认隐藏，便于检查页面后再公开
.\Publish-Steam.ps1 -Mod speed-up -NewItem -SteamUser YOUR_LOGIN

# 已有工坊条目时绑定它，避免创建重复条目（换成实际 ID）
.\Publish-Steam.ps1 -Mod speed-up -PublishedFileId 1234567890 -SteamUser YOUR_LOGIN

# 后续一键打包并更新，自动复用条目 ID 和登录名
.\Publish-Steam.ps1 -Mod speed-up

# 明确同步本地 Steam BBCode 描述（会替换网页上的描述）
.\Publish-Steam.ps1 -Mod speed-up -UpdateDescription

# 明确公开；普通更新不改变现有可见性
.\Publish-Steam.ps1 -Mod speed-up -Visibility Public

# 仅编译打包并生成待上传 VDF，不登录或上传
.\Publish-Steam.ps1 -Mod speed-up -PrepareOnly
```

`-Mod` 是 `mods/` 下的目录名。省略时交互选择一个项目。首次交互运行可输入已有条目 ID 或 `NEW`，已有绑定后直接更新。

发布前同步修改 csproj 与 manifest 的版本，补充 `change.log` 对应版本段落（例如 `2.0.0 - 2026-10-28`）。脚本不会自动加版本号，只提取当前版本的更新记录；可用 `-ChangeNote '本次说明'` 覆盖。

README 与 `change.log` 仅保留在源码目录，不随包上传或安装，资源子目录中的同名文档也会排除；发布脚本仍从源码 `change.log` 提取工坊更新说明。

## 标题与描述规范

清单与 README 的名称遵循[开发文档中的名称与描述规范](DEVELOPMENT.md#mod-名称与描述规范)，工坊标题与正文按语言分别保存。

源码目录的 `workshop-description.txt` 保存默认英文标题与正文。其他详情使用 `workshop-description.<语言代码>.txt`，语言代码为 `zh-CN`（简体中文）、`zh-TW`（繁体中文）、`ru`、`de`、`ja`、`fr`、`es`、`pl`。各文件以 `[h1]标题[/h1]` 开始，正文使用 Steam BBCode。首次发布读取英文文案，没有文件时使用清单。普通包更新保留工坊详情；`-UpdateDescription` 只同步默认英文标题与正文。

各语言详情可用已登录工坊网页的“编辑标题与描述”保存，也可用 Steamworks 的 `SetItemUpdateLanguage` 单独提交标题和描述。Steam 语言代码依次为 `schinese`、`tchinese`、`russian`、`german`、`japanese`、`french`、`spanish`、`polish`，默认文案用 `english`。只改文案时不重新上传 Mod 包，也不另填改动说明。

仅简体中文和繁体中文介绍附上 `[b]交流群 3 群：192285479[/b]`。未支持的语言回退到默认英文；发布后回读各语言的标题、正文，并确认没有旧的语言覆盖改变英文回退。

在原介绍之后、交流群之前补充开发、扫描、权限与兼容性说明，英文页面使用对应英文：本 Mod 按照官方指南开发，并已通过 mod-scan 扫描，未申请额外权限（包括外部反射访问权限）。除本页介绍中列明的功能调整外，通常不会影响原版功能。如与第三方 Mod 一同使用时出现冲突，请优先联系对应第三方 Mod 作者协助排查。扫描通过的表述须有对应发布包的检查记录支持。不要将“未申请外部反射访问权限”写成“未使用任何反射接口”；Harmony 方法定位等实现仍可能使用反射。

工坊介绍不再附加目标游戏版本和内置 ModLoader 的适用说明。机核协议的介绍中也不展示存档身份、系统用户名来源和 Steam 权限等技术说明；开发证据仍保留在各项目文档中。

描述和更新说明使用真实换行，生成 VDF 时也保留引号内的真实换行；不要转换成字面量 `\n` / `\r`，否则工坊页面会直接显示这些字符。已上传的错误文本需要在网页手动更正，或用修复后的脚本明确同步描述；普通更新仍保留网页描述。

2026-11-02 实际上传确认：更新日志中的英文直引号经 VDF 转义后，会被当前 SteamCMD 提前截断，甚至导致配置解析失败。`change.log` 使用 `“…”` 弯引号，上传后核对完整正文；已有截断记录通过工坊页面的“编辑改动说明”修正，无须重复上传同一版本。

每种语言的详情描述分别受 [Valve 的 8000 字节上限](https://partner.steamgames.com/doc/api/ISteamRemoteStorage#k_cchPublishedDocumentDescriptionMax)约束，按 UTF-8 编码计算，BBCode 与换行一并计入。2026-11-01 拆分语言前，物品管理器的双语详情曾因超长在最终提交时返回 `Invalid Parameter`。

封面默认使用源码目录的 `preview.png`，也可指定 `-PreviewFile`，要求小于 1 MB；不提供时更新保留已有封面。首次发布后检查封面、详情、协议及可见性。

包发布脚本不逐语言维护详情。改名或修改功能介绍时，将修改同步到已有的语言文件与工坊详情，并按实际回读范围记录结果。

## 更新说明格式与多语言

`change.log` 面向工坊玩家，使用 UTF-8。每个版本保留一个段落，最新版本放在最前面；标题保持 `1.0.0 - 2026-10-29` 这样的格式，版本号与 manifest 一致。这是本地发布脚本的提取约定，不是 Steam 强制的版本格式。不要在版本号与连字符之间插入“开发修订”等文字，也不要给版本标题加 BBCode，否则脚本可能跳过该段。

正文用简短条目说明新增功能、修复效果和实际使用限制。方法名、编译日志、断言数量和开发验证记录放在 README 或开发文档中。纯文本可以使用；需要排版时采用 [Steam 支持的 BBCode](https://steamcommunity.com/comment/Recommendation/formattinghelp)，例如 `[b]简体中文[/b]`、`[list]`、`[*]`、`[/list]`，不用 Markdown 标题、反引号或表格。

更新说明默认在同一个版本段落内分“简体中文”和“English”两段，内容对应，一次提交。当前脚本只将一个字符串写入 VDF 的 `changenote`，不会自动选择或合并多份语言文件，因此不另建 `change.zh-CN.log` / `change.en.log`，也不为不同语言重复上传同一版本。

Valve 的 [SetItemUpdateLanguage](https://partner.steamgames.com/doc/api/ISteamUGC#SetItemUpdateLanguage) 作用于标题和描述；[SubmitItemUpdate](https://partner.steamgames.com/doc/api/ISteamUGC#SubmitItemUpdate) 接收一个更新说明字符串，未提供独立的更新说明语言参数。更新日志继续使用一个中英双语版本段落。

## 包与发布状态

上传内容来自本次构建的全新 `_build/official-mods/.../packages/<mod>/`，不是可能残留旧文件的 dist。`dist/<mod>-<version>.zip` 供手动分发，Steam 上传的是目录内容。

`.local/steam-workshop/<manifest-ID>.vdf` 保存实际条目 ID，`account.json` 保存登录名；该目录不进入 Git，**不要当缓存清理**。迁移工作目录时保留它，或通过 `-PublishedFileId` 重新绑定。已有非零 ID 不允许通过参数改绑，避免更新错条目。

首次上传前写入 `.pending` 标记。若 SteamCMD 明确返回 `Invalid Password`，条目 ID 未变且没有上传迹象，脚本会单独报告登录失败；新建条目 ID 仍为 0 时自动清除本次标记，修正 Steam 登录名／密码后可重试。密码和 Steam Guard 仍只在 SteamCMD 中交互输入。

如果中断或上传结果不明且 VDF 仍是 ID 0，脚本会阻止再次创建。先在自己的工坊页面核对是否已生成条目，再用实际 `-PublishedFileId` 恢复；不要删除标记盲目重试。若 SteamCMD 已写回 ID，后续运行只更新该 ID。

上传成功必须同时满足退出码、VDF 条目 ID 和匹配的上传成功记录。SteamCMD 可能先打印 `PublishFileID`，再以 `Committing update...Success.` 确认提交；脚本支持此格式，也支持带条目 ID 的 Published／Updated 成功行。首次创建必须核对返回 ID，已有条目不得变更 ID；仅创建条目、开始上传或普通 `Success` 不算完成。遇到其他输出格式仍会停止报错，请核对页面及 SteamCMD 日志，不要因此重新创建条目。上传确认不等于公开或订阅下载验证；Steam 工坊协议未接受时页面可能不可公开。

实现依据：[Valve Workshop / SteamCMD Integration](https://partner.steamgames.com/doc/features/workshop/implementation#SteamCmd)、[游戏官方 Mod 指南](https://github.com/dragonpan2/probably-stolen-modding/blob/main/ModdingGuide.md#publishing-with-steamcmd)。本地检查入口为 `tools/steam-workshop-checks/Verify.ps1`；真实 Steam 登录与上传需单独验证。
