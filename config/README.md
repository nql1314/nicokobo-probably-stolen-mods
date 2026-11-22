# 本地配置

以下位置由 Git 忽略。模板中的凭据字段留空，部署时填写本机配置。

| 用途 | 本地位置 |
| --- | --- |
| Steam 工坊发布 | 将 [账号模板](steam-workshop-account.example.json)复制为 `.local/steam-workshop/account.json`。只填登录名。 |
| Steam 工坊绑定 | `.local/steam-workshop/` 保存条目 ID、待确认状态和恢复记录。迁移时移动整个目录。旧 `.steam-workshop/` 会自动识别，同时存在两个状态目录时停止。 |
| SteamCMD | 解压至 `.local/steamcmd/`，或通过 `-SteamCmdPath` 指定已有安装。SteamCMD 的登录状态随安装目录保存。 |
| Nexus | 使用用户目录内的 Nexus DPAPI 配置。见 [Nexus 发布](../docs/NEXUS_PUBLISHING.md)。 |

编译无需发布账号。游戏路径可通过 `PROBABLY_STOLEN_GAME_DIR`、构建脚本的 `-GameDir` 或 MSBuild 的 `-p:GameDir` 设置。
