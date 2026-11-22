using Newtonsoft.Json;

namespace DefinitelyStolen.Domain;

internal static class Identity
{
    internal const string Owner = "nicokobo.definitely_stolen";
    internal const string SaveKey = Owner + ".state";
    internal const string StartId = Owner + ".pickpocket";
    internal const int NativeStartType = 56;
    internal const string Streetwise = Owner + ".streetwise";
    internal const string Habitual = Owner + ".habitual";
    internal const string CustodyTag = "NICOKOBO_DEFINITELY_STOLEN_CUSTODY";
}

internal sealed class RunState
{
    public TheftMode PreferredMode { get; set; } = TheftMode.Timing;
    public bool ManualPracticeUsed { get; set; }
    public int Schema { get; set; } = 5;
    public string RunId { get; set; } = "";
    public int SlotId { get; set; }
    public long Revision { get; set; }
    public bool Qualified { get; set; }
    public string QualificationSource { get; set; } = "none";
    public bool Habitual { get; set; }
    public int Experience { get; set; }
    public int Wind { get; set; }
    public int LastSettledDay { get; set; } = -1;
    public ulong RandomState { get; set; }
    public bool OpeningGranted { get; set; }
    // Legacy opening receipt; existing saves retain their original cash amount.
    public int OpeningCash { get; set; }
    public int? OpeningCashDelta { get; set; }
    public List<ItemState> OpeningItems { get; set; } = new();
    public NightState? Night { get; set; }
    // Animation feedback belongs to this session; durable outcomes live in Night.
    [JsonIgnore]
    public ActionReceipt? LastAction { get; set; }
    public List<NoteState> Notes { get; set; } = new();
}

internal sealed class NightState
{
    public TheftMode Mode { get; set; } = TheftMode.Timing;
    public TimingAttempt? Timing { get; set; }
    public int Day { get; set; }
    public int GenerationLevel { get; set; }
    public double WindPenalty { get; set; }
    public ulong RandomState { get; set; }
    public int Attempts { get; set; }
    public int SpentActions { get; set; }
    public int? LockedRegion { get; set; }
    public bool Started { get; set; }
    public bool Ended { get; set; }
    public bool Caught { get; set; }
    // Theft has stopped, but carry remains outside until the player sorts and returns.
    public bool Escaped { get; set; }
    public bool Outside { get; set; }
    public int PendingWind { get; set; }
    public bool NoteRolled { get; set; }
    public bool NoteWon { get; set; }
    public List<NpcState> Npcs { get; set; } = new();
    public List<RegionEventState> RegionEvents { get; set; } = new();
    public List<ItemState> Loot { get; set; } = new();
    public List<ItemState> Abandoned { get; set; } = new();
    // Native saves do not serialize the transient afterhour grid. Snapshot its
    // root graphs inside the same native slot, including the player's carry-in.
    public List<CarryState> Carry { get; set; } = new();
}

internal sealed class RegionEventState
{
    public int Region { get; set; }
    public int RuleVersion { get; set; }
    public List<string> EventIds { get; set; } = new();
    public Dictionary<string, double> RoleBonuses { get; set; } = new(StringComparer.Ordinal);
    public double ExposurePercent { get; set; }
}

internal sealed class CarryState
{
    public int UniqueId { get; set; }
    public string Graph { get; set; } = "";
    public string Inventory { get; set; } = "grid";
}

internal sealed class NpcState
{
    public string Key { get; set; } = "";
    public int Region { get; set; }
    public int Position { get; set; }
    public string RoleId { get; set; } = "";
    public string Faction { get; set; } = "";
    public string Sprite { get; set; } = "";
    public string Background { get; set; } = "";
    public int Attempts { get; set; }
    public int SuccessfulThefts { get; set; }
    // Each target starts centered and retains its own next manual success position.
    public double ManualSuccessPosition { get; set; } = .5;
    public bool Alert { get; set; }
    public bool DebugGenerated { get; set; }
    public double TokenAdmission { get; set; }
    public bool TokenEligible { get; set; }
    public List<ItemState> Items { get; set; } = new();
}

internal sealed class ItemState
{
    public string Id { get; set; } = "";
    public int UniqueId { get; set; }
    public string Graph { get; set; } = "";
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public int? NativeStateVersion { get; set; }
    [JsonIgnore]
    internal bool Available => Graph.Length != 0 || NativeStateVersion == null && Id != "pink_alt_injector";
    public long Value { get; set; }
    public bool Rare { get; set; }
    public double Weight { get; set; }
    public int Heat { get; set; }
    public int? Charge { get; set; }
    public int? MaxCharge { get; set; }
    public int? VolumeMl { get; set; }
    public int? CapacityMl { get; set; }
    public int? Purity { get; set; }
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public Dictionary<string, int>? LiquidParts { get; set; }
    public string Condition { get; set; } = "native";
    public bool DebugInjected { get; set; }
}

internal enum AttemptOutcome { Success, HiddenFailure, Caught }
internal enum TheftMode { Probability, Timing }

internal sealed class ActionReceipt
{
    public TheftMode Mode { get; set; }
    public double? StopPosition { get; set; }
    public long Revision { get; set; }
    public int Day { get; set; }
    public int AttemptBefore { get; set; }
    public int LevelBefore { get; set; }
    public string Target { get; set; } = "";
    public AttemptOutcome Outcome { get; set; }
    public double Success { get; set; }
    public double Exposure { get; set; }
    public int ExperienceAward { get; set; }
    public int ItemUniqueId { get; set; }
    public bool Debug { get; set; }
    public string Faction { get; set; } = "";
    public int TargetReputationPenalty { get; set; }
    public int EscapeItemUniqueId { get; set; }
}

internal sealed class NoteState
{
    public int Day { get; set; }
    public string Target { get; set; } = "";
    public int ItemUniqueId { get; set; }
    public string ItemName { get; set; } = "";
    public string EnglishItemName { get; set; } = "";
    public string Body { get; set; } = "";
    public string EnglishBody { get; set; } = "";
    public bool Delivered { get; set; }
    public bool Expired { get; set; }
    public bool Invalidated { get; set; }
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string? InvalidationReason { get; set; }
    public ItemState? Paper { get; set; }
}
