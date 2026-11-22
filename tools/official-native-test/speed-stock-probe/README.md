# 速度与刷新原生探针

按[原生快速回归要求](../../../docs/testing/NATIVE_REGRESSION_RULES.md)执行，使用生成的客户和商品夹具，在测试窗口中发送真实键盘输入。窗口优先放在副屏，副屏不可用时自动使用主屏。

从 2026-10-29 的正式版 Mono 探针收回维护；旧源码、二进制、32 项历史结果和清理记录继续保留在原轮次目录。

使用上一级 [统一流程](../README.md)。`Build-Probe.ps1` 只生成本目录忽略的 `bin/obj/package`，不部署游戏。当前扫描声明为 `files`、`reflection-external`；PID 来自外部监督器的本轮 launch.json，Mod 内不调用 Process API。反射用于已加载的真实 Item Manager 面板和原生按键选项窗口的只读对象定位，不修改加载器或信任判断。

只在明确 `--nicokobo-native-speedstock-test` 参数、GUID 运行 ID、`speed-stock` 模式、新结果路径、匹配的 `NicoT_*` 产品和实际保存路径下启用；普通启动惰性。实际运行先写隔离 PlayerPrefs marker，监督器独立核对。

测试执行真实对话、Tween、时间倍率、顾客/夜店商品刷新及回滚；快捷键等待外部真实输入，另覆盖真实 Unity InputField 聚焦后的 F5、Ctrl+PageUp 屏蔽。按请求修改本轮独立配置、创建临时场景和物品，并阻断 SaveGame/LoadGame/InitialSave。完整 ES3、完整 EndNight、所有顾客、实际气泡鼠标手势仍单列未验证。

Speed Up 1.0.5 回归另在真实主菜单发送 Ctrl+PageUp，核对倍率/启用设置、时间倍率及没有新建周目；局内使用同一按键再验证正常加速。主菜单提示布局与设置翻译显示仍需界面观察。

另在实际 Item Manager 搜索框聚焦时发送 F5/Ctrl+PageUp，验证商品与倍率设置保持原值。该路径使用真实 TMP_InputField，补足旧 Unity InputField 夹具没有覆盖的实际跨 Mod 输入；其他搜索/终端界面仍需单独验收。

改键回归从真实 `GameOptionUIManager` 窗口的旋转键按钮开始捕获，分别等待外部实体 F5、Ctrl+PageUp。原生 `PollKeyCapture` 消费输入并结束捕获；晚序 Update 观察活动捕获和完成当帧的 `SuppressHotkeys`，核对商品 UID 和加速配置保持不变。读取实际待应用键值后直接关闭窗口，不点应用，核原绑定未变。结果保留 `bindingCaptureFrames`；只有本轮实际键盘结果及监督器通过才能计为通过，源码和构建不替代执行。

结果使用 `CreateNew`，状态文件为结果路径后附 `.state.json`；所有检查含预期与观测。超时退出只影响由统一监督器持有的测试进程。这里没有旧 `Cleanup.ps1` 的正式存档恢复功能。

原生延迟计时须跨过初次进场帧，再测真实 `ScheduleFlow`；记录开始／结束的游戏时钟、墙钟、帧号和timeScale，不修改回调或放宽时限。Build26337366曾在启动帧出现游戏时钟前进0.37秒、墙钟仅0.07秒的失败，原记录保留；跨帧后完整23次实体输入／48检查通过，见Git 历史。
