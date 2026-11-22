# 伪人：机械飞升（组件） / Aug: Mechanical Ascension

随机核协议构建和发布。项目与安装入口见[机核协议](../mechcore-protocol/README.md)。组件的内容和数据 owner 为 `nicokobo.aug`。

## 玩法与联动

机械飞升提供独立开局、健康与暴露管理、脑机接口和义体、Nico 工坊、灰线供货及“失物招领”剧情。按 **N** 或拨打 **1016** 打开工坊。玩法、配方和事件见[组件设计](../../docs/design/AUG_MECHANICAL_ASCENSION.md)与[剧情文本](../../docs/design/AUG_LOST_AND_FOUND_STORY_TEXT.md)。

食品加工提供 K04 修补胶配方、压缩营养液和相关灰线供货。未启用食品加工时，常规医疗修补、工坊及主线仍可使用。K05 制造终端由 Forge 提供。

## 配置与保存

从官方 Mod 数据目录读取 `config.json`，示例见 [config.example.json](config.example.json)。剧情开关位于 Mod 设置。开局电池、医疗和电子物资各固定一份，对应配置字段只接受 `1`。已有存档保留原配置快照。

周目、剧情、灰线及成就沿用各自的 `nicokobo.aug.*` 数据键。复制槽在原生载入后核验来源身份并重绑目标槽，随后由游戏正常保存。旧 Demo 存档需由对应 Demo 版本读取。
