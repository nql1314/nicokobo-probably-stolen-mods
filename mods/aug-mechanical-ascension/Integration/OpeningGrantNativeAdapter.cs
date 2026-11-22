using AugMechanicalAscension.Domain;
using AugMechanicalAscension.Persistence;
using AugMechanicalAscension.Services;

namespace AugMechanicalAscension.Integration;

internal sealed record OpeningNativeItemSpec(string Kind, string ItemId, int Units);
internal enum OpeningCaptureStatus
{
    Ready, Unavailable, IdentityMismatch, UnverifiedBoundary,
    InvalidManifest, BlueprintMissing
}
internal sealed record OpeningCaptureResult(OpeningCaptureStatus Status,
    OpeningNativeSnapshot? Snapshot = null, string? Error = null);

// The opening records its own deliveries. Perk items are outside the grant.
internal static class OpeningGrantNativeAdapter
{
    // Directory presence is checked without allocating extra item instances.
    internal static OpeningCaptureResult Capture(PlayerStore? store,
        EmporiumEntry? entry, RunState? state, OpeningNativeItemSpec[]? specs,
        bool initialStateReady, bool playerDestinationVerified)
    {
        if (store == null || entry == null || state == null || specs == null)
            return new(OpeningCaptureStatus.Unavailable);
        if (!initialStateReady || !playerDestinationVerified)
            return new(OpeningCaptureStatus.UnverifiedBoundary);
        if (specs.Length != 7 ||
            specs[0] is not { Kind: "neuralModule", ItemId: NeuralModuleRules.ItemId, Units: AugNumbers.OpeningItems.NeuralInterfaceCount } ||
            specs[1] is not { Kind: "materialRefiner", ItemId: OpeningJournalRules.MaterialRefinerId, Units: AugNumbers.OpeningItems.MaterialRefinerCount } ||
            specs[2] is not { Kind: "battery", ItemId: "energy_credit", Units: AugNumbers.OpeningItems.BatteryCount } ||
            specs[3] is not { Kind: "medical", ItemId: "topical_bandage_item", Units: AugNumbers.OpeningItems.MedicalCount } ||
            specs[4] is not { Kind: "electronic", ItemId: "common_electronic", Units: AugNumbers.OpeningItems.ElectronicCount } ||
            specs[5] is not { Kind: "scrapMetal", ItemId: "scrap_metal", Units: AugNumbers.OpeningItems.ScrapMetalCount } ||
            specs[6] is not { Kind: "materialSign", ItemId: "sign_material", Units: AugNumbers.OpeningItems.MaterialSignCount })
            return new(OpeningCaptureStatus.InvalidManifest);
        try
        {
            if (!Guid.TryParse(state.GameRunId, out var boundRun) ||
                !Guid.TryParse(store.runID, out var nativeRun) ||
                boundRun == Guid.Empty || nativeRun != boundRun ||
                store.saveSlotId != state.SaveSlotId ||
                StoreStation.GetDayCounter() != state.StartDay)
                return new(OpeningCaptureStatus.IdentityMismatch);
            var currentEntry = EmporiumEntry.Instance;
            if (currentEntry == null || NativeObject.Handle(currentEntry) == IntPtr.Zero ||
                NativeObject.Handle(entry) != NativeObject.Handle(currentEntry))
                return new(OpeningCaptureStatus.IdentityMismatch);
            var inventory = entry.invElement;
            if (inventory == null || NativeObject.Handle(inventory) == IntPtr.Zero)
                return new(OpeningCaptureStatus.Unavailable);
            var items = new OpeningItemSnapshot[specs.Length];
            for (int index = 0; index < specs.Length; index++)
            {
                var spec = specs[index];
                if (!DirectoryMaster.Has<GameItem>(spec.ItemId))
                    return new(OpeningCaptureStatus.BlueprintMissing);
                items[index] = new OpeningItemSnapshot(spec.Kind, spec.ItemId,
                    spec.Units, 0, true, true);
            }
            return new(OpeningCaptureStatus.Ready,
                new OpeningNativeSnapshot(state.GameRunId, state.SaveSlotId,
                    state.StartDay, true, true, items));
        }
        catch (Exception ex)
        {
            return new(OpeningCaptureStatus.Unavailable, Error: ex.GetType().Name);
        }
    }

    internal static OpeningGrantResult Apply(PlayerStore? store,
        EmporiumEntry? entry, BoundStart binding, RunState? state,
        OpeningGrantRequest request,
        SessionCoordinator.OpeningAttemptLease? lease)
    {
        if (store == null || entry == null || binding == null ||
            state == null || request == null)
            return new(OpeningGrantStatus.Unavailable);
        if (!OpeningJournalRules.MayStartGrantAttempt(state, request))
            return new(OpeningGrantStatus.InvalidPhase);
        if (lease == null)
            return new(OpeningGrantStatus.UnverifiedBoundary);
        try
        {
            if (!Guid.TryParse(binding.GameRunId, out var boundRun) ||
                boundRun == Guid.Empty || !Guid.TryParse(store.runID, out var nativeRun) ||
                nativeRun != boundRun || binding.SaveSlotId < 0 ||
                store.saveSlotId != binding.SaveSlotId ||
                !Guid.TryParse(state.GameRunId, out var stateRun) ||
                stateRun != boundRun || state.SaveSlotId != binding.SaveSlotId ||
                StoreStation.GetDayCounter() != state.StartDay)
                return new(OpeningGrantStatus.IdentityMismatch);
            var currentEntry = EmporiumEntry.Instance;
            if (currentEntry == null || NativeObject.Handle(currentEntry) == IntPtr.Zero ||
                NativeObject.Handle(entry) != NativeObject.Handle(currentEntry))
                return new(OpeningGrantStatus.IdentityMismatch);
            var inventory = entry.invElement;
            if (inventory == null || NativeObject.Handle(inventory) == IntPtr.Zero)
                return new(OpeningGrantStatus.Unavailable);
            // Consume before CanGrantOne: DirectoryMaster.Item can allocate a
            // native ID and fire a creation hook even when placement fails.
            if (!lease.TryConsumeGrant(state, request))
                return new(OpeningGrantStatus.UnverifiedBoundary);
            var refinerGrant = request.ItemId == "scrap_metal" ? state.Opening!.Grants[1] : null;
            var port = new NativePort(store, entry, inventory, boundRun,
                binding.SaveSlotId, state.StartDay, refinerGrant);
            var result = OpeningGrantTransaction.Apply(port, request);
            if (result.Status != OpeningGrantStatus.Applied ||
                request.ItemId is not (NeuralModuleRules.ItemId or
                    OpeningJournalRules.MaterialRefinerId or "energy_credit"))
                return result;
            int identity = port.LastGrantedItemId;
            string instanceId = $"native-item:{identity}";
            if (identity <= 0 || port.FindGrantedItem(request.ItemId, instanceId) == null ||
                request.ItemId == NeuralModuleRules.ItemId && port.ReadModuleIdentity(identity) == null)
                return result with { Status = OpeningGrantStatus.Indeterminate,
                    Error = "Granted item identity or ownership could not be observed" };
            return result with
            {
                NativeItemInstanceId = instanceId,
                OwnershipEvidenceId = $"main-inventory:{boundRun:D}:{binding.SaveSlotId}:{identity}"
            };
        }
        catch (Exception ex)
        {
            return new(OpeningGrantStatus.Unavailable, Error: ex.GetType().Name);
        }
    }

    internal static bool VerifyCompleted(EmporiumEntry? entry, RunState? state)
    {
        if (entry == null || state?.Opening is not
            { Phase: OpeningJournalRules.Applied } journal ||
            !OpeningJournalRules.Valid(state)) return false;
        try
        {
            var store = (PlayerStore.HasInstance ? PlayerStore.Instance : null);
            var currentEntry = EmporiumEntry.Instance;
            var inventory = entry.invElement;
            if (store == null || currentEntry == null || inventory == null ||
                NativeObject.Handle(currentEntry) != NativeObject.Handle(entry) ||
                !Guid.TryParse(state.GameRunId, out var run) ||
                store.runID != state.GameRunId ||
                store.saveSlotId != state.SaveSlotId ||
                StoreStation.GetDayCounter() != state.StartDay)
                return false;
            bool hasScrapGrant = journal.Grants.Any(grant => grant.Kind == "scrapMetal");
            var port = new NativePort(store, entry, inventory, run,
                state.SaveSlotId, state.StartDay, hasScrapGrant ? journal.Grants[1] : null);
            var battery = port.FindGrantedItem("energy_credit", journal.Grants[2].Result?.NativeItemInstanceId);
            if (battery == null || !NativePort.FullBattery(battery)) return false;
            if (hasScrapGrant &&
                port.ReadRefinerScrapUnits() != AugNumbers.OpeningItems.RefinerScrapMetalCount)
                return false;
            var module = journal.Grants[0];
            var moduleItem = port.FindGrantedItem(module.Request.ItemId, module.Result?.NativeItemInstanceId);
            return moduleItem != null && port.ReadModuleIdentity(moduleItem.uniqueId) != null &&
                module.Result?.OwnershipEvidenceId ==
                    $"main-inventory:{run:D}:{state.SaveSlotId}:{moduleItem.uniqueId}";
        }
        catch { return false; }
    }

    private sealed class NativePort(PlayerStore store, EmporiumEntry entry,
        GameGridInventory inventory, Guid runId, int slotId, int day,
        OpeningGrantRecord? refinerGrant = null)
        : IOpeningGrantPort
    {
        private GameItem? _preparedItem;
        private SlotMarker? _preparedSlot;
        private string? _preparedItemId;
        private GameGridInventory? _preparedDestination;
        private int? _deliveredUnits = 0;
        public int LastGrantedItemId { get; private set; }

        public int? ReadOwnedUnits(string itemId) => Current() ? _deliveredUnits : null;

        public int? ReadRefinerScrapUnits()
        {
            if (!Current()) return null;
            var input = RefinerInput();
            return input == null ? null : ReadDirectUnits(input, "scrap_metal");
        }

        private static int? ReadDirectUnits(GameGridInventory destination, string itemId)
        {
            var children = destination.childItems;
            if (children == null) return null;
            int total = 0;
            for (int index = 0; index < children.Count; index++)
            {
                var item = children[index];
                if (item == null) return null;
                if (item.identifier != itemId) continue;
                if (item.unitCount < 0 || !item.IsTag("IS_OWNED_TAG") ||
                    NativeObject.Handle(item.parentInventory) != NativeObject.Handle(destination))
                    return null;
                total = checked(total + item.unitCount);
            }
            return total;
        }

        private GameGridInventory? RefinerInput()
        {
            var refiner = FindGrantedItem(OpeningJournalRules.MaterialRefinerId,
                refinerGrant?.Result?.NativeItemInstanceId);
            if (refiner == null) return null;
            if (!OfficialMachineRuntimeApi.TryGetInventory(refiner, out var slots) ||
                slots?.ItemInput is not GameGridInventory input ||
                NativeObject.Handle(input) == IntPtr.Zero)
                throw new InvalidOperationException("Opening refiner input unavailable");
            return input;
        }

        public int? ReadModuleIdentity(int expectedId)
        {
            if (!Current() || inventory.childItems == null) return null;
            var children = inventory.childItems;
            return OpeningModuleIdentityRules.FindModule(ReadItems(), expectedId);

            IEnumerable<OpeningModuleIdentityItem?> ReadItems()
            {
                for (int index = 0; index < children.Count; index++)
                {
                    var item = children[index];
                    if (item == null) { yield return null; yield break; }
                    if (item.identifier != NeuralModuleRules.ItemId)
                    {
                        yield return new(item.identifier, item.uniqueId);
                        continue;
                    }
                    var shape = item.shape;
                    yield return new(item.identifier, item.uniqueId,
                        item.unitCount, item.IsTag("IS_OWNED_TAG"), item.unitValue,
                        shape?.width ?? 0, shape?.height ?? 0);
                }
            }
        }

        public GameItem? FindGrantedItem(string itemId, string? instanceId)
        {
            if (!Current() || inventory.childItems == null) return null;
            GameItem? match = null;
            var children = inventory.childItems;
            for (int index = 0; index < children.Count; index++)
            {
                var item = children[index];
                if (item == null) return null;
                // Older battery and refiner receipts omitted an ID and could
                // only have one matching item in the opening inventory.
                if (instanceId != null ? instanceId != $"native-item:{item.uniqueId}"
                    : item.identifier != itemId) continue;
                if (match != null || item.identifier != itemId || item.unitCount != 1 ||
                    !item.IsTag("IS_OWNED_TAG") ||
                    NativeObject.Handle(item.parentInventory) != NativeObject.Handle(inventory))
                    return null;
                match = item;
            }
            return match;
        }

        public bool? CanGrantOne(string itemId)
        {
            if (!Current() || _preparedItem != null ||
                !DirectoryMaster.Has<GameItem>(itemId)) return null;
            var item = CreateOne(itemId);
            if (item == null || !Current()) return null;
            var destination = itemId == "scrap_metal" &&
                _deliveredUnits < AugNumbers.OpeningItems.RefinerScrapMetalCount
                ? RefinerInput() : inventory;
            if (destination == null) return null;
            var slot = destination.TryFindOneValidInventorySlot(item, false);
            if (slot == null) return false;
            if (NativeObject.Handle(slot.targetItem?.parentInventory ?? slot.inventory) !=
                NativeObject.Handle(destination)) return false;
            _preparedItem = item;
            _preparedSlot = slot;
            _preparedItemId = itemId;
            _preparedDestination = destination;
            return true;
        }

        public void GrantOne(string itemId)
        {
            if (!Current() || _preparedItemId != itemId ||
                _preparedItem == null || _preparedSlot == null || _preparedDestination == null ||
                _deliveredUnits is not int delivered)
                throw new InvalidOperationException("Prepared native grant is unavailable");
            var item = _preparedItem;
            int instanceId = item.uniqueId;
            var slot = _preparedSlot;
            var destination = _preparedDestination;
            if (itemId == "scrap_metal" &&
                delivered < AugNumbers.OpeningItems.RefinerScrapMetalCount &&
                NativeObject.Handle(RefinerInput()) != NativeObject.Handle(destination))
                throw new InvalidOperationException("Opening refiner changed before grant");
            _preparedItem = null;
            _preparedSlot = null;
            _preparedItemId = null;
            _preparedDestination = null;
            var placed = slot.targetItem ?? item;
            int expectedUnits = (slot.targetItem?.unitCount ?? 0) + 1;
            _deliveredUnits = null;
            if (slot.TryAcceptOnce(1) != 1 || !Current() ||
                NativeObject.Handle(placed.parentInventory) != NativeObject.Handle(destination) ||
                placed.unitCount != expectedUnits || !placed.IsTag("IS_OWNED_TAG"))
                throw new InvalidOperationException("Native grant destination did not gain one owned unit");
            if (itemId == "energy_credit" && !FullBattery(item))
                throw new InvalidOperationException("New battery is not at 100/100 energy");
            LastGrantedItemId = instanceId;
            _deliveredUnits = delivered + 1;
        }

        private bool Current()
        {
            var currentStore = (PlayerStore.HasInstance ? PlayerStore.Instance : null);
            var currentEntry = EmporiumEntry.Instance;
            var currentInventory = entry.invElement;
            return currentStore != null && currentEntry != null &&
                currentInventory != null &&
                NativeObject.Handle(currentStore) == NativeObject.Handle(store) &&
                NativeObject.Handle(currentEntry) == NativeObject.Handle(entry) &&
                NativeObject.Handle(currentInventory) == NativeObject.Handle(inventory) &&
                Guid.TryParse(store.runID, out var observedRun) &&
                observedRun == runId && store.saveSlotId == slotId &&
                StoreStation.GetDayCounter() == day;
        }

        private static GameItem? CreateOne(string itemId)
        {
            var item = DirectoryMaster.Item(itemId, true);
            return item != null && item.identifier == itemId &&
                item.unitCount == 1 && item.IsTag("IS_OWNED_TAG") &&
                (itemId != "energy_credit" || FullBattery(item))
                ? item : null;
        }

        internal static bool FullBattery(GameItem item)
        {
            if (!item.IsTag("power_source_item")) return false;
            var energy = item.GetTagReadonly("power_source_item_energy");
            var maximum = item.GetTagReadonly("power_source_item_max_energy");
            return energy != null && maximum != null &&
                energy.valueInt == 100 && maximum.valueInt == 100;
        }
    }
}
