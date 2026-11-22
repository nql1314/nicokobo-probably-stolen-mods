using HarmonyLib;
using AugMechanicalAscension.Domain;
using AugMechanicalAscension.Persistence;
using UnityEngine;

namespace AugMechanicalAscension.Integration;

// Aug owns live goals and delivery. Forge owns the shared achievement graph.
// Rewards and their receipts are saved together by the native overnight save.
internal static class NicoWorkshop
{
    internal static bool ForkBusy => _busy;
    internal static void Uninstall()
    { foreach(var lease in Leases)lease.Dispose();Leases.Clear();RemoveHooks(LifecycleHooks);_installed=false;Reset(); }
    private const string OwnerId = "nicokobo.aug";
    private const string TabId = "nicokobo.aug.workshop";
    private static readonly HarmonyLib.Harmony LifecycleHooks = new(OwnerId + ".workshop.lifecycle");
    private sealed record WorkshopHook(Type Type, string Name, Type[] Arguments,
        string? Prefix = null, string? Postfix = null);
    private static readonly List<IDisposable> Leases = [];
    private static readonly (string Key, int Count)[] Groups =
        [("aug.perception", 2), ("aug.actions_warning", 3), ("aug.machine_upgrades", 3),
         ("aug.identity_survival", 4), ("aug.ascension", 1)];
    private static readonly (float X, float Y)[] Positions =
    // Four prerequisite chains radiate around the final achievement. Their
    // outside-to-center order follows the existing claim dependencies.
    [(-.72f, -.85f), (-.36f, -.50f),
     (.94f, -.85f), (.72f, -.56f), (.50f, -.27f),
     (.94f, .85f), (.72f, .56f), (.50f, .27f),
     (-.98f, .84f), (-.78f, .52f), (-.60f, .20f), (-.44f, -.12f), (0f, 0f)];
    private static NativeSessionBridge? _bridge;
    private static PlayerStore? _store;
    private static WorkshopAchievementState? _state, _confirmed;
    private static string? _json, _confirmedJson;
    private static string? _boundRunId;
    private static int _boundSlotId = -1;
    private static Action<string>? _log;
    private static bool _installed, _busy, _blocked, _observing;
    private static string _message = "", _lastFailure = "";
    private static DateTime _nextUpdate;
    private static IReadOnlyDictionary<string, int> _proofCounts = new Dictionary<string, int>();

    internal static bool Install(HarmonyLib.Harmony harmony, NativeSessionBridge bridge, Action<string> log)
    {
        if (_installed) return true;
        _bridge = bridge; _log = log;
        try
        {
            if (!OfficialInventoryPlacementApi.IsAvailable)
                throw new InvalidOperationException("Forge whole-reward placement preview is unavailable");
            if (Groups.Sum(x => x.Count) != WorkshopAchievements.Definitions.Length ||
                Positions.Length != WorkshopAchievements.Definitions.Length)
                throw new InvalidOperationException("Achievement groups do not match the catalog");
            InstallHooks(LifecycleHooks,
            [
                new(typeof(PlayerStore), nameof(PlayerStore.StartNewGame), [], nameof(ResetPrefix)),
                new(typeof(PlayerStore), nameof(PlayerStore.LoadGame), [], nameof(ResetPrefix)),
                new(typeof(PlayerStore), nameof(PlayerStore.SaveGame), [], null, nameof(SavePostfix))
            ]);
            Leases.Add(OfficialLifecycleApi.Subscribe(OwnerId, OwnerId + ".workshop.before_load",
                OfficialLifecyclePhase.BeforeLoad, _ => Reset()));
            var tab = OfficialWorkshopApi.RegisterChain(OwnerId,
                TabId,
                Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "aug.start.name", "zh"),
                Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "aug.start.name", "en"),
                Snapshot,
                Claim,
                Opened, () => AugText.Get("aug.start.name"));
            if (tab.Status is not (SubmitStatus.Accepted or SubmitStatus.AlreadyPresent))
                throw new InvalidOperationException($"Forge workshop={tab.Status}: {tab.Reason}");
            _installed = true;
            Log("Achievements registered; cybernetics=12; final=机械飞升; proof=nonconsuming; payment=none");
            return true;
        }
        catch (Exception ex)
        {
            _installed = false;
            foreach (var lease in Leases) lease.Dispose();
            Leases.Clear();
            RemoveHooks(LifecycleHooks);
            Warn("Achievement provider unavailable: " + ex.Message);
            return false;
        }
    }

    private static void InstallHooks(HarmonyLib.Harmony harmony, IReadOnlyList<WorkshopHook> hooks)
    {
        try
        {
            var resolved = hooks.Select(hook =>
            {
                var method = NativeHookSet.Find(hook.Type, hook.Name, hook.Arguments, declaredOnly:true);
                if (!OfficialHookApi.TryValidateNativeTarget(method, out var reason))
                    throw new InvalidOperationException(reason);
                HarmonyMethod? Callback(string? name) => name == null ? null :
                    new(AccessTools.DeclaredMethod(typeof(NicoWorkshop), name)
                        ?? throw new MissingMethodException(typeof(NicoWorkshop).Name, name));
                return (Method: method, Prefix: Callback(hook.Prefix), Postfix: Callback(hook.Postfix));
            }).ToArray();
            foreach (var hook in resolved) harmony.Patch(hook.Method, hook.Prefix, hook.Postfix);
        }
        catch { RemoveHooks(harmony); throw; }
    }
    private static void RemoveHooks(HarmonyLib.Harmony harmony)
    {
        try { harmony.UnpatchSelf(); }
        catch (Exception ex) { Warn("Workshop hook rollback failed: " + ex.Message); }
    }
    internal static bool HasMechanicalAscension(RunState run)
    {
        var store = SafeStore(run);
        if (!_installed || store == null) return false;
        // Morning-report hooks may run before the first workshop update after load.
        if (NativeObject.Handle(_store) != NativeObject.Handle(store) || _boundRunId != store.runID ||
            _boundSlotId != store.saveSlotId)
            Bind(store);
        return !_blocked && WorkshopAchievements.IsMechanicallyAscended(
            _confirmed, run.GameRunId, run.SaveSlotId);
    }

    internal static int StoryClaimedCount(RunState run)
    {
        HasMechanicalAscension(run); // Bind/validate the same confirmed journal used by survival.
        return !_blocked && _confirmed != null && _confirmed.RunId == run.GameRunId &&
            _confirmed.SlotId == run.SaveSlotId ? WorkshopAchievements.ClaimedCybernetics(_confirmed) : 0;
    }

    internal static int DeliveredNeuralInterfaces(PlayerStore store)
    {
        if (!StartDefinition.MatchesNativeStartType((int)store.startType)) return 0;
        // Read the current staged journal without binding, saving or recovering
        // the workshop from inside an item factory. Decode also checks run/slot.
        var read = OfficialRunDataApi.Read(store, OwnerId, WorkshopAchievements.SaveKey);
        if (read.Status == RunDataStatus.Missing) return 0;
        if (read.Status != RunDataStatus.Present)
            throw new InvalidOperationException("Achievement price record unavailable: " + read.Reason);
        var state = WorkshopAchievements.Decode(read.Json!, store.runID, store.saveSlotId);
        return WorkshopAchievements.DeliveredNeuralInterfaces(state);
    }

    internal static void Update()
    {
        if (!_installed || _busy || DateTime.UtcNow < _nextUpdate) return;
        _nextUpdate = DateTime.UtcNow.AddSeconds(1);
        try
        {
            var run = CurrentRun();
            var store = SafeStore(run);
            if (run == null || store == null)
            {
                if (_store != null) Reset();
                return;
            }
            if (NativeObject.Handle(_store) != NativeObject.Handle(store) || _boundRunId != store.runID || _boundSlotId != store.saveSlotId)
                Bind(store);
        }
        catch (Exception ex) { Warn("Achievement update deferred: " + ex.Message); }
    }

    private static void Bind(PlayerStore store)
    {
        Reset();
        _store = store;
        _boundRunId = store.runID; _boundSlotId = store.saveSlotId;
        var read = OfficialRunDataApi.Read(store, OwnerId, WorkshopAchievements.SaveKey);
        if (read.Status == RunDataStatus.Missing)
            _state = new() { RunId = store.runID, SlotId = store.saveSlotId };
        else if (read.Status == RunDataStatus.Present)
        {
            try { _state = WorkshopAchievements.Decode(read.Json!, store.runID, store.saveSlotId); _json = read.Json; }
            catch (Exception ex) { Block(AugText.Get("aug.workshop.read_failed", ex.Message)); return; }
            if (_state.Pending == null) AcceptLive();
            else Confirm();
        }
        else { Block(AugText.Get("aug.workshop.read_unavailable", read.Reason ?? "")); return; }
        if (_json == null) Stage(_state!);
    }
    private static bool Current() => _store != null && _state != null &&
        NativeObject.Handle(SafeStore(CurrentRun())) == NativeObject.Handle(_store) && _store.runID == _state.RunId &&
        _store.saveSlotId == _state.SlotId;
    private static void ResetPrefix() => Reset();
    private static void Reset()
    {
        _store = null; _state = null; _confirmed = null; _json = null; _confirmedJson = null;
        _boundRunId = null; _boundSlotId = -1;
        _busy = false; _blocked = false; _observing = false;
        _proofCounts = new Dictionary<string, int>();
        _message = ""; _lastFailure = "";
    }
    private static void Observe()
    {
        if (!Current() || _blocked || _busy || _observing || _state!.Pending != null) return;
        _observing = true;
        try
        {
            var items = OfficialInventoryApi.CaptureRunItems(_store!).Select(item =>
                new WorkshopOwnedItem(item.identifier, item.uniqueId, item.unitCount, OfficialInventoryApi.IsPlayerOwned(item))).ToArray();
            _proofCounts = items.Where(item => item.Owned && item.Units > 0 && WorkshopAchievements.Relevant(item.ItemId))
                .GroupBy(item => item.ItemId, StringComparer.Ordinal).ToDictionary(group => group.Key,
                    group => (int)Math.Min(int.MaxValue, group.Sum(item => (long)item.Units)), StringComparer.Ordinal);
            var next = WorkshopAchievements.Latch(_state!, items, StoreStation.GetDayCounter());
            if (next.Completed.Count != _state!.Completed.Count) Stage(next);
        }
        finally { _observing = false; }
    }
    private static void Opened()
    {
        var store = SafeStore(CurrentRun());
        if (!_installed || store == null) return;
        if (NativeObject.Handle(_store) != NativeObject.Handle(store) || _boundRunId != store.runID || _boundSlotId != store.saveSlotId)
            Bind(store);
        if (!Current() || _blocked) return;
        if (_state!.Pending != null) Recover();
        Observe();
    }

    private static bool Stage(WorkshopAchievementState next)
    {
        if (_blocked || _store == null) return false;
        string json = WorkshopAchievements.Encode(next);
        if (json == _json) { _state = next; return true; }
        var write = OfficialRunDataApi.Stage(_store, OwnerId, WorkshopAchievements.SaveKey,
            next.RunId, next.SlotId, _json, json);
        if (write.Status != RunDataStatus.StagedInMemory)
        {
            Block(AugText.Get("aug.workshop.conflict", write.Reason ?? ""));
            return false;
        }
        _state = next; _json = json;
        if (next.Pending == null) AcceptLive();
        return true;
    }
    private static void SavePostfix(PlayerStore __instance)
    {
        if (Current() && NativeObject.Handle(_store) == NativeObject.Handle(__instance) && _json != null) Confirm();
    }
    private static bool AcceptLive()
    {
        if (!Current() || _blocked || _json == null || _state!.Pending != null) return false;
        _confirmedJson = _json;
        _confirmed = _state;
        return true;
    }
    private static bool Confirm()
    {
        if (!Current() || _json == null) return false;
        _confirmedJson = _json;
        _confirmed = _state;
        _lastFailure = "";
        return true;
    }
    private static void Recover()
    {
        if (!Current() || _blocked || _state!.Pending == null || CurrentRun()?.HasPendingExternalOperation != false) return;
        // Pending manifests contain identities, never instructions to create replacement items.
        if (_json != _confirmedJson && !Confirm()) return;
        var items = OfficialInventoryApi.CaptureRunItems(_store!);
        if (!_state.Pending.Items.All(receipt => items.Any(item => item.identifier == receipt.ItemId &&
            item.uniqueId == receipt.UniqueId && item.unitCount >= receipt.Units && OfficialInventoryApi.IsPlayerOwned(item)))) return;
        if (Stage(WorkshopAchievements.Commit(_state)))
        {
            _message = "";
        }
    }
    internal static string DebugUnlockMechanicalAscension()
    {
        try
        {
            var run = CurrentRun();
            var store = SafeStore(run);
            if (!_installed || run == null || store == null)
                return AugText.Get("aug.no_active_aug_achievement_session");
            if (NativeObject.Handle(_store) != NativeObject.Handle(store) || _boundRunId != store.runID || _boundSlotId != store.saveSlotId)
                Bind(store);
            if (!Current() || _blocked || _busy || _state!.Pending != null || run.HasPendingExternalOperation)
                return AugText.Get("aug.achievement_record_unavailable_or_an_operation_is");
            if (WorkshopAchievements.IsMechanicallyAscended(_state, run.GameRunId, run.SaveSlotId))
                return AugText.Get("aug.mechanical_ascension_is_already_unlocked");
            if (!Stage(WorkshopAchievements.UnlockAscensionForDebug(_state)))
                return AugText.Get("aug.achievement_update_failed_check_the_aug_log");
            Log("debug-unlock=mechanical-ascension; items=0; save=deferred-until-overnight");
            return _message = AugText.Get("aug.mechanical_ascension_unlocked_no_items_granted_saves");
        }
        catch (Exception ex)
        {
            Warn("Debug ascension unlock failed: " + ex.Message);
            return AugText.Get("aug.achievement_update_failed_check_the_aug_log");
        }
    }

    private static void Claim(string id)
    {
        if (!Current() || _blocked || _busy || _state!.Pending != null || CurrentRun()?.HasPendingExternalOperation != false) return;
        if (_json != _confirmedJson && !AcceptLive()) return;
        if (_confirmed == null || !WorkshopAchievements.CanClaim(_confirmed, id) ||
            !WorkshopAchievements.CanClaim(_state!, id)) return;
        Deliver(WorkshopAchievements.Definition(id)!);
    }

    private static void Deliver(WorkshopAchievement definition)
    {
        var inventory = EmporiumEntry.Instance?.invElement;
        if (!Current() || inventory == null || NativeObject.Handle(inventory) == IntPtr.Zero) return;
        var products = new List<GameItem>();
        WorkshopRewardReceipt[] receipts = [];
        var before = _state!;
        int cashBefore = _store!.playerCash;
        bool placementAttempted = false, journaled = false;
        _busy = true;
        try
        {
            var owned = OfficialInventoryApi.CaptureRunItems(_store);
            int neuralCount = definition.Rewards.Where(reward => reward.ItemId == NeuralModuleRules.ItemId).Sum(reward => reward.Count);
            if (neuralCount > 0 &&
                owned.Where(x => x.identifier == NeuralModuleRules.ItemId && x.unitCount > 0 && OfficialInventoryApi.IsPlayerOwned(x))
                    .GroupBy(x => x.uniqueId).Sum(x => x.First().unitCount) + neuralCount > NeuralModuleRules.MaximumOwned)
            {
                _message = AugText.Get("aug.the_whole_reward_would_exceed_the_25");
                return;
            }
            var ownedUids = owned.Select(x => x.uniqueId).ToHashSet();
            foreach (var reward in definition.Rewards)
            for (int i = 0; i < reward.Count; i++)
            {
                var item = OfficialItemApi.Create(reward.ItemId, true);
                if (item == null || NativeObject.Handle(item) == IntPtr.Zero || item.parentInventory != null ||
                    products.Any(x => NativeObject.Handle(x) == NativeObject.Handle(item)))
                    throw new InvalidOperationException("Reward factory contract mismatch: " + reward.ItemId);
                products.Add(item);
                if (item.identifier != reward.ItemId || item.unitCount != 1 || item.GetUniqueID() <= 0 ||
                    ownedUids.Contains(item.uniqueId) || products.Take(products.Count - 1).Any(x => x.uniqueId == item.uniqueId))
                    throw new InvalidOperationException("Reward identity contract mismatch");
                if (WorkshopAchievements.IsBattery(item.identifier))
                {
                    int capacity = item.GetTagReadonly("power_source_item_max_energy")?.valueInt ?? 0;
                    if (capacity <= 0 || !item.IsTag("power_source_item")) throw new InvalidOperationException("Battery capacity unavailable");
                    PowerHelper.SetPowerSourceAt(item, capacity);
                }
            }
            receipts = products.Select(x => new WorkshopRewardReceipt(x.identifier, x.uniqueId, x.unitCount,
                WorkshopAchievements.IsBattery(x.identifier) ? x.GetTagReadonly("power_source_item_max_energy")?.valueInt : null)).ToArray();
            var next = WorkshopAchievements.Prepare(before, definition.Id, cashBefore, receipts);
            var plan = OfficialInventoryPlacementApi.PlanWholeGrid(inventory, products);
            if (plan == null)
            {
                _message = AugText.Get("aug.make_room_in_the_main_inventory_for");
                return;
            }
            if (!Current() || _store.playerCash != cashBefore || _state != before ||
                CurrentRun()?.HasPendingExternalOperation != false)
                throw new InvalidOperationException("Run or transaction changed during preflight");
            placementAttempted = true;
            if (!WorkshopAchievementDelivery.Apply(inventory, plan) || _store.playerCash != cashBefore)
                throw new InvalidOperationException("Reward placement readback failed");
            if (!Stage(next)) throw new InvalidOperationException("Delivery journal could not be staged");
            journaled = true;
            // Placement was read back above. Commit the live receipt now; the
            // whole inventory and receipt reach disk at the native night save.
            if (!Stage(WorkshopAchievements.Commit(_state!))) return;
            _message = "";
            Log($"reward={definition.Id}; units={products.Count}; cashUnchanged={_store.playerCash == cashBefore}; save=deferred-until-overnight");
        }
        catch (Exception ex)
        {
            _message = AugText.Get("aug.reward_temporarily_unavailable_check_the_aug_log");
            Warn("Reward delivery deferred: " + ex.Message);
        }
        finally
        {
            if (!journaled && !WorkshopAchievementDelivery.Recall(products))
            {
                if (placementAttempted && receipts.Length == definition.Rewards.Sum(reward => reward.Count))
                    try { Stage(WorkshopAchievements.Prepare(before, definition.Id, cashBefore, receipts)); }
                    catch (Exception ex) { Warn("Interrupted reward journal unavailable: " + ex.Message); }
                Block(AugText.Get("aug.workshop.reward_interrupted"));
            }
            _busy = false;
        }
    }

    private static OfficialWorkshopSnapshot? Snapshot()
    {
        if (!_installed) return null;
        var store = (PlayerStore.HasInstance ? PlayerStore.Instance : null);
        // The page can open before InitialSave assigns the run ID. Eligibility
        // below still requires the initialized session before enabling rewards.
        if (store == null || NativeObject.Handle(store) == IntPtr.Zero ||
            EmporiumEntry.Instance?.invElement == null) return null;
        bool eligible = NativeObject.Handle(SafeStore(CurrentRun())) == NativeObject.Handle(store) && NativeObject.Handle(_store) == NativeObject.Handle(store);
        string unavailable = StartDefinition.MatchesNativeStartType((int)store.startType)
            ? (AugText.Get("aug.aug_run_data_is_not_ready"))
            : (AugText.Get("aug.available_in_the_aug_start"));
        var proofCounts = _proofCounts;
        var entries = WorkshopAchievements.Definitions.Select((definition, index) =>
        {
            bool completed = eligible && _confirmed != null && WorkshopAchievements.Completed(_confirmed, definition.Id);
            bool debugUnlocked = eligible && definition.Id == WorkshopAchievements.AscensionId &&
                _confirmed?.DebugAscensionUnlocked == true;
            bool claimed = debugUnlocked || (eligible && _confirmed?.Claimed.Contains(definition.Id) == true);
            bool prerequisite = eligible && _confirmed != null && WorkshopAchievements.PrerequisitesMet(_confirmed, definition.Id);
            bool pending = _state?.Pending != null || _json != _confirmedJson || CurrentRun()?.HasPendingExternalOperation == true;
            bool proofAvailable = definition.ProofItemId == null || completed || OfficialItemApi.Exists(definition.ProofItemId);
            bool rewardAvailable = definition.Rewards.All(reward => OfficialItemApi.Exists(reward.ItemId));
            string progress = debugUnlocked ? (AugText.Get("aug.unlocked_via_debug_no_reward_items_granted"))
                : definition.Id == WorkshopAchievements.AscensionId
                ? (AugText.Get("aug.cybernetic_rewards_claimed")) + $"{(eligible && _confirmed != null ? WorkshopAchievements.ClaimedCybernetics(_confirmed) : 0)} / 12"
                : completed ? (AugText.Get("aug.completion_recorded")) : (AugText.Get("aug.required_item")) + Title(definition) + " ×1";
            if (definition.ProofItemId is { } proofId)
            {
                int owned = proofCounts.TryGetValue(proofId, out int count) ? count : 0;
                string provider = CyberneticProductDefinitions.Products.Any(product => product.Id == proofId)
                    ? AugText.Get("aug.mechanical_ascension")
                    : AugText.Get("aug.synthesis_expansion");
                progress += AugText.Get("aug.owned_when_this_page_opened_provider", owned, provider);
                progress += AugText.Get("aug.craft_at_the_universal_manufacturing_terminal_k05");
            }
            string state = !eligible ? unavailable : _blocked ? (AugText.Get("aug.achievement_record_unavailable"))
                : claimed && definition.Id == WorkshopAchievements.AscensionId ? (AugText.Get("aug.mechanical_ascension_completed"))
                : claimed ? (AugText.Get("aug.claimed"))
                : !proofAvailable ? (AugText.Get("aug.required_item_provider_unavailable"))
                : !rewardAvailable ? (AugText.Get("aug.reward_item_unavailable"))
                : completed && !prerequisite ? (AugText.Get("aug.completed_claim_prerequisite_reward_first"))
                : completed ? (AugText.Get("aug.ready_to_claim"))
                : (AugText.Get("aug.required_item_not_yet_owned"));
            if (eligible && pending && !claimed) state += AugText.Get("aug.save_pending");
            string requirement = definition.Id == WorkshopAchievements.AscensionId
                ? (AugText.Get("aug.claim_all_twelve_cybernetic_achievement_rewards"))
                : definition.Prerequisite == null ? (AugText.Get("aug.no_prerequisite_achievements"))
                : (AugText.Get("aug.claim_prerequisite_reward")) + Title(WorkshopAchievements.Definition(definition.Prerequisite)!);
            var position = Positions[index];
            return new OfficialWorkshopEntry(definition.Id, Title(definition),
                definition.Id == WorkshopAchievements.AscensionId ? (AugText.Get("aug.final_achievement"))
                    : (AugText.Get("aug.cybernetic_achievement")),
                definition.Id == WorkshopAchievements.AscensionId
                    ? (AugText.Get("aug.claim_all_twelve_cybernetic_achievement_rewards_in"))
                    : (AugText.Get("aug.opening_this_page_checks_for_the_corresponding")),
                requirement, progress,
                debugUnlocked ? (AugText.Get("aug.unlocked")) : claimed ? (AugText.Get("aug.claimed_2")) : completed ? (AugText.Get("aug.claim"))
                    : definition.Id == WorkshopAchievements.AscensionId
                        ? $"{(eligible && _confirmed != null ? WorkshopAchievements.ClaimedCybernetics(_confirmed) : 0)} / 12" : "0 / 1",
                state, debugUnlocked ? (AugText.Get("aug.unlocked_2")) : claimed ? (AugText.Get("aug.claimed")) : (AugText.Get("aug.claim_reward")),
                claimed, prerequisite, eligible && !_blocked && !_busy && !pending && proofAvailable && rewardAvailable &&
                    _confirmed != null && WorkshopAchievements.CanClaim(_confirmed, definition.Id) &&
                    _state != null && WorkshopAchievements.CanClaim(_state, definition.Id))
            {
                Completed = completed,
                Dependencies = definition.Id == WorkshopAchievements.AscensionId ? WorkshopAchievements.BranchEnds
                    : definition.Prerequisite == null ? [] : [definition.Prerequisite],
                Conditions = definition.ProofItemId == null ? [] : [new OfficialWorkshopRequirement(
                    OfficialWorkshopResourceKind.Item, definition.ProofItemId, 1, false, completed) { DisplayName = Title(definition) }],
                Rewards = definition.Rewards.Select(reward => new OfficialWorkshopReward(OfficialWorkshopResourceKind.Item, reward.ItemId, reward.Count)
                    { DisplayName = ItemLabel(reward.ItemId) }).ToArray(),
                GraphX = position.X, GraphY = position.Y
            };
        }).ToArray();
        return new(store.playerCash, !Nicokobo.ModUtilities.ModText.IsChinese, Groups.Select(group =>
        {
            string name = AugText.Get(group.Key);
            return new OfficialWorkshopGroup(name, name, group.Count);
        }).ToArray(), entries, eligible ? _message : unavailable);
    }
    private static RunState? CurrentRun()
    {
        var state = _bridge?.CurrentSession?.State;
        return state is { InitPhase: "complete", FailureReason: "none" } && SafeStore(state) != null ? state : null;
    }
    private static PlayerStore? SafeStore(RunState? state)
    {
        if (_bridge == null || state == null) return null;
        try
        {
            // The lazy Instance getter constructs a store and clears the native
            // main-menu flag. Background lookups must only read an existing store.
            var store = (PlayerStore.HasInstance ? PlayerStore.Instance : null);
            return store != null && NativeObject.Handle(store) != IntPtr.Zero &&
                NativeObject.Handle(store) == _bridge.CurrentStorePointer && store.runID == state.GameRunId &&
                store.saveSlotId == state.SaveSlotId && StartDefinition.MatchesNativeStartType((int)store.startType) ? store : null;
        }
        catch { return null; }
    }
    private static string Title(WorkshopAchievement definition) => definition.ProofItemId == null
        ? AugText.Get("aug.mechanical_ascension") : ItemLabel(definition.ProofItemId);
    private static string ItemLabel(string id) => id.StartsWith("nicokobo.", StringComparison.Ordinal)
        ? AugText.Get(id + ".name") : Nicokobo.ModUtilities.NativeItemText.Name(id, id);
    private static void Block(string message) { _blocked = true; _message = message; Warn(message); }
    private static void Log(string message) { try { _log?.Invoke("[INFO] [Aug/Workshop] " + message); } catch { } }
    private static void Warn(string message)
    {
        if (_lastFailure == message) return;
        _lastFailure = message;
        try { _log?.Invoke("[WARN] [Aug/Workshop] " + message); } catch { }
    }
}
