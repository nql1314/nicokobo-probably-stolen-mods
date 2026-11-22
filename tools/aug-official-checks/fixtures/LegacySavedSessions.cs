using AugMechanicalAscension.Domain;
using AugMechanicalAscension.Persistence;
namespace AugMechanicalAscension.Services;
// Offline compatibility fixtures; these disk-proof methods are not shipped.
internal sealed partial class SessionCoordinator
{
    internal InitialSessionAdoptionResult AdoptReadbackNew(
        InitialSavePlanResult prepared, string? saveDirectory)
    {
        lock (_sync)
        {
            if (_current != null)
                return new(InitialSessionAdoptionStatus.SessionExists);
            if (prepared is not { Status: InitialSavePlanStatus.Ready,
                    Binding: { } binding, State: { } state, Write: { } write } ||
                !Guid.TryParse(binding.Token, out var token) || token == Guid.Empty ||
                state.Revision != 0 || state.InitPhase != "pending" ||
                state.Opening != null ||
                ModDataWriteGate.RecheckPreparedNew(prepared, false, null,
                    binding.GameRunId, binding.SaveSlotId).Status !=
                WriteGateStatus.Ready || write.NextJson != RunStateCodec.Write(state))
                return new(InitialSessionAdoptionStatus.InvalidPlan);
            if (_usedStartTokens.Contains(binding.Token))
                return new(InitialSessionAdoptionStatus.TokenConsumed);
            _usedStartTokens.Add(binding.Token);

            var readback = Es3SlotReadback.Compare(saveDirectory,
                binding.SaveSlotId, binding.GameRunId, write.NextJson,
                new NativeSaveSnapshot(Day: state.StartDay,
                    StartType: StartDefinition.NativeStartTypeId));
            if (readback.Status != SlotReadbackStatus.StateMatched)
                return new(InitialSessionAdoptionStatus.ReadbackMismatch,
                    ReadbackStatus: readback.Status);
            _generation = checked(_generation + 1);
            _current = new SessionView(_generation, state);
            _activeOpeningLease = null;
            return new(InitialSessionAdoptionStatus.Adopted, _current,
                readback.Status);
        }
    }

    internal OpeningSessionAdvanceResult AcceptSavedOpeningRevision(
        RunState previous, RunState next, string? saveDirectory)
    {
        lock (_sync)
        {
            if (_current == null || !ReferenceEquals(_current.State, previous))
                return new(OpeningSessionAdvanceStatus.NoCurrentSession);
            if (previous.Revision == long.MaxValue ||
                ModDataWriteGate.Update(true, RunStateCodec.Write(previous),
                    previous, next, previous.GameRunId, previous.SaveSlotId).Status !=
                WriteGateStatus.Ready)
                return new(OpeningSessionAdvanceStatus.InvalidTransition);

            var journal = next.Opening;
            if (journal == null)
                return new(OpeningSessionAdvanceStatus.InvalidTransition);
            var observed = journal.Grants.Where(x =>
                x.Result is { Status: OpeningGrantStatus.Applied, OwnedAfter: not null }).ToArray();
            var expectedUnits = observed.Length == 0 ? null : observed.ToDictionary(
                x => x.Request.ItemId, x => x.Result!.OwnedAfter!.Value,
                StringComparer.Ordinal);
            var economy = journal.Economy?.After;
            var native = new NativeSaveSnapshot(Day: next.StartDay,
                Cash: economy?.Credits, Rent: economy?.Rent,
                SecurityReputation: economy?.SecurityReputation,
                MainRootUnitCounts: expectedUnits,
                StartType: StartDefinition.NativeStartTypeId);
            var readback = Es3SlotReadback.Compare(saveDirectory, next.SaveSlotId,
                next.GameRunId, RunStateCodec.Write(next), native);
            if (readback.Status != SlotReadbackStatus.StateMatched)
                return new(OpeningSessionAdvanceStatus.ReadbackMismatch,
                    ReadbackStatus: readback.Status);

            _current = new SessionView(_generation, next);
            _activeOpeningLease = null;
            if (OpeningJournalRules.MayStartEconomyAttempt(next))
                _activeOpeningLease = new OpeningAttemptLease(this, _generation,
                    next, null);
            else if (journal.Phase == OpeningJournalRules.Applying)
            {
                var request = journal.Grants.FirstOrDefault(x => x.Result == null)?.Request;
                if (request != null && OpeningJournalRules.MayStartGrantAttempt(next, request))
                    _activeOpeningLease = new OpeningAttemptLease(this, _generation,
                        next, request);
            }
            return new(OpeningSessionAdvanceStatus.Advanced, _activeOpeningLease,
                readback.Status);
        }
    }

    internal RuntimeSessionAdvanceResult AcceptSavedRuntimeRevision(
        RunState previous, RunState next, string? saveDirectory, int nativeDay,
        IReadOnlyDictionary<string, string>? expectedExtraModData = null,
        OwnedCyberneticEffects effects = default, bool mechanicalAscension = false)
    {
        lock (_sync)
        {
            if (_current == null || !ReferenceEquals(_current.State, previous))
                return new(RuntimeSessionAdvanceStatus.NoCurrentSession);
            if (previous.Revision == long.MaxValue ||
                ModDataWriteGate.Update(true, RunStateCodec.Write(previous),
                    previous, next, previous.GameRunId, previous.SaveSlotId,
                    effects, mechanicalAscension).Status !=
                WriteGateStatus.Ready)
                return new(RuntimeSessionAdvanceStatus.InvalidTransition);
            var readback = Es3SlotReadback.Compare(saveDirectory,
                next.SaveSlotId, next.GameRunId, RunStateCodec.Write(next),
                new NativeSaveSnapshot(Day: nativeDay,
                    StartType: StartDefinition.NativeStartTypeId,
                    ExtraModData: expectedExtraModData));
            if (readback.Status != SlotReadbackStatus.StateMatched)
                return new(RuntimeSessionAdvanceStatus.ReadbackMismatch,
                    readback.Status);
            _current = new SessionView(_generation, next);
            _activeOpeningLease = null;
            return new(RuntimeSessionAdvanceStatus.Advanced, readback.Status);
        }
    }

    internal RuntimeSessionAdvanceResult AcceptSavedDebugRevision(
        RunState previous, RunState next, string? saveDirectory, int nativeDay)
    {
        lock (_sync)
        {
            if (_current == null || !ReferenceEquals(_current.State, previous))
                return new(RuntimeSessionAdvanceStatus.NoCurrentSession);
            if (ModDataWriteGate.DebugUpdate(true, RunStateCodec.Write(previous),
                    previous, next, previous.GameRunId, previous.SaveSlotId).Status !=
                WriteGateStatus.Ready)
                return new(RuntimeSessionAdvanceStatus.InvalidTransition);
            var readback = Es3SlotReadback.Compare(saveDirectory,
                next.SaveSlotId, next.GameRunId, RunStateCodec.Write(next),
                new NativeSaveSnapshot(Day: nativeDay,
                    StartType: StartDefinition.NativeStartTypeId));
            if (readback.Status != SlotReadbackStatus.StateMatched)
                return new(RuntimeSessionAdvanceStatus.ReadbackMismatch,
                    readback.Status);
            _current = new SessionView(_generation, next);
            _activeOpeningLease = null;
            return new(RuntimeSessionAdvanceStatus.Advanced,
                readback.Status);
        }
    }

}
