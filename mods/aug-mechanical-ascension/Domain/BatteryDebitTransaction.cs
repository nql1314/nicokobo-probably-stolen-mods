namespace AugMechanicalAscension.Domain;

internal sealed record BatteryDebitRequest(string RequestId, int Cost,
    int ExpectedCharge);

internal sealed record BatteryDebitSnapshot(int Charge, int Available);

internal enum BatteryDebitStatus
{
    Applied, InvalidRequest, Unavailable, SnapshotConflict, NoPower,
    Partial, Indeterminate
}

internal sealed record BatteryDebitResult(BatteryDebitStatus Status,
    BatteryDebitRequest? Request, BatteryDebitSnapshot? Before = null,
    BatteryDebitSnapshot? After = null, int? NativeReturned = null,
    bool NativeCallAttempted = false, string? Error = null);

// Read must verify that the same native battery is still attached to the
// intended eye tool. Remove may partially debit even if it returns less
// than requested. This transaction never retries after a native call.
internal interface IBatteryDebitPort
{
    BatteryDebitSnapshot? Read();
    int Remove(int cost);
}

internal static class BatteryDebitTransaction
{
    internal static BatteryDebitResult Apply(IBatteryDebitPort port,
        BatteryDebitRequest request)
    {
        if (port == null || request == null ||
            !Guid.TryParse(request.RequestId, out var id) || id == Guid.Empty ||
            request.Cost is < 1 or > AugNumbers.Validation.MaxBatteryDebit ||
            request.ExpectedCharge is < 0 or > AugNumbers.Validation.MaxBatteryCharge)
            return new(BatteryDebitStatus.InvalidRequest, request);

        BatteryDebitSnapshot? before = null;
        bool attempted = false;
        int? returned = null;
        try
        {
            before = port.Read();
            if (!Valid(before))
                return new(BatteryDebitStatus.Unavailable, request);
            if (before!.Charge != request.ExpectedCharge)
                return new(BatteryDebitStatus.SnapshotConflict, request, before);
            if (before.Available < request.Cost)
                return new(BatteryDebitStatus.NoPower, request, before);

            // Recheck immediately before the irreversible native call.
            var fresh = port.Read();
            if (!Valid(fresh))
                return new(BatteryDebitStatus.Unavailable, request, before);
            if (fresh != before)
                return new(BatteryDebitStatus.SnapshotConflict, request,
                    before, fresh);

            attempted = true;
            returned = port.Remove(request.Cost);
            var after = port.Read();
            if (!Valid(after))
                return new(BatteryDebitStatus.Indeterminate, request,
                    before, NativeReturned: returned,
                    NativeCallAttempted: true,
                    Error: "Battery readback unavailable");
            return new(returned == request.Cost &&
                after!.Charge == before.Charge - request.Cost
                    ? BatteryDebitStatus.Applied : BatteryDebitStatus.Partial,
                request, before, after, returned, true);
        }
        catch (Exception ex)
        {
            if (!attempted)
                return new(BatteryDebitStatus.Unavailable, request,
                    before, Error: ex.GetType().Name);
            try
            {
                var after = port.Read();
                if (Valid(after))
                    return new(BatteryDebitStatus.Partial, request,
                        before, after, returned, true, ex.GetType().Name);
            }
            catch { /* The native call may have changed power already. */ }
            return new(BatteryDebitStatus.Indeterminate, request,
                before, NativeReturned: returned, NativeCallAttempted: true,
                Error: ex.GetType().Name);
        }
    }

    private static bool Valid(BatteryDebitSnapshot? snapshot)
        => snapshot != null && snapshot.Charge is >= 0 and <= AugNumbers.Validation.MaxBatteryCharge &&
           snapshot.Available is >= 0 and <= AugNumbers.Validation.MaxBatteryCharge &&
           snapshot.Available <= snapshot.Charge;
}
