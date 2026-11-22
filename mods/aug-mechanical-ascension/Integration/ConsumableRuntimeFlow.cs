using HarmonyLib;
using AugMechanicalAscension.Domain;
using UnityEngine;
namespace AugMechanicalAscension.Integration;

// Recovery has its own context-menu row and live consumption transaction.
// It never saves mid-day; inventory and modData share the normal game save.
// Native slot selection, medical predicates, activation and double-clicks keep
// their original behavior; recovery never invokes a native treatment callback.
internal sealed class ConsumableRuntimeFlow
{
    private sealed record Activation(RunState Applying, PlayerStore Store,
        string ItemId, int UniqueId, int UnitsBefore, long Value,
        string OperationId, int Day);

    private sealed record ContextAction(IntPtr Owner, PixelWindow Window,
        GridPixelElement Layout, RichTextElement Button);

    private static ConsumableRuntimeFlow? _active;
    private readonly NativeSessionBridge _bridge;
    private readonly Action<string> _log;
    private ContextAction? _contextAction;

    private ConsumableRuntimeFlow(NativeSessionBridge bridge, Action<string> log)
    {
        _bridge = bridge;
        _log = log;
    }

    internal static bool Install(HarmonyLib.Harmony harmony,
        NativeSessionBridge bridge, Action<string> log)
    {
        if (_active != null) return true;
        try
        {
            var colors = AccessTools.Method(typeof(ItemContextHandler),
                "UpdateSelectButtonColor", [typeof(RichTextElement)]);
            var widths = AccessTools.Method(typeof(ItemContextHandler),
                "EqualizeButtonWidths", Type.EmptyTypes);
            var trigger = AccessTools.Method(typeof(ItemContextHandler),
                "TriggerButton", [typeof(RichTextElement)]);
            if (colors == null || widths == null || trigger == null ||
                colors.ReturnType != typeof(void) ||
                widths.ReturnType != typeof(void) ||
                trigger.ReturnType != typeof(void))
            {
                log("[WARN] [P3] Consumable context actions unavailable; native signature mismatch");
                return false;
            }
            var instance = new ConsumableRuntimeFlow(bridge, log);
            harmony.Patch(colors, postfix: new HarmonyMethod(
                AccessTools.Method(typeof(ConsumableRuntimeFlow), nameof(ContextColorsPostfix))));
            harmony.Patch(widths, postfix: new HarmonyMethod(
                AccessTools.Method(typeof(ConsumableRuntimeFlow), nameof(ContextWidthsPostfix))));
            harmony.Patch(trigger, prefix: new HarmonyMethod(
                AccessTools.Method(typeof(ConsumableRuntimeFlow), nameof(ContextTriggerPrefix))));
            _active = instance;
            log("[P3] Independent consumable context actions installed; medical Restore and electronic Absorb; native selection, treatment and double-click unchanged; live item confirmation; normal game save only");
            return true;
        }
        catch (Exception ex)
        {
            // A partially installed patch remains inert because _active is null.
            log($"[ERROR] [P3] Consumable context actions unavailable; {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static void ContextColorsPostfix(ItemContextHandler __instance,
        RichTextElement? selectedButton)
    {
        var flow = _active;
        if (flow == null || __instance == null || NativeObject.Handle(__instance) == IntPtr.Zero)
            return;
        try
        {
            var item = NativeFields.Get<GameItem>(__instance,"currentItem");
            bool visible = flow._bridge.CurrentSession != null && item != null &&
                NativeObject.Handle(item) != IntPtr.Zero && (flow.IsElectronic(item) || flow.IsMedical(item));
            var action = flow._contextAction;
            if (visible) action = flow.EnsureContextAction(__instance);
            if (action == null || action.Owner != NativeObject.Handle(__instance) ||
                NativeObject.Handle(action.Window) != NativeObject.Handle(NativeFields.Get<PixelWindow>(__instance,"contextWindow"))) return;
            SetContextVisible(action, visible);
            if (!visible) return;

            bool enabled = flow.RecoveryEligible(item!);
            bool selected = selectedButton != null &&
                NativeObject.Handle(selectedButton) == NativeObject.Handle(action.Button);
            var color = enabled
                ? selected ? NativeFields.Get<RenderHandler.ColorPalette>(__instance,"textSelectColor") : NativeFields.Get<RenderHandler.ColorPalette>(__instance,"textColor")
                : selected ? NativeFields.Get<RenderHandler.ColorPalette>(__instance,"textSelectDisabledColor") : NativeFields.Get<RenderHandler.ColorPalette>(__instance,"textDisabledColor");
            var state = flow._bridge.CurrentSession!.State;
            bool electronic = flow.IsElectronic(item!);
            long basis = electronic ? state.ConfigSnapshot.ExposureRepairPerElectronic : flow.MedicalValue(item!);
            long amount = Math.Min(basis, electronic ? state.Exposure : state.ConfigSnapshot.MaxHealth - state.Health);
            string label = electronic
                ? AugText.Get("aug.absorb_exposure_1_item", amount)
                : AugText.Get("aug.repair_body_health_1_item", amount);
            if (!electronic && basis > amount)
                label += AugText.Get("aug.excess_discarded");
            // Native layout measures TMP at a temporary height of one pixel.
            // Use its configured size, not the transient rendered character size.
            var nativeText = NativeFields.Get<TreeNodeRender>(NativeFields.Get<RichTextElement>(__instance,"buttonUse"),"textNode").text;
            var text = NativeFields.Get<TreeNodeRender>(action.Button,"textNode").text;
            text.font = nativeText.font;
            text.fontStyle = nativeText.fontStyle;
            text.enableAutoSizing = false;
            text.fontSize = nativeText.fontSize;
            text.rectTransform.localScale = nativeText.rectTransform.localScale;
            action.Button.SetFixedSize(-1, -1);
            action.Button.SetText(label, 10000, color);
        }
        catch (Exception ex)
        {
            flow.Log($"[WARN] recovery context row unavailable; {ex.GetType().Name}: {ex.Message}");
        }
    }

    private ContextAction? EnsureContextAction(ItemContextHandler handler)
    {
        var window = NativeFields.Get<PixelWindow>(handler,"contextWindow");
        if (window == null || NativeObject.Handle(window) == IntPtr.Zero || window.childElement == null)
            return null;
        if (_contextAction is { } current && current.Owner == NativeObject.Handle(handler) &&
            NativeObject.Handle(current.Window) == NativeObject.Handle(window) && IsContextAttached(current)) return current;

        // Wrap the original menu without replacing any native button. The
        // original native grid remains intact inside the first wrapper row.
        var layout = new GridPixelElement(1, 2);
        layout.handler.gameObject.SetActive(false);
        var button = new RichTextElement();
        button.handler.gameObject.name = "nodeButtonAugRecovery";
        // Initialize text before marking its complete native raycast tree.
        button.SetText("", 10000, NativeFields.Get<RenderHandler.ColorPalette>(handler,"textDisabledColor"));
        if (!layout.AttachPos((PixelElement)button, 0, 1))
            throw new InvalidOperationException("Recovery button could not be attached");
        foreach (var child in layout.handler.GetComponentsInChildren<Transform>(true))
            child.gameObject.tag = "ContextMenuNode";

        var original = window.childElement;
        var originalScale = original.handler.transform.localScale;
        try
        {
            if (!window.Detach(original) || !layout.AttachPos(original, 0, 0) ||
                !window.Attach((PixelElement)layout))
                throw new InvalidOperationException("Recovery menu could not be attached");
            layout.handler.gameObject.SetActive(true);
            return _contextAction = new(NativeObject.Handle(handler), window, layout, button);
        }
        catch
        {
            if (NativeObject.Handle(window.childElement) == NativeObject.Handle(layout))
                window.Detach((PixelElement)layout);
            layout.Detach(original);
            if (window.childElement == null && layout.GetElement(0, 0) == null)
                window.Attach(original);
            if (layout.GetElement(0, 0) == null)
            {
                UnityEngine.Object.Destroy(layout.handler.gameObject);
                UnityEngine.Object.Destroy(button.handler.gameObject);
            }
            throw;
        }
        finally
        {
            // Native Detach preserves world scale at the temporary render root.
            // Reattaching must retain the element's original local scale.
            original.handler.transform.localScale = originalScale;
            window.Validate();
        }
    }

    private static bool IsContextAttached(ContextAction action) =>
        action.Layout.handler.transform.IsChildOf(action.Window.handler.transform);

    private static void SetContextVisible(ContextAction action, bool visible)
    {
        if (!IsContextAttached(action)) return;
        bool attached = NativeObject.Handle(action.Layout.GetElement(0, 1)) == NativeObject.Handle(action.Button);
        if (visible == attached) return;
        var buttonScale = action.Button.handler.transform.localScale;
        try
        {
            // Keep the menu tree stable when other mods wrap it. Only our row
            // moves; hiding an ancestor would also hide their native buttons.
            bool changed = visible
                ? action.Layout.AttachPos((PixelElement)action.Button, 0, 1)
                : action.Layout.Detach((PixelElement)action.Button);
            if (!changed) throw new InvalidOperationException("Recovery row visibility could not be updated");
            action.Button.handler.gameObject.SetActive(visible);
        }
        finally
        {
            action.Button.handler.transform.localScale = buttonScale;
            action.Layout.Validate();
            action.Window.Validate();
        }
    }

    private static void ContextWidthsPostfix(ItemContextHandler __instance)
    {
        var flow = _active;
        var action = flow?._contextAction;
        if (action == null || action.Owner != NativeObject.Handle(__instance) ||
            NativeObject.Handle(action.Window) != NativeObject.Handle(NativeFields.Get<PixelWindow>(__instance,"contextWindow")) ||
            !IsContextAttached(action) ||
            NativeObject.Handle(action.Layout.GetElement(0, 1)) != NativeObject.Handle(action.Button)) return;
        try
        {
            action.Button.SetFixedSize(-1, -1);
            action.Button.SetFixedSize(Math.Max(action.Button.widthPixels,
                NativeFields.Get<RichTextElement>(__instance,"buttonUse").widthPixels), -1);
            action.Layout.Validate();
            action.Window.Validate();
        }
        catch (Exception ex)
        {
            flow!.Log($"[WARN] recovery context width unavailable; {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static bool ContextTriggerPrefix(ItemContextHandler __instance,
        RichTextElement button)
    {
        var flow = _active;
        var action = flow?._contextAction;
        if (action == null || __instance == null ||
            action.Owner != NativeObject.Handle(__instance) || button == null ||
            NativeObject.Handle(button) != NativeObject.Handle(action.Button)) return true;
        try
        {
            var item = NativeFields.Get<GameItem>(__instance,"currentItem");
            if (NativeObject.Handle(action.Window) != NativeObject.Handle(NativeFields.Get<PixelWindow>(__instance,"contextWindow")) ||
                !IsContextAttached(action) ||
                NativeObject.Handle(action.Layout.GetElement(0, 1)) != NativeObject.Handle(action.Button) ||
                item == null || !flow!.RecoveryEligible(item)) return false;
            __instance.Stop();
            flow.ConsumeRecovery(item);
        }
        catch (Exception ex)
        {
            flow!.Log($"[ERROR] recovery click rejected; {ex.GetType().Name}: {ex.Message}");
        }
        return false;
    }

    private void ConsumeRecovery(GameItem item)
    {
        if (!RecoveryEligible(item)) return;
        bool electronic = IsElectronic(item);
        long value = electronic ? 1 : MedicalValue(item);
        var staged = Prepare(item, electronic, value);
        if (staged != null) ConsumeOneNativeUnit(item, staged);
    }

    private bool IsElectronic(GameItem item) => item.identifier == "common_electronic";

    private bool IsMedical(GameItem item) => ConsumableRules.IsMaterialMedical(item.identifier);

    private bool RecoveryEligible(GameItem item)
    {
        try
        {
            if (NativeObject.Handle(item) == IntPtr.Zero) return false;
            bool electronic = IsElectronic(item);
            return (electronic || IsMedical(item)) && Eligible(item, electronic) &&
                (electronic || _bridge.CurrentSession!.State.Health <
                    _bridge.CurrentSession.State.ConfigSnapshot.MaxHealth && MedicalValue(item) > 0);
        }
        catch (Exception ex)
        {
            Log($"[WARN] recovery eligibility unavailable; item={item.identifier}; {ex.GetType().Name}");
            return false;
        }
    }

    private bool Eligible(GameItem item, bool needsExposure)
    {
        try
        {
            var state = _bridge.CurrentSession?.State;
            if (state is not { InitPhase: "complete", FailureReason: "none" } ||
                state.HasPendingExternalOperation ||
                (needsExposure && state.Exposure <= 0) ||
                item.uniqueId <= 0 || item.unitCount <= 0 ||
                !GeneralHelper.IsItemOwned(item))
                return false;
            var store = (PlayerStore.HasInstance ? PlayerStore.Instance : null);
            var entry = EmporiumEntry.Instance;
            return store != null && NativeObject.Handle(store) != IntPtr.Zero &&
                entry != null && NativeObject.Handle(entry) != IntPtr.Zero &&
                entry.invElement != null &&
                NativeObject.Handle(entry.invElement) != IntPtr.Zero &&
                NativeObject.Handle(store) == _bridge.CurrentStorePointer &&
                store.runID == state.GameRunId &&
                store.saveSlotId == state.SaveSlotId &&
                StoreStation.GetDayCounter() == state.LastObservedDay;
        }
        catch { return false; }
    }

    private long MedicalValue(GameItem item)
    {
        long basis = ConsumableRules.IsManufacturedMedical(item.identifier)
            ? OfficialProductionValueApi.ReadBasis(item, item.unitValue) : item.unitValue;
        long innate = 0;
        double final = 1;
        if (item.itemFeatures != null)
        {
            foreach (var feature in item.itemFeatures)
            {
                if (feature == null || feature.isDisabled ||
                    feature.featureType == ItemFeature.FeatureType.Event) continue;
                int modifier = feature.GetActualValueModifier();
                switch (feature.GetValueStage())
                {
                    case ItemFeature.ValueStage.Innate:
                        innate = checked(innate + modifier);
                        break;
                    case ItemFeature.ValueStage.Final:
                        final *= Math.Max(0d, 1d + modifier / 100d);
                        break;
                }
            }
        }
        if (ConsumableRules.MedicalRecovery(basis) == 0) return 0;
        // Match native rounding, retaining actual condition/quality changes
        // while leaving customer, retail and event markups out of recovery.
        return ConsumableRules.MedicalRecovery(
            (long)Math.Round(Math.Max(0d,basis*(1d+innate/100d))*final));
    }

    private Activation? Prepare(GameItem item, bool electronic, long value)
    {
        var state = _bridge.CurrentSession?.State;
        if (state == null || !Eligible(item, electronic)) return null;
        var store = (PlayerStore.HasInstance ? PlayerStore.Instance : null);
        if (store == null) return null;
        int day = state.LastObservedDay;
        int uniqueId = item.uniqueId;
        int units = item.unitCount;
        string itemId = item.identifier;
        string instanceId = $"native-item:{uniqueId}";
        var matching = OfficialInventoryApi.CaptureRunItems(store)
            .Where(current => current.uniqueId == uniqueId).ToArray();
        if (matching.Length != 1 || NativeObject.Handle(matching[0]) != NativeObject.Handle(item) ||
            matching[0].identifier != itemId || matching[0].unitCount != units ||
            !OfficialInventoryApi.IsPlayerOwned(matching[0]))
        {
            Log($"[WARN] pre-use live item rejected; item={itemId}; nativeId={uniqueId}");
            return null;
        }
        string requestId = Guid.NewGuid().ToString("D");
        var prepared = electronic
            ? OperationJournal.PrepareExposureRepair(state, state.RunId,
                state.Revision, requestId, instanceId, false)
            : OperationJournal.PrepareMedical(state, state.RunId,
                state.Revision, requestId, instanceId, value);
        if (prepared.Status != OperationStatus.Applied ||
            !StageTransition(store, state, prepared.State, day)) return null;
        var applying = OperationJournal.MarkApplying(prepared.State,
            prepared.OperationId!);
        if (applying.Status != OperationStatus.Applied ||
            !StageTransition(store, prepared.State, applying.State, day))
        {
            if (ReferenceEquals(_bridge.CurrentSession?.State, prepared.State))
            {
                var canceled = OperationJournal.CancelPrepared(prepared.State,
                    prepared.OperationId!);
                if (canceled.Status == OperationStatus.Applied)
                    StageTransition(store, prepared.State, canceled.State, day);
            }
            Log($"use prepared but not armed; item={itemId}; operation={prepared.OperationId}");
            return null;
        }
        return new(applying.State, store, itemId, uniqueId, units, value,
            prepared.OperationId!, day);
    }

    private void ConsumeOneNativeUnit(GameItem item, Activation activation)
    {
        try
        {
            if (!SameSession(activation)) return;
            if (item.unitCount != activation.UnitsBefore ||
                !GeneralHelper.IsItemOwned(item))
            {
                Recover(activation, "itemChangedBeforeUse");
                return;
            }
            if (activation.UnitsBefore == 1) item.Destroy();
            else item.SetUnitCount(activation.UnitsBefore - 1);
            ConfirmConsumed(activation);
        }
        catch (Exception ex)
        {
            Log($"native consumption ambiguous; item={activation.UniqueId}; {ex.GetType().Name}: {ex.Message}");
            Recover(activation, "nativeConsumeException");
        }
    }

    private void ConfirmConsumed(Activation activation)
    {
        try
        {
            if (!SameSession(activation)) return;
            // The native wrapper may already be destroyed here. Capture the
            // current inventory without dereferencing that former instance.
            int after = activation.UnitsBefore - 1;
            var matching = OfficialInventoryApi.CaptureRunItems(activation.Store)
                .Where(current => current.uniqueId == activation.UniqueId).ToArray();
            bool consumed = after == 0 ? matching.Length == 0 :
                matching.Length == 1 && matching[0].identifier == activation.ItemId &&
                matching[0].unitCount == after && OfficialInventoryApi.IsPlayerOwned(matching[0]);
            if (!consumed)
            {
                Recover(activation, "liveItemConsumptionMismatch");
                return;
            }
            string evidenceId = $"item:{activation.UniqueId}:day:{activation.Day}:spent";
            var confirmed = OperationJournal.ConfirmExternal(activation.Applying,
                activation.OperationId, $"native-item:{activation.UniqueId}",
                activation.Value, evidenceId);
            if (confirmed.Status != OperationStatus.Applied ||
                !StageTransition(activation.Store, activation.Applying,
                    confirmed.State, activation.Day)) return;
            var committed = OperationJournal.Commit(confirmed.State,
                activation.OperationId);
            if (committed.Status != OperationStatus.Applied ||
                !StageTransition(activation.Store, confirmed.State,
                    committed.State, activation.Day)) return;
            Log($"consumption committed; item={activation.ItemId}; nativeId={activation.UniqueId}; "
                + $"units={activation.UnitsBefore}->{after}; "
                + $"health={activation.Applying.Health}->{committed.State.Health}; "
                + $"exposure={activation.Applying.Exposure}->{committed.State.Exposure}");
        }
        catch (Exception ex)
        {
            Log($"consumption confirmation ambiguous; item={activation.UniqueId}; {ex.GetType().Name}: {ex.Message}");
            Recover(activation, "confirmationException");
        }
    }

    private void Recover(Activation activation, string reason)
    {
        if (!SameSession(activation)) return;
        var recovery = OperationJournal.MarkNeedsRecovery(activation.Applying,
            activation.OperationId, reason.Length <= AugNumbers.Validation.MaxIdentifierChars ? reason : "unknown");
        if (recovery.Status == OperationStatus.NeedsRecovery)
            StageTransition(activation.Store, activation.Applying,
                recovery.State, activation.Day);
        Log($"consumption needs recovery; item={activation.ItemId}; reason={reason}");
    }

    private bool SameSession(Activation activation)
        => ReferenceEquals(_bridge.CurrentSession?.State, activation.Applying) &&
           NativeObject.Handle(activation.Store) == _bridge.CurrentStorePointer &&
           activation.Store.runID == activation.Applying.GameRunId &&
           activation.Store.saveSlotId == activation.Applying.SaveSlotId &&
           StoreStation.GetDayCounter() == activation.Day;

    private bool StageTransition(PlayerStore store, RunState previous,
        RunState next, int day)
    {
        var result = RuntimeRevisionNativeSaveAdapter.Stage(store,
            _bridge.Sessions, previous, next, day);
        if (result.Status == RuntimeRevisionSaveStatus.StagedInMemory) return true;
        Log($"[ERROR] consumption journal staging rejected; status={result.Status}; "
            + $"write={result.WriteStatus}; "
            + $"detail={result.Error ?? "none"}");
        return false;
    }

    private void Log(string message)
    {
        try
        {
            if (message.StartsWith("[ERROR] ", StringComparison.Ordinal))
                _log("[ERROR] [P3] Consumable " + message[8..]);
            else if (message.StartsWith("[WARN] ", StringComparison.Ordinal))
                _log("[WARN] [P3] Consumable " + message[7..]);
            else
                _log("[P3] Consumable " + message);
        }
        catch { /* Logging must not alter native activation. */ }
    }
}
