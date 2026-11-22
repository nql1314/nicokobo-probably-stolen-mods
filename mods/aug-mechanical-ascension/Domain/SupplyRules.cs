namespace AugMechanicalAscension.Domain;

internal enum SupplyRefreshStatus { Applied, AlreadyCurrent, DayMismatch, RunNotReady, RunEnded, SpecificationChanged, OperationPending }
internal sealed record SupplyRefreshResult(SupplyRefreshStatus Status, RunState State);

// Inputs here must come from a verified item adapter, never a player-entered estimate.
internal sealed record MedicalSupplyInput(string ItemId, string SpecificationKey, int RecoveryPerUnit, long StandardUnitPrice);

internal static class SupplyRules
{
    internal const string ElectronicItemId = "common_electronic";

    internal static SupplyRefreshResult RefreshMedical(RunState state, int currentDay, MedicalSupplyInput input)
    {
        var status = CheckDay(state, currentDay);
        if (status != null) return new(status.Value, state);
        var previous = state.MedicalSupply;
        if (previous?.PeriodDay == currentDay) return new(SupplyRefreshStatus.AlreadyCurrent, state);
        if (previous?.PeriodDay > currentDay) return new(SupplyRefreshStatus.DayMismatch, state);
        if (string.IsNullOrWhiteSpace(input.ItemId) || input.ItemId.Length > AugNumbers.Validation.MaxIdentifierChars ||
            string.IsNullOrWhiteSpace(input.SpecificationKey) || input.SpecificationKey.Length > AugNumbers.Validation.MaxIdentifierChars ||
            input.RecoveryPerUnit <= 0 || input.StandardUnitPrice <= 0)
            throw new ArgumentException("A verified, effective medical specification and price are required", nameof(input));
        if (previous != null && (previous.ItemId != input.ItemId ||
                                 previous.SpecificationKey != input.SpecificationKey ||
                                 previous.RecoveryPerUnit != input.RecoveryPerUnit))
            return new(SupplyRefreshStatus.SpecificationChanged, state);

        int target = checked((int)((state.ConfigSnapshot.MedicalRecoveryBudget + (long)input.RecoveryPerUnit - 1)
            / input.RecoveryPerUnit));
        var next = new MedicalSupplyState
        {
            PeriodDay = currentDay,
            ItemId = input.ItemId,
            SpecificationKey = input.SpecificationKey,
            RecoveryPerUnit = input.RecoveryPerUnit,
            Stock = Math.Max(previous?.Stock ?? 0, target),
            UnitPrice = Price(input.StandardUnitPrice, state.ConfigSnapshot.SupplyPriceMultiplierBp)
        };
        return new(SupplyRefreshStatus.Applied, state with
        {
            Revision = checked(state.Revision + 1),
            MedicalSupply = next
        });
    }

    internal static SupplyRefreshResult RefreshElectronic(RunState state, int currentDay, long standardUnitPrice)
    {
        var status = CheckDay(state, currentDay);
        if (status != null) return new(status.Value, state);
        var config = state.ConfigSnapshot;
        int period = (currentDay - state.StartDay) / config.ElectronicSupplyCycleDays;
        var previous = state.ElectronicSupply;
        if (previous?.PeriodId == period) return new(SupplyRefreshStatus.AlreadyCurrent, state);
        if (previous?.PeriodId > period) return new(SupplyRefreshStatus.DayMismatch, state);
        if (standardUnitPrice <= 0) throw new ArgumentOutOfRangeException(nameof(standardUnitPrice));
        var next = new ElectronicSupplyState
        {
            PeriodId = period,
            Stock = Math.Max(previous?.Stock ?? 0, config.ElectronicSupplyTargetStock),
            UnitPrice = Price(standardUnitPrice, config.SupplyPriceMultiplierBp)
        };
        return new(SupplyRefreshStatus.Applied, state with
        {
            Revision = checked(state.Revision + 1),
            ElectronicSupply = next
        });
    }

    private static SupplyRefreshStatus? CheckDay(RunState state, int currentDay)
    {
        if (state.FailureReason != "none") return SupplyRefreshStatus.RunEnded;
        if (state.HasPendingExternalOperation)
            return SupplyRefreshStatus.OperationPending;
        if (state.InitPhase != "complete") return SupplyRefreshStatus.RunNotReady;
        if (currentDay < state.StartDay || currentDay != state.LastObservedDay)
            return SupplyRefreshStatus.DayMismatch;
        return null;
    }

    private static long Price(long standardUnitPrice, int multiplierBp)
    {
        decimal raw = (decimal)standardUnitPrice * multiplierBp / 10_000m;
        decimal rounded = decimal.Round(raw, 0, MidpointRounding.AwayFromZero);
        if (rounded > int.MaxValue)
            throw new OverflowException("Supply quote exceeds the game credit range");
        return Math.Max(1, (long)rounded);
    }
}
