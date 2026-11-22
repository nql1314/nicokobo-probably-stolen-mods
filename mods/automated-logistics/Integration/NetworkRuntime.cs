using static PsModI18n.I18n;
using Newtonsoft.Json;
using Nicokobo.AutomatedLogistics.Domain;

namespace Nicokobo.AutomatedLogistics.Integration;

internal sealed partial class AutomatedLogisticsRuntime
{
    private GameItem? _powerRoot;
    internal string NetworkError { get; private set; } = "";
    private static bool OwnedRoot(GameItem? item, string id) => item != null && item.Handle() != IntPtr.Zero &&
        item.identifier == id && item.parentInventory != null && NativeInventoryApi.IsPlayerOwned(item);
    private bool BindNetworkRoots(IReadOnlyList<GameItem> items)
    {
        GameItem? Find(int id, string type)
        {
            var candidates = items.Where(item => OwnedRoot(item, type)).OrderBy(item => item.GetUniqueID()).ToArray();
            return candidates.FirstOrDefault(item => item.GetUniqueID() == id) ?? candidates.FirstOrDefault();
        }
        var resource = State.ResourcesUnlocked ? Find(State.ResourceInstanceId, Content.ResourceDevice) : null;
        var water = State.WaterUnlocked ? Find(State.WaterInstanceId, Content.WaterDevice) : null;
        var power = State.Power.Unlocked ? Find(State.Power.InstanceId, Content.PowerDevice) : null;
        bool changed = !ReferenceEquals(_resourceRoot, resource) || !ReferenceEquals(_waterRoot, water) || !ReferenceEquals(_powerRoot, power);
        _resourceRoot = resource; _waterRoot = water; _powerRoot = power;
        // Missing devices leave the receipt intact; a replacement changes only
        // the physical endpoint, never the grant, contents or shared ledger.
        if (resource != null) State.ResourceInstanceId = resource.GetUniqueID();
        if (water != null) State.WaterInstanceId = water.GetUniqueID();
        if (power != null) State.Power.InstanceId = power.GetUniqueID();
        return changed;
    }
    private void RebindNetworks(bool refresh = false)
    {
        if (State.WaterInstanceId > 0) State.WaterUnlocked = true; // Existing free tank remains available.
        bool changed = BindNetworkRoots(Items());
        if (refresh && !changed) return;
        // Root absence is transient. Keep it separate from a failed resource
        // transaction, whose stop flag must survive background rebinding.
        if (!refresh) _resourceMembersReady = !State.ResourcesUnlocked || State.Resources != null;
        if ((State.Resources != null) && !ApplyResourceCapacity())
            throw new InvalidDataException(PsModI18n.I18n.Get("void_storage_capacity_could_not_be_restored"));
        if (State.Power.Unlocked || State.Power.Height > 0)
        {
            State.Power.Width = Math.Max(State.Power.Width, AutomatedLogisticsNumbers.Parameters.PowerWidth);
            State.Power.Height = Math.Max(State.Power.Height, AutomatedLogisticsNumbers.Parameters.PowerHeight);
            foreach (var device in NetworkDevices(Content.PowerDevice))
                device.contentWindow!.childElement!.Cast<GameGridInventory>().SetShape(State.Power.Width, State.Power.Height);
        }
        BeginTotalStatistics();
        if (changed) { Save(); NetworkPresentationChanged?.Invoke(); }
    }
    internal GameItem[] NetworkDevices(string id) => Ready && IsCurrentRun()
        ? Items().Where(item => OwnedRoot(item, id)).DistinctBy(item => item.GetUniqueID()).ToArray() : [];
    internal GameGridInventory? PowerInventory() => State.Power.Unlocked && OwnedRoot(_powerRoot, Content.PowerDevice)
        ? _powerRoot!.contentWindow?.childElement?.TryCast<GameGridInventory>() : null;
    private sealed class StateChange(AutomatedLogisticsRuntime runtime)
    {
        private readonly string _beforeState = JsonConvert.SerializeObject(runtime.State);
        private readonly string? _beforeData = runtime._json, _run = runtime._run;
        private readonly int _slot = runtime._slot;
        private readonly int _generation = runtime._loadGeneration;
        private readonly IntPtr _store = PlayerStore.Instance?.Handle() ?? IntPtr.Zero;
        private string? _after;
        internal void Commit(Action change)
        {
            if (!Current()) throw new InvalidOperationException(PsModI18n.I18n.Get("the_run_changed_2"));
            change(); _after = JsonConvert.SerializeObject(runtime.State); runtime.Save();
        }
        internal bool Current() => runtime._loadGeneration == _generation && runtime.IsCurrentRun() && runtime._run == _run &&
            runtime._slot == _slot && PlayerStore.Instance?.Handle() == _store;
        internal bool Verify() => Current() && _after != null && NativeRunDataApi.Read(PlayerStore.Instance, Content.Owner, SaveKey).Json == _after;
        internal bool Restore()
        {
            if (!Current()) return false;
            var observed = NativeRunDataApi.Read(PlayerStore.Instance, Content.Owner, SaveKey);
            bool old = _beforeData == null ? observed.Status == RunDataStatus.Missing : observed.Status == RunDataStatus.Present && observed.Json == _beforeData;
            if (!old)
            {
                if (_after == null || observed.Status != RunDataStatus.Present || observed.Json != _after) return false;
                string previous = _beforeData ?? _beforeState;
                NativeRunDataApi.Stage(PlayerStore.Instance, Content.Owner, SaveKey, _run!, _slot, observed.Json, previous);
                if (!Current() || NativeRunDataApi.Read(PlayerStore.Instance, Content.Owner, SaveKey).Json != previous) return false;
                runtime._json = previous;
            }
            else runtime._json = _beforeData;
            runtime.State = JsonConvert.DeserializeObject<AutomatedLogisticsState>(_beforeState)!;
            runtime.Ready = true; runtime.Error = ""; return true;
        }
    }
    private void ResetNetworks()
    {
        _powerStatsDirty = false; _chargeObservations.Clear(); _powerLedgerBusy = false; _powerCollectionPending = true; _powerTransferFailure = null;
        _powerRoot = null; NetworkError = "";
        _resourceUnlockAttempted = _powerUnlockAttempted = false;
        _resourceRoot = _waterRoot = null; ResourceUpgradeStatus = PsModI18n.I18n.Get("hold_alt_and_drag_in_a_matching");
        _resourceMembersReady = false; _resourceTransfers = 0; _resourceTransferGeneration++;
        _resourceTransferItems.Clear(); _resourceTransferContainers.Clear();
        _powerNightDay = null;
    }
}
