using static PsModI18n.I18n;
using Newtonsoft.Json;
using Nicokobo.AutomatedLogistics.Domain;

namespace Nicokobo.AutomatedLogistics.Integration;

internal sealed partial class AutomatedLogisticsRuntime(Action<string> log, Action<string>? debug = null, Action<string>? info = null) : IDisposable
{
    private const string SaveKey = NativeRunDataApi.SaveKey;
    private readonly List<IDisposable> _leases = new();
    private readonly HashSet<int> _fillBusy = new(), _ejectBusy = new();
    internal readonly Dictionary<int, string> Status = new();
    internal readonly Dictionary<int, string> FillStatus = new(), EjectStatus = new();
    internal AutomatedLogisticsState State { get; private set; } = new();
    private string? _json, _run;
    private int _slot = -1;
    internal bool Ready { get; private set; }
    internal string Error { get; private set; } = PsModI18n.I18n.Get("waiting_for_the_run_to_load");
    private float _nextScan;
    private int _cursor;
    private int _loadGeneration;
    internal event Action? NetworkPresentationChanged;
    private bool _resourceUnlockAttempted, _powerUnlockAttempted;
    private bool IsCurrentRun() => PlayerStore.HasInstance && PlayerStore.Instance is { } store && store.Handle() != IntPtr.Zero &&
        store.runID == _run && store.saveSlotId == _slot;

    internal void Install()
    {
        try
        {
            _leases.Add(NativeLifecycleApi.Subscribe(Content.Owner, Content.Owner + ".load",
                NativeLifecyclePhase.AfterGameLoadedLate, context => Load(context.Store)));
            _leases.Add(NativeInventoryAdmissionApi.Register(Content.Owner, Content.PowerDevice, Content.ContentsId,
                (candidate, _) => NetworkStorage.IsPowerRegionItem(candidate)));
            _leases.Add(NativeInventoryAdmissionApi.Register(Content.Owner, Content.ResourceDevice, Content.ContentsId,
                // Native partial transfers can create a new detached split UID.
                // Admission checks the endpoint and kind; transfer leases guard
                // top-level operations rather than rejecting that split.
                (candidate, grid) => NetworkStorage.IsResourceItem(candidate)));
            if (NativeExternalPowerApi.IsAvailable)
                _leases.Add(NativeExternalPowerApi.RegisterMachine(Content.Owner, Content.Owner + ".machine_power", MachineGridEnergy, PlanPower));
            else log(PsModI18n.I18n.Get("external_power_unavailable_machine_grid_supply_is"));
            _leases.Add(NativeLifecycleApi.Subscribe(Content.Owner, Content.Owner + ".power_before_night",
                NativeLifecyclePhase.BeforeNight, context => ObservePowerBeforeNight(context.Run.Day), int.MinValue));
            _leases.Add(NativeLifecycleApi.Subscribe(Content.Owner, Content.Owner + ".restock_diagnostics",
                NativeLifecyclePhase.BeforeNight, _ => TraceNightRestocking(), int.MinValue + 1));
            _leases.Add(NativeLifecycleApi.Subscribe(Content.Owner, Content.Owner + ".feed_before_night",
                NativeLifecyclePhase.BeforeNight, _ =>
                { EnsureLoaded(); foreach (var machine in Machines().Where(MachineAccess.IsFeedDispenser)) FillOnce(machine, trigger: "before-night-feed"); }, int.MinValue + 2));
            _leases.Add(NativeLifecycleApi.Subscribe(Content.Owner, Content.Owner + ".power_after_night",
                NativeLifecyclePhase.AfterNight, _ => ObservePowerAfterNight(), int.MaxValue));
            _leases.Add(NativeMachineAutomationApi.Subscribe(Content.Owner, Content.Owner + ".fill",
                NativeMachineAutomationPhase.BeforeBatch, BeforeBatch));
            _leases.Add(NativeMachineAutomationApi.Subscribe(Content.Owner, Content.Owner + ".eject",
                NativeMachineAutomationPhase.AfterBatch, AfterBatch));
            _leases.Add(NativeLifecycleApi.Subscribe(Content.Owner, Content.Owner + ".night_eject",
                NativeLifecyclePhase.AfterNight, _ =>
                { EnsureLoaded(); foreach (var machine in Machines()) EjectOnce(machine); }, 1000));
        }
        catch { Dispose(); throw; }
    }
    internal void EnsureLoaded()
    {
        if (!PlayerStore.HasInstance) return;
        var store = PlayerStore.Instance;
        if (store == null || store.Handle() == IntPtr.Zero || string.IsNullOrWhiteSpace(store.runID) || store.saveSlotId < 0) return;
        if (store.runID != _run || store.saveSlotId != _slot) Load(store);
    }
    private void Load(PlayerStore store)
    {
        Reset(); _run = store.runID; _slot = store.saveSlotId;
        var result = NativeRunDataApi.Read(store, Content.Owner, SaveKey);
        try
        {
            if (result.Status == RunDataStatus.Missing) State = new();
            else if (result.Status == RunDataStatus.Present)
            {
                State = JsonConvert.DeserializeObject<AutomatedLogisticsState>(result.Json!) ?? throw new InvalidDataException(PsModI18n.I18n.Get("save_data_is_empty"));
                if (!State.IsValid())
                    throw new InvalidDataException(PsModI18n.I18n.Get("invalid_automation_save_version_or_content"));
            }
            else throw new InvalidDataException(result.Reason ?? result.Status.ToString());
            State.Names ??= new();
            _json = result.Json; Ready = true; Error = "";
            RebindNetworks();
        }
        catch (Exception ex) { Error = ex.Message; Ready = false; return; }
        // Restoring optional presentation must not invalidate verified resource data.
        foreach (var item in Items())
        {
            if (item.identifier == "energy_credit_dis") PowerHelper.UpdateEnergyCreditSprite(item);
            try
            {
                if (State.Names.TryGetValue(item.GetUniqueID(), out var name) && InstanceNames.IsValid(name) && CanRename(item))
                    ApplyNativeName(item, name);
            }
            catch (Exception ex) { log(PsModI18n.I18n.Get("name_restoration_failed") + ex.Message); }
            if (item.identifier is not (Content.Terminal or Content.Feeder or Content.Ejector) && !Content.IsDevice(item.identifier)) continue;
            try { Content.ConfigureTool(item); }
            catch (Exception ex) { log(PsModI18n.I18n.Get("device_interaction_restoration_failed") + ex.Message); }
            try { ItemArtwork.Apply(item); }
            catch (Exception ex) { log(PsModI18n.I18n.Get("device_artwork_restoration_failed") + ex.Message); }
        }
    }
    internal void Save()
    {
        if (!Ready) throw new InvalidOperationException(Error);
        if (!IsCurrentRun()) throw new InvalidOperationException(PsModI18n.I18n.Get("the_run_changed_the_previous_write_was"));
        if (!State.IsValid()) throw new InvalidDataException(PsModI18n.I18n.Get("automation_settings_failed_validation"));
        var state = State; int generation = _loadGeneration;
        string next = JsonConvert.SerializeObject(state);
        var store = PlayerStore.Instance; string? run = _run; int slot = _slot;
        bool SameSession() => generation == _loadGeneration && ReferenceEquals(State, state) && IsCurrentRun() &&
            _run == run && _slot == slot && PlayerStore.Instance?.Handle() == store?.Handle();
        var result = NativeRunDataApi.Stage(store, Content.Owner, SaveKey, run!, slot, _json, next);
        if (!SameSession()) throw new InvalidOperationException(PsModI18n.I18n.Get("the_run_reloaded_during_the_settings_write"));
        if (result.Status == RunDataStatus.Indeterminate)
        {
            var observed = NativeRunDataApi.Read(store, Content.Owner, SaveKey);
            if (!SameSession()) throw new InvalidOperationException(PsModI18n.I18n.Get("the_run_reloaded_during_settings_verification_the"));
            if (observed.Status == RunDataStatus.Present && observed.Json == next)
                result = new(RunDataStatus.StagedInMemory, next);
        }
        if (result.Status != RunDataStatus.StagedInMemory)
        { Ready = false; Error = PsModI18n.I18n.Get("failed_to_save_automation_settings") + result.Status; throw new InvalidOperationException(Error); }
        _json = next;
    }
    internal IReadOnlyList<GameItem> Items() => Ready ? NativeInventoryApi.CaptureRunItems(PlayerStore.Instance)
        .Where(NativeInventoryApi.IsPlayerOwned).ToArray() : Array.Empty<GameItem>();
    internal IReadOnlyList<GameItem> Machines() => Items().Where(MachineAccess.Eligible).OrderBy(i => i.GetUniqueID()).ToArray();
    internal MachineSettings Settings(GameItem machine) => State.Machines.TryGetValue(machine.GetUniqueID(), out var settings)
        ? JsonConvert.DeserializeObject<MachineSettings>(JsonConvert.SerializeObject(settings))!
        : new() { MachineId = machine.GetUniqueID() };
    internal bool FillEnabled(GameItem machine) => !State.Machines.TryGetValue(machine.GetUniqueID(), out var settings) || settings.FillEnabled;
    private bool EjectEnabled(GameItem machine) => !State.Machines.TryGetValue(machine.GetUniqueID(), out var settings) || settings.EjectEnabled;
    internal void ChangeSettings(MachineSettings settings)
    {
        // UI snapshots own the switches, not the consumption accumulated since
        // that snapshot was drawn. Copy the current demand before editing.
        var current = State.Machines.TryGetValue(settings.MachineId, out var live)
            ? JsonConvert.DeserializeObject<MachineSettings>(JsonConvert.SerializeObject(live))!
            : new MachineSettings { MachineId = settings.MachineId };
        current.FillEnabled = settings.FillEnabled; current.EjectEnabled = settings.EjectEnabled;
        current.PurifierTarget = settings.PurifierTarget;
        var change = new StateChange(this);
        var result = NativeTransactionApi.Run([new("machine-settings", () =>
        { change.Commit(() => State.Machines[settings.MachineId] = current); if (!change.Verify()) throw new InvalidOperationException(PsModI18n.I18n.Get("machine_settings_could_not_be_verified")); }, change.Restore)]);
        if (!result.Committed) throw new InvalidOperationException(PsModI18n.I18n.Get("machine_settings_were_not_saved") + result.Status);
    }
    internal static bool CanRename(GameItem item) => item != null && item.Handle() != IntPtr.Zero &&
        NativeInventoryApi.IsPlayerOwned(item) && item.GetUniqueID() > 0 &&
        (item.identifier == Content.Terminal || item.IsGameItemType("MACHINE") || item.IsTag("CONTAINER_TAG") || NativeLiquidApi.IsContainer(item));
    internal string InstanceName(GameItem item) => GeneralHelper.HasCustomName(item) ? GeneralHelper.GetCustomName(item) :
        InstanceNames.Suffix(item.GetUniqueID(), State.Names);
    internal string Label(GameItem item) => MachineAccess.DisplayName(item) + "  " +
        InstanceName(item);
    private static void ApplyNativeName(GameItem item, string value)
    {
        if (value.Length == 0) GeneralHelper.RemoveCustomName(item);
        else if (!GeneralHelper.HasCustomName(item) || GeneralHelper.GetCustomName(item) != value)
            GeneralHelper.SetCustomName(item, value);
        if (item.contentWindow?.IsVisible() == true)
            item.contentWindow.SetTitle(GeneralHelper.HasCustomName(item) ? GeneralHelper.GetCustomName(item) : MachineAccess.DisplayName(item));
    }
    internal void Rename(int id, string value)
    {
        var item = Items().FirstOrDefault(i => i.GetUniqueID() == id && CanRename(i));
        if (!Ready || item == null)
            throw new InvalidOperationException(PsModI18n.I18n.Get("the_item_is_no_longer_accessible"));
        value = value.Trim();
        if (value.Length > 0 && !InstanceNames.IsValid(value))
            throw new InvalidOperationException(PsModI18n.I18n.Get("names_allow_up_to_24_characters_with"));
        var state = State; state.Names.TryGetValue(id, out var previous);
        string previousNativeName = GeneralHelper.HasCustomName(item) ? GeneralHelper.GetCustomName(item) : "";
        if (value.Length == 0) state.Names.Remove(id); else state.Names[id] = value;
        try { ApplyNativeName(item, value); Save(); }
        catch
        {
            if (previous == null) state.Names.Remove(id); else state.Names[id] = previous;
            if (ReferenceEquals(State, state) && IsCurrentRun()) ApplyNativeName(item, previousNativeName);
            throw;
        }
    }
    internal void Update()
    {
        EnsureLoaded();
        if (!Ready || !IsCurrentRun()) return;
        CollectPendingGridEnergy();
        if (UnityEngine.Time.unscaledTime < _nextScan) return;
        _nextScan = UnityEngine.Time.unscaledTime + 1;
        GrantPendingTerminal();
        if (!Ready) return;
        UnlockAvailableNetworks();
        if (!Ready) return;
        if (_resourceTransfers == 0 && !NativeInventoryApi.IsItemDragActive) RebindNetworks(refresh: true);
        CollectGridEnergy(); FlushPowerStatistics();
        var machines = Machines();
        if (machines.Count == 0) return;
        // One machine per tick; each native batch also gets its own fresh pass.
        var machine = machines[_cursor % machines.Count];
        _cursor = (_cursor + 1) % machines.Count;
        EjectOnce(machine); FillOnce(machine, trigger: "scan");
    }
    private static int Purity(GameItem item) => item.GetTagReadonly("INGOT_PURITY_TAG")?.valueString switch
    {
        "INGOT_PURITY_VERY_LOW" => 1, "INGOT_PURITY_LOW" => 2, "INGOT_PURITY_FAIR" => 3,
        "INGOT_PURITY_HIGH" => 4, "INGOT_PURITY_VERY_HIGH" => 5, "INGOT_PURITY_PERFECT" => 6, _ => 0
    };
    internal static IReadOnlyList<GameItem> Containers(GameItem machine, GameItem? ownTransfer = null)
    {
        var area = machine.parentInventory;
        if (MachineAccess.IsFeedDispenser(machine) && area?.GetParentItem() is { } host)
            area = host.parentInventory;
        if (area == null || area.IsRemoveLocked()) return Array.Empty<GameItem>();
        var result = new List<GameItem>();
        foreach (var item in area.childItems)
        {
            if (item == null || item.parentInventory?.Handle() != area.Handle() || !NativeInventoryApi.IsPlayerOwned(item) || item.IsGameItemType("MACHINE") ||
                (!item.IsTag("CONTAINER_TAG") && !NativeLiquidApi.IsContainer(item)) ||
                item.identifier.Contains("backpack", StringComparison.OrdinalIgnoreCase) ||
                item.Handle() != ownTransfer?.Handle() && (!item.MayRemove() || NativeInventoryFreezeApi.IsFrozen(item))) continue;
            if (Grids(item).Any() || NativeLiquidApi.IsContainer(item)) result.Add(item);
        }
        return result;
    }
    internal static IEnumerable<GameGridInventory> Grids(GameItem container)
    {
        // Storage bays expose a PixelWindow, not a direct inventory child.
        // Traverse its layout only; never enter another contained item's window.
        var result = new List<GameGridInventory>();
        var seen = new HashSet<IntPtr>();
        void Visit(PixelElement? element)
        {
            if (element == null || element.Handle() == IntPtr.Zero || !seen.Add(element.Handle())) return;
            var grid = element.TryCast<GameGridInventory>();
            if (grid != null)
            { if (grid.GetParentItem()?.Handle() == container.Handle()) result.Add(grid); return; }
            var layout = element.TryCast<GridPixelElement>();
            if (layout != null) foreach (var child in layout.children.OfType<PixelElement>()) Visit(child);
            var machinery = element.TryCast<MachineryPanelRow>();
            if (machinery != null) foreach (var child in machinery.children.OfType<PixelElement>()) Visit(child);
        }
        Visit(container.contentWindow?.childElement);
        if (container.children != null) foreach (var child in container.children)
        { Visit(child.TryCast<PixelElement>()); Visit(child.TryCast<PixelWindow>()?.childElement); }
        return result;
    }
    // The bottom Inventory page is the scene's main inventory window.
    // PlayerStore.GetStoreInventory() is separate and must not be used as its storage.
    internal static GameGridInventory? Warehouse()
    {
        var inventory = EmporiumEntry.Instance?.invElement;
        return inventory != null && inventory.Handle() != IntPtr.Zero ? inventory : null;
    }
    internal void EjectOnce(GameItem machine)
    {
        if (MachineAccess.IsFeedDispenser(machine)) return;
        if (!Ready || !IsCurrentRun() || !MachineAccess.Eligible(machine)) return;
        int id = machine.GetUniqueID();
        if (!_ejectBusy.Add(id)) return;
        try
        {
            if (!EjectEnabled(machine)) { Status[id] = PsModI18n.I18n.Get("ejection_paused"); return; }
            if (NativeModuleApi.FindInstalled(machine, Content.Ejector).Count == 0) { Status[id] = PsModI18n.I18n.Get("ejector_module_not_installed"); return; }
            if (!MachineAccess.TryInventory(machine, out var inventory) || inventory == null) return;
            string? run = _run; int expectedSlot = _slot, generation = _loadGeneration; IntPtr store = PlayerStore.Instance!.Handle();
            bool Current() => CurrentOutput(machine, inventory.Output, run, expectedSlot, store, generation);
            if (inventory.Output.TryCast<GameGridInventory>() is { } output)
            {
                int moved = 0;
                foreach (int instance in output.childItems.ToArray().Where(item => item != null).Select(item => item.GetUniqueID()))
                {
                    if (!Current()) break;
                    var item = output.childItems.ToArray().FirstOrDefault(item => item != null && item.GetUniqueID() == instance &&
                        item.parentInventory?.Handle() == output.Handle() && item.unitCount > 0);
                    if (item == null || !RemovableSource(item)) continue;
                    var result = EjectItem(machine, item, Current);
                    moved = checked(moved + result.Moved);
                    if (result.Indeterminate)
                    { Status[id] = PsModI18n.I18n.Get("ejection_could_not_be_verified") + result.Status; return; }
                }
                Status[id] = output.childItems.Count == 0 ? PsModI18n.I18n.Get("outputs_ejected") :
                    moved > 0 ? PsModI18n.I18n.Get("some_outputs_ejected_the_rest_remain") : PsModI18n.I18n.Get("no_room_in_the_resource_network_nearby");
            }
            else if (inventory.Output.TryCast<GameSlotInventory>()?.childItem is { } vessel && NativeLiquidApi.IsContainer(vessel))
            {
                var before = NativeLiquidApi.Capture(vessel);
                if (before == null || before.TotalParts == 0) { Status[id] = PsModI18n.I18n.Get("output_container_is_empty"); return; }
                string waiting = PurifierCollectionStatus(machine);
                if (waiting.Length > 0) { Status[id] = waiting; return; }
                if (!RemovableSource(vessel)) { Status[id] = PsModI18n.I18n.Get("output_container_is_unavailable_outputs_remain"); return; }
                var purifier = PurifierEjection(machine, vessel, before);
                bool VesselCurrent() => Current() && vessel.parentInventory?.Handle() == inventory.Output.Handle() &&
                    inventory.Output.TryCast<GameSlotInventory>()?.childItem?.Handle() == vessel.Handle();
                var result = EjectLiquid(machine, vessel, before.TotalParts, VesselCurrent);
                if (result.Indeterminate)
                { Status[id] = PsModI18n.I18n.Get("liquid_transfer_could_not_be_verified") + result.Status; return; }
                if (!VesselCurrent()) return;
                RecordPurifierEjection(machine, vessel, before, purifier, result.PartsMoved);
                Status[id] = NativeLiquidApi.Capture(vessel)?.TotalParts == 0 ? PsModI18n.I18n.Get("liquid_ejected_the_output_vessel_remains") :
                    result.PartsMoved > 0 ? PsModI18n.I18n.Get("some_liquid_ejected_the_rest_remains") : PsModI18n.I18n.Get("no_compatible_vessel_or_room_in_the");
            }
            else if (inventory.Output.TryCast<GameSlotInventory>()?.childItem is { } item && RemovableSource(item))
            {
                var result = EjectItem(machine, item, Current);
                if (result.Indeterminate) { Status[id] = PsModI18n.I18n.Get("ejection_could_not_be_verified") + result.Status; return; }
                Status[id] = result.Moved > 0 ? PsModI18n.I18n.Get("outputs_ejected") : PsModI18n.I18n.Get("no_room_in_the_resource_network_nearby");
            }
            else Status[id] = machine.identifier == "evaporator"
                ? PsModI18n.I18n.Get("no_distillate_vessel_native_evaporation_may_continue")
                : PsModI18n.I18n.Get("no_output_yet_waiting_for_native_processing");
        }
        catch (Exception ex) { Status[id] = PsModI18n.I18n.Get("ejection_failed") + ex.Message; }
        finally
        {
            _ejectBusy.Remove(id); EjectStatus[id] = Status.GetValueOrDefault(id) ?? "";
            NotifyMachinePresentation(machine);
        }
    }
    private bool CurrentOutput(GameItem machine, GameInventory output, string? run, int slot, IntPtr storePointer, int generation) =>
        Ready && _loadGeneration == generation && IsCurrentRun() && _run == run && _slot == slot && PlayerStore.Instance?.Handle() == storePointer &&
        MachineAccess.Eligible(machine) && EjectEnabled(machine) &&
        NativeModuleApi.FindInstalled(machine, Content.Ejector).Count > 0 &&
        MachineAccess.TryInventory(machine, out var inventory) && inventory?.Output.Handle() == output.Handle();
    private void Reset()
    { unchecked { _loadGeneration++; } ResetNetworks(); Ready = false; State = new(); _json = _run = null; _slot = -1;
        _fillBusy.Clear(); _ejectBusy.Clear(); _restockLiquidTransfers.Clear(); _consumption.Clear(); _machinePresentation.Clear();
        _machineResultPresentation.Clear(); Status.Clear(); FillStatus.Clear(); EjectStatus.Clear(); _cursor = 0; _nextScan = 0; }
    public void Dispose() { foreach (var lease in _leases) lease.Dispose(); _leases.Clear(); Reset(); }
}
