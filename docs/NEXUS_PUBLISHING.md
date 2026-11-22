# Nexus Mods 同步

已恢复自制正式版 Mod 的 Nexus 发布流程。目标映射保存在 `tools/publish/nexus-mods.json`；不处理 `mod-manager`，不恢复第三方 Nexus 抓取、QQ 或网站转载流程。

```powershell
node tools/publish/Publish-Nexus.mjs pages all
node --use-env-proxy tools/publish/Publish-Nexus.mjs plan all
node --use-env-proxy tools/publish/Publish-Nexus.mjs upload all
node --use-env-proxy tools/publish/Publish-Nexus.mjs verify all
# 也可把 all 换成一个或多个 mods 目录名
```

`plan` 只读线上资料并保存本地计划；`upload` 才写入 Nexus。已有包必须先核对清单、DLL 版本和 ZIP 内容。需要重新打包时使用 `Build-Mods.ps1 -NoInstall`，Forge 通过 `-Mod nicokobo-forge` 单独选择。不安装或启动游戏。

`pages` 仅准备 `_build/nexus-official-sync/ui-copy.json`：从当前 manifest 注入版本，读取已核对的中英文发布文案，并转换 Nexus 不支持的 Steam 标题、编号列表及表格标记。正文与图片仍在已登录的 Nexus 编辑器保存。当前交流群为 3 群 `192285479`；Nexus 物品管理器名称为 `Nico's Item Manager`。正式版文件组按稳定 ID 定位，不能因显示名称修改而误建重复组。

正文核对以当前 README、change.log 和实现为准，工坊文案本身可能留有旧版本及旧机制。图片采用 `.local/steam-workshop` 发布配置对应的正式版封面源文件。设为新封面后检查可见图库；旧图可隐藏时使用可恢复的隐藏。网站若提示旧页头删除不可撤销，则在其他准备完成后单独取得该步骤的确认。

需要同步工坊详情图时，从对应正式版 Steam 页读取图库及正文图片，下载原图并保留来源对应。逐张检查内容，排除重复封面、过时的 Demo 安装要求和已退役机制；正式版条目中的附图也可能过时。本次下载与筛选记录位于 `_build/nexus-official-sync/steam-media/index.json`。选中的图片上传到对应 Nexus 图库，添加功能说明，再读回核对。

Nexus 源码编辑器粘贴长正文后，先读回可见 textarea，确认完整文本与新群号，再切回可视化并保存；直接 fill 后快速切换模式可能恢复旧正文。保存后读回远端正文与图片，不能只依据表单中的值报告完成。

正式版以 `Full Release` 文件组与 Demo 文件分开，版本取正式版 manifest，不因 Demo 的版本号较大就提升正式版版本。首次文件组沿用官方 `Nexus-Mods/upload-action` 的 multipart 流程及 v3 `POST /mod-files`；已有正式版组使用本机 Nexus 上传 Action 的 `dist/index.js`。用 `NEXUS_UPLOAD_ACTION_DIR` 指定检出目录，默认使用 `D:/software/nexus-upload-action`。

所有旧文件保留原分类，不归档、不删除。现有正式版组追加版本时明确设置 `INPUT_ARCHIVE_EXISTING_VERSION=false`。新版成为默认手动下载；原生包不启用 Vortex 安装。Nexus 页面介绍需同步为当前原生加载器说明，并保留旧版适用范围。

沿用 `%USERPROFILE%/.probably-stolen-wiki/nexus-mod-local/config.dpapi` 中的 Nexus 凭据；仅当前 Windows 用户解密并在内存使用。不恢复已退役的网站配置模块，不打印密钥或上传签名地址。

状态保存于 `_build/nexus-official-sync/`。上传中断后先按状态中的 upload/file/version ID 查线上结果；已上传的文件只补缺少的日志和读回，不重复发布。`verify` 检查主文件、字节数、页面版本、完整双语日志，以及计划中所有旧文件的名称、版本和分类均保留。

首次建页使用已登录的 Nexus 网站，完成草稿、首文件及公开，确认账号和页面后将真实 ID 写入映射，再生成计划。不能猜测新页 ID，也不能把 v1 文件 ID 当作 v3 文件组 ID。

页面版本和历史列表使用 v3 接口，字节数读取 v1 单文件详情，避免旧版 Mod 目录在新页发布后仍返回旧状态。网页介绍使用 Nexus 支持的 BBCode；Steam 的 h1/h2 标记需转换为 size/b。
