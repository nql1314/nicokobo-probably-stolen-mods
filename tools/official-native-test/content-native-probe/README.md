# 正式版内容原生探针

探针和 Inspector 覆盖四个内容 Mod、Forge、物品管理器、加速和商品刷新。合成、矩阵和机械飞升类型来自 `MechcoreProtocol.dll`，伪人实例从合包入口 `_aug` 读取。历史十一包／十三包结果保留当时身份；这次只编译探针，没有运行合包原生测试。

按[原生快速回归要求](../../../docs/testing/NATIVE_REGRESSION_RULES.md)执行。直接生成前置，窗口优先放在副屏，副屏不可用时自动使用主屏。结果中的输入夹具不能当作正常玩法取得过程。

维护源码从 `%USERPROFILE%/Documents/Codex/2026-10-29/task/content-native-probe` 收回本目录，外部旧目录保留。本轮接口与官方扫描器核对基线为 Steam Build **26317870**；构建必须显式传入与受测游戏一致的 `GameCodeDirectory`，不回落到历史反编译目录。一个官方 `IMod`，目标 `netstandard2.1`，静态引用只来自游戏 Managed；产品候选只从游戏加载器实际加载的程序集读取。

探针未获 `--nicokobo-native-test` 时不执行任何测试。启用后，先核对实际 `Application.persistentDataPath/companyName/productName` 和 `SteamManager.Initialized == false`，再写隔离 PlayerPrefs marker。外部监督器负责新建克隆、用户选择的 UI 激活方式、启动、PID 与正式环境核对。构建本目录不会启动或安装游戏，也不会编译 11 个产品 Mod。

信任期望默认是普通模式。只有显式传入 `--native-test-developer-mode=true` 才按开发者模式核验，参数只接受小写 `true` / `false`，缺省等同 `false`。默认保留 `loader.ordinary-trust.<id>` 的原有断言；显式开发者模式使用 `loader.developer-trust.<id>`，要求 `TrustRecord.developerMode == true`，实际加载状态可为 `Match` 或 `DeveloperMode`：原生加载器在已批准文件未变时先返回 Match，变化后才使用开发者分支。`environment.trustExpectation` 写为 `ordinary` / `developer`，`environment.loadedModTrust` 记录全部已加载 Mod（包括两个探针）的实际状态。这里只读取，不设置信任、启用状态或扫描政策；开发者模式运行不证明普通信任验收通过。save/reload 必须使用相同信任期望。

## 模式与覆盖

所有模式都核对 11 个目标的实际加载、所选信任期望和候选 DLL；仅六个内容提供者要求非空工厂和图片。`Nicokobo.ContentInterop` 从 **Nicokobo.Forge** 解析；客户端合同在所属程序集内通过真实 facade 解码，不把同名类型当作同一个 CLR 类型。

| 模式 | 真实执行内容 |
|---|---|
| `catalog` | APPRENTICE 新局与 InitialSave；当前工厂、资源、合法 UID/特征归属与无 ModData/真实库存副作用；原生水壶和金属锭工厂对照；缺标签/真假注射剂分类；Forge 私有固定能力、脑机接口与矩阵节点、混合原生能力 tooltip、8 次真实随机节点 smoke。 |
| `production-save` / `production-reload` | 当前生产目录、物料/电/水/失败回滚、原生净水及矩阵、物流；真实 SaveGame；新进程正常 LoadGame/ES3/UID/资源/收据比较。 |
| `review-save` | R01 五种原生酒瓶、R02 容器手动欠额、普通玻璃真实消费后补货和熔炉拒绝/恢复、R03–R04 实际 F32 数量与酒精强度、R05 香水状态补货、R06 网络根替换、R07 机器人/失败产物图释放、R08 Forge 配方原型十轮释放；真实 SaveGame。 |
| `review-reload` | 新进程正常 LoadGame，按保存的实际产物 UID 复读 F32 强度和数量；验证 R05 余欠/香水、R06 三根 UID/内容/容量/储电。 |
| `manager-save` / `manager-reload` | 当前已安装 Item Manager 1.3.2 的真实面板控件回调；原生双语标签名称/说明、未知键回退、int/float/double 外层字段、专用字段去重、输入拒绝及精度、高级标签同控件往返、实际 ×1/×5 按钮；真实 SaveGame 后新进程按生成 UID 读回各 TagState 数值槽。 |
| `aug-save` / `aug-reload` | 正常 Aug 开局、已维护生存/医疗拒绝/制造/工坊/存档场景；可选原生 DuplicateSlot 后的新进程读回。 |
| `theft-save` / `theft-reload` | 正常摸包客开局、真实目标/练习/偷窃/烟幕弹/外出保存与新进程读回。 |
| `night-save` / `night-reload` | 三个完整原生夜晚、正常下一日、真实机器先后顺序和自动 ES3；不直接调用设备周期制造过夜证据。 |
| `storylegal` / `storysettled` / `storylost` / `storybeyond` 的 save/reload | E01–E10即时测试来访，真实选择、制造证明、工坊领取、四结局及必要后日谈；独立原槽和复制槽读回。不推进36夜准备前置，主选项 ID 与未测细分条件分别记录。 |
| `logisticsfull` / `logisticsboundaries` / `deviceflows` 的 save/reload | 分别执行完整注册配方与物流路径、液体和固体拒绝边界、原版 RAS 等活跃设备；固定真实 UID、36组分账本、合法库存几何及原生 ES3。未支持路径仍保留未执行状态。 |
| `vanillachemistry` / `chemicalreactions` 的 save/reload | 七种原版化学成品及22条实际反应规则；真实按钮、原生计时和独立计量断言。`vanillachemistrycapacity-save` 单独保留满输出的必需失败检查。 |
| `containerproduction` / `nightdevices` / `businessdevices` / `nativedna` 的 save/reload | 原生容器生产、夜间设备、售货和方案/播放、DNA提取及应用；材料和电量仅声明合法准备输入，不写生产、成长或完成标记。 |
| `bottleproduction-save` / `bottleproduction-reload` | 三种水瓶独立家族，真实升级、断电/满输出/重复生产及精确7个承诺UID存读；原 `containerproduction` 的四种产出分母与 Box 必需红测单独保留，不能由瓶家族成功覆盖。 |
| `extendedproduction-save` / `extendedproduction-reload` | 原版17种默认研磨、12种默认溶解及大营养果原生变体的30入口，五项拒绝/置换控制，九个完整夜晚的真实反应堆废料、维修及普通吸粪收集/满箱拒绝；固定UID与原槽ES3。动态表型和任意浓度组合另列未测。 |
| `achievements` / `achievementsgaps` / `reputation` / `ratfixture` 的 save/reload | 生成资金、800g鼠、陈酿年龄、声望阈值等输入，真实Forge判定、制造、付费、兑换和领奖；分别保存与跨进程读档。旧 `breeding` 已取消，不续跑自然繁育。Steam账号解锁另列未验。 |
| `augprotection-save` / `augprotection-reload` | 默认500 Aug新局、即时E01证物与开局脑机，生成明确愿意接收禁品的测试客户，执行真实单卖/批卖/嵌套拒绝与空包正例，原UID及独立读档。 |
| `dossier` / `dossieraug` / `dossiertheft` 的 save/reload | 三种真实开局的档案、付费扩展及独立读档。 |

各家族分别记录生成输入与真实执行入口。直接机器 cycle、机器人 Work、陈酿年龄或温度输入只证明相应接口／保存行为；整夜只认实际EndNight、下一日与自动ES3。只运行生产顺序、生命周期及后日谈断言必需的夜晚。R08只覆盖真实Forge读图释放；程序化控件回调不代表物理鼠标手势。随机未发生的分支保留not-tested，不伪造结果。当前完整结果见Git 历史。

清理观察只给已安装候选的真实 `ItemGraphCleanup.Destroy(GameItem)` 添加被动 prefix，记录原始 UID、窗口和 handler，原方法始终执行；不替换工厂、产物、随机数、事务或返回值。探针自己的临时物品 source-link 仓库真实 `mods/shared/ItemGraphCleanup.cs`。

工厂允许原生 `AddItemFeature → GetUniqueID` 分配 UID，例如水质、锭纯度与合成品级。探针验证 UID 为零或本次新分配且不与真实库存冲突，每个特征的 `parentItemUniqueId` 与所属物品一致；ModData 和真实库存分别断言不变。原生 `water_jug`、`metal_ingot` 直接执行注册工厂作对照，不清零 UID、不回拨计数器。

R08 的同步副作用检查仅针对本场景的灌注塔、空酒瓶工厂：每次真实 `ReadItem` 调用前后比较 UID 计数、ModData 和真实库存，不在中间 yield。等待两帧后仍严格检查原始窗口 handler/图根释放，以及十轮真实库存和所有权不变；跨帧全局计数只记观测。保留的 `native-9a9f867c97c94efc95328c69ed9929b6/review-save-99ea3631-4de3-4af3-85e2-ac154dcfd1d3/result.json` 中，旧断言仅因计数 296→297 失败，149 件物品快照深相等，20 个窗口和十轮回收检查均通过；20 个被观察原型 UID 均为 0。该证据没有定位跨帧额外分配的具体来源，不能据此断言某个后台系统造成了变化。

R02 遵循现行物流设计：空酒瓶仍是液体容器，外壳需要手动准备，不能塞入虚空资源网。探针在真实主仓库摆放空/非空瓶，断言其手动欠额及 FillOnce 不转移；普通玻璃则通过真实消费欠额验证自动补货。初版清单把酒瓶自动补货列为必过属于清单错误，未修改产品政策。

## 当前 Item Manager 1.3.2

`manager-save` 从真实已加载 `NicoItemManagerMod.Active.Panel` 建窗、选择当前目录条目，使用产品生成的 TMP_InputField、Toggle、Button 的 UnityEvent 回调。原生样例为 `current_converter` 的整数升级计数/float 转换率、`tilapia` 无后缀 float 健康值，以及 `water_jug` 的 double 温度。外层在未展开高级标签时可见；无后缀 float 改零后再次编辑验证当前编辑会话保留类型；高级区相同数值只显示同步只读值。

本次整体重测使用用户统一版本号后的 1.3.2，实际候选已包含双语标签说明和三个高级回调修复，以本轮 DLL 记录区分历史同版本包。定位控件读取真实 `CustomUITooltipTrigger` 的 `Tag: 原键` 末行，不依赖已本地化的可见名称。四个原生字段的中文/英文名称与说明按独立硬常量核对已安装 `ItemTagDescriptions.Find` 返回值，再核对当前语言实际标签及包含双语名称、当前语言说明、原始键的 tooltip。`NATIVE_PROBE_*` 未知键在外层/高级区保留原键和未确认含义提示。没有切换全局语言：另一语言的元数据已纳入检查，另一语言面板实际渲染仍明确未测。

负数、科学计数法、round-trip 精度、NaN/Infinity/溢出和无效文本通过实际输入回调验证。为覆盖所有分支，只在真实 `energy_credit` 编辑原型上一次性加入 `NATIVE_PROBE_*` 的 int/float/double/disabled-float/bool/string 夹具，并记录 `environment.managerExplicitFixture`；不替换工厂、解析器或任何 UI 回调。高级 string A→B→A、bool true→false→true、disabled float 1.25→2.5→1.25 连续提交使用产品现存行，正常调用 Rebuild 响应产品请求，不在提交之间强制 BuildEditor 掩盖闭包状态问题。

真实 ×1/×5 按钮调用 `Capture → ItemSpawner → DirectoryMaster.Item → SlotMarker.TryAcceptOnce`。结果核对原生库存 UID 差异、各标签槽、已存在物品不变、生成物之间不共享标签对象及后续预览编辑不追改已生成物。`persistence.manager` 独立记录实际 UID 的 enabled/int/float/double/long/string/bool；浮点值用 round-trip 文本保存比较证据，`manager-reload` 在另一进程正常 LoadGame 后读取真实 TagState，对照每个槽。它不使用通用 SnapshotItems 代替数值读回，也不把没有类型判别字段的原生 TagState 宣称为自带 CLR 类型标签；无后缀零值的类型记忆仅承诺当前编辑会话。

这些是程序驱动的真实 UnityEvent 和可见面板构建，不代表实际键鼠输入、焦点/排版验收、完整 UI 十轮或过夜；这些项明确记为未测。测试不要求失败的 save 基线进入 reload，修前失败轮保留，修后使用新成功 save 轮。

## 食品配置前置

先在新隔离环境跑 `catalog`，由当前食品 Mod 的正常缺文件流程生成完整 schema 7。退出后，仅把该隔离目录 `ModData/nicokobo_food_processing/recipes.json` 中 **F32 → Requirements → ResourceId=alcohol_base → Amount** 改成 `2`，再启动 `review-save`。不要修改正式游戏旧 schema 5，也不要只改 SchemaVersion。

探针用真实 `RecipeConfiguration.Parse`、已加载 `FoodDefinitions` 和 G 的 Forge recipe view 三处核对 amount=2。实际产物从 K04 输出库存读取 `ALCOHOL_VALUE`，记录 UID；同类两个独立啤酒→1，混合啤酒/努特加→2。单件对照在同一个隔离进程临时改文件、调用真实 Load，得到 1/4，然后 finally 恢复原文件字节并 Load 回 2；这不代表另一次进程启动。部分堆叠只在原生 `MayStack`/真实堆叠槽允许时进行，不造不可堆叠物品。`review-reload` 仍要求 amount=2。

## 构建与离线检查

```powershell
.\tools\official-native-test\content-native-probe\Build-Probe.ps1 -GameCodeDirectory '<当前待测Build导出的源码目录>'
```

必须显式提供 `-GameCodeDirectory`，其中扫描器源码应从当前待测Build导出；不再隐式引用历史Build26284217。直接编译 Inspector 同样须传 `-p:GameCodeDir=<当前源码目录>`。另可参数化 `-GameDirectory`、`-CandidateDirectory`、`-ArtifactDirectory`；默认候选为正式游戏当前 `Mods`，默认证据和测试包位于 `_build/native-test/content-probe-build/`。`-ProbeOnly` 仅检查探针自身；默认读取 11 包已安装元数据。不会构建产品或安装测试包。

Inspector 接口（所有路径推荐绝对路径）：

```powershell
dotnet <Inspector.dll> --probe-only <probe.dll> <manifest.xml> <output.json> <Managed目录>
dotnet <Inspector.dll> --candidates <probe.dll> <manifest.xml> <output.json> <Managed目录> <Mods目录>
```

它 source-link 当前 `ModScanner/ModScanResult/ModScanFinding/ModCapability`，元数据读取使用 DseCecil，不执行候选程序集。只查编译接口/扫描政策，不证明游戏行为。

## 运行计划与结果

`New-ProbeRunPlan.ps1` 只生成计划，不启动：

```powershell
.\New-ProbeRunPlan.ps1 -IsolationRoot $environment.root -GameDirectory $environment.game `
  -PersistentDirectory $environment.save -Company $environment.company -Product $environment.product `
  -Mode review-save -Slot 0 -ArtifactRoot (Join-Path $environment.root 'manual-plans')
```

GameDirectory 必须是明确 IsolationRoot 的 `game` 子目录；ArtifactRoot 必须在该根内。Product 是 `NicoT_<9 lowercase hex>`。reload 必须传 `-Baseline`，且是同模式族、同隔离存档路径、槽位和信任期望的成功 save 结果；探针在 LoadGame 前再次核对基线、不同进程、槽位与实际 ES3 字节。只有用户明确选择开发者模式时才附加 `-DeveloperMode`；计划将显式传入 `--native-test-developer-mode=true`，默认传入 `false`。`-DuplicateSlot` 只支持 aug-save，`-UseCopy` 只支持 aug-reload。每轮新 GUID/新目录，结果文件 CreateNew，拒绝覆盖。

保留监督器契约：`environment.playerPrefsMarker.key`、`runId/mode/processId/checks/passed/executed`。`passed` 需要所有必需项通过；可选未覆盖使 `coverageComplete=false`，不会伪装成已测试。真实 ES3 保存/重进和仅 JSON 状态比较分别记录。原生、跨进程及 UI 验收以本轮监督器产出的结果为准；离线编译成功不替代它们。
