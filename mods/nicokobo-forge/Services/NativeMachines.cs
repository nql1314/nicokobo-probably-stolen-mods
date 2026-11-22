namespace Nicokobo.ContentInterop;
internal static class NativeMachines
{
    internal const string PurifierWaterSlot = "purifier_water", HydroponicWaterSlot = "hydroponic_water";
    private static readonly IReadOnlyDictionary<string, GameSlotInventory> NoLiquids = new Dictionary<string, GameSlotInventory>();
    internal static bool IsNative(string id) => id is "furnace" or "water_purifier" or "moisture_farm" or "desequencer" or "printer" or "evaporator" or "hydroponic" or "plant_dna_manipulator";
    private static readonly Dictionary<string, Func<GameItem, Action<bool>?>> Observers = new();
    private static readonly Dictionary<string, (string Owner, Action<GameItem, Action> Run)> BatchRunners = new();
    internal static void RegisterBatch(string owner, string machineId, Action<GameItem, Action> run)
    {
        ContentRuntime.RequireOwner(owner);
        if (!IsNative(machineId)) throw new ArgumentException("Not a native production machine", nameof(machineId));
        if (BatchRunners.TryGetValue(machineId, out var previous) && previous.Owner != owner)
            throw new InvalidOperationException("Native batch runner already registered: " + machineId);
        BatchRunners[machineId] = (owner, run ?? throw new ArgumentNullException(nameof(run)));
    }
    internal static IDisposable Subscribe(string owner, Func<GameItem, Action<bool>?> before)
    { ContentRuntime.RequireOwner(owner); Observers.Add(owner, before); return new CallbackLease(() => Observers.Remove(owner)); }
    internal static void Remove(string owner)
    {
        Observers.Remove(owner);
        foreach (string id in BatchRunners.Where(entry => entry.Value.Owner == owner).Select(entry => entry.Key).ToArray()) BatchRunners.Remove(id);
    }
    internal static Action<bool>[]? Before(GameItem machine)
    {
        var completions = new List<Action<bool>>();
        foreach (var before in Observers.Values.ToArray())
        {
            var after = before(machine);
            if (after == null) { foreach (var done in completions) done(false); return null; }
            completions.Add(after);
        }
        return completions.ToArray();
    }
    internal static void Process(GameItem machine)
    {
        var cycle = machine.onCycleEndSlotItemFunc;
        if (cycle != null) Invoke(cycle, machine, machine.parentInventory, machine.GetIncompleteSlotInInventory());
    }
    internal static void Invoke(Action<GameItem, GameInventory, SlotMarker> cycle, GameItem machine, GameInventory parent, SlotMarker slot)
    {
        if (!IsNative(machine.identifier)) { cycle(machine, parent, slot); return; }
        if (BatchRunners.TryGetValue(machine.identifier, out var runner))
            runner.Run(machine, () => InvokeBatch(cycle, machine, parent, slot));
        else InvokeBatch(cycle, machine, parent, slot);
    }
    private static void InvokeBatch(Action<GameItem, GameInventory, SlotMarker> cycle, GameItem machine, GameInventory parent, SlotMarker slot)
    {
        var after = Before(machine);
        if (after == null) return;
        bool success = false;
        try
        {
            using var power = ForgePower.BeginOperation();
            cycle(machine, parent, slot); power.Complete(); success = true;
        }
        finally { foreach (var callback in after) callback(success); }
    }
    internal static bool TryGetInventory(GameItem machine, out NativeMachineInventory? inventory)
    {
        if (!IsNative(machine.identifier))
        {
            var ports = MachineCatalog.Ports(machine); inventory = null;
            if (ports?.Modules == null) return false;
            inventory = new(ports.Battery, ports.Modules, ports.Input, ports.Output,
                ports.Water == null ? new Dictionary<string, GameSlotInventory>() : new Dictionary<string, GameSlotInventory> { ["water"] = ports.Water });
            return true;
        }
        inventory = null;
        var battery = MachineHelper.GetBodyStorage(machine, 0) as GameSlotInventory;
        var modules = MachineHelper.GetBodyStorage(machine, 1) as GameInventory;
        if (battery == null || modules == null) return false;
        if (machine.identifier is "water_purifier" or "moisture_farm")
        {
            var water = MachineHelper.GetBodyStorage(machine, 2) as GameSlotInventory;
            if (water == null) return false;
            inventory = new(battery, modules, null, water,
                machine.identifier == "water_purifier" ? new Dictionary<string, GameSlotInventory> { [PurifierWaterSlot] = water } : NoLiquids,
                MachineHelper.GetBodyStorage(machine, 3) as GameSlotInventory);
            return true;
        }
        if (machine.identifier == "printer")
        {
            var materials = MachineHelper.GetBodyStorage(machine, 2) as GameGridInventory;
            var printed = MachineHelper.GetBodyStorage(machine, 4) as GameGridInventory;
            if (materials == null || printed == null) return false;
            inventory = new(battery, modules, materials, printed, NoLiquids,
                MachineHelper.GetBodyStorage(machine, 5) as GameSlotInventory);
            return true;
        }
        if (machine.identifier == "evaporator")
        {
            var distillate = MachineHelper.GetBodyStorage(machine, 3) as GameSlotInventory;
            if (distillate == null) return false;
            inventory = new(battery, modules, null, distillate, NoLiquids,
                MachineHelper.GetBodyStorage(machine, 4) as GameSlotInventory);
            return true;
        }
        if (machine.identifier == "hydroponic")
        {
            var water = MachineHelper.GetBodyStorage(machine, 2) as GameSlotInventory;
            var harvest = MachineHelper.GetBodyStorage(machine, 4) as GameGridInventory;
            if (water == null || harvest == null) return false;
            inventory = new(battery, modules, null, harvest,
                new Dictionary<string, GameSlotInventory> { [HydroponicWaterSlot] = water },
                MachineHelper.GetBodyStorage(machine, 5) as GameSlotInventory);
            return true;
        }
        if (machine.identifier == "plant_dna_manipulator")
        {
            var ejected = MachineHelper.GetBodyStorage(machine, 6) as GameGridInventory;
            if (ejected == null) return false;
            inventory = new(battery, modules, null, ejected, NoLiquids,
                MachineHelper.GetBodyStorage(machine, 5) as GameSlotInventory);
            return true;
        }
        bool furnace = machine.identifier == "furnace";
        var input = MachineHelper.GetBodyStorage(machine, furnace ? 2 : 3) as GameInventory;
        var output = MachineHelper.GetBodyStorage(machine, furnace ? 3 : 4) as GameInventory;
        if (input == null || output == null || (furnace ? input.TryCast<GameGridInventory>() == null || output.TryCast<GameGridInventory>() == null :
            input.TryCast<GameSlotInventory>() == null || output.TryCast<GameSlotInventory>() == null)) return false;
        inventory = new(battery, modules, input, output, NoLiquids,
            furnace ? MachineHelper.GetBodyStorage(machine, 4) as GameSlotInventory : null);
        return true;
    }
}
