using AugMechanicalAscension.Domain;
namespace AugMechanicalAscension.Integration;

internal enum DebugItemSpawnStatus
{
    Placed, IdentityMismatch, InventoryUnavailable, InventoryFull, Failed,
    Unconfirmed
}

internal sealed record DebugItemSpawnResult(DebugItemSpawnStatus Status,
    string? Error = null);

internal sealed record DebugCyberneticFillResult(DebugItemSpawnStatus Status,
    int Added = 0, int AlreadyOwned = 0, string? Error = null);

internal enum DebugNativeActionStatus
{
    Applied, IdentityMismatch, ClientActive, DayAdvanceBlocked, Unconfirmed, Failed
}

internal sealed record DebugNativeActionResult(DebugNativeActionStatus Status,
    int Before = 0, int After = 0, string? Error = null);

internal sealed class DebugRuntimeController(NativeSessionBridge bridge,
    Action<string> log)
{
    private const string ImmunivaxId = "large_purple_injector";

    internal DebugNativeActionResult AddMoney()
    {
        try
        {
            var store = CurrentStore();
            if (store == null) return new(DebugNativeActionStatus.IdentityMismatch);
            int before = store.playerCash;
            int expected = checked(before + 10000);
            store.ModCash(10000, false);
            int after = store.playerCash;
            log($"[Debug] add-money; cash={before}->{after}");
            return new(after == expected ? DebugNativeActionStatus.Applied
                : DebugNativeActionStatus.Unconfirmed, before, after);
        }
        catch (Exception ex)
        {
            log($"[Debug] add-money; failed={ex.GetType().Name}: {ex.Message}");
            return new(DebugNativeActionStatus.Failed, Error: ex.Message);
        }
    }

    internal DebugNativeActionResult AdvanceDay()
    {
        try
        {
            var store = CurrentStore();
            if (store == null) return new(DebugNativeActionStatus.IdentityMismatch);
            if (store.currentClientInstance?.GetClientBlueprint() != null)
                return new(DebugNativeActionStatus.ClientActive);
            int before = StoreStation.GetDayCounter();
            int expected = checked(before + 1);
            if (store.storeState == PlayerStore.StoreState.MORNING) store.BeginDay();
            if (NativeObject.Handle(CurrentStore()) != NativeObject.Handle(store))
                return new(DebugNativeActionStatus.IdentityMismatch);
            if (store.storeState == PlayerStore.StoreState.OPEN) store.EndDay();
            // EndDay can be intercepted by a due story decision or a run ending.
            // Only finish the night after the native shop really closes.
            if (NativeObject.Handle(CurrentStore()) != NativeObject.Handle(store))
                return new(DebugNativeActionStatus.IdentityMismatch);
            if (store.storeState != PlayerStore.StoreState.CLOSED)
                return new(DebugNativeActionStatus.DayAdvanceBlocked);
            store.EndNight();
            int after = StoreStation.GetDayCounter();
            log($"[Debug] advance-day; day={before}->{after}; state={store.storeState}");
            return new(after == expected ? DebugNativeActionStatus.Applied
                : DebugNativeActionStatus.DayAdvanceBlocked, before, after);
        }
        catch (Exception ex)
        {
            log($"[Debug] advance-day; failed={ex.GetType().Name}: {ex.Message}");
            return new(DebugNativeActionStatus.Failed, Error: ex.Message);
        }
    }

    private PlayerStore? CurrentStore()
    {
        var state = bridge.CurrentSession?.State;
        var store = (PlayerStore.HasInstance ? PlayerStore.Instance : null);
        return state != null && store != null && NativeObject.Handle(store) != IntPtr.Zero &&
            NativeObject.Handle(store) == bridge.CurrentStorePointer &&
            store.runID == state.GameRunId && store.saveSlotId == state.SaveSlotId &&
            state.InitPhase == "complete" && state.FailureReason == "none"
                ? store : null;
    }

    internal DebugCyberneticFillResult FillMissingCybernetics()
    {
        int added = 0, alreadyOwned = 0;
        GameItem? item = null;
        bool placementAttempted = false;
        try
        {
            var view = bridge.CurrentSession;
            var store = (PlayerStore.HasInstance ? PlayerStore.Instance : null);
            if (view == null || store == null || NativeObject.Handle(store) == IntPtr.Zero ||
                NativeObject.Handle(store) != bridge.CurrentStorePointer ||
                store.runID != view.State.GameRunId ||
                store.saveSlotId != view.State.SaveSlotId ||
                view.State.InitPhase != "complete" ||
                view.State.FailureReason != "none")
                return new(DebugItemSpawnStatus.IdentityMismatch);
            var inventory = EmporiumEntry.Instance?.invElement;
            if (inventory == null || NativeObject.Handle(inventory) == IntPtr.Zero)
                return new(DebugItemSpawnStatus.InventoryUnavailable);

            // Fresh run traversal includes owned cybernetics inside bags and machines.
            // Detached factory previews and NPC stock must not count as owned items.
            var ownedIds = OfficialInventoryApi.CaptureRunItems(store)
                .Where(current => current.unitCount > 0 && OfficialInventoryApi.IsPlayerOwned(current))
                .Select(current => current.identifier).ToHashSet(StringComparer.Ordinal);
            // Use Nico Workshop's twelve proof items, including Synthesis cybernetics.
            foreach (var itemId in WorkshopAchievements.Definitions
                .Where(definition => definition.ProofItemId != null)
                .Select(definition => definition.ProofItemId!))
            {
                if (ownedIds.Contains(itemId))
                {
                    alreadyOwned++;
                    continue;
                }
                if (!DirectoryMaster.Has<GameItem>(itemId))
                    return new(DebugItemSpawnStatus.Failed, added, alreadyOwned,
                        $"Cybernetic blueprint unavailable: {itemId}");
                item = DirectoryMaster.Item(itemId, true);
                if (item == null || NativeObject.Handle(item) == IntPtr.Zero ||
                    item.identifier != itemId || item.unitCount != 1 ||
                    item.parentInventory != null || !OfficialInventoryApi.IsPlayerOwned(item))
                    return new(DebugItemSpawnStatus.Failed, added, alreadyOwned,
                        $"Invalid cybernetic factory result: {itemId}");
                var slot = inventory.TryFindOneValidInventorySlot(item, false);
                if (slot == null || NativeObject.Handle(slot) == IntPtr.Zero)
                    return new(DebugItemSpawnStatus.InventoryFull, added, alreadyOwned);
                if (bridge.CurrentSession?.State.RunId != view.State.RunId ||
                    NativeObject.Handle((PlayerStore.HasInstance ? PlayerStore.Instance : null)) != NativeObject.Handle(store) ||
                    NativeObject.Handle(EmporiumEntry.Instance?.invElement) != NativeObject.Handle(inventory))
                    return new(DebugItemSpawnStatus.IdentityMismatch, added, alreadyOwned);

                placementAttempted = true;
                int accepted = slot.TryAcceptOnce(1);
                GC.KeepAlive(item);
                bool confirmed = NativeObject.Handle(item.parentInventory) == NativeObject.Handle(inventory) &&
                    item.unitCount == 1 && OfficialInventoryApi.IsPlayerOwned(item) &&
                    OfficialInventoryApi.CaptureRunItems(store).Any(current => NativeObject.Handle(current) == NativeObject.Handle(item));
                log($"[Debug] fill-cybernetics; item={itemId}; accepted={accepted}; confirmed={confirmed}");
                if (accepted != 1 || !confirmed)
                    return new(accepted == 0 && item.parentInventory == null
                        ? DebugItemSpawnStatus.Failed : DebugItemSpawnStatus.Unconfirmed,
                        added, alreadyOwned, $"Placement not confirmed: {itemId}");
                added++;
                item = null;
                placementAttempted = false;
            }
            return new(DebugItemSpawnStatus.Placed, added, alreadyOwned);
        }
        catch (Exception ex)
        {
            log($"[Debug] fill-cybernetics; added={added}; failed={ex.GetType().Name}: {ex.Message}");
            return new(placementAttempted ? DebugItemSpawnStatus.Unconfirmed
                : DebugItemSpawnStatus.Failed, added, alreadyOwned, ex.Message);
        }
        finally
        {
            try
            {
                if (item != null && NativeObject.Handle(item) != IntPtr.Zero && item.parentInventory == null)
                    item.Destroy();
            }
            catch (Exception ex)
            {
                log($"[WARN] [Debug] cybernetic cleanup failed; {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    internal RuntimeRevisionSaveResult SetHealth(int requested)
        => Apply(state => WithVitals(state,
            Math.Clamp(requested, 0, state.ConfigSnapshot.MaxHealth), state.Exposure),
            "health", requested);

    internal RuntimeRevisionSaveResult SetExposure(int requested)
        => Apply(state => WithVitals(state, state.Health,
            Math.Clamp(requested, 0, state.ConfigSnapshot.MaxExposure)),
            "exposure", requested);

    private static RunState WithVitals(RunState state, int health, int exposure)
        => state with
        {
            Revision = checked(state.Revision + 1),
            Health = health,
            Exposure = exposure,
            ExposureDeadlineDay = exposure == state.ConfigSnapshot.MaxExposure
                ? state.LastObservedDay : null,
            FailureReason = health == 0 ? "bodyCollapse"
                : exposure == state.ConfigSnapshot.MaxExposure ? "exposure" : "none"
        };

    internal DebugItemSpawnResult SpawnGenuineImmunivax()
    {
        GameItem? item = null;
        bool placementAttempted = false;
        try
        {
            var view = bridge.CurrentSession;
            var store = (PlayerStore.HasInstance ? PlayerStore.Instance : null);
            if (view == null || store == null || NativeObject.Handle(store) == IntPtr.Zero ||
                NativeObject.Handle(store) != bridge.CurrentStorePointer ||
                store.runID != view.State.GameRunId ||
                store.saveSlotId != view.State.SaveSlotId ||
                view.State.InitPhase != "complete" ||
                view.State.FailureReason != "none")
                return new(DebugItemSpawnStatus.IdentityMismatch);
            var inventory = EmporiumEntry.Instance?.invElement;
            if (inventory == null || NativeObject.Handle(inventory) == IntPtr.Zero)
                return new(DebugItemSpawnStatus.InventoryUnavailable);
            if (!DirectoryMaster.Has<GameItem>(ImmunivaxId))
                return new(DebugItemSpawnStatus.Failed, "Immunivax blueprint unavailable");

            // This native factory sets both the real injector state and its
            // appraisal feature to genuine; a display-only condition is insufficient.
            var genuine = ItemConditionList.CreateGenuineInjector();
            if (genuine == null || NativeObject.Handle(genuine) == IntPtr.Zero ||
                string.IsNullOrWhiteSpace(genuine.identifier) ||
                string.IsNullOrWhiteSpace(genuine.category))
                return new(DebugItemSpawnStatus.Failed, "Genuine condition unavailable");
            int before = CountGenuineOwnedUnits(inventory, genuine);
            item = PreBuiltItemHelper.CreateRealGenuineInjector();
            if (!IsGenuineOwnedImmunivax(item, genuine) || item!.unitCount != 1)
                return new(DebugItemSpawnStatus.Failed, "Native factory returned an invalid injector");
            var slot = inventory.TryFindOneValidInventorySlot(item, false);
            if (slot == null || NativeObject.Handle(slot) == IntPtr.Zero)
                return new(DebugItemSpawnStatus.InventoryFull);
            if (bridge.CurrentSession?.State.RunId != view.State.RunId ||
                NativeObject.Handle((PlayerStore.HasInstance ? PlayerStore.Instance : null)) != NativeObject.Handle(store) ||
                NativeObject.Handle(EmporiumEntry.Instance?.invElement) != NativeObject.Handle(inventory))
                return new(DebugItemSpawnStatus.IdentityMismatch);

            placementAttempted = true;
            int accepted = slot.TryAcceptOnce(1);
            GC.KeepAlive(item);
            int after = CountGenuineOwnedUnits(inventory, genuine);
            log($"[Debug] spawn={ImmunivaxId}; accepted={accepted}; genuineOwned={before}->{after}");
            if (accepted == 1 && after == checked(before + 1))
                return new(DebugItemSpawnStatus.Placed);
            return new(accepted == 0 && after == before
                    ? DebugItemSpawnStatus.Failed : DebugItemSpawnStatus.Unconfirmed,
                "Native placement did not confirm one genuine owned unit");
        }
        catch (Exception ex)
        {
            log($"[Debug] spawn={ImmunivaxId}; failed={ex.GetType().Name}: {ex.Message}");
            return new(placementAttempted ? DebugItemSpawnStatus.Unconfirmed
                : DebugItemSpawnStatus.Failed, ex.Message);
        }
        finally
        {
            // Never force an item into a full inventory or destroy a placed item.
            try
            {
                if (item != null && NativeObject.Handle(item) != IntPtr.Zero &&
                    item.parentInventory == null) item.Destroy();
            }
            catch (Exception ex)
            {
                log($"[WARN] [Debug] injector cleanup failed; {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    private static int CountGenuineOwnedUnits(GameGridInventory inventory,
        ItemCondition genuine)
    {
        var items = inventory.childItems
            ?? throw new InvalidOperationException("Main inventory children unavailable");
        int count = 0;
        foreach (var item in items)
            if (IsGenuineOwnedImmunivax(item, genuine))
                count = checked(count + item.unitCount);
        return count;
    }

    private static bool IsGenuineOwnedImmunivax(GameItem? item,
        ItemCondition genuine)
    {
        if (item == null || NativeObject.Handle(item) == IntPtr.Zero ||
            item.identifier != ImmunivaxId || item.unitCount < 1 ||
            !item.IsTag("IS_OWNED_TAG") || item.itemFeatures == null) return false;
        int matching = 0;
        foreach (var feature in item.itemFeatures)
        {
            if (feature == null || !feature.useCondition) continue;
            var real = feature.realCondition;
            if (real == null || NativeObject.Handle(real) == IntPtr.Zero ||
                real.category != genuine.category) continue;
            if (real.identifier != genuine.identifier) return false;
            matching++;
        }
        return matching == 1;
    }

    private RuntimeRevisionSaveResult Apply(Func<RunState, RunState> mutate,
        string field, int requested)
    {
        var view = bridge.CurrentSession;
        var store = (PlayerStore.HasInstance ? PlayerStore.Instance : null);
        if (view == null || store == null || NativeObject.Handle(store) == IntPtr.Zero ||
            NativeObject.Handle(store) != bridge.CurrentStorePointer ||
            store.runID != view.State.GameRunId ||
            store.saveSlotId != view.State.SaveSlotId ||
            view.State.InitPhase != "complete")
        {
            log($"[Debug] set={field}; requested={requested}; " +
                $"rejected=identity-or-phase; phase={view?.State.InitPhase ?? "none"}");
            return new(RuntimeRevisionSaveStatus.IdentityMismatch,
                Error: "No active Aug session");
        }
        int day = StoreStation.GetDayCounter();
        RunState next;
        try { next = mutate(view.State); }
        catch (Exception ex) when (ex is ArithmeticException or
                                   ArgumentException)
        {
            return new(RuntimeRevisionSaveStatus.WriteRejected,
                Error: ex.Message);
        }
        var result = RuntimeRevisionNativeSaveAdapter.ApplyDebug(store,
            bridge.Sessions, view.State, next, day);
        log($"[Debug] set={field}; requested={requested}; " +
            $"status={result.Status}; revision={view.State.Revision}->{next.Revision}");
        return result;
    }
}
