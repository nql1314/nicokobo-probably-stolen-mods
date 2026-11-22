namespace Nicokobo.AutomatedLogistics.Domain;

public enum PurifierCollectionTarget { MachineLimit, PureWater }

public sealed class MachineSettings
{
    public int MachineId { get; set; }
    public bool FillEnabled { get; set; } = true;
    public bool EjectEnabled { get; set; } = true;
    public bool HasConsumption { get; set; }
    public List<RestockItem> PendingItems { get; set; } = new();
    public List<RestockLiquid> PendingLiquids { get; set; } = new();
    public PurifierCycle? Purifier { get; set; }
    public PurifierCollectionTarget PurifierTarget { get; set; }
    public string PrinterRecipe { get; set; } = "";
    public HydroponicFeed? Hydroponic { get; set; }
    public List<string> FeedItemIds { get; set; } = new();
}

public sealed class HydroponicFeed
{
    public RestockLiquid Supply { get; set; } = new();
    public List<RestockLiquidPart> ObservedContents { get; set; } = new();
    public bool RequiresRecording { get; set; }
    public bool Valid() => Supply != null && Supply.Valid() && Supply.TargetParts > 0 &&
        Supply.Basis.Sum(part => (long)part.Parts) == Supply.TargetParts &&
        ObservedContents != null && ObservedContents.All(part => part != null && !string.IsNullOrWhiteSpace(part.LiquidId) && part.Parts > 0 &&
            !(part.Value < 0) && !(part.QualityBasis < 0)) && ObservedContents.Select(part => part.LiquidId).Distinct().Count() == ObservedContents.Count;
}

// A purifier transforms its input in place. Keep the original feed separate
// from the changing product, and repay only the volume actually auto-ejected.
public sealed class PurifierCycle
{
    public RestockLiquid Feed { get; set; } = new();
    public List<RestockLiquidPart> ObservedContents { get; set; } = new();
    public bool Valid() => Feed != null && Feed.Valid() && Feed.Remaining == 0 && Feed.TargetParts > 0 &&
        Feed.Basis.Sum(part => (long)part.Parts) == Feed.TargetParts &&
        ObservedContents != null && ObservedContents.All(part => part != null && !string.IsNullOrWhiteSpace(part.LiquidId) && part.Parts > 0 &&
            !(part.Value < 0) && !(part.QualityBasis < 0)) && ObservedContents.Select(part => part.LiquidId).Distinct().Count() == ObservedContents.Count;
}

public sealed class RestockItem
{
    public string ItemId { get; set; } = "";
    public int Purity { get; set; }
    public int Quality { get; set; }
    public string MaterialState { get; set; } = "";
    public bool Manual { get; set; }
    public int Remaining { get; set; }
    public int TargetCount { get; set; }
    public bool Valid() => !string.IsNullOrWhiteSpace(ItemId) && MaterialState != null && Purity is >= 0 and <= 6 && Quality is >= 0 and <= 4 &&
        Remaining >= 0 && TargetCount >= Remaining;
}

public sealed class RestockLiquidPart
{
    public string LiquidId { get; set; } = "";
    public int Parts { get; set; }
    public decimal? Value { get; set; }
    public decimal? QualityBasis { get; set; }
}

public sealed class RestockLiquid
{
    public string SlotId { get; set; } = "";
    public int VesselId { get; set; }
    public int WaterQuality { get; set; }
    public int Remaining { get; set; }
    public int TargetParts { get; set; }
    public List<RestockLiquidPart> Basis { get; set; } = new();
    public bool Valid() => !string.IsNullOrWhiteSpace(SlotId) && VesselId > 0 && WaterQuality is >= 0 and <= 3 &&
        Remaining >= 0 && TargetParts >= Remaining && Basis is { Count: > 0 } &&
        Basis.All(part => part != null && !string.IsNullOrWhiteSpace(part.LiquidId) && part.Parts > 0 &&
            !(part.Value < 0) && !(part.QualityBasis < 0)) && Basis.Select(part => part.LiquidId).Distinct().Count() == Basis.Count;
}

public sealed class AutomatedLogisticsState
{
    public int Version { get; set; } = 6;
    public bool TerminalGranted { get; set; }
    public Dictionary<int, MachineSettings> Machines { get; set; } = new();
    public Dictionary<int, string> Names { get; set; } = new();
    public ResourceStorageState? Resources { get; set; }
    public PowerNetworkState Power { get; set; } = new();
    public int ResourceInstanceId { get; set; }
    public int WaterInstanceId { get; set; }
    public bool ResourcesUnlocked { get; set; }
    public bool WaterUnlocked { get; set; }
    public TotalHistory[] WaterHistory { get; set; } = [new(), new(), new()];
    public bool ResourcesEnabled { get; set; } = true;
    public bool IsValid() => Version == 6 && Machines != null && ResourceInstanceId >= 0 && WaterInstanceId >= 0 &&
        (!ResourcesUnlocked || ResourceInstanceId > 0) && (!WaterUnlocked || WaterInstanceId > 0) && WaterHistory is { Length: 3 } && WaterHistory.All(history => history != null && history.Valid()) && (Resources == null || Resources.Valid()) && Power != null && Power.Valid() &&
        Machines.All(pair => pair.Key > 0 && pair.Value != null && pair.Value.MachineId == pair.Key &&
            pair.Value.PrinterRecipe != null && pair.Value.PurifierTarget is PurifierCollectionTarget.MachineLimit or PurifierCollectionTarget.PureWater &&
            pair.Value.PendingItems != null && pair.Value.PendingItems.All(item => item != null && item.Valid()) &&
            pair.Value.PendingLiquids != null && pair.Value.PendingLiquids.All(liquid => liquid != null && liquid.Valid()) &&
            pair.Value.FeedItemIds != null && pair.Value.FeedItemIds.All(id => !string.IsNullOrWhiteSpace(id)) &&
            pair.Value.FeedItemIds.Distinct(StringComparer.Ordinal).Count() == pair.Value.FeedItemIds.Count &&
            (pair.Value.Hydroponic == null || pair.Value.Hydroponic.Valid()) &&
            (pair.Value.Purifier == null || pair.Value.Purifier.Valid()));
}

public static class InstanceNames
{
    public const int MaxLength = 24;
    public static bool IsValid(string? value) => !string.IsNullOrWhiteSpace(value) &&
        value == value.Trim() && value.Length <= MaxLength && !value.Any(c => char.IsControl(c) || c is '<' or '>');
    public static string Suffix(int id, IReadOnlyDictionary<int, string> names) =>
        names.TryGetValue(id, out var name) && IsValid(name) ? name : "#" + id;
}
