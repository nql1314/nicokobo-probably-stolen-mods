using AugMechanicalAscension.Domain;
using AugMechanicalAscension.Persistence;

namespace AugMechanicalAscension.Services;

internal sealed record SessionView(long Generation, RunState State);

internal enum InitialSessionAdoptionStatus
{
    Adopted, InvalidPlan, SessionExists, TokenConsumed, ReadbackMismatch
}

internal sealed record InitialSessionAdoptionResult(InitialSessionAdoptionStatus Status,
    SessionView? Session = null, SlotReadbackStatus? ReadbackStatus = null);

internal enum OpeningSessionAdvanceStatus
{
    Advanced, NoCurrentSession, InvalidTransition, ReadbackMismatch
}

internal sealed record OpeningSessionAdvanceResult(OpeningSessionAdvanceStatus Status,
    SessionCoordinator.OpeningAttemptLease? Lease = null,
    SlotReadbackStatus? ReadbackStatus = null);

internal enum RuntimeSessionAdvanceStatus
{
    Advanced, NoCurrentSession, InvalidTransition, ReadbackMismatch
}

internal sealed record RuntimeSessionAdvanceResult(RuntimeSessionAdvanceStatus Status,
    SlotReadbackStatus? ReadbackStatus = null);

// Managed-only lifecycle. Missing or damaged save data never creates a new Mod run.
internal sealed partial class SessionCoordinator
{
    private readonly HashSet<string> _usedStartTokens = new(StringComparer.Ordinal);
    private readonly object _sync = new();
    private long _generation;
    private SessionView? _current;
    private OpeningAttemptLease? _activeOpeningLease;

    internal SessionView? Current
    {
        get { lock (_sync) return _current; }
    }

    internal OpeningAttemptLease? CurrentOpeningLease
    {
        get { lock (_sync) return _activeOpeningLease; }
    }

    // Managed pre-save construction only. After InitialSave has staged a
    // specific state, use AdoptInitialState to preserve that state's RunId.
    internal SessionView? BeginNew(BoundStart binding, string buildProfileId, int startDay, ConfigSnapshot config)
    {
        lock (_sync)
        {
            if (_current != null || string.IsNullOrWhiteSpace(binding.Token) || _usedStartTokens.Contains(binding.Token))
                return null;

            var state = RunStateCodec.CreateNew(buildProfileId, binding.GameRunId, binding.SaveSlotId, startDay, config);
            _generation = checked(_generation + 1);
            _usedStartTokens.Add(binding.Token);
            _current = new SessionView(_generation, state);
            return _current;
        }
    }

    // Use the exact state staged for InitialSave. CreateNew would generate a
    // different Mod RunId and cannot be used after the first native save.


    internal InitialSessionAdoptionResult AdoptInitialState(InitialSavePlanResult prepared)
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

            _generation = checked(_generation + 1);
            _current = new SessionView(_generation, state);
            _activeOpeningLease = null;
            return new(InitialSessionAdoptionStatus.Adopted, _current);
        }
    }

    internal ReadResult Restore(string? json, string gameRunId, int saveSlotId)
        => RestoreReadResult(RunStateCodec.ReadForGame(json, gameRunId, saveSlotId), gameRunId, saveSlotId);

    // A native adapter may pass the already validated result from ModDataReader.
    internal ReadResult RestoreReadResult(ReadResult result, string gameRunId, int saveSlotId)
    {
        lock (_sync)
        {
            _generation = checked(_generation + 1);
            _current = null;
            _activeOpeningLease = null;
            if (result.Status == ReadStatus.Ready)
            {
                if (result.State == null)
                    return new(ReadStatus.Corrupt, null, "Ready result has no state");
                if (result.State.GameRunId != gameRunId || result.State.SaveSlotId != saveSlotId)
                    return new(ReadStatus.RunMismatch, null, "Native save identity changed during restore");
                _current = new SessionView(_generation, result.State);
            }
            return result;
        }
    }

    internal bool IsCurrent(long generation, string runId)
    {
        lock (_sync)
            return _current?.Generation == generation && _current.State.RunId == runId;
    }

    internal void Clear()
    {
        lock (_sync)
        {
            _generation = checked(_generation + 1);
            _current = null;
            _activeOpeningLease = null;
        }
    }

    // A delayed save failure must not erase a newer run or a different state.
    internal bool ClearIfCurrent(RunState expected)
    {
        lock (_sync)
        {
            if (_current == null || !ReferenceEquals(_current.State, expected))
                return false;
            _generation = checked(_generation + 1);
            _current = null;
            _activeOpeningLease = null;
            return true;
        }
    }



    // Fresh opening operations advance only after their exact live journal is
    // staged. Restoring an applying journal never grants an attempt lease.
    internal OpeningSessionAdvanceResult AcceptStagedOpeningRevision(
        RunState previous, RunState next, string? stagedJson)
    {
        lock (_sync)
        {
            if (_current == null || !ReferenceEquals(_current.State, previous))
                return new(OpeningSessionAdvanceStatus.NoCurrentSession);
            if (next.Opening == null || stagedJson != RunStateCodec.Write(next) ||
                ModDataWriteGate.Update(true, RunStateCodec.Write(previous), previous,
                    next, previous.GameRunId, previous.SaveSlotId).Status != WriteGateStatus.Ready)
                return new(OpeningSessionAdvanceStatus.InvalidTransition);
            _current = new SessionView(_generation, next);
            _activeOpeningLease = null;
            if (OpeningJournalRules.MayStartEconomyAttempt(next))
                _activeOpeningLease = new OpeningAttemptLease(this, _generation, next, null);
            else if (next.Opening.Phase == OpeningJournalRules.Applying)
            {
                var request = next.Opening.Grants.FirstOrDefault(x => x.Result == null)?.Request;
                if (request != null && OpeningJournalRules.MayStartGrantAttempt(next, request))
                    _activeOpeningLease = new OpeningAttemptLease(this, _generation, next, request);
            }
            return new(OpeningSessionAdvanceStatus.Advanced, _activeOpeningLease);
        }
    }

    // Gameplay revisions advance the live session and modData together.
    // Inventory and Mod state reach disk at the game's normal save boundary.
    internal RuntimeSessionAdvanceResult AcceptStagedRuntimeRevision(
        RunState previous, RunState next, string? stagedJson, int nativeDay,
        OwnedCyberneticEffects effects = default, bool mechanicalAscension = false,
        bool debug = false)
    {
        lock (_sync)
        {
            if (_current == null || !ReferenceEquals(_current.State, previous))
                return new(RuntimeSessionAdvanceStatus.NoCurrentSession);
            var gate = debug
                ? ModDataWriteGate.DebugUpdate(true, RunStateCodec.Write(previous),
                    previous, next, previous.GameRunId, previous.SaveSlotId)
                : ModDataWriteGate.Update(true, RunStateCodec.Write(previous),
                    previous, next, previous.GameRunId, previous.SaveSlotId, effects, mechanicalAscension);
            if (next.LastObservedDay != nativeDay || next.InitPhase != "complete" ||
                next.FailureReason != "none" || gate.Status != WriteGateStatus.Ready ||
                stagedJson != RunStateCodec.Write(next))
                return new(RuntimeSessionAdvanceStatus.InvalidTransition);
            _current = new SessionView(_generation, next);
            _activeOpeningLease = null;
            return new(RuntimeSessionAdvanceStatus.Advanced);
        }
    }



    // A terminal revision disables live gameplay and displays the ending, but
    // never replaces the last playable slot. Only a validated transition of the
    // current session can enter this state; no disk readback is claimed here.
    internal RuntimeSessionAdvanceResult AcceptRuntimeFailure(
        RunState previous, RunState next, int nativeDay,
        OwnedCyberneticEffects effects = default, bool mechanicalAscension = false,
        bool debug = false)
    {
        lock (_sync)
        {
            if (_current == null || !ReferenceEquals(_current.State, previous))
                return new(RuntimeSessionAdvanceStatus.NoCurrentSession);
            if (next.InitPhase != "complete" || next.LastObservedDay != nativeDay ||
                next.FailureReason is not ("bodyCollapse" or "exposure"))
                return new(RuntimeSessionAdvanceStatus.InvalidTransition);
            string expected = RunStateCodec.Write(previous);
            var gate = debug
                ? ModDataWriteGate.DebugUpdate(true, expected, previous, next,
                    previous.GameRunId, previous.SaveSlotId)
                : ModDataWriteGate.Update(true, expected, previous, next,
                    previous.GameRunId, previous.SaveSlotId, effects, mechanicalAscension);
            if (gate.Status != WriteGateStatus.Ready)
                return new(RuntimeSessionAdvanceStatus.InvalidTransition);
            _current = new SessionView(_generation, next);
            _activeOpeningLease = null;
            return new(RuntimeSessionAdvanceStatus.Advanced);
        }
    }



    internal sealed class OpeningAttemptLease
    {
        private readonly SessionCoordinator _owner;
        private readonly long _generation;
        private readonly RunState _state;
        private readonly string _runId;
        private readonly string _gameRunId;
        private readonly int _slotId;
        private readonly long _revision;
        private readonly string _operationId;
        private readonly OpeningGrantRequest? _request;
        private bool _consumed;

        internal OpeningAttemptLease(SessionCoordinator owner, long generation,
            RunState state, OpeningGrantRequest? request)
        {
            _owner = owner;
            _generation = generation;
            _state = state;
            _runId = state.RunId;
            _gameRunId = state.GameRunId;
            _slotId = state.SaveSlotId;
            _revision = state.Revision;
            _operationId = state.Opening!.OperationId;
            _request = request;
        }

        internal bool TryConsumeEconomy(RunState state) => TryConsume(state, null);

        internal bool TryConsumeGrant(RunState state, OpeningGrantRequest request)
            => request != null && TryConsume(state, request);

        internal OpeningProgressResult PlanUnobservedEconomyRecovery(
            OpeningEconomyApplyResult result)
        {
            lock (_owner._sync)
                return _consumed && StillBound(_state, null)
                    ? OpeningProgressRules.MarkUnobservedEconomy(_state,
                        _operationId, _revision, result)
                    : new(OpeningProgressStatus.UnverifiedBoundary, _state);
        }

        internal OpeningProgressResult PlanUnobservedGrantRecovery(
            OpeningGrantResult result)
        {
            lock (_owner._sync)
                return _consumed && _request != null && StillBound(_state, _request)
                    ? OpeningProgressRules.MarkUnobservedGrant(_state,
                        _request, _revision, result)
                    : new(OpeningProgressStatus.UnverifiedBoundary, _state);
        }

        private bool TryConsume(RunState state, OpeningGrantRequest? request)
        {
            lock (_owner._sync)
            {
                if (_consumed || !StillBound(state, request))
                    return false;
                _consumed = true;
                return true;
            }
        }

        private bool StillBound(RunState state, OpeningGrantRequest? request)
            => ReferenceEquals(_owner._activeOpeningLease, this) &&
               _owner._current?.Generation == _generation &&
               ReferenceEquals(_owner._current.State, _state) &&
               ReferenceEquals(state, _state) && _request == request &&
               state.RunId == _runId && state.GameRunId == _gameRunId &&
               state.SaveSlotId == _slotId && state.Revision == _revision &&
               state.Opening?.OperationId == _operationId;
    }
}
