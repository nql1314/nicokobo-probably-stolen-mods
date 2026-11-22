using HarmonyLib;
using AugMechanicalAscension.Domain;
using Nicokobo.ContentInterop;
using MechcoreProtocol;

namespace AugMechanicalAscension.Integration;

// Registers the neural interface as a real, individually owned native node.
internal static class NeuralModuleDirectory
{
    internal static void Uninstall(){_startReadiness?.Dispose();_startReadiness=null;_bridge=null;}
    private const string TemplateId = "node_small";
    private const string OverclockEffect = "nicokobo.aug.effect.overclock";
    private const int OverclockPercent = AugNumbers.NeuralInterface.OverclockPercent;
    private static readonly LocalizedItemText Description = new("aug.install_in_a_machine_s_module_bay");
    private static readonly LocalizedItemText Flavor = new("aug.the_machine_needs_a_node_and_you");
    private static System.Action<GameItem, GameInventory>? _overclockAction;
    private static Action<string>? _log;
    private static NativeSessionBridge? _bridge;
    private static DateTime _nextBaseRefreshCheckUtc;
    private static DateTime _nextBaseRefreshAttemptUtc;
    private static bool _machineBasesNeedRefresh;
    private static readonly OwnedItemCandidates Interfaces = new(
        store => store.FindAllItem(false), item => item.identifier == NeuralModuleRules.ItemId);
    private static bool _nodeRegistrationStaged;
    private static bool _overclockRegistrationStaged;
    private static IDisposable? _startReadiness;
    private static NativeApplicationStatus? _nodeApplication;
    private static NativeApplicationStatus? _overclockApplication;

    // Native directories can initialize between menu confirmation and InitialSave.
    // Staged is selectable, but never authorizes serialization of the new Aug run.
    internal static bool ReadyForStart
    {
        get
        {
            if (!_nodeRegistrationStaged || !_overclockRegistrationStaged ||
                !OfficialCapabilities.Current.NativeItemDirectoryHook) return false;
            RefreshStartRegistrations();
            return _nodeApplication is NativeApplicationStatus.Staged or NativeApplicationStatus.Applied &&
                _overclockApplication is NativeApplicationStatus.Staged or NativeApplicationStatus.Applied;
        }
    }

    internal static bool RecheckForStart()
    {
        return ReadyForStart;
    }

    internal static bool ReadyForInitialSave => RecheckForStart() &&
        _nodeApplication == NativeApplicationStatus.Applied &&
        _overclockApplication == NativeApplicationStatus.Applied &&
        OfficialCapabilities.Current.NativeEffectRegistration;

    private static void RefreshStartRegistrations()
    {

        _nodeApplication = OfficialItemApi.Snapshot().FirstOrDefault(view =>
            view.OwnerId == "nicokobo.aug" && view.ContentId == NeuralModuleRules.ItemId)?.Status;
        _overclockApplication = OfficialEffectApi.Snapshot().FirstOrDefault(view =>
            view.OwnerId == "nicokobo.aug" && view.ContentId == OverclockEffect)?.Status;
    }

    internal static bool Install(HarmonyLib.Harmony harmony, Action<string> log)
    {
        _log = log;
        harmony = new HarmonyLib.Harmony("nicokobo.aug.neural");
        _nodeRegistrationStaged = false;
        _overclockRegistrationStaged = false;
        _nodeApplication = _overclockApplication = null;
        try
        {
            _startReadiness?.Dispose();
            _startReadiness = OfficialContentApi.Subscribe("nicokobo.aug",
                "nicokobo.aug.neural_start_ready", view =>
                {
                    if (view.OwnerId == "nicokobo.aug" &&
                        (view.ContentId == NeuralModuleRules.ItemId || view.ContentId == OverclockEffect))
                        RefreshStartRegistrations();
                });
            var moduleTooltip = AccessTools.Method(typeof(ModuleHelper),
                nameof(ModuleHelper.CreateModuleTooltip),
                [typeof(RichTextBuilder), typeof(GameItem)])
                ?? throw new MissingMethodException(nameof(ModuleHelper),
                    nameof(ModuleHelper.CreateModuleTooltip));
            if (!OfficialHookApi.TryValidateNativeTarget(moduleTooltip, out var reason))
                throw new InvalidOperationException(reason);
            harmony.Patch(moduleTooltip, prefix: new HarmonyMethod(
                AccessTools.Method(typeof(NeuralModuleDirectory), nameof(ModuleTooltipPrefix))));
            var effect = OfficialEffectApi.RegisterFixedAbility("nicokobo.aug", OverclockEffect,
                CreateOverclockEffect, IsModule);
            if (effect.Status is not (SubmitStatus.Accepted or SubmitStatus.AlreadyPresent))
                throw new InvalidOperationException(
                    $"Nicokobo Forge registration rejected overclock: effect={effect.Status}; {effect.Reason}");
            _overclockRegistrationStaged = true;
            var node = OfficialItemApi.RegisterNode("nicokobo.aug",
                NeuralModuleRules.ItemId, Create,
                new(NightShopStockPolicy.Repeatable, CanOfferAtNightShop,
                    Name: new("aug.brain_computer_interface"),
                    ShortDescription: Description, FlavorText: Flavor));
            if (node.Status is not (SubmitStatus.Accepted or SubmitStatus.AlreadyPresent))
                throw new InvalidOperationException(
                    $"Nicokobo Forge registration rejected neural interface: node={node.Status}");
            _nodeRegistrationStaged = true;
            log($"[P2] Nicokobo Forge neural node registration staged; node={node.Status}; " +
                "native application awaits directory init");
            return true;
        }
        catch (Exception ex)
        {
            _nodeRegistrationStaged = false;
            _overclockRegistrationStaged = false;
            try { _startReadiness?.Dispose(); } catch { }
            _startReadiness = null;
            try { harmony.UnpatchSelf(); }
            catch (Exception cleanup) { log("[ERROR] [P2] Neural tooltip rollback failed: " + cleanup.Message); }
            log($"[ERROR] [P2] NeuralModuleDirectory hooks failed; {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    internal static void BindSession(NativeSessionBridge bridge) => _bridge = bridge;

    // Native ownership is the source of the shared stat level. Scan at a bounded
    // interval so a night-shop purchase changes every owned interface even when
    // no Mod-state revision was written by the native transaction.
    internal static void RefreshOwnedBases()
    {
        var bridge = _bridge;
        var view = bridge?.CurrentSession;
        var now = DateTime.UtcNow;
        if (bridge == null || view == null || view.State.InitPhase != "complete" ||
            view.State.FailureReason != "none" ||
            now < _nextBaseRefreshCheckUtc || now < _nextBaseRefreshAttemptUtc ||
            OfficialInventoryApi.IsItemDragActive) return;
        _nextBaseRefreshCheckUtc = now.AddSeconds(AugNumbers.Timing.NeuralBaseRefreshIntervalSeconds);
        var state = view.State;
        var store = (PlayerStore.HasInstance ? PlayerStore.Instance : null);
        if (store == null || NativeObject.Handle(store) == IntPtr.Zero ||
            NativeObject.Handle(store) != bridge.CurrentStorePointer ||
            store.runID != state.GameRunId || store.saveSlotId != state.SaveSlotId ||
            !StartDefinition.MatchesNativeStartType((int)store.startType)) return;
        try
        {
            var found = new Dictionary<int, GameItem>();
            foreach (var item in Interfaces.Read(store))
            {
                if (!IsModule(item) || !GeneralHelper.IsItemOwned(item)) continue;
                if (item.unitCount != 1 ||
                    !item.IsTag("IS_OWNED_TAG") || !found.TryAdd(item.uniqueId, item))
                    throw new InvalidOperationException("Neural ownership mismatch");
            }
            if (found.Count == 0) return;
            var (performance, efficiency, quality) =
                NeuralModuleRules.BaseStats(found.Count);
            string description = Description.For();
            var prepared = new List<(GameItem Item, TagState Perf, TagState Eff, TagState Qual)>(found.Count);
            foreach (var item in found.Values)
            {
                if (item.shortDescription != description)
                    item.shortDescription = description;
                if (item.IsTag("NOT_FOR_RESALE") &&
                    !item.DisableTag("NOT_FOR_RESALE", false))
                    throw new InvalidOperationException("Owned neural interface resale tag could not be disabled");
                if (item.IsTag("NOT_FOR_RESALE"))
                    throw new InvalidOperationException("Owned neural interface still has resale tag");
                var perf = item.state?.GetTag("BONUS_PERCENTAGE_PERFORMANCE_INT");
                var eff = item.state?.GetTag("BONUS_PERCENTAGE_EFFICIENCY_INT");
                var qual = item.state?.GetTag("BONUS_PERCENTAGE_QUALITY_INT");
                if (perf == null || eff == null || qual == null)
                    throw new InvalidOperationException("Native neural stat tag unavailable");
                prepared.Add((item, perf, eff, qual));
            }
            int updated = 0;
            foreach (var (item, perf, eff, qual) in prepared)
            {
                if (perf.valueInt == performance && eff.valueInt == efficiency &&
                    qual.valueInt == quality) continue;
                _machineBasesNeedRefresh = true;
                perf.valueInt = performance;
                eff.valueInt = efficiency;
                qual.valueInt = quality;
                ModuleEffectHelper.ResetAllTempStat(item);
                updated++;
            }
            if (_machineBasesNeedRefresh)
            {
                int machines = RefreshInstalledNeuralMachines(store.FindAllItem(true));
                _machineBasesNeedRefresh = false;
                _log?.Invoke($"[P3] Neural bases refreshed; items={updated}; machines={machines}; " +
                    $"base={performance}/{efficiency}/{quality}; " +
                    "native save/readback still required");
            }
        }
        catch (Exception ex)
        {
            _nextBaseRefreshAttemptUtc = DateTime.UtcNow.AddSeconds(AugNumbers.Timing.NeuralBaseRefreshFailureRetrySeconds);
            _log?.Invoke($"[P3] Neural base refresh deferred; " +
                $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static int RefreshInstalledNeuralMachines(System.Collections.Generic.List<GameItem> items)
    {
        int refreshed = 0;
        var visited = new HashSet<IntPtr>();
        foreach (var machine in items)
        {
            if (machine == null || NativeObject.Handle(machine) == IntPtr.Zero || machine.unitCount <= 0 ||
                !visited.Add(NativeObject.Handle(machine)) || !OfficialInventoryApi.IsPlayerOwned(machine)) continue;
            var installed = OfficialModuleApi.GetInventory(machine)?.childItems;
            if (installed == null) continue;
            foreach (var module in installed)
            {
                if (!IsModule(module) || module.unitCount <= 0) continue;
                // Resetting temporary node tags alone does not update the host's
                // aggregate totals or drawn power. Recompute after every owned
                // interface has received its new base values.
                OfficialModuleApi.Recompute(machine);
                refreshed++;
                break;
            }
        }
        return refreshed;
    }

    private static bool CanOfferAtNightShop()
    {
        var bridge = _bridge;
        var view = bridge?.CurrentSession;
        var store = (PlayerStore.HasInstance ? PlayerStore.Instance : null);
        if (bridge == null || view == null || store == null ||
            NativeObject.Handle(store) == IntPtr.Zero || NativeObject.Handle(store) != bridge.CurrentStorePointer ||
            store.runID != view.State.GameRunId ||
            store.saveSlotId != view.State.SaveSlotId ||
            !StartDefinition.MatchesNativeStartType((int)store.startType) ||
            view.State.InitPhase != "complete" || view.State.FailureReason != "none" ||
            view.State.HasPendingExternalOperation)
            return false;
        int owned = CountOwned(store);
        return owned >= 1 && owned < NeuralModuleRules.MaximumOwned;
    }

    private static int CountOwned(PlayerStore store)
    {
        var all = store.FindAllItem(true);
        if (all == null) return 0;
        int count = 0;
        for (int index = 0; index < all.Count; index++)
        {
            var item = all[index];
            if (IsModule(item) && item.unitCount == 1 && item.IsTag("IS_OWNED_TAG"))
                count++;
        }
        return count;
    }

    private static int CurrentUnitValue()
    {
        var store = (PlayerStore.HasInstance ? PlayerStore.Instance : null);
        if (store == null || NativeObject.Handle(store) == IntPtr.Zero) return (int)AugNumbers.NeuralInterface.BasePrice;
        return NeuralModuleRules.UnitValue(CountOwned(store),
            NicoWorkshop.DeliveredNeuralInterfaces(store));
    }

    private static GameItem Create()
    {
        if (!OfficialEffectApi.Snapshot().Any(view => view.OwnerId == "nicokobo.aug" &&
            view.ContentId == OverclockEffect && view.Status == NativeApplicationStatus.Applied))
            throw new InvalidOperationException("Registered overclock effect has not been applied");
        var item = DirectoryMaster.Create<GameItem>(TemplateId);
        if (item == null || item.identifier != TemplateId || item.unitCount != 1)
            throw new InvalidOperationException("Native small-node template is unavailable");
        if (item.shape == null || item.shape.width != 1 || item.shape.height != 1)
            throw new InvalidOperationException($"Small-node 1x1 shape unavailable; "
                + $"shape={item.shape?.width}x{item.shape?.height}");
        item.identifier = NeuralModuleRules.ItemId;
        FixedModuleAbilities.Add(item, OverclockEffect, OverclockPercent);
        foreach (var (tagName, value) in new[]
        {
            ("BONUS_PERCENTAGE_EFFICIENCY_INT", AugNumbers.NeuralInterface.EfficiencyPerOwned),
            ("BONUS_PERCENTAGE_PERFORMANCE_INT", AugNumbers.NeuralInterface.PerformancePerOwned),
            ("BONUS_PERCENTAGE_QUALITY_INT", AugNumbers.NeuralInterface.QualityPerOwned)
        })
        {
            var stat = item.state?.GetTag(tagName);
            if (stat == null) throw new InvalidOperationException($"Missing node stat {tagName}");
            stat.valueInt = value;
        }
        // Native node creation initializes temporary stats before these bases are assigned.
        ModuleEffectHelper.ResetAllTempStat(item);
        var overclock = item.state?.GetTag("MODULE_EFFECT1_TAG");
        if (overclock == null || overclock.valueString != OverclockEffect)
            throw new InvalidOperationException("Overclock effect slot is unavailable");
        ProbablyStolen.ModSupport.ModItemTypeRules.ApplyTemplateTypes(item, cybernetic: true, requiredNativeType: "NODE");
        item.name = AugText.Get("aug.brain_computer_interface");
        item.shortDescription = Description.For();
        item.flavorText = Flavor.For();
        item.unitBaseValue = 0;
        item.unitValue = CurrentUnitValue();
        item.mayStackItemFunc = null;
        item.SetSpriteAndShapeFromMod(MechcorePackage.Id, ItemArtwork.NeuralInterface);
        ProbablyStolen.ModSupport.NativeContrabandStatus.Apply(item, AugNumbers.NeuralInterface.ContrabandLevel);
        if (item.IsTag("NOT_FOR_RESALE") &&
            !item.DisableTag("NOT_FOR_RESALE", false))
            throw new InvalidOperationException("Neural interface resale tag could not be disabled");
        if (item.IsTag("NOT_FOR_RESALE") || !item.IsGameItemType("NODE") ||
            item.GetTagReadonly("MODULE_EFFECT1_TAG")?.valueString != OverclockEffect ||
            item.GetTagReadonly("MODULE_EFFECT1_TAG")?.valueInt != OverclockPercent ||
            !string.IsNullOrEmpty(item.GetTagReadonly("MODULE_EFFECT2_TAG")?.valueString) ||
            item.identifier != NeuralModuleRules.ItemId)
            throw new InvalidOperationException("Neural module factory verification failed");
        if (!_nodeProbeDone)
        {
            _nodeProbeDone = true;
            _log?.Invoke($"[P2] Neural interface type={string.Join(",", item.GetGameItemType().ToArray())}; "
                + $"overclock={OverclockEffect}; base={AugNumbers.NeuralInterface.PerformancePerOwned}/{AugNumbers.NeuralInterface.EfficiencyPerOwned}/{AugNumbers.NeuralInterface.QualityPerOwned}; adjacency={OverclockPercent}%");
        }
        return item;
    }

    private static bool _nodeProbeDone;

    private static ModuleEffectHelper.ModuleEffect CreateOverclockEffect()
    {
        _overclockAction ??= ApplyOverclock;
        return new ModuleEffectHelper.ModuleEffect
        {
            effectType = ModuleEffectHelper.ModuleEffect.ModuleEffectType.NodeOnly,
            identifier = OverclockEffect,
            displayName = AugText.Get("aug.overclock"),
            description = AugText.Get("aug.gains_of_each_adjacent_module_s_positive", OverclockPercent),
            onAnyAction = _overclockAction,
        };
    }

    private static void ApplyOverclock(GameItem item, GameInventory inventory)
    {
        if (!IsModule(item)) return;
        var adjacent = item.FindAdjacentType<GameItem>();
        if (adjacent == null) return;
        AddPositiveAdjacentBonus("PERFORMANCE");
        AddPositiveAdjacentBonus("EFFICIENCY");
        AddPositiveAdjacentBonus("QUALITY");

        void AddPositiveAdjacentBonus(string attribute)
        {
            long positiveBases = 0;
            foreach (var neighbor in adjacent)
            {
                if (neighbor == null || NativeObject.Handle(neighbor) == IntPtr.Zero ||
                    !neighbor.IsGameItemType("MODULE")) continue;
                int value = neighbor.GetTagReadonly(
                    $"BONUS_PERCENTAGE_{attribute}_INT")?.valueInt ?? 0;
                if (value > 0) positiveBases += value;
            }
            int bonus = (int)Math.Min(int.MaxValue,
                positiveBases * OverclockPercent / 100);
            if (bonus == 0) return;
            var temporary = item.state?.GetTag($"TEMP_PERCENTAGE_{attribute}_INT");
            if (temporary == null || temporary.Enable() == null) return;
            temporary.SetInt((int)Math.Min(int.MaxValue,
                (long)temporary.valueInt + bonus));
        }
    }

    private static bool IsModule(GameItem? item)
        => _nodeRegistrationStaged && item != null && NativeObject.Handle(item) != IntPtr.Zero &&
           item.identifier == NeuralModuleRules.ItemId;

    private static bool ModuleTooltipPrefix(RichTextBuilder builder, GameItem item)
    {
        if (!IsModule(item)) return true;
        var cyan = RenderHandler.ColorPalette.Cyan;
        builder.AddLine(AugText.Get("aug.module_effects"),
            color: cyan, bold: true);
        AddStat("BONUS_PERCENTAGE_PERFORMANCE_INT",
            AugText.Get("aug.performance"));
        AddStat("BONUS_PERCENTAGE_EFFICIENCY_INT",
            AugText.Get("aug.efficiency"));
        AddStat("BONUS_PERCENTAGE_QUALITY_INT",
            AugText.Get("aug.quality"));
        builder.AddLine(AugText.Get("aug.overclock"),
            color: cyan, bold: true);
        builder.AddLine(AugText.Get("aug.gains_of_each_adjacent_module_s_positive", OverclockPercent),
            color: cyan);
        if (_bridge?.CurrentSession is { } session && PlayerStore.HasInstance &&
            NativeObject.Handle(PlayerStore.Instance) == _bridge.CurrentStorePointer &&
            PlayerStore.Instance.runID == session.State.GameRunId && PlayerStore.Instance.saveSlotId == session.State.SaveSlotId)
        {
            int count = CountOwned(PlayerStore.Instance);
            var stats = NeuralModuleRules.BaseStats(count);
            builder.AddLine(AugText.Get("aug.owned_across_all_machines_and_storage_each", count, stats.Performance, stats.Efficiency, stats.Quality), color: cyan);
            builder.AddLine(count >= NeuralModuleRules.MaximumOwned
                ? AugText.Get("aug.ownership_limit_reached")
                : AugText.Get("aug.next_interface_base_price_native_shop_modifiers", CurrentUnitValue()), color: cyan);
        }
        return false;

        void AddStat(string tag, string label)
        {
            int baseValue = item.GetTagReadonly(tag)?.valueInt ?? 0;
            string temporaryTag = tag.Replace("BONUS_PERCENTAGE_", "TEMP_PERCENTAGE_");
            int currentValue = item.GetTagReadonly(temporaryTag)?.valueInt ?? baseValue;
            string signedCurrent = $"{(currentValue >= 0 ? "+" : "")}{currentValue}%";
            if (currentValue == baseValue)
            {
                builder.AddLine($"- {label} {signedCurrent}", color: cyan);
                return;
            }
            int temporaryBonus = currentValue - baseValue;
            builder.AddLine(AugText.Get("aug.base_temporary", label, signedCurrent, baseValue, temporaryBonus),
                color: cyan);
        }
    }
}

