using MechcoreProtocol.SynthesisExpansion.Definitions;

namespace MechcoreProtocol.SynthesisExpansion;

internal static class SynthesisCyberEye
{
    private static readonly HashSet<string> Paid = new(StringComparer.Ordinal);
    private static (PlayerStore? Store, int Slot, string Run) _run;
    internal static void Reset() { Paid.Clear(); _run = default; }

    internal static void Tooltip(RichTextBuilder builder, GameItem item)
    {
        var store = SynthesisMod.Store;
        if (store == null || item.uniqueId <= 0 || !CyberEyeScan.IsSupportedItemId(item.identifier)) return;
        var eyes = store.FindAllItem().Where(eye => eye.identifier == SynthesisDefinitions.CyberEye.Id &&
            eye.unitCount > 0 && GeneralHelper.IsItemOwned(eye)).ToArray();
        if (eyes.Length == 0) return;
        var scan = CyberEyeScan.Capture(item);
        if (scan == null) return;
        var run = (store, store.saveSlotId, store.runID);
        if (_run != run) { Paid.Clear(); _run = run; }
        GameSlotInventory? best = null;
        int bestEnergy = -1;
        foreach (var eye in eyes)
        {
            var slot = AugScanner.GetBatterySlot(eye);
            var source = slot?.childItem;
            if (source == null || source.unitCount != 1 || source.parentInventory != slot || !GeneralHelper.IsItemOwned(source) ||
                SynthesisPower.IsFaulted(source)) continue;
            int energy = SynthesisPower.Energy(source);
            if (energy > bestEnergy) { best = slot; bestEnergy = energy; }
            if (energy >= scan.Cost) break;
        }
        if (best == null)
        {
            builder.AddLine(SynthesisText.Get("synthesis.install_an_available_power_source_in_the"));
            return;
        }
        if (!Paid.Contains(scan.Fingerprint))
        {
            if (bestEnergy < scan.Cost)
            {
                builder.AddLine(SynthesisText.Get("synthesis.high_tech_glasses_power_too_low") + $"{bestEnergy}/{scan.Cost}");
                return;
            }
            if (SynthesisPower.Debit(best, scan.Cost) != PowerDebit.Committed) return;
            Paid.Add(scan.Fingerprint);
        }
        builder.AddLine(SynthesisText.Get("synthesis.high_tech_glasses_value") + scan.Value.ToString("N0"));
        foreach (string display in scan.RealDisplays)
            builder.AddLine(SynthesisText.Get("synthesis.high_tech_glasses_scan") + display);
    }
}
