# 成就探针浮点投影检查

离线读取实际失败result，使用游戏自带Newtonsoft相同字节，验证Single/Double的JSON投影、明确double提升的精确往返及真实样本上下1 ULP拒绝。不会加载游戏程序集或运行Unity，不修改任何原结果或存档。

```powershell
dotnet run --project tools/forge-official-checks/AchievementJsonProjection/Forge.AchievementJson.Checks.csproj -c Release -- <achievements-reload-result.json> <new-evidence.json> <game-Managed/Newtonsoft.Json.dll> [deviceflows-reload-result.json]
```

只验证浮点证据投影，不把旧失败或该离线检查当成新进程原生验收。修复需在新probe中重新保存及读回。
