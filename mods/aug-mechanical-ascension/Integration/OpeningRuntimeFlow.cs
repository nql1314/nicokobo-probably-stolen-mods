using AugMechanicalAscension.Domain;
using AugMechanicalAscension.Persistence;
using AugMechanicalAscension.Services;

using UnityEngine;

namespace AugMechanicalAscension.Integration;

// A new start receives its opening after the first native save. Subsequent
// opening revisions are verified in memory and saved with the first night.
// Reloading the untouched initial save rebuilds the whole opening together.
internal sealed class OpeningRuntimeFlow(NativeSessionBridge bridge,
    Action<string> log)
{
    private (IntPtr StorePointer, int Slot, string GameRunId)? _newRun;
    private (IntPtr StorePointer, int Slot, string GameRunId, long Revision)?
        _sameSessionApplied;
    private string? _lastAutoCommitAttempt;
    private string? _lastBaselineAttempt;
    private static readonly OpeningNativeItemSpec[] Specs =
    [
        new("neuralModule", NeuralModuleRules.ItemId, AugNumbers.OpeningItems.NeuralInterfaceCount),
        new("materialRefiner", OpeningJournalRules.MaterialRefinerId, AugNumbers.OpeningItems.MaterialRefinerCount),
        new("battery", "energy_credit", AugNumbers.OpeningItems.BatteryCount),
        new("medical", "topical_bandage_item", AugNumbers.OpeningItems.MedicalCount),
        new("electronic", "common_electronic", AugNumbers.OpeningItems.ElectronicCount),
        new("scrapMetal", "scrap_metal", AugNumbers.OpeningItems.ScrapMetalCount),
        new("materialSign", "sign_material", AugNumbers.OpeningItems.MaterialSignCount)
    ];

    internal void Update()
    {
        try { AdvanceAutomatic(); }
        catch (Exception ex)
        {
            log($"[ERROR] [P2] Automatic opening aborted; {ex.GetType().Name}: {ex.Message}");
        }
    }

    internal void QueueNewRun(PlayerStore store)
    {
        _newRun = (NativeObject.Handle(store), store.saveSlotId, store.runID);
        _sameSessionApplied = null;
        log($"[P2] Automatic opening queued; slot={store.saveSlotId}");
    }

    private void AdvanceAutomatic()
    {
        // Wait for the native opening/load callback and only read its existing
        // store. Menu polling must never create the native rendering/store tree.
        if (_newRun == null && _sameSessionApplied == null &&
            bridge.NativeLoadedRevision == null) return;
        var store = (PlayerStore.HasInstance ? PlayerStore.Instance : null);
        var entry = EmporiumEntry.Instance;
        var view = bridge.CurrentSession;
        if (store == null || entry == null || view == null ||
            NativeObject.Handle(store) != bridge.CurrentStorePointer ||
            !StartDefinition.MatchesNativeStartType((int)store.startType) ||
            store.runID != view.State.GameRunId ||
            store.saveSlotId != view.State.SaveSlotId ||
            StoreStation.GetDayCounter() != view.State.StartDay)
            return;

        var state = view.State;
        bool freshLoadedBaseline = bridge.NativeLoadedRevision == 0 &&
            state.InitPhase == "pending" && state.Revision == 0;
        if (freshLoadedBaseline || _newRun is { } queued &&
            queued.StorePointer == NativeObject.Handle(store) &&
            queued.Slot == state.SaveSlotId &&
            queued.GameRunId == state.GameRunId)
        {
            _newRun = null; // A failed or uncertain attempt must not replay on another frame.
            if (state.InitPhase != "pending" || state.Revision != 0) return;
            string baselineKey = $"{view.Generation}|{state.GameRunId}|{state.SaveSlotId}";
            if (_lastBaselineAttempt == baselineKey) return;
            _lastBaselineAttempt = baselineKey;
            Prepare(store, entry, state);
            state = bridge.Sessions.Current?.State ?? state;
            if (state.Opening?.Phase == OpeningJournalRules.Prepared)
                Apply(store, entry, state);
            state = bridge.Sessions.Current?.State ?? state;
            if (state.Opening?.Phase == OpeningJournalRules.Applied)
                _sameSessionApplied = (NativeObject.Handle(store), state.SaveSlotId,
                    state.GameRunId, state.Revision);
            return;
        }

        if (state.Opening?.Phase != OpeningJournalRules.Applied) return;
        bool sameSession = _sameSessionApplied is { } applied &&
            applied.StorePointer == NativeObject.Handle(store) &&
            applied.Slot == state.SaveSlotId &&
            applied.GameRunId == state.GameRunId &&
            applied.Revision == state.Revision;
        if (!sameSession && bridge.NativeLoadedRevision != state.Revision) return;
        var key = $"{view.Generation}|{state.GameRunId}|{state.SaveSlotId}|{state.Revision}";
        if (_lastAutoCommitAttempt == key) return;
        _lastAutoCommitAttempt = key;
        _sameSessionApplied = null;
        Commit(store, entry, state);
    }

    private void Prepare(PlayerStore store, EmporiumEntry entry, RunState state)
    {
        var data = store.modData;
        if (data == null || !data.TryGetValue(RunState.SaveKey, out var current) || current != RunStateCodec.Write(state)) return;
        var capture = OpeningGrantNativeAdapter.Capture(store, entry, state,
            Specs, initialStateReady: true,
            playerDestinationVerified: true);
        if (capture.Status != OpeningCaptureStatus.Ready || capture.Snapshot == null)
        {
            log($"[WARN] [P2] Opening preflight rejected manifest; status={capture.Status}; "
                + $"error={capture.Error ?? "none"}");
            return;
        }
        // The grant and its receipt are saved together by the game.
        var plan = OpeningSavePlanner.Prepare(state, capture.Snapshot, true,
            data[RunState.SaveKey]);
        if (plan.Status != OpeningSavePlanStatus.Ready || plan.State == null)
        {
            log($"[WARN] [P2] Opening journal rejected; status={plan.Status}; "
                + $"gate={plan.GateStatus}");
            return;
        }
        Save(store, state, plan.State, "prepared");
    }

    private void Apply(PlayerStore store, EmporiumEntry entry, RunState prepared)
    {
        var begun = OpeningProgressRules.BeginApplying(prepared,
            prepared.Opening!.OperationId, prepared.Revision,
            preparedJournalVerified: true);
        if (begun.Status != OpeningProgressStatus.Advanced || begun.State == null ||
            !Save(store, prepared, begun.State, "applying")) return;

        var sessions = bridge.Sessions;
        var state = sessions.Current!.State;
        var lease = sessions.CurrentOpeningLease;
        var binding = new BoundStart(state.RunId, 0,
            state.GameRunId, state.SaveSlotId);
        var economy = OpeningEconomyNativeAdapter.Apply(store, binding, state, lease);
        var economicProgress = OpeningProgressRules.RecordEconomy(state,
            state.Opening!.OperationId, state.Revision, economy);
        if (economicProgress.Status == OpeningProgressStatus.NoJournalObservation &&
            lease != null)
            economicProgress = lease.PlanUnobservedEconomyRecovery(economy);
        log($"[P2] Opening economy observation={economicProgress.Status}; "
            + $"native={economy.Status}");
        if (economicProgress.State == null ||
            economicProgress.Status is not
                (OpeningProgressStatus.Advanced or OpeningProgressStatus.NeedsRecovery) ||
            !Save(store, state, economicProgress.State, "economy")) return;
        if (economicProgress.Status != OpeningProgressStatus.Advanced) return;

        foreach (var recordedGrant in state.Opening!.Grants)
        {
            state = sessions.Current!.State;
            var request = recordedGrant.Request;
            lease = sessions.CurrentOpeningLease;
            var grant = OpeningGrantNativeAdapter.Apply(store, entry,
                binding, state, request, lease);
            var progress = OpeningProgressRules.RecordGrant(state,
                request, state.Revision, grant);
            if (progress.Status == OpeningProgressStatus.NoJournalObservation &&
                lease != null)
                progress = lease.PlanUnobservedGrantRecovery(grant);
            log($"[P2] Opening grant={recordedGrant.Kind}; "
                + $"observation={progress.Status}; native={grant.Status}");
            if (progress.State == null ||
                progress.Status is not
                    (OpeningProgressStatus.Advanced or OpeningProgressStatus.NeedsRecovery) ||
                !Save(store, state, progress.State, recordedGrant.Kind)) return;
            if (progress.Status != OpeningProgressStatus.Advanced) return;
        }
        log("[P2] Opening grants applied; awaiting live-state reconciliation");
    }

    private void Commit(PlayerStore store, EmporiumEntry entry, RunState applied)
    {
        var economy = applied.Opening!.Economy!.After!;
        if (store.modData == null || !store.modData.ContainsKey(RunState.SaveKey) ||
            store.modData[RunState.SaveKey] != RunStateCodec.Write(applied) ||
            !OpeningGrantNativeAdapter.VerifyCompleted(entry, applied) ||
            store.playerCash != economy.Credits || store.rentValue != economy.Rent ||
            StoreReputation.GetSecReputation() != economy.SecurityReputation)
        {
            log("[ERROR] [P2] Opening commit rejected; live native reconciliation failed");
            return;
        }
        var planned = OpeningProgressRules.PrepareCommit(applied,
            applied.Opening.OperationId, applied.Revision,
            appliedJournalVerified: true,
            nativeOpeningReconciledVerified: true);
        if (planned.Status == OpeningProgressStatus.Advanced && planned.State != null)
            Save(store, applied, planned.State, "committed");
    }

    private bool Save(PlayerStore store, RunState previous, RunState next,
        string step)
    {
        var saved = OpeningRevisionNativeSaveAdapter.Apply(store,
            bridge.Sessions, previous, next);
        log($"[P2] Opening step={step}; status={saved.Status}; "
            + $"stage={saved.StageStatus}; save=deferred-until-overnight; error={saved.Error ?? "none"}");
        return saved.Status == OpeningRevisionSaveStatus.Advanced;
    }
}
