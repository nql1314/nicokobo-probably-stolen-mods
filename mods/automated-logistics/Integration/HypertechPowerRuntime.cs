using static PsModI18n.I18n;

namespace Nicokobo.AutomatedLogistics.Integration;

internal sealed partial class AutomatedLogisticsRuntime
{
    private void GenerateHypertechPower(int day)
    {
        if (!Ready || !IsCurrentRun() || _powerLedgerBusy || day < 0 || day < State.Power.HypertechDay) return;
        var generators = NetworkDevices(Content.PowerDevice).SelectMany(NativeSaveGraphApi.Items)
            .DistinctBy(item => item.Handle()).Where(HypertechBatteryHelper.IsHypertechBattery)
            .Where(item => item.unitCount > 0 && NativeInventoryApi.IsPlayerOwned(item) &&
                !NativeInventoryFreezeApi.IsFrozen(item) && !item.parentInventory!.IsRemoveLocked())
            .Select(item => (Id: item.GetUniqueID(), Output: HypertechBatteryHelper.GetOutput(item)))
            .Where(item => item.Id > 0 && item.Output >= 0 &&
                (State.Power.HypertechDay != day || !State.Power.HypertechGenerators.Contains(item.Id)))
            .OrderBy(item => item.Id).ToArray();
        if (generators.Length == 0) return;
        var change = new StateChange(this);
        _powerLedgerBusy = true;
        try
        {
            var result = NativeTransactionApi.Run([new("hypertech-grid-generation", () =>
            {
                change.Commit(() =>
                {
                    foreach (var generator in generators)
                        State.Power.AddHypertechGeneration(day, generator.Id, generator.Output, PowerCapacity());
                });
                if (!change.Verify()) throw new InvalidOperationException(PsModI18n.I18n.Get("hypertech_generation_could_not_be_saved"));
            }, change.Restore)]);
            if (result.Committed) { _powerStatsDirty = true; NetworkPresentationChanged?.Invoke(); }
            else if (!result.Restored && change.Current())
                NetworkError = PsModI18n.I18n.Get("hypertech_generation_rollback_failed") + result.Status;
        }
        finally { _powerLedgerBusy = false; }
    }
}
