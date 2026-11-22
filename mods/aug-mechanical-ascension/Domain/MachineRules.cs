namespace AugMechanicalAscension.Domain;

internal enum MachineStatus
{
    Applied, Duplicate, ModuleMissing, ModuleAlreadyConnected,
    MachineAlreadyConnected, UnsupportedModel, NotOwned, NotConnected,
    InvalidInput, RunMismatch, RevisionConflict, RunNotReady, RunEnded,
    DayMismatch, OperationPending
}

internal sealed record MachineResult(MachineStatus Status, RunState State);

internal sealed record MachineConnectionSnapshot(int GameDay,
    string ModuleInstanceId, string MachineInstanceId, string ModelId,
    bool ModuleOwnedVerified, bool MachineOwnedVerified,
    string OwnershipEvidenceId);

// Each saved module receipt names a real item instance. The native adapter must
// recheck current ownership for both objects before changing a connection.
internal static class MachineRules
{
    internal static MachineResult Connect(RunState state, string expectedRunId,
        long expectedRevision, MachineConnectionSnapshot snapshot)
    {
        if (state.RunId != expectedRunId) return new(MachineStatus.RunMismatch, state);
        if (state.Revision != expectedRevision)
            return new(MachineStatus.RevisionConflict, state);
        if (state.InitPhase != "complete") return new(MachineStatus.RunNotReady, state);
        if (state.FailureReason != "none") return new(MachineStatus.RunEnded, state);
        if (state.HasPendingExternalOperation)
            return new(MachineStatus.OperationPending, state);
        if (snapshot == null || !ValidText(snapshot.ModuleInstanceId) ||
            !ValidText(snapshot.MachineInstanceId) || !ValidText(snapshot.ModelId) ||
            !ValidText(snapshot.OwnershipEvidenceId) ||
            snapshot.ModuleInstanceId == snapshot.MachineInstanceId)
            return new(MachineStatus.InvalidInput, state);
        if (snapshot.GameDay != state.LastObservedDay)
            return new(MachineStatus.DayMismatch, state);
        if (!snapshot.ModuleOwnedVerified || !snapshot.MachineOwnedVerified)
            return new(MachineStatus.NotOwned, state);
        if (!state.NeuralModules.Receipts.Any(x =>
                x.InstanceId == snapshot.ModuleInstanceId))
            return new(MachineStatus.ModuleMissing, state);
        if (!state.ConfigSnapshot.MachineProfiles.Any(x =>
                x.ModelId == snapshot.ModelId))
            return new(MachineStatus.UnsupportedModel, state);
        if (state.Machines.Connections.Any(x =>
                x.ModuleInstanceId == snapshot.ModuleInstanceId))
            return new(MachineStatus.ModuleAlreadyConnected, state);
        if (state.Machines.Connections.Any(x =>
                x.ItemInstanceId == snapshot.MachineInstanceId))
            return new(MachineStatus.MachineAlreadyConnected, state);
        if (state.Machines.NextConnectionEpoch == long.MaxValue ||
            state.Revision == long.MaxValue)
            return new(MachineStatus.InvalidInput, state);
        var connection = new MachineConnection(snapshot.ModuleInstanceId,
            snapshot.MachineInstanceId, snapshot.ModelId,
            state.Machines.NextConnectionEpoch, snapshot.GameDay,
            snapshot.OwnershipEvidenceId);
        return Change(state, state.Machines with
        {
            NextConnectionEpoch = state.Machines.NextConnectionEpoch + 1,
            Connections = [.. state.Machines.Connections, connection]
        });
    }

    // The write gate checks the exact connection delta independently of the
    // machine-cycle rules. Native ownership evidence is still the adapter's job.
    internal static bool ValidTransition(RunState previous, RunState next)
    {
        var before = previous.Machines;
        var after = next.Machines;
        if (before?.Connections == null || after?.Connections == null)
            return false;
        if (before.Connections.SequenceEqual(after.Connections))
            return before.NextConnectionEpoch == after.NextConnectionEpoch;
        if (before.NextConnectionEpoch == long.MaxValue ||
            after.NextConnectionEpoch != before.NextConnectionEpoch + 1)
            return false;
        if (after.Connections.Length == before.Connections.Length + 1)
        {
            var added = after.Connections[^1];
            return before.Connections.SequenceEqual(after.Connections.SkipLast(1)) &&
                added.Epoch == before.NextConnectionEpoch &&
                added.ConnectedDay == next.LastObservedDay &&
                next.NeuralModules.Receipts.Any(x =>
                    x.InstanceId == added.ModuleInstanceId) &&
                next.ConfigSnapshot.MachineProfiles.Any(x =>
                    x.ModelId == added.ModelId) &&
                !before.Connections.Any(x =>
                    x.ModuleInstanceId == added.ModuleInstanceId ||
                    x.ItemInstanceId == added.ItemInstanceId) &&
                ValidText(added.OwnershipEvidenceId);
        }
        if (after.Connections.Length != before.Connections.Length - 1)
            return false;
        var afterSet = new HashSet<MachineConnection>(after.Connections);
        var removed = before.Connections.FirstOrDefault(x => !afterSet.Contains(x));
        if (removed == null || !after.Connections.SequenceEqual(
                before.Connections.Where(x => x != removed)))
            return false;
        return true;
    }

    private static MachineResult Change(RunState state, MachineState machines)
        => new(MachineStatus.Applied, state with
        { Revision = state.Revision + 1, Machines = machines });

    internal static bool ValidText(string? value)
        => !string.IsNullOrWhiteSpace(value) && value.Length <= AugNumbers.Validation.MaxIdentifierChars;
}
