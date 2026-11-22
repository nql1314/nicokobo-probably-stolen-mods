namespace Nicokobo.AutomatedLogistics;

internal sealed record AutomatedLogisticsParameters(int PowerWidth, int PowerHeight);

internal static class AutomatedLogisticsNumbers
{
    internal const int ResourceWidth = 20, ResourceInitialRows = 5, ResourceMaxRows = 20;
    internal const int PowerMaxRows = 16, WaterMaxParts = 1_000_000_000;
    internal const int PowerEnergyPerRow = 1000; // kJ per shared grid row.
    internal const int WaterInitialParts = 10_000_000; // 10 L, 1000 native parts/ml.
    // Shared defaults for normal builds and offline checks.
    internal static readonly AutomatedLogisticsParameters Parameters = new(
        PowerWidth: 16, PowerHeight: 5);
    internal const string ResourceUpgrade = "SHOWCASE_I";
    internal const string CommercialPowerUpgrade = "COMMERCIAL_POWER";
    internal const string WaterUpgrade = "RUSTWATER_TAP";
#if AUTOMATED_LOGISTICS_INTERNAL_TEST
    internal static bool InternalTestPreset => true;
#else
    internal static bool InternalTestPreset => false;
#endif
    internal static AutomatedLogisticsParameters Require() => Parameters;
}
