using Nicokobo.ContentInterop;
namespace Nicokobo.AutomatedLogistics.Native;
internal static class NativeExternalPowerApi
{
    internal static bool IsAvailable => true;
    internal static IDisposable RegisterMachine(string owner, string key, Func<GameItem, int> energy, Func<GameItem, int, INativePowerTransaction?> plan) =>
        ForgePower.Register(owner, energy, (item, cost) =>
        {
            var receipt = plan(item, cost);
            return receipt == null ? null : new object[] { (Func<bool>)receipt.Validate, (Action)receipt.Debit,
                (Func<bool>)receipt.Verify, (Func<bool>)receipt.Restore, (Action<string>)receipt.Fault, (Action)receipt.Dispose };
        });
    internal static GameItem? GetDevicePowerSource(GameItem device) => ForgePower.Source(device);
    internal static bool CanSupplyDevice(GameItem device) => ForgePower.CanSupply(device);
}
