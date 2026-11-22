# Forge 原生边界离线回归

本工程把 `NativeWorkshop`、`AchievementRules`、`RunDataGate`、`NativeLiquidApi`、`NativeLiquidValueRuntime`、`LiquidValueLedger` 及相关契约和液体数学实现直接链接到检查程序。`Ports.cs` 仅替代原生/Unity边界，不复制生产绑定、JSON、CAS、收据恢复或估价逻辑。

```powershell
dotnet run --project tools/forge-official-checks/NativeDomain/Forge.NativeDomain.Checks.csproj -c Release
dotnet run --project tools/forge-official-checks/NativeDomain/Forge.NativeDomain.Checks.csproj -c Release --no-build -- --measure
```

普通模式检查 Normal 同步加载的两槽迁移、已领取幂等、Pending 原收据确认、缺物品不补发、错误 run/schema/结构、普通 Open/Update/InitialSave 与延迟 Normal 无迁移权限，以及活动 store/run/modData/CAS 变化。液体覆盖全部组分、容量、原生 tag 直接修改、旧账本、品质基准、消费、稀释、倒液、倒空和新对象重载。

`--measure` 固定一个有三类液体组分、其中两类带价值的容器，预热 30 次后测五组各 1000 次生产 `RefreshValue` 调用。分配使用 `GC.GetAllocatedBytesForCurrentThread`，时间使用 `Stopwatch`。JSON调用计数通过仅测计数时启用的 Newtonsoft 数组转换器观察，耗时及分配样本不启用转换器。普通无账本水容器另外做 1000 次对照。

这是 .NET 10 上的生产源码检查，库存、tag 和事件来自测试端口。它不证明 Harmony 在真实游戏中安装、原生事件顺序、ES3存盘、进程重启、官方 DuplicateSlot 文件行为或 Unity 的实际性能。完整原生验收由正式测试流程独立执行。
