# 机核协议：伪人飞升 / Mechcore Protocol: Aug Ascension

当前源码版本 **1.0.11**，适用于正式版 **0.46D / Steam Build 26525552**。已加载的 `ko` 和 `ko-KR` 翻译可互补缺失词条，缺译时回退到英语。[Steam 创意工坊](https://steamcommunity.com/sharedfiles/filedetails/?id=3831045337)

合成扩展、模组矩阵和伪人：机械飞升现整合为一个 Mod，共用一个入口、程序集和资源目录。需要单独安装并启用 [NicoKobo Forge](../nicokobo-forge/README.md)；[食品加工](../food-processing/README.md)和[自动化物流](../automated-logistics/README.md)保持独立。

Version **1.0.11** lists both eligible storage bays in the Scavenging Robot recipe. Requires NicoKobo Forge.

## 内容与操作

| 组件 | 内容 | 详细说明 |
| --- | --- | --- |
| 合成扩展 | 原矿与材料、三台自有工业机器、Forge K05 配方、30 条固定配方、六件义体、机器人与功能设备 | [组件说明](../mechcore-protocol-synthesis-expansion/README.md)、[设计与待办](../../docs/design/SYNTHESIS_EXPANSION.md) |
| 模组矩阵 | 18 件有效模组／节点，另保留已停用的批量烧制模组供旧档读取；六种摆放效果、连续成长、机器能力、K02 升级与认知涡轮 | [组件说明](../mechcore-protocol-module-matrix/README.md)、[设计与待办](../../docs/design/MODULE_MATRIX.md) |
| 伪人：机械飞升 | 独立开局、生存、脑机接口、七件自产义体、13 项工坊成就、灰线供货与失物招领剧情 | [组件说明](../aug-mechanical-ascension/README.md)、[设计与待办](../../docs/design/AUG_MECHANICAL_ASCENSION.md) |

玻璃回收的四条配方现由材料精炼机加工，沿用原配方 ID 和自定义用量／产量。机核协议已移除原版熔炉的专用加工补丁与配方扩展；S26 批量烧制模组停用并停止供货，已有实物保留。原有连续加工及连续加速、节能、校准模组保留，三台加工机仍按性能增加每晚加工批数。

按 **G** 查询配方；Nico 工坊保留物品双击、**N** 和电话 **1016** 三个入口。合成、矩阵和机械飞升之间的配方与效果已在同包内登记，不需要再安装三个旧包。K05 仍由 Forge 提供。

食品加工是可选联动：安装后提供 K04 修补胶配方、压缩营养液和相关灰线商品。未安装时隐藏这些配方和商品；常规医疗修补、工坊和主线仍可使用。自动化物流可接入机器的真实投入、产出与电源。

## 身份与配置

- 包 ID 为 `nicokobo.mechcore_protocol`，仅声明 `MechcoreProtocol.dll`，唯一 `IMod` 入口为 `MechcoreProtocol.MechcoreMod`。能力声明为空；结局信件使用清理后的本地系统用户名。
- 原物品、配方、效果、开局与存档键保持原值，包括 `nicokobo.mechcore.synthesis`、`nicokobo.mechcore_protocol.module_matrix` 和 `nicokobo.aug` 前缀。它们是内容身份，不再是需要安装的三个包。
- 配方文件、机械飞升 `config.json` 和结局信件继续使用原官方 Mod 数据目录。矩阵五个开关和剧情开关集中到机核协议设置；首次定义采用原组件的已保存值，新包自身的已保存设置优先。
- 新实物使用合包图集；已有正式版实物保存的三个旧图集 owner 在加载图片时映射到新包。保留 ID 和读取路径不代表已完成合包后的原生存读档验收，也不增加 Demo 存档转换。

旧三包与新包不能同时启用，否则会重复注册同一批内容。合包检测到仍启用的旧包时会停止初始化并在日志说明；后续安装应先由玩家停用旧三包。本次源码修改没有操作游戏中的旧包、启用状态或存档。

## 工程与资源

三个组件目录保存源码和原始资源。开发编译统一使用 [`MechcoreProtocol.csproj`](MechcoreProtocol.csproj)，打包选择 `mechcore-protocol`。

```powershell
dotnet build .\mods\mechcore-protocol\MechcoreProtocol.csproj -c Release
```

编译前自动执行 [Sync-MechcoreResources.ps1](../../tools/build/Sync-MechcoreResources.ps1)，合并三个组件的纹理。同路径的纹理内容有冲突时会报错，内容一致的现有纹理会保留。游戏内文案从 [Localization.csv](../Localization.csv) 的 mechcore-protocol 行生成。修改组件中的原图或翻译表后重新编译。

需要打包时使用仓库 [Build-Mods.ps1](../../Build-Mods.ps1) 的 `-Mod mechcore-protocol`；打包和安装不由普通编译自动触发。完整首发说明见 [change.log](change.log)。

## 验证

离线检查覆盖[合成](../../tools/synthesis-official-checks/)、[矩阵](../../tools/matrix-official-checks/)和[机械飞升](../../tools/aug-official-checks/)。游戏内加载、操作与跨进程读档按[原生回归要求](../../docs/testing/NATIVE_REGRESSION_RULES.md)验证。

## 社区维护 / Community maintenance

本 Mod 后续由社区维护。遇到问题或有建议，请提交 Issue。修复、翻译和文档改动可先 Fork 仓库，再向主分支提交 Pull Request。[开源仓库](https://github.com/nql1314/nicokobo-probably-stolen-mods) · [新建 Issue](https://github.com/nql1314/nicokobo-probably-stolen-mods/issues/new) · [提交指南](https://github.com/nql1314/nicokobo-probably-stolen-mods#社区维护)

This mod is moving to community maintenance. Open an Issue for bugs or suggestions. To contribute fixes, translations, or docs, fork the repository and open a pull request against the main branch. [Source code](https://github.com/nql1314/nicokobo-probably-stolen-mods) · [New Issue](https://github.com/nql1314/nicokobo-probably-stolen-mods/issues/new) · [Contribution guide](https://github.com/nql1314/nicokobo-probably-stolen-mods#社区维护)
