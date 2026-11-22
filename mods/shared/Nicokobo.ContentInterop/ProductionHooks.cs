namespace Nicokobo.ContentInterop;

public static class MachineCatalog
{
    private sealed class Entry
    {
        internal string Owner = "";

        internal Func<GameItem, MachinePorts> Ports = null!;
        internal Func<IReadOnlyList<RecipeView>> Recipes = null!;
    }
    private static readonly Dictionary<string, Entry> Entries = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, (string Owner, string Machine, Func<IReadOnlyList<RecipeView>> Recipes)> NativeRecipes = new(StringComparer.Ordinal);
    public static void Register(string owner, string machineId, Func<GameItem, MachinePorts> ports, Func<IReadOnlyList<RecipeView>> recipes, int nightOrder = 10)
    {
        ContentRuntime.RequireOwner(owner);
        if (Entries.TryGetValue(machineId, out var old) && old.Owner != owner)
            throw new InvalidOperationException("Machine contract already registered: " + machineId);
        Entries[machineId] = new() { Owner = owner, Ports = ports, Recipes = recipes };
        MachineSchedule.Set(owner, machineId, nightOrder);
    }
    public static bool Contains(string machineId) => Entries.ContainsKey(machineId);
    public static IReadOnlyList<string> MachineIds() => Entries.Keys.Concat(NativeRecipes.Values.Select(entry => entry.Machine))
        .Concat(ExtraRecipes.MachineIds()).Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToArray();
    public static MachinePorts? Ports(GameItem machine)
    {
        if (Entries.TryGetValue(machine.identifier, out var entry)) return entry.Ports(machine);
        if (machine.identifier != "furnace" || !NativeRecipes.Values.Any(value => value.Machine == "furnace")) return null;
        var input = MachineHelper.GetBodyStorage(machine, 2) as GameInventory ?? throw new InvalidOperationException("Furnace input inventory missing");
        var output = MachineHelper.GetBodyStorage(machine, 3) as GameInventory ?? throw new InvalidOperationException("Furnace output inventory missing");
        return new MachinePorts(input, output,
            MachineHelper.GetBodyStorage(machine, 1) as GameInventory, MachineHelper.GetBodyStorage(machine, 0) as GameSlotInventory);
    }
    public static IReadOnlyList<RecipeView> Recipes(string machineId) =>
        (Entries.TryGetValue(machineId, out var entry) ? entry.Recipes().Concat(Additional(machineId)) : Additional(machineId))
        .GroupBy(recipe => recipe.Id, StringComparer.Ordinal).Select(group => group.First()).ToArray();
    private static IEnumerable<RecipeView> Additional(string id) => NativeRecipes.Values.Where(entry => entry.Machine == id)
        .SelectMany(entry => entry.Recipes()).Concat(ExtraRecipes.ForMachine(id));
    public static IReadOnlyList<RecipeView> Recipes(GameItem machine) => Recipes(machine.identifier).Where(recipe => recipe.IsAvailableFor(machine)).ToArray();
    public static void AddNativeRecipes(string owner, string machineId, Func<IReadOnlyList<RecipeView>> recipes)
    {
        ContentRuntime.RequireOwner(owner);
        NativeRecipes[owner + ":" + machineId] = (owner, machineId, recipes);
    }
    internal static void Remove(string owner)
    {
        MachineSchedule.Remove(owner);
        foreach (string id in Entries.Where(entry => entry.Value.Owner == owner).Select(entry => entry.Key).ToArray()) Entries.Remove(id);
        foreach (string id in NativeRecipes.Where(entry => entry.Value.Owner == owner).Select(entry => entry.Key).ToArray()) NativeRecipes.Remove(id);
    }
}

public static class ProductionHooks
{
    private sealed class Observer
    {
        internal string Owner = "";
        internal Action<WorkContext>? Before;
        internal Action<WorkResult>? After;
    }
    private static readonly Dictionary<string, Observer> Observers = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, Action> NightFlushers = new(StringComparer.Ordinal);
    // Registered before content late-night handlers so physical work finishes
    // before per-night growth, rewards and saved-state bookkeeping.
    public static void RegisterNightFlush(string owner, Action flush)
    {
        ContentRuntime.RequireOwner(owner);
        NightFlushers[owner] = flush ?? throw new ArgumentNullException(nameof(flush));
    }
    internal static void FlushNight()
    {
        foreach (var flush in NightFlushers.Values.ToArray()) flush();
    }
    [ThreadStatic] private static Attempt? Current;
    public static void Subscribe(string owner, string key, Action<WorkContext>? before, Action<WorkResult>? after)
    {
        ContentRuntime.RequireOwner(owner);
        if (Observers.TryGetValue(key, out var old) && old.Owner != owner) throw new InvalidOperationException("Production observer key collision: " + key);
        Observers[key] = new() { Owner = owner, Before = before, After = after };
    }
    internal static void Remove(string owner)
    { foreach (string key in Observers.Where(entry => entry.Value.Owner == owner).Select(entry => entry.Key).ToArray()) Observers.Remove(key); NightFlushers.Remove(owner); }
    public static Attempt Begin(WorkContext context)
    {
        if (Current is { } active && active.Context.Machine == context.Machine)
            return new Attempt(context, active);
        var attempt = new Attempt(context, null) { Previous = Current };
        Current = attempt;
        try
        {
            foreach (var observer in Observers.Values.ToArray()) observer.Before?.Invoke(context);
            attempt.Capture();
            return attempt;
        }
        catch { Current = attempt.Previous; throw; }
    }
    public sealed class Attempt : IDisposable
    {
        internal readonly WorkContext Context;
        private readonly Attempt? _parent;
        internal Attempt? Previous;
        private readonly Dictionary<GameItem, int> _inputs = new();
        private GameItem? _water;
        private int _waterBefore;
        private bool _committed, _closed;
        internal Attempt(WorkContext context, Attempt? parent) { Context = context; _parent = parent; }
        internal void Capture()
        {
            foreach (var item in Context.Ports.Input.childItems) _inputs[item] = item.unitCount;
            _water = Context.Ports.Water?.childItem;
            _waterBefore = _water == null ? 0 : WaterHelper.GetTotalVolume(_water);
        }
        public void Commit() { if (_parent != null) _parent.Commit(); else _committed = true; }
        // Native adapters use observed input loss to recognize a completed work callback.
        public bool HasInputConsumption => _parent?.HasInputConsumption ?? _inputs.Any(pair =>
            pair.Value > (pair.Key.parentInventory == Context.Ports.Input ? pair.Key.unitCount : 0));
        public void Dispose()
        {
            if (_closed) return;
            _closed = true;
            if (_parent != null) return;
            Current = Previous;
            var consumed = _inputs.Select(pair => new ConsumedItem(pair.Key,
                Math.Max(0, pair.Value - (pair.Key.parentInventory == Context.Ports.Input ? pair.Key.unitCount : 0))))
                .Where(item => item.Amount > 0).ToArray();
            int water = _water != null ? Math.Max(0, _waterBefore - WaterHelper.GetTotalVolume(_water)) : 0;
            var result = new WorkResult(Context, _committed, consumed, water);
            foreach (var observer in Observers.Values.ToArray())
                try { observer.After?.Invoke(result); }
                catch (Exception error) { UnityEngine.Debug.LogWarning("[ContentInterop] After-work observer failed: " + error); }
        }
    }
}
