# 物流正式版定向离线检查

运行实际生产源码中的净水收取目标、严格液体规格、schema6、逐组分液体计价、反向补偿、终端输入捕获和生产调用调度，并可对构建 DLL 与当前游戏程序集做元数据契约检查。调度检查执行真实 Harmony 补丁、原生机器观察者和电力收据，游戏循环与库存边界使用窄替身；不启动 Unity、不安装、不触碰存档。

回归保护原委托对象／Target、实际调用机会与顺序、第二阶段进料斗边界、原版 Early／Late 处理、加速器／调试及手动加工、取消通知、电力补偿、跨周目／槽／日期／容器身份及异常后的清理。移除包装前新增的回调身份断言已在旧实现上失败。

游戏自带 Harmony 面向 Unity Mono；离线 CoreCLR 检查只在自身进程中选择其 Cecil 生成器，并启用可信的进程内补丁元数据序列化，结束后恢复应用设置。这些设置不会写入游戏或 Mod 配置。

从仓库根目录运行：

```powershell
dotnet run --project .\tools\logistics-official-checks\Logistics.Official.Checks.csproj -c Release -- .\mods\automated-logistics .\mods\automated-logistics\bin\Release\netstandard2.1\Nicokobo.AutomatedLogistics.dll
```

第三个参数可指定另一份正式版Managed目录。暂存或独立输出构建可用`-p:LogisticsSource=<源码目录>`指定生产源码。

第四个参数可指定构建后的 `Nicokobo.Forge.dll`：检查会使用这个 DLL 中的真实调用点选择器与 Transpiler 读取第三个参数下的游戏方法，核对所有原版生产调用都被覆盖、Early／Late 调用未被替换，并检查工厂包装和委托写入已移除。它不会执行游戏的 `EndNight` 或机器工厂。
