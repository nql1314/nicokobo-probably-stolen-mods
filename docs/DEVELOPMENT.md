# 开发与游戏资料维护

## 正式版游戏内翻译

在 [正式版翻译表](../mods/Localization.csv) 中维护游戏内自有文案。每行由包目录、稳定的文案键和各语言译文组成。新增语言时直接使用语言代码作为列名，未完成的词条可以留空。使用 `{0}`、`{1}` 等占位符插入变量，用 `\n` 表示换行。原版物品名、人物名和机制术语通过游戏本地化 API 读取，再作为参数插入。

编译会从同一张表生成程序集使用的词条和游戏加载器读取的 `Localization/*.csv`。机核协议的合成扩展、模组矩阵和机械飞升共用 `mechcore-protocol` 包名。

游戏选中社区语言后，在目标 Mod 的 `Localization/` 中放入对应语言代码的 CSV 即可补充译文。每个词条先查完整代码，再查兼容代码或基础语言，最后查英语。同一语言下，已加载的 CSV 优先于程序集内置文案。`ko` 和 `ko-KR` 可以互相补齐缺失词条。启用兼容的繁中补丁时使用 `zh-TW`。

`Init` 和 `OnEnable` 只登记键和文字回调，显示时才取文案。原生设置的分组接收普通字符串，因此用翻译键分组，打开设置时再渲染标题。已有窗口需要原生物品名时，等新语言的资源就绪后再刷新。

自有 Mod 的界面名称写在 `nicokobo-forge` 的 `mod.name.<Mod ID>` 行。Forge 直接使用编译进程序集的译名，让未启用的 Mod 也能在列表中显示当前语言。清单中的名称用于 Forge 未启用时的显示。

## Mod 名称与描述规范

清单和 README 标题使用 `中文名 / English Name`，正文先中文后英文。Steam 工坊为不同语言使用独立详情。

名称的两种语言用一个空格、斜杠、一个空格（` / `）分隔。描述按语言分段，使用真实换行；同一种语言有多段时先放完该语言内容，再放另一种语言，避免逐句交错。

工坊英文标题与正文保存在 `workshop-description.txt`，其他已支持语言保存在 `workshop-description.<语言代码>.txt`。各文件以对应语言的 `[h1]标题[/h1]` 开始。英文用作默认详情，简体和繁体中文保留交流群，其他语言不附群号。游戏清单和 README 保留上述双语格式。

描述应简要说明实际功能、适用条件和必要前置，并与当前实现一致。

一般 Mod 的清单示例：

```xml
<Name>加速 / Speed Up</Name>
<Description>调整对话、顾客和卷帘门速度，支持局内全局倍速。
Adjust dialogue, customer and shutter speeds, with an optional global speed multiplier during play.</Description>
```

修改文案时同步核对以下来源：

- `manifest.xml` 的 `Name` 和 `Description`：游戏内显示名称与短描述；修改显示文案时保留 `ID`、程序集名称和其他身份字段。
- `workshop-description.txt` 及各语言文件：标题和介绍只使用对应语言。长描述可以展开功能与操作，移除已废弃的功能说明和过时的版本信息。
- README 主标题：与清单名称保持一致；README 中已有的双语介绍采用同一语言顺序。

工坊标题、长描述的上传来源及已有语言版本的核对方式见 [Steam 发布说明](STEAM_PUBLISHING.md#标题与描述规范)。

## 正式版构建、打包与安装

普通源码修改先使用 `dotnet build <项目.csproj> -c Release` 单独编译相关正式版项目，默认不打包或安装。

合成扩展、模组矩阵和机械飞升统一编译 [`MechcoreProtocol.csproj`](../mods/mechcore-protocol/MechcoreProtocol.csproj)。

构建包契约检查和 Forge 离线扫描器时，用 `-p:GameCodeDir=<快照>/code/Assembly-CSharp` 指定目标 Build 的源码目录。游戏不在默认目录时，同时用 `-p:GameManaged=<同一 Build 的 Managed 目录>` 指定程序集路径。解包步骤见采集工作区的 `docs/UNPACKING.md`。

明确要求打包时，使用仓库根目录的统一入口：

```powershell
.\Build-Mods.ps1
.\Build-Mods.ps1 -NoInstall
.\Build-Mods.ps1 -Mod food-processing -NoInstall
.\Build-Mods.ps1 -Mod nicokobo-forge -NoInstall
```

脚本从 `mods/` 读取项目和 manifest，使用正式版 Managed 程序集完成 Release 编译，核对 Mod ID、程序集名称和版本，仅打包 manifest、清单声明的 DLL、封面和所需资源。省略 `-Mod` 时处理除 `nicokobo-forge` 外的正式版项目；Forge 独立分发，必须显式选择，内容包构建不会自动加入前置。

README 与 `change.log` 留在源码目录，不进入发布包或游戏安装目录，资源子目录中的同名文档也会排除，更新时清理目标中的旧文档。生成目录为 `dist/<mod>/`，ZIP 名称含版本号；默认安装到正式版游戏 `Mods/<mod>/`，`-NoInstall` 只生成本地包。安装需游戏退出，文件复制后进行字节核对；相同 ID 出现在其他目录或目标目录归属不符时停止，保留其他 Mod、配置和存档。

Demo 源码、构建配置和专用检查可在 Git 提交 `ece7bcb5` 查阅。复查旧版时使用该提交中的工程及配套版本的 Demo Forge。[Demo 源码](https://github.com/nql1314/probably-stolen/tree/ece7bcb525123b364053d74dd2ed62cce51f7d46/mods-melonloader)

正式版发布使用 [Steam 创意工坊入口](STEAM_PUBLISHING.md)：`Publish-Steam.ps1` 编译打包指定 Mod，上传本次构建目录并保存工坊条目 ID。普通开发编译与本地打包不触发上传。

## 针对性检查

按所属项目运行与改动相关的检查。打包入口的验证包括 manifest、实际程序集版本、ZIP 内容及安装文件一致性；这些结果不代表游戏内扫描、授权、加载或交互已经通过。

Python 图鉴回归、图标校验和游戏验证按本次改动所需范围执行。游戏数据与带日期的历史验证记录保留，不将旧版的检查结果扩大为正式版已通过。

## 核对游戏版本与契约

正式版项目使用 Mono 与内置 ModLoader，从 Steam `appmanifest_4348910.acf` 读取当前 `buildid`，接口以实际 `Managed/Assembly-CSharp.dll` 为准。

旧 Demo 使用 App `4349200` 与 IL2CPP，各旧工程目标和验证版本以对应 README 为准；Build `25382790` 仅是部分历史探针的基线。Steam Build、游戏内部版本和 MelonLoader 日志中的 `Game Version` 分别记录，更新后重新核对目标签名。

本地检查、Release 包、安装文件一致性、加载日志、实际操作和保存重载分别记录。现行验收范围见正式版各 Mod README 与迁移记录；旧探针和清单只支持当时注明的版本，历史证据见Git 历史。

## 游戏资料依赖

Mod 编译使用已安装游戏中的 Managed 程序集。依赖游戏代码的离线检查用 `-p:GameCodeDir=<目标 Build 的导出代码目录>` 指定快照。

物品管理器封面生成器通过 `-AssetRoot` 读取对应版本的 `game-assets/`。
