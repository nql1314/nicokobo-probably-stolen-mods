using static PsModI18n.I18n;

namespace Nicokobo.AutomatedLogistics.Integration;

internal sealed partial class AutomatedLogisticsRuntime
{
    internal Func<bool> FeedDispenserRestockingEnabled { get; init; } = () => true;

    private static int StoredFeedKcal(GameItem machine) => machine.GetTagReadonly(MachineFeedDispenser.STORED_KCAL_TAG)?.valueInt ?? 0;
    private static int FoodKcal(GameItem item) => item.IsTag("CALORIE_VALUE_TAG")
        ? item.GetTagReadonly("CALORIE_VALUE_TAG")?.valueInt ?? 0 : 0;
    private static bool IsDispenserFood(GameItem item) => item.unitCount > 0 && FoodKcal(item) > 0 &&
        !item.IsTag("ANIMAL_ITEM_TAG") && !AquacultureHelper.IsFish(item) && !NeedsManualRestock(item);

    internal void ObserveDispenserFood(GameItem food)
    {
        if (SaveManager.IsDecoding || !Ready || !IsCurrentRun() || !IsDispenserFood(food) ||
            food.parentInventory?.GetParentItem() is not { } machine || !MachineAccess.IsFeedDispenser(machine) ||
            !MachineAccess.Eligible(machine) ||
            MachineFeedDispenser.GetStorageGrid(machine)?.Handle() != food.parentInventory.Handle()) return;
        var ids = Settings(machine).FeedItemIds;
        if (ids.Contains(food.identifier, StringComparer.Ordinal)) return;
        ids.Add(food.identifier); SaveFeedSelection(machine, ids);
    }

    private void SaveFeedSelection(GameItem machine, List<string> ids)
    {
        var settings = Settings(machine); settings.FeedItemIds = ids;
        var change = new StateChange(this);
        var result = NativeTransactionApi.Run([new("feed-selection", () =>
        { change.Commit(() => State.Machines[settings.MachineId] = settings); if (!change.Verify()) throw new InvalidOperationException("Feed selection readback failed"); }, change.Restore)]);
        if (!result.Committed) throw new InvalidOperationException(PsModI18n.I18n.Get("feed_selection_was_not_saved") + result.Status);
        NotifyMachinePresentation(machine);
    }

    internal string RecordDispenserFoods(GameItem machine, bool clear = false)
    {
        EnsureLoaded();
        if (!Ready || !IsCurrentRun() || !MachineAccess.IsFeedDispenser(machine) || !MachineAccess.Eligible(machine) ||
            NativeInventoryApi.IsItemDragActive || MachineFeedDispenser.GetStorageGrid(machine) is not { } grid)
            return PsModI18n.I18n.Get("feed_dispenser_is_unavailable");
        var ids = clear ? new List<string>() : Settings(machine).FeedItemIds.Concat(grid.childItems
            .Where(IsDispenserFood).Select(food => food.identifier)).Distinct(StringComparer.Ordinal).ToList();
        SaveFeedSelection(machine, ids);
        return clear ? PsModI18n.I18n.Get("feed_selection_cleared_manually_insert_the_desired") :
            ids.Count == 0 ? PsModI18n.I18n.Get("insert_the_desired_foods_manually_before_recording") :
            PsModI18n.I18n.Get("recorded_food_types_insert_more_types_manually", ids.Count);
    }

    private MachineAutomationStatus FeedDispenserAutomationStatus(GameItem machine)
    {
        if (!FeedDispenserRestockingEnabled())
            return new(MachineAutomationSignal.Disabled, PsModI18n.I18n.Get("feed_dispenser_restocking_is_disabled_in_mod"));
        if (!FillEnabled(machine)) return new(MachineAutomationSignal.Disabled, PsModI18n.I18n.Get("feed_restocking_paused"));
        if (!MachineFeedDispenser.IsAutoBlendOn(machine))
            return new(MachineAutomationSignal.Disabled, PsModI18n.I18n.Get("enable_the_dispenser_s_native_auto_blend"));
        if (MachineFeedDispenser.GetStorageGrid(machine) is not { } grid || grid.IsInsertLocked() || grid.IsRemoveLocked())
            return new(MachineAutomationSignal.Shortage, PsModI18n.I18n.Get("food_intake_is_unavailable"));
        int remaining = MachineFeedDispenser.MAX_KCAL - StoredFeedKcal(machine);
        if (remaining <= 0) return new(MachineAutomationSignal.Enabled, PsModI18n.I18n.Get("feed_storage_is_full_restocking_resumes_after"));
        var ids = State.Machines.GetValueOrDefault(machine.GetUniqueID())?.FeedItemIds;
        if (ids == null || ids.Count == 0)
            return new(MachineAutomationSignal.Shortage, PsModI18n.I18n.Get("manually_insert_the_foods_to_restock_multiple"));
        var foods = grid.childItems.Where(food => ids.Contains(food.identifier) && IsDispenserFood(food) && RemovableSource(food))
            .Concat(RestockSources(machine, false).Select(source => source.Item).Where(food => ids.Contains(food.identifier) && IsDispenserFood(food))).ToArray();
        if (foods.Length == 0) return new(MachineAutomationSignal.Shortage, PsModI18n.I18n.Get("waiting_for_the_selected_foods"));
        return new(MachineAutomationSignal.Enabled, foods.Any(food => FoodKcal(food) <= remaining)
            ? PsModI18n.I18n.Get("restocking_repeatedly_within_remaining_calorie_capacity")
            : PsModI18n.I18n.Get("not_enough_capacity_for_one_food_item"));
    }

    private string FeedDispenserPlan(GameItem machine)
    {
        var ids = State.Machines.GetValueOrDefault(machine.GetUniqueID())?.FeedItemIds ?? [];
        var lines = new List<string>
        {
            PsModI18n.I18n.Get("feed_storage_kcal", StoredFeedKcal(machine), MachineFeedDispenser.MAX_KCAL),
            FeedDispenserAutomationStatus(machine).Description,
            PsModI18n.I18n.Get("no_logistics_module_required_with_native_auto"),
            PsModI18n.I18n.Get("only_manually_inserted_or_recorded_foods_are"),
            PsModI18n.I18n.Get("selected_foods") + (ids.Count == 0 ? PsModI18n.I18n.Get("none") : string.Join("、", ids.Select(MachineAccess.Name)))
        };
        foreach (var group in RestockSources(machine, false).Where(source => ids.Contains(source.Item.identifier) && IsDispenserFood(source.Item))
            .GroupBy(source => (source.Tier, source.Item.identifier)))
        {
            string place = group.Key.Tier switch { 0 => PsModI18n.I18n.Get("resource_network"), 1 => PsModI18n.I18n.Get("nearby_containers"), _ => PsModI18n.I18n.Get("warehouse") };
            string feed = MachineAccess.DisplayName(group.First().Item);
            lines.Add(PsModI18n.I18n.Get("feed_source", feed, group.Sum(source => (long)source.Item.unitCount), place));
        }
        return string.Join("\n", lines);
    }

    private int FillFeedDispenser(GameItem machine)
    {
        int id = machine.GetUniqueID(), moved = 0;
        Status[id] = FeedDispenserAutomationStatus(machine).Description;
        if (!FeedDispenserRestockingEnabled() || !MachineFeedDispenser.IsAutoBlendOn(machine) || MachineFeedDispenser.GetStorageGrid(machine) is not { } grid ||
            grid.IsInsertLocked() || grid.IsRemoveLocked() ||
            State.Machines.GetValueOrDefault(id)?.FeedItemIds is not { Count: > 0 } ids) return 0;
        int generation = _loadGeneration; string? run = _run; int slot = _slot; var store = PlayerStore.Instance;
        bool Current() => Ready && generation == _loadGeneration && IsCurrentRun() && _run == run && _slot == slot &&
            PlayerStore.Instance == store && MachineAccess.Eligible(machine) && FeedDispenserRestockingEnabled() && FillEnabled(machine) &&
            MachineFeedDispenser.IsAutoBlendOn(machine) && !NativeInventoryApi.IsItemDragActive &&
            MachineFeedDispenser.GetStorageGrid(machine)?.Handle() == grid.Handle() && !grid.IsInsertLocked() && !grid.IsRemoveLocked() &&
            ReferenceEquals(State.Machines.GetValueOrDefault(id)?.FeedItemIds, ids);
        var rejected = new HashSet<IntPtr>();
        while (Current())
        {
            int before = StoredFeedKcal(machine), remaining = MachineFeedDispenser.MAX_KCAL - before;
            if (remaining <= 0) break;
            bool Fits(GameItem food) => !rejected.Contains(food.Handle()) && ids.Contains(food.identifier) &&
                IsDispenserFood(food) && FoodKcal(food) <= remaining;
            var food = grid.childItems.FirstOrDefault(item => Fits(item) && RemovableSource(item));
            if (food != null)
            {
                int calories = FoodKcal(food);
                // Use native conversion so meat species, feed categories and prions
                // remain native data. Never synthesize kcal or feeding results.
                if (!MachineFeedDispenser.TryBlendItem(machine, food)) { rejected.Add(food.Handle()); continue; }
                if (!Current()) return moved;
                if (StoredFeedKcal(machine) != before + calories)
                { Status[id] = PsModI18n.I18n.Get("blending_could_not_be_verified_check_feed"); return moved; }
                continue;
            }
            var source = RestockSources(machine, false).FirstOrDefault(value => Fits(value.Item));
            if (source == null || !SourceCurrent(machine, source, false)) break;
            using var lease = source.Member ? BeginResourceSourceTransfer(source.Item, false, source.Tier == 0) : null;
            if (source.Member && lease == null) break;
            if (!Current() || !SourceCurrent(machine, source, false)) break;
            // The native insertion callback excludes the newly inserted item
            // until its deferred pass. Verify the physical move, then blend it.
            var result = NativeItemTransferApi.Move(source.Item, grid, 1);
            moved += result.Moved;
            if (!Current()) return moved;
            if (result.Indeterminate)
            { Status[id] = PsModI18n.I18n.Get("feed_transfer_could_not_be_verified_check"); return moved; }
            if (result.Moved == 0) rejected.Add(source.Item.Handle());
        }
        if (Current()) Status[id] = FeedDispenserAutomationStatus(machine).Description;
        return moved;
    }
}
