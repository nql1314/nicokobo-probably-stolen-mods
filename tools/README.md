# 工具索引

从仓库根目录执行下表的命令。外部依赖、游戏路径和发布凭据见[开发说明](../docs/DEVELOPMENT.md)与[配置模板](../config/README.md)。

| 目录或文件 | 用途 |
| --- | --- |
| [build/](build/) | 正式版打包、本地化生成、机核协议资源汇总。根目录的 `Build-Mods.ps1` 继续提供打包和安装入口。 |
| [publish/](publish/) | 发布自制 Mod 到 Steam 与 Nexus。根目录的 `Publish-Steam.ps1`、`Publish-Steam.cmd` 以及已有的 `tools/Publish-Nexus.mjs` 继续可用。 |
| [assets/](assets/) | 导出开局图标、食品图标、扒窃肖像和物品管理器封面。 |
| [official-native-test/](official-native-test/) | 正式版隔离原生测试工具，执行规则见[原生回归要求](../docs/testing/NATIVE_REGRESSION_RULES.md)。 |
| `*-checks/`、[shared/](shared/) | 离线验证项目及共用夹具。 |

## 离线检查

用 .NET 10 SDK 构建。检查程序按项目目标运行在 .NET 6、8 或 10 上，执行前用 `dotnet --list-runtimes` 确认已安装所需版本。需要读取 Mod DLL 的检查先编译对应 Mod 的 Release 版本。

| 范围 | 入口 |
| --- | --- |
| 食品加工、物流、扒窃 | [食品加工 README](../mods/food-processing/README.md#开发与验证)、[物流检查](logistics-official-checks/README.md)、[肯定是偷的 README](../mods/definitely-stolen/README.md#构建与验证) |
| 机核协议 | [合成](../mods/mechcore-protocol-synthesis-expansion/README.md#验证)、[矩阵](../mods/mechcore-protocol-module-matrix/README.md#验证)、[机械飞升](aug-official-checks/README.md) |
| Forge | [跨程序集检查](forge-official-checks/)与[原生边界离线回归](forge-official-checks/NativeDomain/README.md) |
| 加速、商品刷新、物品管理器 | [加速](speed-up-checks/)、[商品刷新](stock-reroll-official-checks/)、[物品管理器](nico-item-manager-checks/) |
| 构建和发布脚本 | [包校验与更新夹具](content-package-checks/Verify.ps1)、[Steam 发布夹具](steam-workshop-checks/Verify.ps1) |

[包契约检查](content-package-checks/Program.cs)对 Forge 及四个内容包的已编译 DLL 使用游戏扫描器源码。通过 `-p:GameCodeDir` 指定同 Build 的解包代码目录，并向程序传入实际 Managed 目录及各包的 manifest、主 DLL。可用 `--single` 运行其中一包。检查只读取程序集和清单。实际的加载、交互和跨进程存读档见[原生回归要求](../docs/testing/NATIVE_REGRESSION_RULES.md)。
