namespace AugMechanicalAscension.Domain;

internal sealed record StoryChoice(string EventId, string OptionId, int Day);
internal sealed record StoryPending(string RequestId, string ActionId, int Day,
    int CashBefore, int CashDelta, int CoreUid)
{
    public int[] OutputUids { get; init; } = [];
    public int[] RemovedUids { get; init; } = [];
    public int MaterialUid { get; init; }
    public int MaterialUnitsBefore { get; init; }
}

internal sealed record StoryState
{
    public const string SaveKey = "nicokobo.aug.story.lost_and_found";
    public const string CoreId = "nicokobo.aug.story.distress_core";
    public int SchemaVersion { get; init; } = 1;
    public int ContentVersion { get; init; } = 4;
    public string GameRunId { get; init; } = "";
    public int SaveSlotId { get; init; }
    public string StartId { get; init; } = RunState.AugMechanicalAscensionId;
    public long Revision { get; init; }
    // Null means a pre-existing record has not chosen its opening schedule yet.
    public bool? CatchUpOpening { get; init; }
    public StoryChoice[] Choices { get; init; } = [];
    public string? ActiveEventId { get; init; }
    public int DecisionStartedDay { get; init; } = -1;
    public int EncounterDay { get; init; } = -1;
    public int LastStartedDay { get; init; } = -1;
    public int CoreUid { get; init; }
    public string CoreDisposition { get; init; } = "none";
    public bool CoreConsented { get; init; }
    public bool CoreCustodyConsented { get; init; }
    public StoryPending? Pending { get; init; }
    public string? EndingId { get; init; }
    public int EndingDay { get; init; } = -1;
    public bool EpilogueSeen { get; init; }
    public StoryItemReceipt[] Items { get; init; } = [];
    public bool RecordingRepaired { get; init; }
    public bool ConfessionHeard { get; init; }
    public bool CopyDelivered { get; init; }
    public int Investigation { get; init; }
    public int ExperimentDay { get; init; } = -1;
    public int InvestigationDay { get; init; } = -1;
    public bool ProstheticInspected { get; init; }
    public bool ProstheticMarksTransferred { get; init; }
    public bool PhotoInspected { get; init; }
    public bool DiagnosticRetained { get; init; }
    public bool OfferReserved { get; init; }
    public string HiddenClosedReason { get; init; } = "none";
    public string Choice(string id) => Choices.FirstOrDefault(x => x.EventId == id)?.OptionId ?? "";
}

internal sealed record StoryItemReceipt(string Key, int Uid, bool Removed = false)
{
    public int CounterNoticeDay { get; init; } = -1;
}

internal readonly record struct StoryContext(int Day, int StartDay, int OwnedCybernetics,
    int Cash, bool OwnsCore, bool MechanicalAscensionCompleted = false, bool IgnoreVisitDays = false);
internal sealed record StoryOption(string Id, string TextKey, int CashDelta = 0, bool HasConsequence = true)
{
    internal string Label(bool? english = null) => AugText.GetFor(TextKey, english);
    internal string Result(bool? english = null) => HasConsequence ? AugText.GetFor(TextKey + ".consequence", english) : "";
    internal string Chinese => Label(false);
    internal string English => Label(true);
    internal string Consequence => Result(false);
    internal string EnglishConsequence => Result(true);
}

internal sealed record StoryEvent(string Id, int RequiredCybernetics, StoryOption[] Options)
{
    private string Key => "aug.story." + Id.ToLowerInvariant();
    internal string Title(bool? english = null) => AugText.GetFor(Key + ".name", english);
    internal string SpeakerName(bool? english = null) => AugText.GetFor(Key + ".speaker", english);
    internal string Dialogue(bool? english = null) => AugText.GetFor(Key + ".body", english);
    internal string Name => Title(false);
    internal string EnglishName => Title(true);
    internal string Speaker => SpeakerName(false);
    internal string EnglishSpeaker => SpeakerName(true);
    internal string Body => Dialogue(false);
    internal string EnglishBody => Dialogue(true);
}
