# 肯定是偷的 / Definitely Stolen

## 当前版本与发布说明

当前版本 **1.0.4**，沿用原有 Steam 创意工坊条目。开局、行动面板和夜间提示可读取社区语言的翻译。发布包基于正式版 **0.46D / Steam Build 26525552**。[Steam 创意工坊条目](https://steamcommunity.com/sharedfiles/filedetails/?id=3831046095)

- 成功、失手未暴露、暴露增加的风声分别为 2、1、10 点。
- 暴露时安保声望固定扣 1 点，目标所属阵营按地点另扣；随身携带的当晚赃物按价值计入原版罪案记录。
- 原版烟雾弹仍可自动脱身并保留赃物，暴露时的代价照常结算。

完整变更见 [change.log](change.log)。

See the bilingual release notes for **1.0.4**. [change.log](change.log)

原版烟雾弹（`smoke_grenade`）保留游戏原有用途，并加入扒窃自动脱身效果。中英文物品提示保留“浓烟呛得人睁不开眼，再稳的枪口也容易偏了准头。”，同时说明随身携带时暴露会自动消耗一枚、保留赃物并结束当晚偷窃。

2026-11-02 源码调整：脱身烟幕弹退出供货和掉落候选池，保留物品注册、资源及旧存档中的使用效果。到店小偷每次供货独立按 5% 概率追加一枚原版烟雾弹，实际摆放遵循原生柜台容量规则；原版烟雾弹也可自动触发扒窃脱身。

正式版依赖独立 [NicoKobo Forge 前置](../nicokobo-forge/README.md)（`nicokobo.forge`，协议 1），内容包不附带公共 DLL。

1.0.3 发布包通过 Release 编译、Build 26525552 的官方扫描规则及 8692 项离线检查。离线检查不代表界面、存档重载或玩法已实机通过。

目标为 Unity Mono、官方内置 ModLoader。此前 **Build 26284217 / 0.46D** 的六包普通授权加载、新职业／候选／夜间事件编译及 8,657 项离线检查属于历史基线。原始范围见Git 历史和[职业与夜间事件设计](../../docs/design/DEFINITELY_STOLEN.md)。

开局与两个起始特性图标已按[原版菜单图标规范](../../docs/STARTING_ICON_STYLE_GUIDE.md)重做：开局19×19深色圆徽记，特性36×36白色透明遮罩，由原版染色；源图位于 `assets/starting-icons/`，统一运行 `tools/assets/export-starting-icons.py` 导出。此项已完成相关项目编译和静态图标对照，尚未游戏内确认。

2026-10-29 界面调整：三个地点卡片分别使用原有夜市、换乘区、商业区背景。进入地点后通过官方 `StageObject`／`PanelObject` 展示全屏背景、可点击人物和右侧操作栏，原生地面、口袋及背包窗口保持原位；移除旧大号浮动窗口及逐帧位置修补。摸包客开局改用独立 `pickpocket` 图标，顺手牵羊与惯偷天赋图标保持各自身份。已重新打包安装；尚未启动游戏确认实际显示。

本包延续旧版玩法和稳定身份，schema 5 兼容已有周目状态：

- 官方「摸包客」开局：保留原有开局资源，额外黑灯；随机赃物与信用点差额合计 500，下层声望 −15。摸包客自带顺手牵羊，熟练度 2 级；惯偷为 5 级。
- 官方「顺手牵羊」「惯偷」天赋。其他开局通过顺手牵羊取得资格；惯偷需已有资格，失手暴露率减半。已有无资格周目不补发资格或开局物品。
- 官方下层夜市、交通换乘区、上层商业区三个夜间地点。每晚各三个不同职业、每人五件不重复物品，每人最多成功偷取三次。
- 31 种常规职业和八位特殊人物，共 101 张透明立绘、9 张背景及对应鞋底／地面站位。目录的 33 个可用角色定义包含索耶、伦警官两个专用职业；物品池共 147 个候选 ID、395 条职业物品关系。
- 新职业覆盖养殖、水培、打印、水质、育种和医疗配送。生成随身五件候选时，普通职业核心物品权重为 2、生活用品为 1；成功偷取时普通品仍等概率，稀有规则沿用原值。新增职业和物品仅用于夜间，不改变摸包客开局候选范围。
- 商贩补货夜、水培丰收夜、技工换班夜、货运到站夜、技术展销夜和治安临检夜，由原生每日事件处理抽取，持续一晚，可同区叠加。事件、职业权重和暴露修正与三区九人一起固定，预览和读档不重抽。
- 六种原生活鱼、水质维护用品和特殊人物专属稀有候选。轻量候选固定 UID、价值、稀有标记、正权重、电量、液体组分、真伪和鱼性别；成功后才生成实物。已保存候选不随新平衡重抽。
- 手动滑块、第一次免费练习、自动滑动判定、6 秒超时暴露、目标独立的动态成功区。熟练度、警觉、重复尝试、声望处罚、风声和歇夜衰减均保留。
- 小偷到店时，仅对已有资格周目判定当天情报；已生成目标/纸条身份不因重载重抽，库存满时保留待领取收据。
- 原版烟雾弹：到店小偷每次供货有 5% 概率追加一枚，保留原版用途，并支持扒窃自动脱身。
- 旧脱身烟幕弹：1×2、不可堆叠、价值 120，不再加入供货和掉落候选池，保留物品及已有实例。
- 两种烟雾弹均须放在随身口袋、背包或外出准备网格中；暴露时自动消耗一枚、保留全部当晚赃物、停止当晚偷窃和练习，声望、经验和风声处罚仍执行。成功或未暴露的失败不消耗；店内存货无效。
- 原生携带网格、容器内容、精确放置、保管限制、丢弃、正常返店和失败回滚。只在第一次实际偷窃预扣一次外出次数；返店不重复扣，免费练习与未行动巡视不扣。
- Alt+D 官方 CustomUI 调试面板：等级、风声、行动、概率覆盖、NPC 职业/站位替换、候选搜索、物品生成、电量与水质档位、真实候选状态。

## 使用

新建周目选择「摸包客」，或为其他开局选择「顺手牵羊」。晚上打开「离开店铺」，选择任一扒窃地区，点击人物后开始练习或偷取。手动按空格或点击停手，自动模式会滑动并完成一次判定。

旧存档或未选择「顺手牵羊」的人物，可在主菜单「模组」→「肯定是偷的」齿轮配置中开启「无需特性开启偷窃」（`AllowTheftWithoutPerk`，默认关闭），应用后载入存档即可使用三个扒窃地点。普通人物从 0 级熟练度开始，不补发摸包客开局物品、现金或声望变化；已有偷窃进度与当晚目标保留。关闭开关后，配置解锁的人物暂时无法偷窃，进度保留；原有摸包客及特性资格照常生效。开启时，新建其他人物也可直接选择「惯偷」，无需额外选择「顺手牵羊」。

Existing saves and other starts can enable **Allow theft without Streetwise** (`AllowTheftWithoutPerk`, off by default) in this mod's settings, then apply and load the save. Ordinary characters start at proficiency 0 without Pickpocket opening rewards. Existing progress and targets are preserved. Disabling removes only configuration-based access and keeps progress; Pickpocket and Streetwise access still work. New characters can choose Habitual thief directly while this setting is enabled.

实物仍在游戏原生外出库存中操作。把要带回的物品放进背包或口袋；松散地面物品会留在外面。原生返店按钮会提示地面遗留物。进行中的偷窃必须先结算才能返店；烟幕脱身后仍能整理物品。

官方 Mod 配置保留 `ManualDifficultyCoefficient`（0–2）及 `RandomizeSuccessZoneAfterSuccess`（默认开启）。系数只影响下次手动或练习，不影响进行中的区间与自动判定。

## 稳定身份与正常存档

| 用途 | ID |
| --- | --- |
| manifest / owner | `nicokobo.definitely_stolen` |
| 起始方案 | `nicokobo.definitely_stolen.pickpocket` |
| 天赋 | `nicokobo.definitely_stolen.streetwise`、`nicokobo.definitely_stolen.habitual` |
| 夜间地点 | `nicokobo.definitely_stolen.lower_night_market`、`nicokobo.definitely_stolen.transit_hub`、`nicokobo.definitely_stolen.upper_commercial` |
| 烟幕弹 | `nicokobo.definitely_stolen.escape_smoke` |
| 周目状态 | 官方 `nicokobo.definitely_stolen:state`；可校验旧键 `nicokobo.definitely_stolen.state` |

开局奖励在游戏 `InitialSave` 前、原生及其他开局物品完成后准备。偷窃、返店和情报均不主动保存；状态只在游戏 `SaveGame` 前暂存进官方 ModData，随后由原生保存统一写盘。

schema 1/2/3/4 按原规则迁至 schema 5。已生成的旧夜晚不补事件、不刷新目标；进行中的判定保留冻结区宽，情报与随机状态保持原样。新夜晚保存三区事件 ID、规则版本与有效修正。

支持的固定候选沿用轻量规则；随机药液、种子、饲料、DNA 磁盘、有状态作物／滤芯及金属锭保留完整原生图和状态版本。旧轻量药剂缺失组分时排除抽取并标明原因；旧轻量作物／金属模板保留原有支持语义，不补造历史状态。营养果种子候选恢复时保留已记录基础价；交付后的普通原生加载仍执行当前周目的品系定价。

实物和携带图保留原生 SaveItemNode、容器节点与 `forgeItemFeatures` 字段，使用游戏 ES3 的内存序列化保留私有特性，不引入运行期文件读写。开局与已送达纸条只保留收据身份，不重复保存完整图。

原生复制槽保留 runID、改目标槽：仅当 payload 的源 runID 与游戏真实 runID 一致、目标槽与 `NewGameData.saveSlot` 一致时重绑 SlotId，保留奖励、RNG、NPC 和物品 UID。历史日期读取沿用该历史记录本身。不同 runID、未知 schema 或互相冲突的旧/新键会报错，不当作空新局。此机制不自动搬运或修改玩家 Demo 存档。

## 构建与验证

从仓库根目录执行。游戏安装在其他目录时，先按[构建说明](../../README.md#编译)设置 PROBABLY_STOLEN_GAME_DIR。

```powershell
dotnet build .\mods\definitely-stolen\DefinitelyStolen.csproj -c Release -p:NuGetAudit=false
dotnet run --project .\tools\theft-official-checks\Theft.Official.Checks.csproj -c Release -p:NuGetAudit=false -- .\mods\definitely-stolen .\mods\definitely-stolen\bin\Release\netstandard2.1\DefinitelyStolen.dll
```

包内只有一个 IMod 入口；加载器自动安装入口程序集的 20 个玩法补丁和共享设置本地化补丁。通过轻量客户端接入独立的 NicoKobo Forge 前置，包内不附带 `Nicokobo.ContentInterop.dll`。`Capabilities` 为空；外出恢复只定位明确的 `MapUIManager` 游戏方法，原版烟雾弹供货追加在小偷第二段对白的原生供货回调之后。

正式版缺少即时恢复保存外出的公开入口，因此 `Native.RestoreOutside` 精确调用 Build 26274380 的 `MapUIManager.OnGoingOutside(AfterhourLocation)`、`ShowStage`、`OpenLocationPanel` 并恢复 Current，避免重放新访问次数。此处随游戏更新需重新核对。普通进出地区始终走官方地点和返回流程。

离线检查覆盖领域规则、旧 schema、候选图保存、动态区间、情报／开局收据、开局隔离、职业核心权重、事件快照／风险、复制槽身份、烟幕弹、所有立绘与背景，以及 20 个补丁目标签名。编译和离线检查不证明玩家扫描激活、Unity UI、原生种子回调、实物携带或跨进程保存重载；地区收益也尚未进行游戏内平衡验收。本轮不直接操作游戏 Mods、配置、信任、存档或启用状态。

新增立绘使用内置 imagegen 生成；[提示词与来源](Assets/Portraits/new-occupations.json)及统一导出入口 `python tools/assets/export-theft-portraits.py` 保留在源码侧，导出生成 384×576 PNG 和对应脚底锚点。当前立绘预览（本地采集工作区 `output/theft-1.2.0/new-occupations-preview.jpg`）仅用于美术核对。
