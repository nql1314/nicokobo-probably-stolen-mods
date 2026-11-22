namespace Nicokobo.ContentInterop;
public sealed record OfficialWorkshopGroup(string ChineseTitle, string EnglishTitle,
    int EntryCount);
public enum OfficialWorkshopResourceKind { Credits, Item }

/// <summary>A condition checked by the content provider in the current run.
/// Consume describes the provider's unlock transaction, not a Official transfer.</summary>
public sealed record OfficialWorkshopRequirement(OfficialWorkshopResourceKind Kind,
    string ResourceId, int Quantity, bool Consume, bool Satisfied)
{
    public string? DisplayName { get; init; }
}

/// <summary>A grant made exactly once by the content provider after unlock.</summary>
public sealed record OfficialWorkshopReward(OfficialWorkshopResourceKind Kind,
    string ResourceId, int Quantity)
{
    public string? DisplayName { get; init; }
}

public sealed record OfficialWorkshopEntry(string Id, string Title, string Subtitle,
    string Description, string Requirement, string CostText, string NodeCostText,
    string StateText, string ActionText, bool Unlocked, bool PrerequisiteMet,
    bool ActionEnabled)
{
    /// <summary>Stable IDs of prerequisite nodes. IDs from another chain are allowed.</summary>
    public IReadOnlyList<string> Dependencies { get; init; } = [];
    /// <summary>All conditions must be satisfied. The provider rechecks them at unlock.</summary>
    public IReadOnlyList<OfficialWorkshopRequirement> Conditions { get; init; } = [];
    public IReadOnlyList<OfficialWorkshopReward> Rewards { get; init; } = [];
    /// <summary>Optional achievement state. Unlocked remains the claimed/unlocked state.</summary>
    public bool? Completed { get; init; }
    /// <summary>Thematic links only; these never gate an action.</summary>
    public IReadOnlyList<string> VisualLinks { get; init; } = [];
    /// <summary>Optional graph coordinates in [-1, 1]. Omit both for radial layout.</summary>
    public float? GraphX { get; init; }
    public float? GraphY { get; init; }
}
public sealed record OfficialWorkshopSnapshot(int Credits, bool English,
    IReadOnlyList<OfficialWorkshopGroup> Groups,
    IReadOnlyList<OfficialWorkshopEntry> Entries, string Message = "");
public sealed record OfficialWorkshopTabHeader(string Id, string ChineseTitle,
    string EnglishTitle);


public enum SubmitStatus { Accepted, AlreadyPresent, Invalid, Conflict }
public sealed record SubmitResult(SubmitStatus Status, string Reason);
