# 机械飞升正式版离线检查

2026-10-31：二进制边界检查的 `aug` 引用改为合包 `MechcoreProtocol.csproj`。runtime 模式增加真实图集 owner 补丁及原生 `ModConfig` 缓存／反序列化检查，覆盖旧开关读取和新包保存值优先；仅在内存准备输入，不写玩家配置文件。

从当前正式版源码直接链接域、codec、来源证明及原子重绑逻辑。运行前核对本机 Managed 与目标游戏 Build；默认 `AugSource` 指向仓库正式版包。

```powershell
dotnet run --project tools/aug-official-checks -c Release
dotnet run --project tools/aug-official-checks -c Release -- runtime
dotnet run --project tools/aug-official-checks -c Release -- story
dotnet run --project tools/aug-official-checks -c Release -- json
dotnet run --project tools/aug-official-checks -c Release -- loaded-copy
dotnet run --project tools/aug-official-checks -c Release -- opening-config
dotnet run --project tools/aug-official-checks -c Release -- protected-items
```

这些检查不启动游戏，不修改信任或玩家存档。golden 中是跨 JSON 后端的固定测试载荷；复制槽覆盖真实源码的同槽零写、四键来源一致性、未完成事务拒绝和原子回滚。原生 Harmony 调用、实际资源扣费和 ES3 跨进程保存由独立隔离探针验收。

`runtime` 通过带别名的项目引用构建义体升格与自动化物流正式版 DLL，直接调用其中的恢复准入、批次基准读取、灰线目录判断、修补胶与义体配方注册。价值传输连接到直接链接的 Forge `ProductionValue` 真实实现，退出时恢复连接；配方注册只捕获传输端的实际声明。物品及目录使用真实游戏类型，只设置所需的托管字段，不构造 Unity 场景、不执行物品工厂。

拟态皮肤检查调用实际声明中的原料判定，接受同步香水 5/5，拒绝 4/5 至 0/5 及缺少次数状态的物品，并检查中英文满瓶提示。物流回归调用实际材料快照/匹配和根绑定方法，验证已使用香水不能抵扣满瓶、同一 v6 的旧空串香水欠额可由真实满瓶补回且保留数量和其他条件、新次数规格保持严格匹配、三类虚空设备失去原根后可接替并保存 UID，以及领取收据、容量和储电保持不变。这些检查不等于原生右键操作、Unity 对象释放或跨进程交易验收。

`loaded-copy` 直接调用生产 `PrepareLoaded` / `Apply`，输入是游戏已经加载的 ModData 字符串，不构造 ES3 carrier、不再次打开存档。带匹配来源地址的同槽未知 schema 留给普通 reader；跨槽四载体由各自真实 codec 验证，并覆盖冲突别名、孤立辅助载体、逐写 CAS 与条件回滚。没有来源地址时不能证明同槽，须拒绝。

`opening-config` 从实际正式版程序集读取 `OpeningRuntimeFlow.Specs`，并调用源码链接的 `OpeningSavePlanner`：三种开局物资数量只能为1，其他值在新配置入口拒绝；默认配置、InitialCredits=10000 和其他受支持参数能形成真实有效的开局清单。历史 Codec 字段与范围保持不变。

`protected-items` 直接调用正式版 DLL 中实际销售资格、单卖前缀和批卖过滤 helper，以真实 GameItem/PixelWindow/GameGridInventory 类型的托管图验证空包、普通货物、根/一层/两层证物和脑机接口。只准备托管字段，不构造Unity场景、安装Harmony或调用真实顾客交易；因此不代表原生商业成交或跨进程库存验收。
