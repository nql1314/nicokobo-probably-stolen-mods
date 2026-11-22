using Nicokobo.ContentInterop;

namespace MechcoreProtocol.SynthesisExpansion;

internal enum PowerDebit { Committed, Rejected, Faulted }

internal static class SynthesisPower
{
    private static readonly HashSet<GameItem> Faulted = new();
    internal static void Reset() => Faulted.Clear();
    internal static bool IsFaulted(GameItem source) => Faulted.Contains(source);
    internal static int Energy(GameItem source)
    {
        if (DevicePower.TryEnergy(source, out int sharedEnergy)) return sharedEnergy;
        if (HypertechBatteryHelper.IsHypertechBattery(source)) return HypertechBatteryHelper.GetOutput(source);
        if (CurrentConverterHelper.IsCurrentConverter(source))
        {
            if (SynthesisMod.Store == null || !CurrentConverterHelper.CanSupply(source, 1)) return 0;
            if (CurrentConverterHelper.isNightCycle) return int.MaxValue;
            float rate = CurrentConverterHelper.GetRate(source);
            return rate > 0 ? (int)Math.Min(int.MaxValue, Math.Floor(SynthesisMod.Store.playerCash / (double)rate)) : 0;
        }
        return PowerHelper.GetAvailableEnergyFromItem(source, false);
    }

    internal static PowerDebit Debit(GameSlotInventory slot, int amount)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        var source = slot.childItem;
        if (source == null || source.unitCount != 1 || !GeneralHelper.IsItemOwned(source)) return PowerDebit.Rejected;
        if (Faulted.Contains(source)) return PowerDebit.Faulted;
        try
        {
            if (DevicePower.TryDebit(source, amount, out bool sharedDrawn))
                return sharedDrawn ? PowerDebit.Committed : PowerDebit.Rejected;
        }
        catch (Exception error)
        {
            // A recognized provider's failure must never fall through to a
            // second native debit, nor be restored with a local battery tag.
            SynthesisMod.Warn("Shared device power debit failed: " + error.Message);
            Faulted.Add(source);
            return PowerDebit.Faulted;
        }
        int before = Energy(source);
        if (before < amount || !PowerHelper.CanDrawPowerSource(source, amount)) return PowerDebit.Rejected;
        if (amount == 0) return PowerDebit.Committed;
        bool physical = source.IsTag("power_source_item") && !CurrentConverterHelper.IsCurrentConverter(source) &&
            !HypertechBatteryHelper.IsHypertechBattery(source);
        try
        {
            bool drawn = PowerHelper.DrawPowerSource(source, amount);
            int after = Energy(source);
            if (drawn && source.parentInventory == slot &&
                (after == before - amount || HypertechBatteryHelper.IsHypertechBattery(source) ||
                 CurrentConverterHelper.IsCurrentConverter(source))) return PowerDebit.Committed;
            if (!drawn && after == before) return PowerDebit.Rejected;
        }
        catch (Exception error) { SynthesisMod.Warn("Device power debit failed: " + error.Message); }
        if (physical)
        {
            try
            {
                PowerHelper.SetPowerSourceAt(source, before);
                if (source.parentInventory == slot && Energy(source) == before) return PowerDebit.Rejected;
            }
            catch (Exception error) { SynthesisMod.Warn("Device power restore failed: " + error.Message); }
        }
        Faulted.Add(source);
        return PowerDebit.Faulted;
    }
}
