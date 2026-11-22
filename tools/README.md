# 工具索引

从仓库根目录执行下表的命令。外部依赖、游戏路径和发布凭据见[开发说明](../docs/DEVELOPMENT.md)与[配置模板](../config/README.md)。

| 目录或文件 | 用途 |
| --- | --- |
| [build/](build/) | 正式版打包、本地化生成、机核协议资源汇总。根目录的 `Build-Mods.ps1` 继续提供打包和安装入口。 |
| [publish/](publish/) | 发布自制 Mod 到 Steam 与 Nexus。根目录的 `Publish-Steam.ps1`、`Publish-Steam.cmd` 以及已有的 `tools/Publish-Nexus.mjs` 继续可用。 |
| [assets/](assets/) | 导出开局图标、食品图标、扒窃肖像和物品管理器封面。 |
| [official-native-test/](official-native-test/) | 正式版隔离原生测试工具，执行规则见[原生回归要求](../docs/testing/NATIVE_REGRESSION_RULES.md)。 |
| `*-checks/`、[shared/](shared/) | 离线验证项目及共用夹具。 |
