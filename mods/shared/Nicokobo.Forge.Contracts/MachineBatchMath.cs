namespace Nicokobo.ContentInterop;
internal static class MachineBatchMath
{
    internal static NativeMachineLiquidSnapshot Consume(NativeMachineLiquidSnapshot before, int parts, string? liquidId)
    {
        if (parts < 0 || before.Contents.Any(part => part.Parts < 0 || part.Value < 0 || part.QualityBasis < 0 ||
            part.QualityBasis != null && part.Value == null) || parts > before.TotalParts)
            throw new ArgumentOutOfRangeException(nameof(parts));
        if (parts == 0) return before;
        var amounts = before.Contents.Select(part => part.Parts).ToArray();
        if (liquidId != null)
        {
            int index = before.Contents.ToList().FindIndex(part => part.LiquidId == liquidId);
            if (index < 0 || amounts[index] < parts) throw new ArgumentOutOfRangeException(nameof(liquidId));
            amounts[index] -= parts;
        }
        else
        {
            // Exact proportional draw. Largest fractional remainders receive
            // the leftover parts, so rounding never creates or loses volume.
            var draws = amounts.Select(amount => (int)((long)amount * parts / before.TotalParts)).ToArray();
            int leftover = parts - draws.Sum();
            foreach (int index in Enumerable.Range(0, amounts.Length).OrderByDescending(index =>
                (long)amounts[index] * parts % before.TotalParts).ThenBy(index => index).Take(leftover)) draws[index]++;
            for (int index = 0; index < amounts.Length; index++) amounts[index] -= draws[index];
        }
        return before with { Contents = Array.AsReadOnly(before.Contents.Select((part, index) =>
            part with { Parts = amounts[index], Value = part.Value == null || amounts[index] == 0 ? null :
                NativeProductionValueMath.RemainingLiquidValue(part.Value.Value, part.Parts, amounts[index]),
                QualityBasis = part.QualityBasis == null || amounts[index] == 0 ? null :
                    NativeProductionValueMath.RemainingLiquidValue(part.QualityBasis.Value, part.Parts, amounts[index]) }).ToArray()) };
    }

    internal static NativeMachineLiquidSnapshot Add(NativeMachineLiquidSnapshot before,
        IReadOnlyList<NativeMachineLiquidPart> contents)
    {
        var amounts = before.Contents.ToDictionary(part => part.LiquidId, part => part.Parts, StringComparer.Ordinal);
        var values = before.Contents.ToDictionary(part => part.LiquidId, part => part.Value, StringComparer.Ordinal);
        var bases = before.Contents.ToDictionary(part => part.LiquidId, part => part.QualityBasis, StringComparer.Ordinal);
        foreach (var part in contents)
        {
            if (part.Parts <= 0 || part.Value < 0 || part.QualityBasis < 0 ||
                part.QualityBasis != null && part.Value == null || !amounts.ContainsKey(part.LiquidId))
                throw new ArgumentOutOfRangeException(nameof(contents));
            if (part.QualityBasis != null || bases[part.LiquidId] != null)
                bases[part.LiquidId] = checked((bases[part.LiquidId] ?? values[part.LiquidId] ??
                    NativeProductionValueMath.NativeLiquidValue(amounts[part.LiquidId])) +
                    (part.QualityBasis ?? part.Value ?? NativeProductionValueMath.NativeLiquidValue(part.Parts)));
            if (part.Value != null || values[part.LiquidId] != null)
                values[part.LiquidId] = checked((values[part.LiquidId] ??
                    NativeProductionValueMath.NativeLiquidValue(amounts[part.LiquidId])) +
                    (part.Value ?? NativeProductionValueMath.NativeLiquidValue(part.Parts)));
            amounts[part.LiquidId] = checked(amounts[part.LiquidId] + part.Parts);
        }
        var result = before with { Contents = Array.AsReadOnly(before.Contents.Select(part =>
            part with { Parts = amounts[part.LiquidId], Value = values[part.LiquidId], QualityBasis = bases[part.LiquidId] }).ToArray()) };
        if (result.TotalParts > result.CapacityParts) throw new ArgumentOutOfRangeException(nameof(contents));
        return result;
    }

    internal static decimal BaseValue(NativeMachineLiquidSnapshot snapshot) =>
        snapshot.Contents.Sum(part => part.Value ?? NativeProductionValueMath.NativeLiquidValue(part.Parts));
    internal static decimal QualityBasis(NativeMachineLiquidSnapshot snapshot) =>
        snapshot.Contents.Sum(part => part.QualityBasis ?? part.Value ?? NativeProductionValueMath.NativeLiquidValue(part.Parts));

    /// <summary>Value follows the actual transferred component volumes. Other
    /// liquids keep their own values and an untracked source uses native pricing.</summary>
    internal static (NativeMachineLiquidSnapshot Source, NativeMachineLiquidSnapshot Target) TransferValues(
        NativeMachineLiquidSnapshot sourceBefore, NativeMachineLiquidSnapshot sourceAfter,
        NativeMachineLiquidSnapshot targetBefore, NativeMachineLiquidSnapshot targetAfter)
    {
        var source = new List<NativeMachineLiquidPart>();
        var target = new List<NativeMachineLiquidPart>();
        foreach (var from in sourceBefore.Contents)
        {
            var remaining = sourceAfter.Contents.Single(part => part.LiquidId == from.LiquidId);
            var to = targetBefore.Contents.Single(part => part.LiquidId == from.LiquidId);
            var received = targetAfter.Contents.Single(part => part.LiquidId == from.LiquidId);
            int removed = from.Parts - remaining.Parts, added = received.Parts - to.Parts;
            if (removed < 0 || added < 0 || added > removed)
                throw new InvalidOperationException("Liquid transfer volumes changed unexpectedly");
            decimal? remainingValue = from.Value == null || remaining.Parts == 0 ? null : from.Parts == 0 ? 0 :
                NativeProductionValueMath.RemainingLiquidValue(from.Value.Value, from.Parts, remaining.Parts);
            decimal? remainingBasis = from.QualityBasis == null || remaining.Parts == 0 ? null : from.Parts == 0 ? 0 :
                NativeProductionValueMath.RemainingLiquidValue(from.QualityBasis.Value, from.Parts, remaining.Parts);
            decimal? receivedValue = to.Value;
            decimal? receivedBasis = to.QualityBasis;
            if (added > 0 && (from.QualityBasis != null || to.QualityBasis != null))
                receivedBasis = checked((to.QualityBasis ?? to.Value ?? NativeProductionValueMath.NativeLiquidValue(to.Parts)) +
                    (from.QualityBasis == null ? (from.Value == null ? NativeProductionValueMath.NativeLiquidValue(added) :
                        from.Value.Value * added / from.Parts) : from.QualityBasis.Value * added / from.Parts));
            if (added > 0 && (from.Value != null || to.Value != null))
                receivedValue = checked((to.Value ?? NativeProductionValueMath.NativeLiquidValue(to.Parts)) +
                    (from.Value == null ? NativeProductionValueMath.NativeLiquidValue(added) :
                        from.Value.Value * added / from.Parts));
            source.Add(remaining with { Value = remainingValue, QualityBasis = remainingBasis });
            target.Add(received with { Value = receivedValue, QualityBasis = receivedBasis });
        }
        return (sourceAfter with { Contents = Array.AsReadOnly(source.ToArray()) },
            targetAfter with { Contents = Array.AsReadOnly(target.ToArray()) });
    }
}
