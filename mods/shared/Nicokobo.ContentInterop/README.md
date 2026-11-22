# NicoKobo Forge 正式版公共层

公共实现已拆入独立前置 **NicoKobo Forge**（`nicokobo.forge`，服务协议 1），入口工程为 [NicokoboForge.csproj](../../nicokobo-forge/NicokoboForge.csproj)，运行包只分发 `Nicokobo.Forge.dll`。本目录保留由前置工程直接编译的机器互操作源码；固定模组能力位于前置工程。四个内容包不再引用或分发 `Nicokobo.ContentInterop.dll`，本目录不再提供独立库工程。

内容包通过 [轻量客户端](../Nicokobo.Forge.Client/) 和共享视图源文件调用前置。标准 `IServiceProvider` 提供只含 BCL／原生游戏类型的委托协议；静态目录、生产队列、G 窗口和供货补丁只在前置中维护。不同内容版本可共用兼容协议，避免同名不同版本 DLL 混载。前置缺失或协议不匹配时停止内容激活。

完整包结构、官方扫描边界、加载时序与迁移约定见 [Forge 前置说明](../../nicokobo-forge/README.md)。物品、配方、owner、配置及保存身份保持原值，独立 Demo Forge 仓库不参与正式版运行。

## 官方负责与共享层保留的边界

| 能力 | 当前处理 |
| --- | --- |
| 模组加载、设置、资源和本地化 | 使用官方 `IMod`、`ModConfig`、资源目录和 `ModHelper`；共享层不重建加载器或资源平台 |
| 生命周期、顾客和掉落 | 使用 `ModHook`；供货通过 `OnGenerateCustomerVeryLate` 和可修改结果的 `OnLootRolled` 接入 |
| 窗口和按键配置 | G 查询使用官方 `CustomUIManager`、`CustomUIBuilder` 和选项接口 |
| 开局、人脉和周目数据 | 内容接官方接口；Forge 仅共用 owner／当前周目／槽位／旧值校验后的内存暂存，不增加保存系统 |
| 库存和物品移动 | Forge `Services/` 统一预检与真实搬运，继续使用原生库存／槽位，不维护库存副本或私有存档包 |
| 跨包机器与配方 | 保留 `MachineCatalog`、`ExtraRecipes`，承载当前启用内容的真实槽位、输入条件及动态配方 |
| 实际生产消耗 | 保留 `ProductionHooks` 的提交状态、实物消耗和耗水量，供物流补货；不再扫描、镜像或上报产出列表 |
| 共享电网与供货规则 | 保留 `DevicePower` 及供货筛选／权重；这是内容互操作规则，官方基础接口不代替它们 |
| 固定模组能力 | Forge 的 `FixedModuleAbilities` 私有表保存固定能力及回调；只挂接计算和提示，不修改原版随机生成方法 |

不移入 Demo 的 IL2CPP 修补、BuildProbe 平台、缺失物品自动恢复或历史存档转换。当前原生档案箱扩容由 ForgeDossier 独立处理。

## 生命周期与包边界

入口 `OnEnable` 通过 `ForgeClient.Enable` 排队；资源加载阶段连接前置后调用内容激活及 `ContentRuntime.Attach(contentOwnerId)`，目录就绪后发布本包机器、配方、供货及电源。`OnDisable` 调用 `Detach`，只清理该 owner 的登记。首个 owner 创建一个公共 G 查询窗口／官方按键配置并订阅生命周期；最后一个 owner 退出时关闭窗口、撤销选项及共享供货补丁。

机核协议只有一个客户端和入口，依次启动合成、矩阵与机械飞升组件；三个原内容 owner 各自 Attach／Detach，保存、配方和服务登记继续使用原身份，资源及官方设置归属合包。

各入口程序集补丁交由官方加载器安装。前置不调用重复的自动 `PatchAll`；共享供货适配器使用独立固定 Harmony owner，安装失败撤销已装补丁。首次 attach 订阅公共夜间 flush，早于各包的 Late 闲置成长结算；Forge 机器队列则在原版普通生产循环内清算。禁用普通内容 Mod 需按游戏要求重启，不承诺热卸载。

共享库本身不需要额外能力。顾客供货使用官方 `OnGenerateCustomerVeryLate` 接入现有对话，不扫描或补丁编译器生成的方法。掉落改用官方 `OnLootRolled`，通过公开 `GetTableEntries` 取得快照后应用原有供货权重，不修改注册表。没有额外供货条目且不需过滤时保留原结果，不增加抽取；保留此前回调提供的表外结果。

共享供货仅保留四处限定到游戏类型的补丁：发明家／普通供应商作用域、统一 `ItemSpawner.Spawn` 和柜台入库。`SpawnFromTable`／`SpawnFromTableGroup` 本来就调用 `Spawn`，不再重复补丁这两个包装入口。矩阵及机械飞升已删除随机候选表的 IL 补丁，固定能力通过前置自己的表解析；Forge 和四包的 `Capabilities` 均为空。内容包通过 `Prerequisites` 声明 Forge 前置，并在资源加载阶段确认服务可用和协议兼容。库不修改加载器、扫描器、信任、启用状态或 Developer mode。

## 机器、配方及 G 查询

- `MachineCatalog.Register(owner, machineId, ports, recipes)` 发布实际原生槽位和实时配方；`AddNativeRecipes` 合并原生熔炉等附加目录。不同 owner 不能覆盖同一机器。
- `MachinePorts` 提供投料、产物及可选模组、电源、水容器库存。`Recipes(machine)` 会应用机器模式过滤；查询目录不执行生产。
- `IngredientView.Matches(item)` 是扣料条件，展示 ID 不能代替它。`WaterParts` 使用原生单位，1 ml = 1000 parts。
- `ExtraRecipes.Register` 发布动态配方；可用性每次读取，`ResolveOutputId` 在真正生产时选产物，`OutputCount` 支持真实多件产出。detach 后立即移除该 owner。
- G 查询窗口可搜索物品、机器、材料和配方 ID，并分页显示；名称／图标原型缓存不分配 UID，语言变化时清缓存。已接入的内容共用 Forge 提供的窗口和按键绑定。

矩阵 K02 配方 ID 为 `nicokobo.mechcore_protocol.module_matrix.recipe.rare_module_upgrade`，任意两个基础模组／节点可混用，生成一个当前可用的随机稀有模组／节点。它复用合成扩展实际生产事务，通过动态配方目录登记。K05 通用终端由 Forge 提供，合成扩展、机械飞升和物流登记各自的附加配方。

## 生产回执及物流

物流通过 `ProductionHooks.Subscribe(owner, id, before, after)` 接入。Before 在材料、电源及产物空间检查前执行，然后拍摄本批实物快照。生产提交后标记 `Commit`，scope 结束发送 `WorkResult`。失败也发送结果，收到回调不代表成功。

回执只包含物流需要的实际扣除物品、跨堆叠数量和水量差。删除仅被检查工具消费的 `WorkResult.Products`／`ProducedItem`，不再为此扫描并复制产出库存；出料直接读取原生库存。同机嵌套共用一次前后回调，每次真实批次独立结算，避免熔炉批量及玻璃回收重复记账。After 观察者异常记录后继续其他观察者；Before 异常向生产方传播。

当前接入路径包括食品加工、合成设备和物流适配的原生机器。物流移动真实库存，遵守原生容量、锁定、UID 及电源计费。夜间只排队原生实际产生的机会，在普通生产循环结束、第二阶段进料斗之前按序执行，保留当次 `CurrentConverterHelper.isNightCycle`；不凭空补跑批次。

Forge 不再通过机器或目录工厂包装 `onCycleEndSlotItemFunc`。原版夜间、加速器及调试加工的调用处进入统一边界，保留原回调和 Target。手动加工通过客户端 `NativeMachineAccess.Process(machine)` 进入同一边界；直接调用物品回调不执行 Forge 的原生机器物流和电力补偿。此接口新增服务操作 `native.process`，自动化物流需与对应 Forge 一起更新。当前编译及离线检查不代表组合游戏验收。

`DevicePower.Register(owner, query, debit)` 让电源归属方处理真实共享电网。查询不改状态，先解析所有声明以拒绝多 owner 冲突，再原子扣费。已识别电源不能回退原生路径重复扣费；不足和超容量按设备规则失败，不拼凑虚拟电量余额。

## 义体与供货

`AugmentationKeys.CognitiveTurbo` 保留 `nicokobo.aug.cybernetic.cognitive_turbo`。只统计当前周目实际拥有的正数量植入物；矩阵据此接入成长上限 +50%、学习算法和真实加工成长。机械飞升提供真实七件义体及脑机接口，不创建占位物。合成扩展的目录随实际提供者更新。

`SupplyCatalog.Register(owner, offers)` 共享一次 Mod 总预算。直抽保持原生 1／Mod 0.25；表抽按保留条目的正权重总和 `W` 分配合计 `W × 0.25`，原生总重 1000 时追加 250，占比 20%。保留指定商品、原生上架及容量检查。矿工整批只抽一次，原版／石英／钛为 70%／15%／15%，原矿掉落权重 125。候选来自当次可用真实工厂，detach 后立即移出；不替换全局战利品表。供货包括食品饮料、矿料、合成材料、符合职业的矩阵模组和已注册医疗物品；各包专有夜店／剧情供货仍由本包负责。

## 验证

独立前置拆分前的六包历史基线完成 Release 编译，0 警告、0 错误；六包离线扫描无 HardFlags 或漏报能力，共享 DLL 一致。142 项供货定向检查及 154 项食品生产检查通过；合成／矩阵关联检查工程完成编译，未扩大运行完整套件。同步移除检查工具对产出镜像的依赖，改读实际产出库存，并更新旧权限断言。

当前离线入口见[工具索引](../../../tools/README.md#离线检查)。最小原生场景覆盖开局七类物品和工坊 13 项奖励，不能代表新共享库已完成原生验收；旧 `dist` 包也不代表新源码。详见Git 历史。
