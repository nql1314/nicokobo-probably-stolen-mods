using HarmonyLib;
using AugMechanicalAscension.Domain;
using ProbablyStolen.ModSupport;

namespace AugMechanicalAscension.Integration;

// Native temporary selling features show the modifier and price it consistently.
// Only player-owned items directly on a counter receive this character's tag.
internal static class BargainRuntimeFlow
{
    private const string PerkFeatureId = "nicokobo.aug.neural_network.selling_feature";
    private const string RemovedAiFeatureId = "nicokobo.aug.bargain.ai_assistant";
    // ProbablyMultiplayer 1.2.0 NativeVisit.ClientId, shared by host and visitor.
    private const string MultiplayerClientId = "lemocuber_probablymultiplayer_human";
    private const int MaxDiagnosticLines = AugNumbers.BargainRuntimeFlowLimits.MaxDiagnosticLines;

    private static NativeSessionBridge? _bridge;
    private static Action<string>? _log;
    private static int _diagnosticLines;
    private static bool _ready;
    private static bool _refreshing;

    internal static bool Install(HarmonyLib.Harmony harmony,
        NativeSessionBridge bridge, Action<string> log)
    {
        _ready = false;
        _bridge = bridge;
        _log = log;
        harmony = new HarmonyLib.Harmony("nicokobo.aug.bargain");
        try
        {
            var weighted = AccessTools.Method(typeof(GameItem),
                nameof(GameItem.OnAddedToWeightedArea), [typeof(bool)])
                ?? throw new MissingMethodException(nameof(GameItem),
                    nameof(GameItem.OnAddedToWeightedArea));
            harmony.Patch(weighted, postfix: new HarmonyMethod(
                typeof(BargainRuntimeFlow), nameof(WeightedAreaPostfix)));
            var removed = AccessTools.Method(typeof(GameItem),
                nameof(GameItem.OnRemovedFromWeightedArea), Type.EmptyTypes)
                ?? throw new MissingMethodException(nameof(GameItem),
                    nameof(GameItem.OnRemovedFromWeightedArea));
            harmony.Patch(removed, postfix: new HarmonyMethod(
                typeof(BargainRuntimeFlow), nameof(WeightedAreaRemovedPostfix)));
            var sellPanel = AccessTools.Method(typeof(NegociationUIManager),nameof(NegociationUIManager.UpdateUI),[typeof(GameItem)]) ?? throw new MissingMethodException("NegociationUIManager.UpdateUI(GameItem)");
            harmony.Patch(sellPanel,prefix:new HarmonyMethod(typeof(BargainRuntimeFlow),nameof(SellPanelPrefix)));
            var value = AccessTools.Method(typeof(GameItem), nameof(GameItem.GetCurrentValue),
                [typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(long)])
                ?? throw new MissingMethodException(nameof(GameItem), nameof(GameItem.GetCurrentValue));
            var negotiated = AccessTools.Method(typeof(GameItem), nameof(GameItem.GetNegociatedValue), Type.EmptyTypes)
                ?? throw new MissingMethodException(nameof(GameItem), nameof(GameItem.GetNegociatedValue));
            foreach (var target in new[] { value, negotiated })
                harmony.Patch(target,
                    prefix: new HarmonyMethod(typeof(BargainRuntimeFlow), nameof(ValuePrefix)));
            var encode = AccessTools.Method(typeof(PlayerStore), nameof(PlayerStore.EncodeItem),
                [typeof(GameItem)])
                ?? throw new MissingMethodException(nameof(PlayerStore), nameof(PlayerStore.EncodeItem));
            harmony.Patch(encode, prefix: new HarmonyMethod(
                typeof(BargainRuntimeFlow), nameof(EncodeItemPrefix)));
            _ready = true;
            log("[P3] Aug selling feature installed; counter-only=true; clear-on-remove=true; native price-stage hook=false");
            return true;
        }
        catch (Exception ex)
        {
            harmony.UnpatchSelf();
            log($"[ERROR] [P3] Bargain runtime hook failed; {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static void WeightedAreaPostfix(GameItem __instance)
        => RefreshSellingFeature(__instance);

    private static void WeightedAreaRemovedPostfix(GameItem __instance)
        => RemoveSellingFeatures(__instance);

    private static void SellPanelPrefix(GameItem __0)
        => RefreshSellingFeature(__0);

    private static void ValuePrefix(GameItem __instance)
        => RefreshSellingFeature(__instance);

    // Multiplayer captures the item before reading its value, and may reuse a cached estimate.
    private static void EncodeItemPrefix(GameItem __0)
    {
        if ((PlayerStore.HasInstance ? PlayerStore.Instance : null)?.currentClientInstance?.GetClientBlueprint()?.identifier == MultiplayerClientId)
            RemoveSellingFeatures(__0);
    }

    private static void RemoveSellingFeatures(GameItem? item)
    {
        if (!_ready || item == null || NativeObject.Handle(item) == IntPtr.Zero) return;
        try
        {
            TemporarySellingFeature.Remove(item, PerkFeatureId);
            TemporarySellingFeature.Remove(item, RemovedAiFeatureId);
        }
        catch (Exception ex)
        {
            _log?.Invoke($"[P3] Removed sale feature cleanup failed; " +
                $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static bool IsOnCounter(GameItem item)
    {
        var inventory = item.parentInventory;
        var emporium = EmporiumEntry.Instance;
        if (inventory == null || NativeObject.Handle(inventory) == IntPtr.Zero ||
            emporium == null || NativeObject.Handle(emporium) == IntPtr.Zero) return false;
        return NativeObject.Handle(inventory) == NativeObject.Handle(emporium.frontInvinvElement) ||
            NativeObject.Handle(inventory) == NativeObject.Handle(emporium.backInvinvElement);
    }

    private static void RefreshSellingFeature(GameItem? item)
    {
        if (!_ready || _refreshing || item == null || NativeObject.Handle(item) == IntPtr.Zero) return;
        _refreshing = true;
        bool applied = false;
        try
        {
            TemporarySellingFeature.Remove(item, RemovedAiFeatureId);
            if (item.unitCount <= 0 || !IsOnCounter(item)) return;
            var bridge = _bridge;
            var view = bridge?.CurrentSession;
            var store = (PlayerStore.HasInstance ? PlayerStore.Instance : null);
            if (bridge == null || view == null || store == null ||
                NativeObject.Handle(store) == IntPtr.Zero ||
                NativeObject.Handle(store) != bridge.CurrentStorePointer ||
                !StartDefinition.MatchesNativeStartType((int)store.startType) ||
                store.runID != view.State.GameRunId ||
                store.saveSlotId != view.State.SaveSlotId ||
                view.State.InitPhase != "complete" ||
                view.State.FailureReason != "none" ||
                !GeneralHelper.IsItemOwned(item)) return;

            var client = store.currentClientInstance?.GetClientBlueprint();
            if (client == null || NativeObject.Handle(client) == IntPtr.Zero ||
                client.identifier == MultiplayerClientId) return;

            int thresholdShift = CyberneticItemOwnership.Capture(store)
                .ExposureThresholdShift(view.State.ConfigSnapshot.CyberneticPrices);
            int modifier = BargainRules.CalculatePerkModifier(client.isAug,
                view.State.Exposure, view.State.ConfigSnapshot, thresholdShift);
            TemporarySellingFeature.Ensure(item, PerkFeatureId, modifier,
                AugText.Get("aug.aug"), enabled: true);
            applied = true;
            Diagnose(item, client, modifier);
        }
        catch (Exception ex)
        {
            _log?.Invoke($"[P3] Aug counter sale feature unavailable; " +
                $"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            try { if (!applied) TemporarySellingFeature.Remove(item, PerkFeatureId); }
            catch (Exception ex)
            {
                _log?.Invoke($"[P3] Aug counter feature cleanup failed; {ex.GetType().Name}: {ex.Message}");
            }
            finally { _refreshing = false; }
        }
    }

    private static void Diagnose(GameItem item, StoreClient client,
        int perkModifier)
    {
        var log = _log;
        if (log == null || _diagnosticLines >= MaxDiagnosticLines) return;
        _diagnosticLines++;
        log($"[P3] Aug counter sale feature; item={item.identifier}; "
            + $"client={client.identifier}; isAug={client.isAug}; "
            + $"perk={perkModifier}");
    }
}
