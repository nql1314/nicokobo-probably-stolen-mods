# 正式版原生测试运行器

此目录维护正式版 Mono 游戏的隔离准备、当前已安装候选复制、正常 Mod UI、原生探针监督和受限清理。源码迁自 2026-10-29 外部临时工具；历史目录保持原状。需要 PowerShell 7、.NET 10 SDK，以及安装 UnityPy 的 Python。

运行器不编译业务 Mod，不安装到正式游戏，不修改正式信任、启用状态或存档。测试结果仅代表实际执行并通过的检查；环境准备、离线扫描和脚本契约检查不能代替原生场景验证。

## 环境与边界

- 每次准备创建 ArtifactRoot/native-<32位GUID>，拒绝复用已有目录。environment.json 与 ownership.json 共同绑定本轮 GUID、游戏副本、保存目录及注册表身份；旧环境格式不受支持。
- 读取指定 appmanifest 的当前 App ID、Build 和安装完成状态。ExpectedBuild 可明确锁定本轮版本，不内置历史 Build。
- 原样复制当前游戏运行文件，仅在副本中修改 PlayerSettings 的等长 productName、`app.info` 同名产品以及 SteamManager.Awake 的 Steam 启动部分。两处产品名同时隔离，覆盖 Unity 早期启动和中断退出；启动守卫核对证明文件与实际 persistentDataPath/company/product。Cecil只允许Awake方法体改变，正常加载、扫描、信任、启用和保存实现不变。
- 在副本写入与 appmanifest 相同的 steam_appid.txt，保留已验证的独立启动条件。
- 保存路径是正式保存目录的同级唯一 NicoT_<9位GUID>；PlayerPrefs 同样使用独立产品名。保护正式 Mods 全目录、正式保存全目录（含索引、ES3、Mod 附属文件）、关键原生文件及正式 PlayerPrefs 的字节证据。
- Verify-FormalPreservation 只比较和报告，不用任何旧快照覆盖正式数据。每次验证写入新的 verify-<phase>-<GUID>，失败时保留环境和错误证据。
- Stage 默认复制全部当前已安装 manifest 包；也可用 `CandidatePackageDirectory` 指定已经构建并静态检查的本地候选根目录，无须先安装到正式游戏。两种来源均按 ID 检查真实依赖及程序集身份，再加入指定探针包。2026-10-30 的实际候选为 Forge、六个内容包、四个工具包，共 11 包；代码不以固定数量代替身份/依赖检查。可用 ExpectedModIds 明确锁定候选集合。
- 候选及探针首次只 stage 一次；资源与 DLL 的完整文件清单和哈希被冻结。正式安装集合变化需准备新环境。隔离副本中已登记的包可通过 Update-IsolatedPackage 留存旧包与清单后更新：默认只接受探针，业务包必须显式选择 Business 和 DeveloperMode，并核验用户已启用的对应设置。
- 所有游戏启动先要求现有游戏退出。普通 UI 用于在副本内查看、信任和启用；脚本不直接写启用列表、不绕过扫描或安全确认。

## 准备与候选复制

从仓库根目录执行。以下示例的 PythonPath 需要替换为本机已安装 UnityPy 的解释器。首次 stage 可选正式安装中的业务包或本地候选根目录；测试工具不替业务包编译或安装到正式游戏。

~~~powershell
$runner = Join-Path $PWD 'tools/official-native-test/native-tools'
$game = 'F:/SteamLibrary/steamapps/common/Probably Stolen'
dotnet build "$runner/ContentNativeTools.csproj" -c Release "-p:GameManaged=$game/Probably Stolen_Data/Managed"
$prepare = @{
  GameDirectory = $game
  AppManifestPath = 'F:/SteamLibrary/steamapps/appmanifest_4348910.acf'
  FormalSaveDirectory = '%USERPROFILE%/AppData/LocalLow/Questing Goose Studio/Probably Stolen'
  ArtifactRoot = "$PWD/_build/native-test"
  PythonPath = '<包含 UnityPy 的 python.exe 绝对路径>'
  ExpectedBuild = '26317870' # 必须重新核对本次实际 appmanifest，不能照抄历史版本。
}
$envPath = & "$runner/Prepare-IsolatedOfficialGame.ps1" @prepare
$probes = @(
  "$PWD/_build/native-test/content-probe-build/package",
  "$PWD/tools/official-native-test/speed-stock-probe/package"
)
& "$runner/Package-IsolatedContent.ps1" -EnvironmentPath $envPath -ProbePackagePath $probes
# 使用本地候选时，在同一首次调用另加 -CandidatePackageDirectory '<包含各包子目录的根>'。
& "$runner/Open-IsolatedReviewGame.ps1" -EnvironmentPath $envPath
~~~

Prepare 另支持 RunId、Company、FormalProduct、GameVersion 和 ToolsDll；这些字段必须与当前真实 PlayerSettings 匹配。默认公司 Questing Goose Studio、产品 Probably Stolen、版本 0.46D。Prepare 只输出本轮 environment.json 路径到成功输出流；日志在本轮目录。

先用探针项目自己的离线 scanner 检查包。打开副本的普通 Mod UI 后，确认候选和探针经过正常扫描，按用户授权完成信任、启用并正常退出。两个探针无显式启用参数时保持休眠。退出后运行 Verify，随后每次原生测试会再次进行启动前验证。

## 更新隔离副本中的已登记包

修正测试夹具后，可在所有游戏退出时更新同一环境内已登记的一个探针。Update-IsolatedProbe 是 Update-IsolatedPackage 的 Probe 薄入口，默认流程不允许业务包或新 ID。先对新包执行正常离线 scanner，再调用：

~~~powershell
& "$runner/Update-IsolatedProbe.ps1" -EnvironmentPath $envPath -ProbePackagePath "$PWD/_build/native-test/content-probe-build/package"
& "$runner/Open-IsolatedReviewGame.ps1" -EnvironmentPath $envPath
~~~

默认 Probe 更新后必须在副本的普通 Mod UI 中重新扫描、审核，并按正常流程启用和退出。用户已在该副本中启用此探针 Developer mode 时，可以显式传 DeveloperMode；脚本只读取现有设置，不替用户开启它。

若正在迭代原生测试已复现的业务缺陷，且用户已在该副本的普通 Mod UI 中为目标业务包启用 Developer mode，可仅替换这个已登记业务包，正式安装继续保持原字节：

~~~powershell
& "$runner/Update-IsolatedPackage.ps1" -EnvironmentPath $envPath -PackagePath '<新版完整业务包目录>' -Role Business -DeveloperMode
~~~

Business 必须同时显式指定 Role 和 DeveloperMode；脚本读取该环境隔离保存目录的 SaveFile.es3，只接受与目标 ID 唯一匹配、source 为 Local、developerMode 为 true 的原生 modTrust 记录。它不创建信任、不写启用列表，也不修改 Developer mode。仅传 DeveloperMode 仍是 Probe 模式。后续监督调用需传 DeveloperMode，以实际原生加载结果验证开发者信任状态。

两种角色都会先核对当前全部候选清单、运行时隔离、正式文件与 PlayerPrefs 不变；只允许替换登记目录中的同一 manifest ID，不接受其他目录或新增 ID。旧完整包和两份 installed 清单移入本环境的 probe-updates/<更新GUID>（Probe）或 package-updates/<更新GUID>（Business），记录请求、前后清单哈希及结果。其他包逐文件确认不变，旧测试结果不改写；DeveloperMode 更新还核对隔离 SaveFile.es3 前后字节不变。脚本不启动游戏，不写正式数据、trust、enabled 或 PlayerPrefs。

任一包更新后的完整清单哈希都会使旧 save baseline 失效，必须先用新候选执行 save，再执行 reload。更新中途失败保留旧包、当前副本及失败证据，不自动恢复或掩盖旧失败结果；当前清单缺失/不符时后续启动会被既有候选验证拒绝。正式数据出现外部变化时，更新在修改候选前停止；不得用更新入口重写保护基线或恢复旧正式数据。

## 原生监督

启动时优先选择副屏，系统未检测到副屏时自动使用主屏。content 模式可传 `-AllowPrimaryDisplay` 主动指定主屏。探针核对实际显示器及 1280×720 窗口状态，并写入结果。Steam 客户端可在启动和测试期间继续运行，隔离副本仍检查 `SteamManager.Initialized == false`。

Invoke-IsolatedContentProbe 支持 catalog，以及 production、aug、theft、theftui、review、manager、manageren、night、grayline、dossier、dossieraug、dossiertheft、logisticsfull、achievements、reputation、breeding 各族的 save/reload，另有 performance-save 和 speed-stock。四条完整自然剧情分别为 storylegal、storysettled、storylost、storybeyond 的 save/reload。每次调用创建新的模式/GUID 子目录，结果文件不得已存在。

~~~powershell
& "$runner/Verify-FormalPreservation.ps1" -EnvironmentPath $envPath -Phase after-review
& "$runner/Invoke-IsolatedContentProbe.ps1" -EnvironmentPath $envPath -Mode review-save -Slot 100 -TimeoutSeconds 960
# 替换为刚才成功 save 的 result.json；不能使用其他环境或失败结果。
$baseline = '<同一 environment 下成功 review-save 的 result.json>'
& "$runner/Invoke-IsolatedContentProbe.ps1" -EnvironmentPath $envPath -Mode review-reload -Slot 100 -TimeoutSeconds 960 -Baseline $baseline
& "$runner/Invoke-IsolatedContentProbe.ps1" -EnvironmentPath $envPath -Mode speed-stock -Slot 0 -TimeoutSeconds 960
~~~

- content 模式启用 --nicokobo-native-test；speed-stock 仅启用 --nicokobo-native-speedstock-test。speed-stock 强制 Slot=0、至少 930 秒监督超时，禁止 Baseline/DuplicateSlot/UseCopy。
- 速度探针的真实键盘请求写入 result.json.state.json。由操作员按探针请求使用真实输入完成；监督器不注入键盘，不替代 Unity.Update。
- reload 必须引用本环境内成功的相同模式家族 save，邻接 supervisor.json 也必须通过；环境 GUID、候选清单哈希、保存路径和 slot 必须一致。aug、四条完整剧情、achievements 的 save 可传 DuplicateSlot，对应 reload 可传 UseCopy；Slot 必须对应保存结果的 duplicateSlotId。复制只验证同一结局和已完成业务的持久化，其他结局仍需独立新局。
- 剧情使用隔离Aug的 `initialCredits=10000`，成就使用150000输入以走真实物业购买；需要的资金、日期、年龄和阈值也可按场景明确生成。记录并恢复隔离配置完整原字节／存在状态，不修改正式配置，不把输入夹具当作经济取得或业务结果。
- `passed` 仅表示该轮必需且已执行的检查通过。逐项 `not-tested`、`coverageComplete`、库存/机器/配方/成就矩阵和外部监督结果必须同时阅读；真实事务回调不等于自然完整夜晚，开发者模式也不等于普通信任模式。
- 旧自然 `breeding` 模式只保留历史证据，快速回归不启动、不续跑。直接生成800g鼠等资格输入，声望从阈值附近用真实兑换跨线；完成与领奖仍走真实实现。
- 原生客户队列不持久化；不要通过新存盘把未知必选访客冒充可恢复。直接准备测试客户并清除无关自然队列，剧情走即时测试来访。保存读档绑定实际成功的本轮业务基线。
- 结果核对 runId、mode、PID、实际 persistentDataPath/company/product/Steam 状态与必需检查。PlayerPrefs 读取 environment.playerPrefsMarker.key，并独立查询实际隔离注册表的值，要求等于本次 runId。
- 超时或异常只尝试终止本脚本创建、启动时间与可执行路径匹配的PID。非零退出、缺结果、旧身份、必需跳过、正式数据变化均不能通过。launch.json、Player.log、result.json、state请求和supervisor.json保留，历史繁育检查点不作为新测试通过记录。
- supervisor.passed 表示该轮检查与运行前后保护验证通过；完整环境清理由单独 cleanup.json 记录。二者都通过才可声明本轮已完成并清理。

## 清理

全部轮次结束、游戏正常退出后，用本环境记录的 runId 指定清理目标：

~~~powershell
$environment = Get-Content -LiteralPath $envPath -Raw | ConvertFrom-Json
& "$runner/Clean-IsolatedRuntime.ps1" -EnvironmentPath $envPath -ConfirmRunId $environment.runId
~~~

清理首先验证正式数据；在本轮 cleanup-evidence 内保留完整隔离保存、实际测试包及独立 PlayerPrefs 导出并核对复制字节，再删除该环境的 game、唯一隔离保存目录和独立注册表产品项。运行日志、验证、保护清单和失败证据全部保留。目标路径、环境归属和 reparse point 必须通过检查。正式数据不执行恢复；清理开始后不会自动重试或接受历史快照。

## 维护验证

2026-10-30 迁入时：ContentNativeTools Release 编译 0 警告、0 错误；8 个 PowerShell 文件解析通过；真实 Common 的 12 个协议/路径/依赖断言通过。当前 11 个已安装包的 manifest、DLL 身份和依赖已只读验证。旧顶层 PlayerPrefs marker 读取在当前结果结构下失败，现有嵌套结构由真实公共函数正确接受；错误身份、空检查、必需跳过、启用 Steam、缺少 Forge 和相邻路径冒充均被拒绝。

这些属于工具验证。原生场景、正式包离线 scanner、普通 UI 激活和最终清理由每轮自己的证据记录，不在此固化为已通过。

Update-IsolatedProbe 新增后已解析，并在临时无游戏夹具中执行真实入口：探针更新成功，旧完整包及两份清单原字节保留，业务包/其他探针/旧失败结果不变；真实 reload 条件拒绝旧清单哈希；业务 ID、未知 ID、无变化包、被改动的当前候选均拒绝。正式数据夹具外部变化时，入口在更新前拒绝，保留变化，不恢复旧字节。真实游戏进程存在时的拒绝也已观测。此验证没有向实际测试环境部署。

Update-IsolatedPackage 的 Probe/Business 真实入口已在新建的无游戏夹具中通过 16 组契约检查，包括 Business 显式参数与对应 Local 开发者记录、错误角色/未知 ID/错误目录/候选篡改拒绝、完整旧包和清单归档、其他包及 formal/trust/enabled/旧失败字节保持、旧 baseline 失效及外部正式数据漂移拒绝。安全函数未被替换；没有修改实际游戏或设置。证据保留在 _build/native-test/package-update-validation-b95371f9a5b34e628f6a2804458cca31/，包括 validation.json、validation.log 和可复查的夹具入口。
