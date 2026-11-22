using static PsModI18n.I18n;
using Newtonsoft.Json;
using Nicokobo.AutomatedLogistics.Domain;

namespace Nicokobo.AutomatedLogistics.Integration;

internal sealed partial class AutomatedLogisticsRuntime
{
    // InitialSave also serves history forks; only real openings grant here.
    // OnNewGameStarted runs before HandleSkipIntro and HandleNewGamePerk add
    // warehouse items. Place logistics gifts after native items and before the first save.
    internal void GrantOpeningPowerGrid(PlayerStore store)
    {
        if (PlayerStore.Instance != store || NativeRunForkApi.IsForking(store) || !StartingPerk.IsMakeshiftReactor()) return;
        var data = store.modData ?? throw new InvalidOperationException("Opening logistics data is unavailable");
        var state = ReadOpeningState(data.TryGetValue(SaveKey, out var json) ? json : null);
        if (state.Power.Unlocked) return;

        var grid = NativeItemApi.Create(Content.PowerDevice);
        bool granted = false;
        try
        {
            var entry = EmporiumEntry.Instance;
            foreach (var inventory in new[] { Warehouse(), entry?.backInvinvElement, entry?.showcaseElement })
            {
                if (inventory == null) continue;
                var slot = inventory.TryFindOneValidInventorySlot(grid, false);
                if (slot == null || !slot.IsValid() || slot.targetItem != null || slot.TryAcceptOnce(1) != 1) continue;
                if (grid.parentInventory != inventory || !OwnedRoot(grid, Content.PowerDevice)) break;

                state.Power = CreateUnlockedPowerState(state.Power, grid);
                data[SaveKey] = JsonConvert.SerializeObject(state);
                granted = true;
                break;
            }
        }
        finally
        {
            if (!granted) Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(grid);
        }
    }

    // Existing runs receive their pending terminal in the background scan below.
    internal void GrantOpeningTerminal(PlayerStore store)
    {
        if (store == null || PlayerStore.Instance?.Handle() != store.Handle()) return;
        if (NativeRunForkApi.IsForking(store)) return;
        var data = store.modData ?? throw new InvalidOperationException("Terminal grant receipts unavailable");
        bool present = data.ContainsKey(SaveKey);
        string? before = present ? data[SaveKey] : null;
        var state = ReadOpeningState(before);
        if (state.TerminalGranted) return;
        bool owned = NativeInventoryApi.CaptureRunItems(store).Any(item => item.identifier == Content.Terminal && NativeInventoryApi.IsPlayerOwned(item));
        state.TerminalGranted = true;
        string after = JsonConvert.SerializeObject(state);
        // InitialSave has not allocated the native identity yet. Record in the
        // same owned modData payload; the normal pending-grant path reads it too.
        if (PlayerStore.Instance?.Handle() != store.Handle() || data.ContainsKey(SaveKey) != present ||
            present && data[SaveKey] != before)
            throw new InvalidOperationException("Terminal grant source changed before commit");
        if (present) data[SaveKey] = after; else data.Add(SaveKey, after);
        if (data[SaveKey] != after) throw new InvalidOperationException("Terminal grant receipt readback failed");
        if (owned) return;
        GameItem? terminal = null;
        try
        {
            var inventory = Warehouse() ?? throw new InvalidOperationException(PsModI18n.I18n.Get("starting_warehouse_is_not_ready"));
            terminal = NativeItemApi.Create(Content.Terminal);
            var slot = inventory.TryFindOneValidInventorySlot(terminal, false);
            if (slot == null || !slot.IsValid() || slot.TryAcceptOnce(1) != 1 || terminal.parentInventory?.Handle() != inventory.Handle())
                throw new InvalidOperationException(PsModI18n.I18n.Get("starting_terminal_could_not_be_placed_in"));
            info?.Invoke(PsModI18n.I18n.Get("automatedlogistics_granted_starting_network_terminal_1"));
        }
        catch
        {
            if (terminal?.parentInventory == null && data.ContainsKey(SaveKey) && data[SaveKey] == after)
            { if (present) data[SaveKey] = before!; else data.Remove(SaveKey); }
            throw;
        }
        finally { if (terminal != null && terminal.parentInventory == null) terminal.Destroy(); }
    }

    private static AutomatedLogisticsState ReadOpeningState(string? json)
    {
        var state = json == null ? new AutomatedLogisticsState() : JsonConvert.DeserializeObject<AutomatedLogisticsState>(json);
        if (state?.IsValid() != true) throw new InvalidDataException("Opening logistics state is invalid");
        return state;
    }

    private void GrantPendingTerminal()
    {
        if (!Ready || !IsCurrentRun() || State.TerminalGranted || NativeInventoryApi.IsItemDragActive || _resourceTransfers != 0) return;
        bool existing = Items().Any(item => item.identifier == Content.Terminal && NativeInventoryApi.IsPlayerOwned(item));
        var change = new StateChange(this);
        var steps = new List<NativeTransactionStep>();
        if (!existing) steps.Add(PlaceDeviceStep(Content.Terminal, change, _ => { }, () => { }));
        steps.Add(new("terminal-grant-receipt", () =>
        {
            change.Commit(() => State.TerminalGranted = true);
            if (!change.Verify()) throw new InvalidOperationException(PsModI18n.I18n.Get("terminal_grant_receipt_could_not_be_verified"));
        }, change.Restore));
        var result = NativeTransactionApi.Run(steps);
        if (!result.Committed && !result.Restored && change.Current()) { Ready = false; Error = result.Status; }
        if (result.Committed && !existing)
            info?.Invoke(PsModI18n.I18n.Get("automatedlogistics_granted_missing_network_terminal_1"));
    }
}
