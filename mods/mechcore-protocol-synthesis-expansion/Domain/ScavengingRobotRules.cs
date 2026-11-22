namespace MechcoreProtocol.SynthesisExpansion.Domain;

public static class ScavengingRobotRules
{
    public const string ScannerChanceTag = "SCAV_SCANNER_DOUBLE_CHANCE";
    public const int MaxScannerChance = 100;
    public const string BrokenTag = "BROKEN_MACHINE_TAG";
    public const string LastNightTag = "NICOKOBO_SCAVENGING_ROBOT_NIGHT_V1";
    public const string StorageShapeTag = "NICOKOBO_SCAVENGING_ROBOT_STORAGE_V1";
    public const string SkipJunkTag = "NICOKOBO_SCAVENGING_ROBOT_SKIP_JUNK_V1";

    // Native inventory shapes use 0 for usable cells and 1 for blocked cells.
    public static bool IsValidStorageShape(int width, string? data) =>
        width > 0 && data is { Length: > 0 } && data.Length % width == 0 &&
        data.Contains('0') && data.All(cell => cell is '0' or '1');

    public static bool IsValidScannerChance(int? chance) =>
        chance is >= 0 and <= MaxScannerChance;

    public static int ScannerLevel(int chance) => IsValidScannerChance(chance)
        ? Math.Max(1, (chance - SynthesisNumbers.ScavengingRobot.InitialScannerChance) / SynthesisNumbers.ScavengingRobot.ScannerUpgradeStep + 1)
        : throw new ArgumentOutOfRangeException(nameof(chance));

    public static int UpgradeScanner(int chance) => IsValidScannerChance(chance) && chance < MaxScannerChance
        ? Math.Min(MaxScannerChance, chance + SynthesisNumbers.ScavengingRobot.ScannerUpgradeStep)
        : throw new ArgumentOutOfRangeException(nameof(chance));

    public static string NightKey(int slot, string run, int day) =>
        slot >= 0 && !string.IsNullOrWhiteSpace(run) && day >= 0
            ? $"{slot}:{run}:{day}" : "";

    public static bool CanWork(bool broken, string? lastNight, string night, int? batteryEnergy) =>
        !broken && night.Length != 0 && lastNight != night &&
        batteryEnergy >= SynthesisNumbers.ScavengingRobot.NightlyPowerCost;

    public static bool BreaksDown(int roll) =>
        roll >= 0 && roll < SynthesisNumbers.ScavengingRobot.BreakdownChancePercent;
}
