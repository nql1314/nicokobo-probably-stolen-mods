namespace Nicokobo.ContentInterop;
public enum OfficialPresentationStatus
{
    Unavailable, Invalid, Conflict, Indeterminate, Queued, Arrived, Dismissed, Opened, Closed
}

public sealed record OfficialPresentationResult(OfficialPresentationStatus Status, string? Reason = null);
public sealed record OfficialDialogueContext(long Generation, string RunId, int SlotId,
    string OwnerId, string RequestId, string DialogueId, string ChoiceId);

/// <summary>Confirm is called at most once per visit, on the game thread. Revalidate
/// content state/resources here; return the response to display. No save is implied.</summary>
public sealed record OfficialDialogueChoice(string Id, string Label, string Preview,
    Func<OfficialDialogueContext, string> Confirm);
public sealed record OfficialDialogueDefinition(string Id, string Speaker, string Body,
    IReadOnlyList<OfficialDialogueChoice> Choices, string ConfirmLabel, string BackLabel,
    string LeaveLabel, string LaterLabel, string FailureText)
{
    /// <summary>When false, choosing an option invokes Confirm and shows its response directly.</summary>
    public bool RequireConfirmation { get; init; } = true;
    /// <summary>Whether the initial choice menu offers deferring the conversation.</summary>
    public bool AllowLater { get; init; } = true;
    /// <summary>Navigation labels when a visitor menu exceeds the native button count.</summary>
    public string NextPageLabel { get; init; } = "More choices";
    public string PreviousPageLabel { get; init; } = "Previous choices";
}

/// <summary>A freshly quoted purchase. Content owns resources and persistence.</summary>
public sealed record OfficialSupplierOffer(string Id, string Label, string Preview,
    Func<OfficialDialogueContext, string> Purchase, string? ConfirmLabel = null)
{
    /// <summary>False keeps the quoted option visible but blocks native selection.</summary>
    public bool Enabled { get; init; } = true;
}
public sealed record OfficialSupplierPage(string Body, IReadOnlyList<OfficialSupplierOffer> Offers)
{
    /// <summary>Optional direct offer selected by the character after its native choices
    /// become visible. Manual selection on that page is held until it runs.</summary>
    public string? AutomaticOfferId { get; init; }
    public double AutomaticDelaySeconds { get; init; } = 0.6;
}
public sealed record OfficialSupplierDefinition(string Id, string Speaker,
    Func<OfficialDialogueContext, OfficialSupplierPage> Capture,
    Action<OfficialDialogueContext> Dismiss, string ConfirmLabel, string BackLabel,
    string ContinueLabel, string LeaveLabel, string FailureText)
{
    /// <summary>When false, choosing an offer invokes Purchase and shows its response directly.</summary>
    public bool RequireConfirmation { get; init; } = true;
    /// <summary>Adds Continue to the menu itself, recapturing after an external item interaction.</summary>
    public bool AllowMenuRefresh { get; init; }
    /// <summary>Optional content condition for continuing menus and purchase replies.
    /// Null preserves the repeatable supplier flow.</summary>
    public Func<OfficialDialogueContext, bool>? CanContinue { get; init; }
    public string NextPageLabel { get; init; } = "More choices";
    public string PreviousPageLabel { get; init; } = "Previous choices";
}

public sealed class OfficialDialogueScript
{
    internal OfficialDialogueScript(string ownerId, OfficialDialogueDefinition definition)
    { OwnerId = ownerId; Definition = definition; }
    public string OwnerId { get; }
    public OfficialDialogueDefinition Definition { get; }
}
