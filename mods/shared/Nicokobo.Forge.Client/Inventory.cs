namespace Nicokobo.ContentInterop;

public static class NativeItemTransferApi
{
    public static NativeItemTransferResult Move(GameItem item, GameGridInventory target, int maximum) => MoveCore(item, target, maximum);
    public static NativeItemTransferResult Move(GameItem item, GameSlotInventory target, int maximum) => MoveCore(item, target, maximum);
    private static NativeItemTransferResult MoveCore(GameItem item, GameInventory target, int maximum)
    { var v = ForgeClient.Call<object[]>("inventory.move", item, target, maximum); return new((int)v[0], (bool)v[1], (string)v[2]); }
}
public static class InventoryPlacement
{
    public static IReadOnlyList<InventoryPlacementEntry>? PlanWholeGrid(GameGridInventory grid, IReadOnlyList<GameItem> items) => Plan(grid, items, true);
    public static IReadOnlyList<InventoryPlacementEntry>? PlanWholeStacks(GameGridInventory grid, IReadOnlyList<GameItem> items) => Plan(grid, items, false);
    private static IReadOnlyList<InventoryPlacementEntry>? Plan(GameGridInventory grid, IReadOnlyList<GameItem> items, bool singles) =>
        ForgeClient.Call("inventory.plan", grid, items.ToArray(), singles) is object[][] values
            ? values.Select(v => new InventoryPlacementEntry((GameItem)v[0], (GridShape)v[1])).ToArray() : null;
}
public static class NativeInventoryFreezeApi
{
    public static bool IsFrozen(GameItem item) => ForgeClient.Call<bool>("inventory.frozen", item);
    public static IDisposable? TryAcquire(IEnumerable<GameItem> items, string? group = null) => (IDisposable?)ForgeClient.Call("inventory.acquire", items.ToArray(), group!);
}
public static class NativeInventoryAdmissionApi
{
    public static IDisposable Register(string owner, string device, string inventory, Func<GameItem, GameGridInventory, bool> predicate) =>
        ForgeClient.Call<IDisposable>("inventory.admission", owner, device, inventory, predicate);
}
public static class NativeLiquidApi
{
    public static bool IsContainer(GameItem item) => ForgeClient.Call<bool>("liquid.container", item);
    public static NativeMachineLiquidSnapshot? Capture(GameItem item) => ForgeClient.Call("liquid.capture", item) is object[] value ? ForgeWire.Liquid(value) : null;
    public static int WaterQuality(GameItem item) => ForgeClient.Call<int>("liquid.quality", item);
    public static bool Write(GameItem item, NativeMachineLiquidSnapshot value) => ForgeClient.Call<bool>("liquid.write", item, ForgeWire.Liquid(value));
    public static bool Matches(NativeMachineLiquidSnapshot? value, NativeMachineLiquidSnapshot expected) =>
        value != null && value.CapacityParts == expected.CapacityParts && value.Contents.SequenceEqual(expected.Contents);
}
public static class NativeLiquidTransferApi
{
    public static NativeLiquidTransferResult Pour(GameItem source, GameItem target) => Pour(source, target, int.MaxValue);
    public static NativeLiquidTransferResult Pour(GameItem source, GameItem target, int maximum, Func<bool>? allowed = null)
    { var v = ForgeClient.Call<object[]>("liquid.pour", source, target, maximum, allowed!); return new((int)v[0], (bool)v[1], (string)v[2]); }
}
public static class NativeLiquidValueRuntime
{
    public static bool HasLedger(GameItem item) => ForgeClient.Call<bool>("liquid.valued", item);
    public static void Store(GameItem item, NativeMachineLiquidSnapshot value) => ForgeClient.Call("liquid.value", item, ForgeWire.Liquid(value));
    public static void RefreshValue(GameItem item) => ForgeClient.Call("liquid.refresh", item);
}
public static class ForgeTransactions
{
    public static NativeTransactionResult Run(IReadOnlyList<NativeTransactionStep> steps)
    {
        var v = ForgeClient.Call<object[]>("transaction.run", (object)steps.Select(s => new object[] { s.Name, s.Apply, s.Restore }).ToArray());
        return new((bool)v[0], (bool)v[1], (string)v[2]);
    }
}
