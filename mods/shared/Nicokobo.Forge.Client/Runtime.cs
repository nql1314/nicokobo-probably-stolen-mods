namespace Nicokobo.ContentInterop;

public static class ForgePower
{
    public static IDisposable Register(string owner, Func<GameItem, int> energy, Func<GameItem, int, object[]?> plan) => ForgeClient.Call<IDisposable>("power.machine", owner, energy, plan);
    public static int RawEnergy(GameItem item) => ForgeClient.Call<int>("power.raw", item);
    public static GameItem? Source(GameItem item) => (GameItem?)ForgeClient.Call("power.source", item);
    public static bool CanSupply(GameItem item) => ForgeClient.Call<bool>("power.consumer.available", item);
}
public static class NativeMachineAccess
{
    // The driver repeats complete native batches, including provider cleanup.
    public static void RegisterBatch(string owner, string machineId, Action<GameItem, Action> run) => ForgeClient.Call("native.batch.register", owner, machineId, run);
    public static IDisposable Subscribe(string owner, Func<GameItem, Action<bool>?> before) => ForgeClient.Call<IDisposable>("native.subscribe", owner, before);
    // Manual work enters here to retain native logistics and power receipts;
    // directly invoking an unchanged item callback bypasses Forge services.
    public static void Process(GameItem machine) => ForgeClient.Call("native.process", machine);
    public static NativeMachineInventory? Capture(GameItem item) => ForgeClient.Call("native.inventory", item) is object[] v
        ? new((GameSlotInventory?)v[0], (GameInventory)v[1], (GameInventory?)v[2], (GameInventory)v[3],
            (IReadOnlyDictionary<string, GameSlotInventory>)v[4], (GameSlotInventory?)v[5]) : null;
}
public static class NightShop
{
    public static void Register(string owner, Func<GameItem[], string[]> stock) => ForgeClient.Call("stock.register", owner, stock);
}
public static class RunDataGate
{
    public static RunDataValue Read(PlayerStore? store, string owner, string key) => Result(ForgeClient.Call<object[]>("run.read", store!, owner, key));
    public static RunDataValue Stage(PlayerStore? store, string owner, string key, string run, int slot, string? expected, string next) =>
        Result(ForgeClient.Call<object[]>("run.stage", store!, owner, key, run, slot, expected!, next));
    private static RunDataValue Result(object[] value) => new((RunDataState)(int)value[0], (string?)value[1], (string?)value[2]);
}
