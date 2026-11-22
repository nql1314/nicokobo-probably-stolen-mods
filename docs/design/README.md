# 内容 Mod 设计

| Mod 或组件 | 设计入口 |
| --- | --- |
| 肯定是偷的 | [职业、区域与夜间事件](DEFINITELY_STOLEN.md) |
| 自动化物流 | [补货、出料、仓储、水罐和电网](AUTOMATED_LOGISTICS.md) |
| 食品加工 | [机器、配方、营养与冷链](FOOD_PROCESSING.md) |
| 机核协议 · 合成扩展 | [材料、生产链与义体](SYNTHESIS_EXPANSION.md) |
| 机核协议 · 模组矩阵 | [模组、节点与成长](MODULE_MATRIX.md) |
| 机核协议 · 机械飞升 | [开局、生存、工坊与失物招领](AUG_MECHANICAL_ASCENSION.md)、[剧情文本](AUG_LOST_AND_FOUND_STORY_TEXT.md) |

合成扩展、模组矩阵和机械飞升由[机核协议](../../mods/mechcore-protocol/README.md)统一构建和发布。四个内容 Mod 通过 [NicoKobo Forge](../../mods/nicokobo-forge/README.md)使用公共服务。机器归属、交易、持久化和跨包接口见[互操作说明](../../mods/shared/Nicokobo.ContentInterop/README.md)。

设计包含已实现、待实施和待定内容。包版本以各项目的 manifest 为准。已有存档使用的物品、配方、效果、开局、owner 和数据键由源码定义。原生加载、操作和跨进程存读档按[回归要求](../testing/NATIVE_REGRESSION_RULES.md)核验。
