using HarmonyLib;
using Nicokobo.ModUtilities;
using System.Reflection;
using UnityEngine;

namespace Nicokobo.ContentInterop;

// Built-in provider for native gameplay goals. It owns no content Mod upgrades.
internal static class NativeWorkshop
{
    internal const string OwnerId = "nicokobo.forge";
    internal const string ChainId = "nicokobo.forge.native_workshop";
    // Achievement art describes the goal independently of its reward items.
    internal static Sprite? ReadIcon(string id)
    {
        string? art = id switch
        {
            "pure_water_jug" => "water",
            "rat_800g" => "rat",
            "quality_overload" => "metal",
            "master_brewer" => "wine",
            "scavenger_toolset" => "tools",
            "max_scav_scanner" => "scanner",
            "wealthy_store" => "wealth",
            "store_attractiveness" => "store",
            "faction_max_rep" => "renown",
            AchievementRules.VictoryNode => "victory",
            _ => null
        };
        return art == null ? null : CustomUIManager.Instance.GetSprite(OwnerId + ":achievement_" + art);
    }
    private static readonly (string Key, int Count)[] Groups =
        [("forge.industry", 4), ("forge.collection_gear", 2), ("forge.business_reputation", 3),
         ("forge.store_victory_2", 1)];
    private static PlayerStore? _store;
    private static AchievementState? _state, _confirmed;
    private static string? _json, _confirmedJson;
    private static Action<string>? _log;
    private static bool _enabled, _bindRequested, _busy, _blocked, _observing;
    private static DateTime _nextUpdate;
    private static string _message = "", _lastFailure = "";
    private static Dictionary<string, AchievementEvidence> _evidence = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> Unavailable = new(StringComparer.Ordinal);

    private static readonly HarmonyLib.Harmony Hooks = new(ChainId);
    internal sealed record Hook(MethodInfo Target, string? Prefix = null, string? Postfix = null);

    internal static void Install()
    {
        if (_enabled) return;
        _log = Debug.LogWarning;
        var hooks = new List<Hook>
        {
            new(AccessTools.DeclaredMethod(typeof(PlayerStore), nameof(PlayerStore.StartNewGame), []), nameof(Reset)),
            new(AccessTools.DeclaredMethod(typeof(PlayerStore), nameof(PlayerStore.LoadGame), []), nameof(Reset)),
            new(AccessTools.DeclaredMethod(typeof(PlayerStore), nameof(PlayerStore.InitialSave), []), Postfix: nameof(InitialPostfix)),
            new(AccessTools.DeclaredMethod(typeof(PlayerStore), nameof(PlayerStore.OnStoreOwned), []), Postfix: nameof(StoreOwnedPostfix))
        };
        try
        {
            InstallHooks(Hooks, hooks, name => new HarmonyMethod(typeof(NativeWorkshop), name));
            NativeVictoryBudget.Install();
            var tab = OfficialWorkshopApi.RegisterChain(OwnerId,
                ChainId,
                Nicokobo.ModUtilities.ModText.GetFor("nicokobo.forge", "forge.base_game", "zh"),
                Nicokobo.ModUtilities.ModText.GetFor("nicokobo.forge", "forge.base_game", "en"),
                Snapshot,
                Claim,
                Opened, () => ForgeText.Get("forge.base_game"));
            if (tab.Status is not (SubmitStatus.Accepted or SubmitStatus.AlreadyPresent)) throw new InvalidOperationException(tab.Reason);
            OfficialWorkshopApi.SetDefaultChain(ChainId);
            ModHook.OnGameLoadedInit += Reset;
            ModHook.OnGameLoadedNormal += Loaded;
            _enabled = _bindRequested = true;
        }
        catch { Uninstall(); throw; }
    }

    internal static void InstallHooks(HarmonyLib.Harmony harmony, IEnumerable<Hook> hooks, Func<string, HarmonyMethod> callback)
    {
        var resolved = hooks.Select(hook =>
        {
            if (!OfficialStoryHooks.ValidateTarget(hook.Target, out var reason)) throw new InvalidOperationException(reason);
            return (hook.Target, Prefix: hook.Prefix == null ? null : callback(hook.Prefix),
                Postfix: hook.Postfix == null ? null : callback(hook.Postfix));
        }).ToArray();
        foreach (var hook in resolved) harmony.Patch(hook.Target, hook.Prefix, hook.Postfix);
    }

    internal static void Uninstall()
    {
        ModHook.OnGameLoadedInit -= Reset;
        ModHook.OnGameLoadedNormal -= Loaded;
        Hooks.UnpatchAll(Hooks.Id); NativeVictoryBudget.Uninstall();
        _enabled = false; Reset();
    }

    private static void Loaded() { _bindRequested = true; Bind(loaded: true); }

    internal static void Update()
    {
        if (!_enabled) return;
        var now = DateTime.UtcNow;
        if (_busy || now < _nextUpdate) return;
        _nextUpdate = now.AddMilliseconds(1000);
        try
        {
            if (_bindRequested) Bind();
        }
        catch (Exception ex) { Warn("Update deferred: " + ex.Message); }
    }

    private static void Bind(bool loaded = false)
    {
        var store = PlayerStore.HasInstance ? PlayerStore.Instance : null;
        var inventory = EmporiumEntry.Instance?.invElement;
        if (store == null || inventory == null || string.IsNullOrWhiteSpace(store.runID) || store.saveSlotId < 0 || store.modData == null) return;
        var data = store.modData;
        string runId = store.runID;
        int slotId = store.saveSlotId;
        _store = store; _bindRequested = false; _blocked = false;
        _state = null; _json = null; _confirmed = null; _confirmedJson = null;
        NativeVictoryBudget.SetActive(false, null);
        var read = RunDataGate.Read(store, OwnerId, AchievementRules.SaveKey);
        bool SameContext() => PlayerStore.HasInstance && ReferenceEquals(PlayerStore.Instance, store) &&
            ReferenceEquals(store.modData, data) && store.runID == runId && store.saveSlotId == slotId;
        if (!SameContext()) { Block(ForgeText.Get("forge.workshop.context_changed")); return; }
        if (read.Status == RunDataState.Missing)
        {
            _state = AchievementState.Empty(runId, slotId);
        }
        else if (read.Status == RunDataState.Present)
        {
            try
            {
                var state = loaded ? AchievementRules.DecodeLoadedRun(read.Json!, runId)
                    : AchievementRules.Decode(read.Json!, runId, slotId);
                if (!SameContext()) { Block(ForgeText.Get("forge.workshop.context_changed")); return; }
                _state = state; _json = read.Json;
                if (state.SlotId != slotId)
                {
                    // DuplicateSlot copies modData unchanged. Only this Normal
                    // callback may CAS-stage the selected slot, with the exact
                    // loaded JSON as its baseline. Pending retains its receipts
                    // and waits for Opened/Recover after inventory restoration.
                    Stage(state with { SlotId = slotId });
                    return;
                }
            }
            catch (Exception ex) { Block(ForgeText.Get("forge.workshop.read_failed", ex.Message)); return; }
            if (_state.Pending == null) AcceptLive();
        }
        else { Block(ForgeText.Get("forge.workshop.read_unavailable", read.Reason ?? "")); return; }
        if (_json == null) Stage(_state!);
    }

    private static bool Current() => _store != null && _state != null && PlayerStore.HasInstance &&
        ReferenceEquals(PlayerStore.Instance, _store) && _store.runID == _state.RunId && _store.saveSlotId == _state.SlotId;

    private static void InitialPostfix() => _bindRequested = true;
    private static void Reset()
    {
        OfficialWorkshopApi.CloseWindow();
        _store = null; _state = null; _confirmed = null; _json = null; _confirmedJson = null;
        _bindRequested = true; _busy = false; _blocked = false;
        NativeVictoryBudget.Reset();
        _message = ""; _lastFailure = ""; Unavailable.Clear(); _evidence.Clear(); _nextUpdate = DateTime.MinValue;
    }

    private static void Observe()
    {
        if (_blocked || _busy || _observing) return;
        _observing = true;
        try
        {
            if (!Current()) return;
            var store = _store!;
            Unavailable.Clear();
            _evidence = AchievementRules.Evaluate(NativeAchievementAdapter.Capture(store, Unavailable), store.IsPropertyPaid);
            var next = AchievementRules.Latch(_state!, _evidence.Where(x => !Unavailable.ContainsKey(x.Key)).ToDictionary(x => x.Key, x => x.Value));
            if (next.Completed.Count != _state!.Completed.Count) Stage(next);
        }
        finally { _observing = false; }
    }

    private static void Opened()
    {
        if (_bindRequested) Bind();
        if (!Current() || _blocked) return;
        if (_state!.Pending != null) Recover();
        Observe();
    }

    private static void StoreOwnedPostfix(PlayerStore __instance)
    {
        if (!__instance.IsPropertyPaid) return;
        try
        {
            if (_bindRequested) Bind();
            if (Current() && ReferenceEquals(_store, __instance))
                Stage(AchievementRules.RecordStoreOwnership(_state!));
        }
        catch (Exception ex) { Warn("Could not record store ownership: " + ex.Message); }
    }

    private static bool Stage(AchievementState next)
    {
        if (_blocked || _store == null) return false;
        string json = AchievementRules.Encode(next);
        if (json == _json) { _state = next; return true; }
        var staged = RunDataGate.Stage(_store, OwnerId, AchievementRules.SaveKey, next.RunId, next.SlotId, _json, json);
        if (staged.Status != RunDataState.StagedInMemory) { Block(ForgeText.Get("forge.workshop.conflict", staged.Reason ?? "")); return false; }
        _state = next; _json = json;
        if (next.Pending == null) AcceptLive();
        return true;
    }

    private static void AcceptLive()
    {
        // Completed goals and claimed rewards take effect in this session.
        // Inventory and modData reach disk together at the normal night save.
        _confirmedJson = _json; _confirmed = _state;
        NativeVictoryBudget.SetActive(!_blocked && AchievementRules.HasVictoryBudgetReward(_confirmed),
            _store!.currentClientInstance?.GetClientBlueprint());
    }
    private static void Recover()
    {
        if (!Current() || _blocked || _state!.Pending is not { } pending) return;
        // Never create replacements: a saved partial delivery keeps its exact receipt.
        var owned = _store!.FindAllItem().Where(GeneralHelper.IsItemOwned)
            .Select(item => new RewardReceipt(item.identifier, item.GetUniqueID(), item.unitCount));
        if (!AchievementRules.ReceiptsMatch(pending, owned))
        {
            _message = ForgeText.Get("forge.reward_delivery_is_incomplete_no_duplicate_items");
            return;
        }
        if (Stage(AchievementRules.Claim(_state))) _message = "";
    }

    private static void Claim(string id)
    {
        if (id == AchievementRules.VictoryNode && !NativeVictoryBudget.Installed) return;
        if (!Current() || _blocked || _busy || _state!.Pending != null || _json != _confirmedJson ||
            _confirmed?.Completed.ContainsKey(id) != true || _state.Claimed.Contains(id)) return;
        var definition = AchievementRules.Definitions.FirstOrDefault(x => x.Id == id);
        if (definition != null) Deliver(id, definition.Rewards);
    }

    private static void Deliver(string nodeId, AchievementReward[] rewards)
    {
        if (!Current() || _busy || _blocked || _state!.Pending != null || _json != _confirmedJson) return;
        // The victory badge is virtual and must also be claimable with a full or locked inventory.
        if (rewards.Length == 0)
        {
            if (Stage(_state with { Pending = new(nodeId, []) }) && Stage(AchievementRules.Claim(_state!))) _message = "";
            return;
        }
        var inventory = EmporiumEntry.Instance?.invElement;
        if (inventory == null) return;
        var products = new List<GameItem>();
        RewardReceipt[] receipts = [];
        var before = _state;
        bool placementAttempted = false, journaled = false;
        _busy = true;
        try
        {
            foreach (var reward in rewards)
                for (int i = 0; i < reward.Count; i++)
                {
                    var product = DirectoryMaster.Item(reward.ItemId, true) ?? throw new InvalidOperationException("Reward factory unavailable: " + reward.ItemId);
                    if (product.parentInventory != null || products.Contains(product))
                        throw new InvalidOperationException("Reward factory contract mismatch: " + reward.ItemId);
                    products.Add(product);
                    if (product.identifier != reward.ItemId || product.unitCount != 1 || product.GetUniqueID() <= 0 ||
                        products.Take(products.Count - 1).Any(x => x.uniqueId == product.uniqueId))
                        throw new InvalidOperationException("Reward factory contract mismatch: " + reward.ItemId);
                }
            receipts = products.Select(x => new RewardReceipt(x.identifier, x.uniqueId, x.unitCount)).ToArray();
            var plan = InventoryPlacement.PlanWholeGrid(inventory, products);
            if (plan == null)
            {
                _message = ForgeText.Get("forge.make_room_in_the_main_inventory_for");
                return;
            }
            if (!Current() || _state != before) throw new InvalidOperationException("Run changed during reward preflight");
            placementAttempted = true;
            if (!WorkshopRewardDelivery.Apply(inventory, plan)) throw new InvalidOperationException("Reward placement readback failed");
            var pending = new PendingReward(nodeId, receipts);
            if (!Stage(before with { Pending = pending })) throw new InvalidOperationException("Delivery journal could not be staged");
            journaled = true;
            // Placement was verified above. Finalize the live claim now so it
            // cannot be repeated before the normal night save.
            if (!Stage(AchievementRules.Claim(_state!))) return;
            _message = "";
            Log($"reward={nodeId}; items={products.Count}; save=deferred-until-overnight");
        }
        catch (Exception ex)
        {
            _message = ForgeText.Get("forge.reward_unavailable_check_the_forge_log_before");
            Warn("Delivery deferred: " + ex.Message);
        }
        finally
        {
            if (!journaled && !WorkshopRewardDelivery.Recall(products))
            {
                // Retain exact identities if native rollback is incomplete. Quarantine rather than resend.
                if (placementAttempted)
                {
                    Stage(before with { Pending = new(nodeId, receipts) });
                    Block(ForgeText.Get("forge.workshop.reward_interrupted"));
                }
            }
            _busy = false;
        }
    }

    private static OfficialWorkshopSnapshot? Snapshot()
    {
        if (!_enabled) return null;
        if (!Current()) return null;
        var entries = AchievementRules.Definitions.Select(definition =>
        {
            bool completed = _confirmed?.Completed.ContainsKey(definition.Id) == true;
            bool claimed = _confirmed?.Claimed.Contains(definition.Id) == true;
            bool pending = _state?.Pending != null || _json != _confirmedJson;
            bool rewardAvailable = definition.Rewards.All(reward => DirectoryMaster.Has<GameItem>(reward.ItemId)) &&
                (definition.Id != AchievementRules.VictoryNode || NativeVictoryBudget.Installed);
            string progress = completed ? (ForgeText.Get("forge.completion_recorded"))
                : _evidence.GetValueOrDefault(definition.Id)?.Progress ?? (ForgeText.Get("forge.checking_progress"));
            string state = claimed ? (ForgeText.Get("forge.claimed")) : completed ? (ForgeText.Get("forge.completed_unclaimed")) : (ForgeText.Get("forge.in_progress"));
            if (definition.Id == AchievementRules.VictoryNode && claimed)
                state = ForgeText.Get("forge.victory_badge_awarded_npc_purchase_budgets_x2");
            if (_blocked) state = ForgeText.Get("forge.achievement_record_unavailable");
            else if (!rewardAvailable) state = ForgeText.Get("forge.reward_unavailable");
            else if (pending) state += ForgeText.Get("forge.delivery_pending");
            else if (!completed && Unavailable.ContainsKey(definition.Id)) state = ForgeText.Get("forge.progress_temporarily_unavailable");
            string progressLabel = definition.Id is "pure_water_jug" or "quality_overload"
                ? (ForgeText.Get("forge.requirement")) : (ForgeText.Get("forge.progress"));
            string cost = progressLabel + progress;
            if (definition.Id == AchievementRules.VictoryNode) cost += ForgeText.Get("forge.reward_victory_commemorative_badge_all_npc_purchase");
            return new OfficialWorkshopEntry(definition.Id, ForgeText.Get("forge.achievement." + definition.Id + ".name"),
                definition.Id == "workshop_master" ? (ForgeText.Get("forge.store_victory")) : (ForgeText.Get("forge.base_game_achievement")),
                ForgeText.Get("forge.achievement." + definition.Id + ".description"),
                ForgeText.Get("forge.checked_when_this_page_opens_no_prerequisite"), cost, completed ? (ForgeText.Get("forge.claim")) : "0 / 1",
                state, claimed ? (ForgeText.Get("forge.claimed")) : (ForgeText.Get("forge.claim_reward")),
                claimed, true, completed && !claimed && !pending && !_blocked && !_busy && rewardAvailable && _state?.Claimed.Contains(definition.Id) != true)
            {
                Completed = completed,
                Rewards = definition.Rewards.Select(x => new OfficialWorkshopReward(OfficialWorkshopResourceKind.Item, x.ItemId, x.Count)
                {
                    DisplayName = NativeItemText.Name(x.ItemId, ForgeText.Get(x.NameKey))
                }).ToArray(),
                VisualLinks = definition.Id == "workshop_master" ? [] : ["workshop_master"],
                GraphX = definition.X, GraphY = definition.Y
            };
        }).ToArray();
        return new(_store!.playerCash, !ModText.IsChinese, Groups.Select(group =>
        {
            string name = ForgeText.Get(group.Key);
            return new OfficialWorkshopGroup(name, name, group.Count);
        }).ToArray(), entries, _message);
    }
    private static void Block(string reason)
    {
        _blocked = true; NativeVictoryBudget.SetActive(false, null); _message = reason; Warn(reason);
    }
    private static void Log(string line) { try { _log?.Invoke("[INFO] [NicokoboForge/Workshop] " + line); } catch { } }
    private static void Warn(string line)
    {
        if (_lastFailure == line) return;
        _lastFailure = line;
        try { _log?.Invoke("[WARN] [NicokoboForge/Workshop] " + line); } catch { }
    }
}
