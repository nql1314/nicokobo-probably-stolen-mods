using AugMechanicalAscension.Domain;
using AugMechanicalAscension.Services;

namespace AugMechanicalAscension.Integration;

// Native port for the opening economy transaction. A lease is issued only
// after a fresh same-session revision has matched the native save slot.
// OpeningRuntimeFlow invokes it once per opening revision. No save, retry, or
// rollback here.
internal static class OpeningEconomyNativeAdapter
{
    private const string SecurityFaction = "FACTION_SECURITY";

    internal static OpeningEconomyApplyResult Apply(PlayerStore? store,
        BoundStart binding, RunState? state,
        SessionCoordinator.OpeningAttemptLease? lease)
    {
        if (store == null || binding == null || state == null)
            return new(OpeningEconomyApplyStatus.Unavailable);
        if (!OpeningJournalRules.MayStartEconomyAttempt(state))
            return new(OpeningEconomyApplyStatus.InvalidPhase);
        if (lease == null)
            return new(OpeningEconomyApplyStatus.UnverifiedBoundary);
        try
        {
            if (!Guid.TryParse(binding.GameRunId, out var boundRun) ||
                boundRun == Guid.Empty || !Guid.TryParse(store.runID, out var nativeRun) ||
                nativeRun != boundRun || binding.SaveSlotId < 0 ||
                store.saveSlotId != binding.SaveSlotId ||
                !Guid.TryParse(state.GameRunId, out var stateRun) ||
                stateRun != boundRun || state.SaveSlotId != binding.SaveSlotId ||
                StoreStation.GetDayCounter() != state.StartDay)
                return new(OpeningEconomyApplyStatus.IdentityMismatch);
            if (!lease.TryConsumeEconomy(state))
                return new(OpeningEconomyApplyStatus.UnverifiedBoundary);
            return OpeningEconomyTransaction.Apply(new NativePort(store,
                boundRun, binding.SaveSlotId, state.StartDay),
                state.ConfigSnapshot);
        }
        catch (Exception ex)
        {
            return new(OpeningEconomyApplyStatus.Unavailable, Error: ex.GetType().Name);
        }
    }

    private sealed class NativePort(PlayerStore store, Guid runId, int slotId,
        int day) : IOpeningEconomyPort
    {
        public OpeningEconomySnapshot? Read()
        {
            if (!Current()) return null;
            var security = StoreReputation.GetSecFaction();
            if (security == null) return null;
            double exactSecurity = security.GetReputationExact();
            int visibleSecurity = StoreReputation.GetSecReputation();
            if (!double.IsFinite(exactSecurity) || exactSecurity != visibleSecurity)
                return null;
            return new OpeningEconomySnapshot(store.playerCash, store.rentValue,
                visibleSecurity, NativeReadVerified: true);
        }

        public void AddCash(int delta)
        {
            RequireCurrent();
            store.ModCash(delta, false);
        }

        public void SetRent(int value)
        {
            RequireCurrent();
            store.rentValue = value;
        }

        public void AddSecurityReputation(int delta)
        {
            RequireCurrent();
            // Opening applies an exact configured adjustment to the existing
            // reputation. Gameplay multipliers must not change this delta.
            StoreReputation.ModReputationRaw(SecurityFaction, delta, false);
        }

        private void RequireCurrent()
        {
            if (!Current())
                throw new InvalidOperationException("Native opening run changed");
        }

        private bool Current()
        {
            var current = (PlayerStore.HasInstance ? PlayerStore.Instance : null);
            return current != null && NativeObject.Handle(current) == NativeObject.Handle(store) &&
                Guid.TryParse(store.runID, out var observedRun) &&
                observedRun == runId && store.saveSlotId == slotId &&
                StoreStation.GetDayCounter() == day;
        }
    }
}
