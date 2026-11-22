# 模组前置框架 / NicoKobo Forge

## 当前版本与发布说明

当前源码版本 **1.0.13**，适用正式版 **0.46D**。Nico 工坊的成就、奖励和配方查询可读取社区语言的翻译。[Steam 创意工坊](https://steamcommunity.com/sharedfiles/filedetails/?id=3831025262)

完整变更见 [change.log](change.log)。

English release notes are included in [change.log](change.log).

Forge 使用 Mod ID `nicokobo.forge` 和服务协议 **1**，适配 Unity Mono 与官方加载器。

本包持有机器目录、批次事务、库存预检与搬运、液体与计价、耗电适配、夜间队列、供货、固定能力、G 查询，以及工坊、电话、访客对话和阅读窗的公共实现。全域制造终端、工坊名片、十项原版成就、通关预算奖励及档案箱扩容由 Forge 提供。Forge 持有内置成就的证明、奖励收据与周目 schema；各内容包持有自己追加的配方、效果、剧情、配置、数据和奖励事务。

在 Forge 的 Mod 设置中可关闭「启用 N 键打开成就」（默认开启）。拨打 1016 仍可打开 Nico 工坊。G 键只在所指物品或机器有相关配方时打开查询，指向空处则浏览已登记的全部配方。全域制造终端有可用配方时才加入博士供货和拾荒掉落。

Forge 的 Mod 设置中可关闭「档案箱扩容」（`EnableDossierExpansion`，默认开启），应用后重启游戏生效。开启时档案箱至少为 16×16，购买原版升级后至少为 17×16，并在新局首次保存前自动收纳主仓库里的开局文档。关闭后使用原版的 10×10 和升级后的 17×10，开局文档按原版处理。旧存档有物品占用额外格子时，会按原版规则自动整理，遵循图钉固定和自动旋转设置。所有物品都能排进原版格子时恢复原版尺寸，整理失败时保留扩容和原摆放。

## 生产调用入口

机器回调对象保持原有身份。夜间、加速器与调试加工在实际调用处接入补料、电力补偿和出料。自定义机器生产在原版第二阶段进料斗前完成。手动加工通过 `NativeMachineAccess.Process(machine)` 进入同一流程。

## 包与依赖

Forge 包包含 `manifest.xml`、`Nicokobo.Forge.dll`、共用终端／名片图标和终端本地化。四个内容包只带各自入口 DLL 与资源，在 `Prerequisites` 声明 `nicokobo.forge`，不再附带 `Nicokobo.ContentInterop.dll`。

为遵守当前扫描器的静态依赖边界，内容包不静态引用其他 Mod 程序集。轻量客户端和视图模型从 `../shared/Nicokobo.Forge.Client/Client.props` 编入内容入口；实际服务通过已启用 Forge 的标准 `IServiceProvider` 接口取得。边界仅传递 BCL 值、强类型委托和原生游戏对象，不做外部反射、动态程序集加载或加载器补丁。共享目录、队列与补丁只存在于 Forge。

加载器在全部本地／工坊包初始化后才加载资源。客户端据此延后内容激活，不依赖文件夹名或保存顺序；若当前资源事件属于该内容包，会补交这次资源通知，避免漏载。前置缺失或协议不匹配时停止内容初始化，撤销该内容的自动补丁并记录具体错误。官方 `Prerequisites` 本身只提示，不提供强制版本解析；相关包都应启用后重启再加载存档。

协议 1 的字段与转换定义在 `../shared/Nicokobo.Forge.Contracts/Wire.cs`，公共视图与现有命名空间 `Nicokobo.ContentInterop` 保持一致。内容包版本可以不同；兼容的客户端共用唯一前置实现。协议变更须显式处理兼容，不通过多份同名 DLL 争抢加载顺序。

## 服务职责

| 能力 | Forge 持有 | 内容包持有 |
| --- | --- | --- |
| 生产 | 跨堆叠选料、整批落位、扣料／水／电、失败恢复、生产基准价 | 配方、营养、品级、加价、产物属性回调 |
| K05 终端 | 稳定 ID、工厂、库存、图标、夜店与拾荒入口、实际制造 | 各包追加的制造配方；合成扩展另提供自身产物加工回调 |
| 库存与液体 | 落位预检、真实搬运、倒液、组分价值、准入与临时占用 | 发奖收据、偷窃候选图、物流来源／网络／调度 |
| 机器与电量 | 原生机器包装、耗电钩子、设备供电槽登记、真实夜间机会队列 | 电网容量、余额、生产顺序声明、补货和出料策略 |
| 工坊 | 多 owner 页面、N 键、1016 电话、入口说明名片、快照显示与动作分派 | 证明、资格、费用、奖励、保存与领奖回调 |
| 电话与对话 | 号码归属、访客生命周期、确认门控、分页、供应商菜单、阅读窗 | 联系人可见条件、人物、文案、交易和剧情结果 |
| 输入 | 文本焦点、手势释放、阅读窗拖动与显示生命周期 | 内容窗口的布局和控件 |
| 供货与数据 | 夜店追加入口；按 owner／当前周目／槽位／旧值暂存 JSON | 候选和概率、数据键、schema、正常保存策略 |

目录、队列、窗口、号码与回调均由单一前置持有，owner 退出时清理自身登记。内容包没有 Forge 的静态程序集引用。认知涡轮的物品判断已回到矩阵联动代码，未被使用的机械飞升 `OfficialItemTransferApi` 已移除。

## 固定模组能力

`FixedModuleAbilities.Register(owner, id, appliesTo, name, description, compute)` 在前置自己的能力表登记内容回调，不写入 `ModuleEffectHelper.moduleEffects`，也不修改原版 `InitRandomEffect`。`Add(item, id, parameter)` 绑定能力；原有 `MODULE_EFFECT1_TAG`／`MODULE_EFFECT2_TAG`、效果 ID 和参数保持原格式，旧物品直接生效，不重写存档或生成替代物品。

前置只在原生 `HandleModuleEffectAction` 正常执行后分派固定能力，并在提示框遇到私有能力时处理原生／私有混合槽。原版单独的词缀提示仍走原方法。能力计算的适用范围及数值由内容包提供；停用内容 owner 清理其能力和回调。这里使用普通 Prefix／Postfix，三份 manifest 的 `Capabilities` 均为空。

矩阵的五种节点能力及机械飞升超频使用独立能力表。六种布局随机词缀开启时仍按原版目录参与随机生成；关闭时只保留私有显示定义，已保存槽位不变，新随机池中没有它们。

这组 API 版本为 1，通过协议 1 的操作提供；旧客户端可继续使用前置。矩阵与机械飞升启动时检查固定能力 API 1，而不是比较 Mod 版本号；缺少该 API 时在内容注册前停止并提示更新前置。

## 构建与安装

只编译：`dotnet build mods/nicokobo-forge/NicokoboForge.csproj -c Release`。

Forge 独立打包、独立发布和安装，不随内容包自动构建或附送。明确需要时，从仓库根目录执行 `Build-Mods.ps1 -Mod nicokobo-forge -NoInstall`；安装时去掉 `-NoInstall`。普通 `Build-Mods.ps1` 默认不选择 Forge，`-Mod` 选择内容包也不会自动加入前置。

当前四个内容包各自声明 `nicokobo.forge` 依赖，由用户单独添加前置。合成、矩阵和伪人已合为[机核协议](../mechcore-protocol/README.md)；食品加工、物流和肯定是偷的仍分别分发。旧内置公共 DLL 的内容包须更新为依赖版后再与新前置一起启用。更新内容包时按旧 manifest 清理被替代的公共 DLL；配置、启用状态、信任和存档不变。

## 社区维护 / Community maintenance

本 Mod 后续由社区维护。遇到问题或有建议，请提交 Issue。修复、翻译和文档改动可先 Fork 仓库，再向主分支提交 Pull Request。[开源仓库](https://github.com/nql1314/nicokobo-probably-stolen-mods) · [新建 Issue](https://github.com/nql1314/nicokobo-probably-stolen-mods/issues/new) · [提交指南](https://github.com/nql1314/nicokobo-probably-stolen-mods#社区维护)

This mod is moving to community maintenance. Open an Issue for bugs or suggestions. To contribute fixes, translations, or docs, fork the repository and open a pull request against the main branch. [Source code](https://github.com/nql1314/nicokobo-probably-stolen-mods) · [New Issue](https://github.com/nql1314/nicokobo-probably-stolen-mods/issues/new) · [Contribution guide](https://github.com/nql1314/nicokobo-probably-stolen-mods#社区维护)
