using UnityEngine;
namespace Nicokobo.ContentInterop;

internal static partial class ForgeServices
{
    internal static object? Dispatch(string operation, object[] args)
    {
        switch (operation)
        {
            case "batch.select": return ProductionBatch.SelectInputs((GameGridInventory)args[0], ((object[][])args[1]).Select(ForgeWire.Ingredient))?
                .Select(take => new object[] { take.Item, take.Count, take.Before }).ToArray();
            case "batch.run": return ProductionBatch.Run((GameItem)args[0], (GameGridInventory)args[1], (GameGridInventory)args[2],
                ((object[][])args[3]).Select(value => new BatchInput((GameItem)value[0], (int)value[1], (int)value[2])).ToArray(),
                (int)args[4], (Func<string>)args[5], (Action<GameItem, int>)args[6], (int)args[7], (GameItem?)args[8], (int)args[9]);
            case "value.input": return ProductionValue.InputValue((GameItem)args[0]);
            case "value.innate": return ProductionValue.InnateFactor((GameItem)args[0]);
            case "value.compose": return ProductionValue.ComposeRealValue((long)args[0], (long)args[1], (double)args[2]);
            case "value.set": ProductionValue.SetBasis((GameItem)args[0], (long)args[1]); return null;
            case "value.read": return ProductionValue.ReadBasis((GameItem)args[0], (long)args[1]);
            case "value.restore": ProductionValue.RestoreBasis((GameItem)args[0]); return null;
            case "inventory.move":
                var moved = args[1] is GameGridInventory grid ? NativeItemTransferApi.Move((GameItem)args[0], grid, (int)args[2]) :
                    NativeItemTransferApi.Move((GameItem)args[0], (GameSlotInventory)args[1], (int)args[2]);
                return new object[] { moved.Moved, moved.Indeterminate, moved.Status };
            case "inventory.plan":
                var plan = (bool)args[2] ? InventoryPlacement.PlanWholeGrid((GameGridInventory)args[0], (GameItem[])args[1]) :
                    InventoryPlacement.PlanWholeStacks((GameGridInventory)args[0], (GameItem[])args[1]);
                return plan?.Select(p => new object[] { p.Item, p.Shape }).ToArray();
            case "inventory.frozen": return NativeInventoryFreezeApi.IsFrozen((GameItem)args[0]);
            case "inventory.acquire": return NativeInventoryFreezeApi.TryAcquire((GameItem[])args[0], (string?)args[1]);
            case "inventory.admission": return NativeInventoryAdmissionApi.Register((string)args[0], (string)args[1], (string)args[2], (Func<GameItem, GameGridInventory, bool>)args[3]);
            case "liquid.container": return NativeLiquidApi.IsContainer((GameItem)args[0]);
            case "liquid.capture": var liquid = NativeLiquidApi.Capture((GameItem)args[0]); return liquid == null ? null : ForgeWire.Liquid(liquid);
            case "liquid.quality": return NativeLiquidApi.WaterQuality((GameItem)args[0]);
            case "liquid.write": return NativeLiquidApi.Write((GameItem)args[0], ForgeWire.Liquid((object[])args[1]));
            case "liquid.pour":
                var poured = NativeLiquidTransferApi.Pour((GameItem)args[0], (GameItem)args[1], (int)args[2], (Func<bool>?)args[3]);
                return new object[] { poured.PartsMoved, poured.Indeterminate, poured.Status };
            case "liquid.valued": return NativeLiquidValueRuntime.HasLedger((GameItem)args[0]);
            case "liquid.value": NativeLiquidValueRuntime.Store((GameItem)args[0], ForgeWire.Liquid((object[])args[1])); return null;
            case "liquid.refresh": NativeLiquidValueRuntime.RefreshValue((GameItem)args[0]); return null;
            case "transaction.run":
                var result = ForgeTransactions.Run(((object[][])args[0]).Select(v => new NativeTransactionStep((string)v[0], (Action)v[1], (Func<bool>)v[2])).ToArray());
                return new object[] { result.Committed, result.Restored, result.Status };
            case "input.register": Nicokobo.Forge.Input.TextInputFocus.Register((string)args[0], (Func<bool>)args[1]); return null;
            case "input.remove": Nicokobo.Forge.Input.TextInputFocus.Unregister((string)args[0]); return null;
            case "input.block": Nicokobo.Forge.Input.TextInputFocus.BlockCurrentFrame(); return null;
            case "input.suppress": return Nicokobo.Forge.Input.TextInputFocus.ShouldSuppressHotkeys();
            case "window.pointer": return ProbablyStolen.ModSupport.ReadingWindowDrag.ContainsPointer((GameObject?)args[0]);
            case "window.cursor": ProbablyStolen.ModSupport.ReadingWindowDrag.KeepCursor((GameObject?)args[0]); return null;
            case "window.drag": ProbablyStolen.ModSupport.ReadingWindowDrag.Attach((GameObject?)args[0], (float)args[1]); return null;
            case "window.clamp": ProbablyStolen.ModSupport.ReadingWindowDrag.Clamp((RectTransform)args[0]); return null;
            case "power.machine": return ForgePower.RegisterMachine((string)args[0], (string)args[0], (Func<GameItem, int>)args[1], (Func<GameItem, int, object[]?>)args[2]);
            case "power.consumer": DevicePower.RegisterConsumer((string)args[0], (string)args[1], (Func<GameItem, GameItem?>)args[2]); return null;
            case "power.raw": return ForgePower.RawEnergy((GameItem)args[0]);
            case "power.source": return ForgePower.GetDevicePowerSource((GameItem)args[0]);
            case "power.consumer.available": return ForgePower.CanSupplyDevice((GameItem)args[0]);
            case "native.subscribe": return NativeMachines.Subscribe((string)args[0], (Func<GameItem, Action<bool>?>)args[1]);
            case "native.batch.register": NativeMachines.RegisterBatch((string)args[0], (string)args[1], (Action<GameItem, Action>)args[2]); return null;
            case "native.process": NativeMachines.Process((GameItem)args[0]); return null;
            case "native.inventory": return NativeMachines.TryGetInventory((GameItem)args[0], out var inventory) && inventory != null
                ? new object[] { inventory.Battery!, inventory.Modules, inventory.ItemInput!, inventory.Output, inventory.LiquidInputs, inventory.Auxiliary! } : null;
            case "stock.register": NightShop.Register((string)args[0], (Func<GameItem[], string[]>)args[1]); return null;
            case "run.read": var read = RunDataGate.Read((PlayerStore?)args[0], (string)args[1], (string)args[2]); return new object[] { (int)read.Status, read.Json!, read.Reason! };
            case "run.stage": var stage = RunDataGate.Stage((PlayerStore?)args[0], (string)args[1], (string)args[2], (string)args[3], (int)args[4], (string?)args[5], (string)args[6]); return new object[] { (int)stage.Status, stage.Json!, stage.Reason! };
            default: return DispatchPresentation(operation, args);
        }
    }
}
public static class ForgeTransactions
{
    public static NativeTransactionResult Run(IReadOnlyList<NativeTransactionStep> steps)
    {
        if (steps.Count == 0 || steps.Any(s => string.IsNullOrWhiteSpace(s.Name) || s.Apply == null || s.Restore == null))
            throw new ArgumentException("A nonempty transaction is required");
        var result = MachineTransaction.Run(steps.Select(s => new MachineTransactionStep(s.Name, s.Apply, s.Restore)).ToArray());
        return new(result.Committed, result.Restored, result.Status);
    }
}
