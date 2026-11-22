namespace Nicokobo.ContentInterop;

public static class ContentRuntime
{
    public static bool IsAttached(string owner) => ForgeClient.Connected && ForgeClient.Call<bool>("runtime.attached", owner);
    public static void Attach(string owner) => ForgeClient.Call("runtime.attach", owner);
    public static void Detach(string owner) { if (ForgeClient.Connected) ForgeClient.Call("runtime.detach", owner); }
}
public static class RecipeQuery
{
    public static void OpenItem(string id) => ForgeClient.Call("recipe.open", id);
}
public static class ExtraRecipes
{
    public static void Register(string owner, ExtraRecipe recipe) => ForgeClient.Call("extra.register", owner, ForgeWire.Recipe(recipe));
    public static IReadOnlyList<ExtraRecipe> ForMachine(string machineId) => ForgeClient.Call<object[][]>("extra.for", machineId).Select(value => (ExtraRecipe)ForgeWire.Recipe(value)).ToArray();
    public static IReadOnlyList<string> MachineIds() => ForgeClient.Call<string[]>("extra.machines");
}
public static class MachineCatalog
{
    public static void Register(string owner, string machineId, Func<GameItem, MachinePorts> ports, Func<IReadOnlyList<RecipeView>> recipes, int nightOrder = 10) =>
        ForgeClient.Call("machine.register", owner, machineId, (Func<GameItem, object[]>)(item => ForgeWire.Ports(ports(item))), (Func<object[][]>)(() => recipes().Select(ForgeWire.Recipe).ToArray()), nightOrder);
    public static bool Contains(string machineId) => ForgeClient.Call<bool>("machine.contains", machineId);
    public static IReadOnlyList<string> MachineIds() => ForgeClient.Call<string[]>("machine.ids");
    public static MachinePorts? Ports(GameItem machine) => ForgeClient.Call("machine.ports", machine) is object[] value ? ForgeWire.Ports(value) : null;
    public static IReadOnlyList<RecipeView> Recipes(string machineId) => ForgeClient.Call<object[][]>("machine.recipes", machineId).Select(ForgeWire.Recipe).ToArray();
    public static IReadOnlyList<RecipeView> Recipes(GameItem machine) => Recipes(machine.identifier).Where(recipe => recipe.IsAvailableFor(machine)).ToArray();
    public static void AddNativeRecipes(string owner, string machineId, Func<IReadOnlyList<RecipeView>> recipes) =>
        ForgeClient.Call("machine.native", owner, machineId, (Func<object[][]>)(() => recipes().Select(ForgeWire.Recipe).ToArray()));
}
public static class ProductionHooks
{
    public static void RegisterNightFlush(string owner, Action flush) => ForgeClient.Call("production.flush", owner, flush);
    public static void Subscribe(string owner, string key, Action<WorkContext>? before, Action<WorkResult>? after) =>
        ForgeClient.Call("production.subscribe", owner, key, before == null ? null! : (Action<object[]>)(value => before(ForgeWire.Context(value))),
            after == null ? null! : (Action<object[]>)(value => after(ForgeWire.Result(value))));
    public static Attempt Begin(WorkContext context) => new(ForgeClient.Call<object[]>("production.begin", (object)ForgeWire.Context(context)));
    public sealed class Attempt : IDisposable
    {
        private readonly Action _commit, _dispose;
        private readonly Func<bool> _consumed;
        internal Attempt(object[] handle) { _commit = (Action)handle[0]; _consumed = (Func<bool>)handle[1]; _dispose = (Action)handle[2]; }
        public void Commit() => _commit();
        public bool HasInputConsumption => _consumed();
        public void Dispose() => _dispose();
    }
}
public static class DevicePower
{
    public static void RegisterConsumer(string owner, string id, Func<GameItem, GameItem?> source) => ForgeClient.Call("power.consumer", owner, id, source);
    public static void Register(string owner, Func<GameItem, int?> query, Func<GameItem, int, bool?> debit) => ForgeClient.Call("power.register", owner, query, debit);
    public static bool TryEnergy(GameItem source, out int energy)
    { var value = ForgeClient.Call<object[]>("power.energy", source); energy = (int)value[1]; return (bool)value[0]; }
    public static bool TryDebit(GameItem source, int amount, out bool success)
    { var value = ForgeClient.Call<object[]>("power.debit", source, amount); success = (bool)value[1]; return (bool)value[0]; }
}
public static class SupplyCatalog
{
    public static void Register(string owner, IEnumerable<StockOffer> offers) => ForgeClient.Call("supply.register", owner, offers.Select(ForgeWire.Offer).ToArray());
    public static IReadOnlyList<StockOffer> Available(StockCategory categories, StockSupplier supplier = StockSupplier.Any, bool nightShop = false) =>
        ForgeClient.Call<object[][]>("supply.available", (int)categories, (int)supplier, nightShop).Select(ForgeWire.Offer).ToArray();
}
