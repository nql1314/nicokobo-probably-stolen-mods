# 正式版原生测试工具

执行前遵守[原生快速回归要求](../../docs/testing/NATIVE_REGRESSION_RULES.md)。所有前置直接生成，窗口优先放在副屏，副屏不可用时自动使用主屏。Steam 客户端可继续运行，隔离副本保持 Steam API 未初始化。分别记录输入夹具和实际业务结果。保存和读档绑定同一候选，使用独立进程。隔离身份同时覆盖 `globalgamemanagers` 与早期启动的 `app.info`，核验两者及真实 PlayerPrefs。

本目录维护正式版 Unity Mono / 内置 ModLoader 的测试工具。业务候选默认取当前正式游戏已安装的包；修复回归可显式给 `Package-IsolatedContent.ps1 -CandidatePackageDirectory` 传入已构建并冻结的本地候选。该脚本本身不编译业务 Mod，只校验并复制到唯一隔离副本。测试使用独立 `NicoT_*` 保存目录和 PlayerPrefs，不改写正式启用/信任记录，也不自动用旧快照恢复正式存档。

- `native-tools/`：本轮环境、已安装候选、运行监督、正式数据核对及隔离环境清理。
- `content-native-probe/`：普通加载、生产、物流、Aug、扒窃、修复回归与独立进程 ES3 读回。
- `speed-stock-probe/`：速度、刷新、真实键盘与输入框屏蔽。临时场景阻断保存，不能代替 ES3 验收。

## 构建与准备

从仓库根目录用 PowerShell 执行，路径按本次实际环境传入。测试工具构建不调用根目录 `Build-Mods.ps1`，不会安装到正式游戏。

```powershell
& ./tools/official-native-test/content-native-probe/Build-Probe.ps1 -GameCodeDirectory '<当前待测Build导出的源码目录>'
& ./tools/official-native-test/speed-stock-probe/Build-Probe.ps1
& ./tools/official-native-test/native-tools/Prepare-IsolatedOfficialGame.ps1 `
    -GameDirectory 'F:\SteamLibrary\steamapps\common\Probably Stolen' `
    -AppManifestPath 'F:\SteamLibrary\steamapps\appmanifest_4348910.acf' `
    -FormalSaveDirectory (Join-Path $env:USERPROFILE 'AppData\LocalLow\Questing Goose Studio\Probably Stolen') `
    -ArtifactRoot (Join-Path $PWD '_build/native-test') `
    -PythonPath (Get-Command python -CommandType Application).Source
```

准备脚本读取当前 appmanifest，创建新的 `native-<GUID>` 环境，输出 `environment.json` 路径。它复制游戏运行时，修改副本的 Unity 产品身份和 Steam 初始化；核对加载器、扫描、信任及保存方法没有被改动。Steam 服务、工坊、云存档与成就不在该副本验收范围内。

正式数据保护按**本轮**建立：保存全部实际字节及候选身份，核验不一致时报错，绝不覆盖用户之后的更改。不得把迁移前的 environment、旧 PID 或旧 Cleanup.ps1 当成本轮环境；身份以新 GUID 和实际字节记录为准，不能仅凭父目录日期判断。

```powershell
$environmentFile = '<本次准备输出的绝对路径>/environment.json'
& ./tools/official-native-test/native-tools/Package-IsolatedContent.ps1 `
    -EnvironmentPath $environmentFile `
    -ProbePackagePath @(
        (Join-Path $PWD '_build/native-test/content-probe-build/package'),
        (Join-Path $PWD 'tools/official-native-test/speed-stock-probe/package')
    )
& ./tools/official-native-test/native-tools/Open-IsolatedReviewGame.ps1 -EnvironmentPath $environmentFile
```

通过副本的正常 Mods 界面检查和启用 11 个候选及两探针，完成必要的正常授权与重启；不打开 Developer mode。普通启动未带专用参数，两探针保持惰性。先正常退出该游戏，再执行监督器。

若只是修正已安装的测试探针，可在游戏退出且正式保护核验仍通过时使用 `native-tools/Update-IsolatedProbe.ps1 -EnvironmentPath $environmentFile -ProbePackagePath '<新探针包绝对路径>'`。它保留旧完整探针包和候选清单，验证其他包不变，再写入新版清单；随后需要普通 UI 重新审核。旧 save 基线因候选清单变化不能继续使用。若正式候选或正式数据已被其他操作改变，保留旧环境，重新准备当前基线，不覆盖旧保护记录来消除失败。

## 执行顺序

所有轮次有独立运行 ID、日志与结果文件；`-Slot` 只能使用这份独立环境中的测试槽位。

默认按 Developer mode 关闭验收。用户明确要求开发者模式时，先由用户在副本正常 Mods 界面开启，再给监督器传 `-DeveloperMode`；这只选择测试期望，不设置任何信任/启用状态。实际记录中的开关必须为 true，加载状态可以是文件未变化时的 `Match` 或变化后走开发者分支的 `DeveloperMode`。成功 save/reload 基线必须使用同一 `trustExpectation`，不能把开发者模式结果当作普通信任验收。

```powershell
& ./tools/official-native-test/native-tools/Invoke-IsolatedContentProbe.ps1 `
    -EnvironmentPath $environmentFile -Mode catalog -Slot 0
```

catalog 后确认隔离目录已由食品 Mod 正常生成完整 schema 7。仅在隔离的 `ModData/nicokobo_food_processing/recipes.json` 中将 F32 的 `alcohol_base.Amount` 设为 2，保留其他当前默认值。不要改正式目录中的旧 schema 5 文件或只替换版本号。

```powershell
& ./tools/official-native-test/native-tools/Invoke-IsolatedContentProbe.ps1 `
    -EnvironmentPath $environmentFile -Mode review-save -Slot 1
& ./tools/official-native-test/native-tools/Invoke-IsolatedContentProbe.ps1 `
    -EnvironmentPath $environmentFile -Mode review-reload -Slot 1 `
    -Baseline '<上一步成功的本轮 result.json 绝对路径>'
```

继续按清单选择 `production-save/reload`、`aug-save/reload`、`theft-save/reload`，每组使用自己的槽位及成功基线。剧情前置走即时测试来访；鼠标手势另列未验证，不能用程序化局部调用的结果代替。

`night-save/night-reload` 保留验证生产顺序所需的三个完整原生夜晚：真实卷帘、关店、床铺和结束按钮，由游戏协程执行 EndNight、BeginDay 与自动保存。F30 的真实消费建立奶酪欠额，第二夜验证先食品缺料、后灌装产四份奶酪，第三夜食品消费一份并产两份成品；逐夜核对十个生命周期事件、资源和自动 ES3，再用新进程读档。生成客户与库存前置并清除无关自然队列，不等待随机访客；不写受测欠额或产物，不直接分派夜晚事件代替整夜。未覆盖的成长／电网／养殖组合和物理鼠标手势单列。

```powershell
& ./tools/official-native-test/native-tools/Invoke-IsolatedContentProbe.ps1 `
    -EnvironmentPath $environmentFile -Mode night-save -Slot 4
& ./tools/official-native-test/native-tools/Invoke-IsolatedContentProbe.ps1 `
    -EnvironmentPath $environmentFile -Mode night-reload -Slot 4 `
    -Baseline '<同一候选集合内成功 night-save 的 result.json 绝对路径>'
```

最新 Item Manager 使用 `manager-save/manager-reload` 单独验证，包含实际编辑控件回调、数值类型/边界、真实生成和新进程读档。为该组选择未占用的独立槽位；reload 只引用同一候选集合里通过的 manager-save 结果。

语言验证按游戏实际主菜单流程执行：`manager-save/reload` 选中文，`manageren-save/reload` 选英文；在进入库存前触发真实语言下拉回调，并等待本地化初始化及必要的语言事件完成。库存场景没有语言下拉，不在库存内热切。英文保存/读档完成后再执行中文组，通过同一原生回调恢复隔离环境的语言偏好。两组核对四个已知字段的实际名称、双语说明/单位/raw tag，以及未知键回退；不把字符串断言当作全部字体视觉验收。

档案箱使用 `dossier-save/reload`、`dossieraug-save/reload`、`dossiertheft-save/reload`，分别建立普通、机械飞升、摸包客新局。读取真实16×16网格，走原生网络升级的选择/付款按钮，核对扣300现金后17×16、文档UID/数量/坐标和书内页保持。移出一份初始文档后保存重进，要求该文档仍在外部，不能重新执行新局归档。原生教程书内部页面属于嵌套内容，不作为漏收的顶层文件。

`grayline-save/reload` 使用机械飞升新局的隔离 `initialCredits=10000` 配置，记录并恢复原字节／不存在状态。用真实测试来访立即打开 E01/E02，实际支付调查费、领取证物，再用灰线测试来访完成三笔真实购买和重复扣款保护。在选定商品的原生 `OnItemBought` 前注入一次异常，核对当次暂停、已有收据不变，以及四个 Mod 忙碌标记同时开启时原生 ES3 仍写入新的存档标记和待确认交易。真实送客后执行两次完整夜间结算，核对次日新货单的第四笔购买及自动保存；无关普通客户仅作为输入队列夹具移除。不等待完整周目，不写入剧情、交易或领奖结果。reload 只接受同候选成功基线，在新进程核对 D3、四件实物 UID、现金及剧情／供货状态。

完整回归另有以下独立家族。每个 save/reload 使用同环境、同13包字节、同信任期望、不同原生进程；新增场景源码存在不等于已经执行通过。

| 家族 | 实际范围和边界 |
| --- | --- |
| `storylegal` / `storysettled` / `storylost` / `storybeyond` | 10000开局输入、真实制造和13领奖，E01–E10即时测试来访、真实选项及付款；仅执行断言所需的结夜与后日谈，支持真实DuplicateSlot和原/复制槽独立读档。不写完成标志。 |
| `achievements` / `achievementsgaps` | 生成资金、800g鼠、陈酿年龄和吸引力等输入；Forge十目标由真实页面判定、制造／付费及领奖，再验证ES3。夹具不是正常玩法获得过程。 |
| `reputation` / `ratfixture` | 声望设在阈值附近，真实兑换跨过阈值；直接生成800g鼠并核对资格与读档。旧 `breeding` 自然遗传／成长模式已取消，不在快速回归中续跑。 |
| `augprotection` | 真实伪人证物/脑机、嵌套库存和真实买家单/批卖保护；保留空容器阳性对照及原UID读档。 |
| `logisticsfull` / `logisticsboundaries` | 运行时配方和适用传输层、35设备接口能力、36液体与边界、真实S06完整夜；原生化学变化、持久化与默认tag省略分别验证，unsupported/partial不提升为活跃全覆盖。 |
| `deviceflows` | 35设备idle/unsupported控制、两台RAS两夜喂养/成长/发电/收获、Hopper跨页与接收回调重入。只有实际工作的设备行可标活跃。 |
| `containerproduction` | 原版三瓶/一盒按钮、电子零件真实升级、缺电/满输出、重复产出和原UID读档。 |
| `vanillachemistry` / `vanillachemistrycapacity` | 7原版化学成品及真实拒绝；满输出为独立必需家族，任何失败仍阻断整体验收。容量家族仅有 `-save`，不接受失败基线读档。 |
| `chemicalreactions` / `nightdevices` / `businessdevices` | 原版22化学规则及实际加热；夜间充电/冷藏/陈酿等设备；售货/饮水/成交、方案和卡带。使用各自正常入口，不以直接夜回调冒充完整夜晚；以本轮真实结果为准。 |

监督时间只覆盖必要的真实操作，不为自然客户、繁殖、成长或完整周目前置延长测试。若进程或宿主会话意外消失，保留原日志并核对进程／正式保护；缺结果或监督记录不能算通过，失败或不明基线不能用于普通 reload。

```powershell
& ./tools/official-native-test/native-tools/Invoke-IsolatedContentProbe.ps1 `
    -EnvironmentPath $environmentFile -Mode speed-stock -Slot 0 -TimeoutSeconds 1200
```

速度/刷新轮读取该轮 `result.json.state.json` 的 `phase=key`、`request`、`key`、`label`，向**这次拥有的游戏窗口**发送指定真实按键。每次请求只响应一次；没有输入时探针有界失败。该轮采用 `--nicokobo-native-speedstock-test`，不会同时启动内容探针；内容轮使用 `--nicokobo-native-test`。输入框覆盖当前 Item Manager 的真实 TMP 搜索框和 Unity InputField 夹具；这些按键结果不能代表全部 Mod 搜索/终端界面已手测。

## 结果与保留

以监督器、探针逐项观测和正式数据核对共同判定，不单看退出码或顶层 `passed`。任何必需场景未执行都保留为未测/阻塞。失败轮次、原候选、日志、保护清单、保存收据与种子均保留；后续成功不覆盖它们。

完成后先确认自建进程已退出，再使用当前环境的验证/清理入口。清理仅针对当前拥有的隔离目录与注册表身份，先保留保存证据，不能恢复或删除正式保存。删除若被自动审批拒绝，记录拒绝并保留现场，不改用其他渠道删除。

外部历史源码位于 `%USERPROFILE%/Documents/Codex/2026-10-29/task/{content-native-probe,native-tools}`；旧速度/刷新证据位于 `_build/native-test/20261029-speed-stock-ad87733c`。本次迁移复制维护源码，原历史目录保持原样。Demo 工具留作历史来源，不进入这套运行链。
