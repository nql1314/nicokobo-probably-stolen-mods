namespace AugMechanicalAscension.Domain;

internal sealed record OpeningGrantRequest(string OperationId, string ItemId,
    int Units, int ExpectedOwnedBefore);

internal enum OpeningGrantStatus
{
    Applied, InvalidRequest, IdentityMismatch, InvalidPhase,
    UnverifiedBoundary, Unavailable, SnapshotConflict,
    NoSpace, Partial, Indeterminate
}

internal sealed record OpeningGrantResult(OpeningGrantStatus Status,
    int? OwnedBefore = null, int? OwnedAfter = null,
    int GrantCalls = 0, string? Error = null,
    string? NativeItemInstanceId = null,
    string? OwnershipEvidenceId = null);

// A native port must count only units proved to belong to the player in the
// intended destination. The native transfer return value is only a hint.
// The caller must persist OperationId before applying; this class cannot
// deduplicate a callback after a process restart.
internal interface IOpeningGrantPort
{
    int? ReadOwnedUnits(string itemId);
    bool? CanGrantOne(string itemId);
    void GrantOne(string itemId);
}

internal static class OpeningGrantTransaction
{
    internal static OpeningGrantResult Apply(IOpeningGrantPort port,
        OpeningGrantRequest request)
    {
        if (!Guid.TryParse(request.OperationId, out var operationId) ||
            operationId == Guid.Empty || string.IsNullOrWhiteSpace(request.ItemId) ||
            request.ItemId.Length > AugNumbers.Validation.MaxIdentifierChars || request.Units is < 1 or > AugNumbers.Validation.MaxGrantUnits ||
            request.ExpectedOwnedBefore < 0 ||
            request.ExpectedOwnedBefore > int.MaxValue - request.Units)
            return new(OpeningGrantStatus.InvalidRequest);

        int? before;
        try { before = port.ReadOwnedUnits(request.ItemId); }
        catch (Exception ex)
        {
            return new(OpeningGrantStatus.Unavailable, Error: ex.GetType().Name);
        }
        if (before == null || before < 0)
            return new(OpeningGrantStatus.Unavailable);
        if (before != request.ExpectedOwnedBefore)
            return new(OpeningGrantStatus.SnapshotConflict, before, before);

        int calls = 0;
        for (int unit = 1; unit <= request.Units; unit++)
        {
            try
            {
                bool? canGrant = port.CanGrantOne(request.ItemId);
                if (canGrant == null)
                    return calls == 0
                        ? new(OpeningGrantStatus.Unavailable, before, before, calls)
                        : ObserveFailure(port, request.ItemId, before.Value,
                            calls, "Inventory placement could not be checked");
                if (canGrant == false)
                    return calls == 0
                        ? new(OpeningGrantStatus.NoSpace, before, before, calls)
                        : ObserveFailure(port, request.ItemId, before.Value,
                            calls, "No valid inventory slot");
                calls++;
                port.GrantOne(request.ItemId);
                int? observed = port.ReadOwnedUnits(request.ItemId);
                if (observed != before + unit)
                    return ObserveFailure(port, request.ItemId, before.Value,
                        calls, "Owned unit count did not advance by one");
            }
            catch (Exception ex)
            {
                if (calls == 0)
                    return new(OpeningGrantStatus.Unavailable, before, before,
                        Error: ex.GetType().Name);
                return ObserveFailure(port, request.ItemId, before.Value,
                    calls, ex.GetType().Name);
            }
        }
        return new(OpeningGrantStatus.Applied, before,
            before + request.Units, calls);
    }

    private static OpeningGrantResult ObserveFailure(IOpeningGrantPort port,
        string itemId, int before, int calls, string error)
    {
        try
        {
            int? after = port.ReadOwnedUnits(itemId);
            if (after == null || after < 0)
                return new(OpeningGrantStatus.Indeterminate, before,
                    GrantCalls: calls, Error: error);
            return new(OpeningGrantStatus.Partial, before, after,
                calls, error);
        }
        catch
        {
            return new(OpeningGrantStatus.Indeterminate, before,
                GrantCalls: calls, Error: error);
        }
    }
}
