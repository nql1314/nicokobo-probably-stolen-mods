# nicokobo-probably-stolen-mods

本仓库维护深空当铺正式版的 NicoKobo Mod，包括前置框架、玩法扩展和工具。各 Mod 的 README 说明功能、依赖和验证范围，版本以各自的 `manifest.xml` 和 `change.log` 为准。

## 正式版 Mod

| 工具与前置 | 内容与联动 |
| --- | --- |
| [NicoKobo Forge](mods/nicokobo-forge/README.md) | [食品加工](mods/food-processing/README.md) |
| [物品管理器](mods/nico-item-manager/README.md) | [机核协议：伪人飞升](mods/mechcore-protocol/README.md) |
| [加速](mods/speed-up/README.md) | [自动化物流](mods/automated-logistics/README.md) |
| [商品刷新](mods/stock-reroll/README.md) · [商品刷新与更好的商人兼容补丁](mods/stock-reroll-better-merchants/README.md) | [肯定是偷的](mods/definitely-stolen/README.md) |

## 编译

构建需要 Windows、PowerShell 7.4 或更新的 7.x 版本、.NET SDK，以及 Steam 安装的 Probably Stolen 正式版。仓库使用 .NET 10 SDK 验证。项目引用游戏安装目录下的 `Probably Stolen_Data/Managed/`。离线检查的运行时要求见[工具索引](tools/README.md#离线检查)。

从仓库根目录运行，路径换成自己的游戏安装目录。

```powershell
$env:PROBABLY_STOLEN_GAME_DIR = 'C:\SteamLibrary\steamapps\common\Probably Stolen'
dotnet build ./mods/speed-up/SpeedUp.csproj -c Release
```

也可用 `-p:GameDir=<游戏目录>` 指定单次编译路径。打包用 `./Build-Mods.ps1 -Mod speed-up -NoInstall`，生成文件位于 `dist/`。省略 `-NoInstall` 会安装到游戏目录，先保存并退出游戏。Forge 要用 `-Mod nicokobo-forge` 显式选择。Steam 工坊与 Nexus 的发布流程见[文档索引](docs/README.md)。

## 仓库结构

| 目录 | 内容 |
| --- | --- |
| [mods/](mods/) | 正式版包、机核协议组件、共享代码和翻译表 |
| [tools/](tools/README.md) | 构建、发布、Mod 素材生成和离线检查 |
| [docs/](docs/README.md) | Mod 开发、玩法设计和验证说明 |
| [assets/](assets/) | Mod 封面和开局图标的源素材 |
| [skills/](skills/probably-stolen-official-mods/SKILL.md) | 正式版 Mod 开发与研究技能 |
| [config/](config/README.md) | 配置模板。真实配置和账号状态存放在本地 `.local/` |

## 许可证

本仓库的自有源码、工具和文档采用 [MIT License](LICENSE)。第三方素材及相关授权说明见[开源与素材授权](docs/OPEN_SOURCE.md)。
