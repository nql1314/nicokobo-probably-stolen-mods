namespace Nicokobo.ContentInterop;
internal static class OfficialStorySupplierContract
{
    internal static void Validate(string owner, OfficialSupplierDefinition definition)
    {
        if (definition == null || !OfficialStoryConversationContract.Owned(owner, definition.Id) ||
            string.IsNullOrWhiteSpace(definition.Speaker) || definition.Capture == null || definition.Dismiss == null ||
            new[] { definition.ConfirmLabel, definition.BackLabel, definition.ContinueLabel,
                definition.LeaveLabel, definition.FailureText, definition.NextPageLabel,
                definition.PreviousPageLabel }.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Invalid owned supplier definition");
    }
    internal static OfficialSupplierPage FreezePage(OfficialSupplierPage page)
    {
        if (page == null || page.Body == null || page.Offers == null) throw new ArgumentException("Invalid supplier quote page");
        var offers = page.Offers.ToArray();
        if (offers.Any(x => x == null || !OfficialStoryConversationContract.Request(x.Id) ||
                string.IsNullOrWhiteSpace(x.Label) || x.Preview == null || x.Purchase == null ||
                x.ConfirmLabel != null && string.IsNullOrWhiteSpace(x.ConfirmLabel)) ||
            offers.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != offers.Length)
            throw new ArgumentException("Invalid supplier quote offers");
        if (page.AutomaticOfferId != null &&
            (!offers.Any(x => x.Id == page.AutomaticOfferId && x.Enabled) ||
             !double.IsFinite(page.AutomaticDelaySeconds) || page.AutomaticDelaySeconds <= 0))
            throw new ArgumentException("Automatic selection needs an enabled offered ID and a positive finite delay");
        return page with { Offers = Array.AsReadOnly(offers) };
    }
}

/// <summary>A frozen managed definition. Native objects are owned by the queued visit.</summary>
internal static class OfficialStoryConversationContract
{
    internal static bool ValidOwner(string owner) => !string.IsNullOrWhiteSpace(owner) &&
        owner.Split('.').Length >= 2 && owner.Split('.').All(part => part.Length > 0 &&
            part.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_'));
    internal static bool Owned(string owner, string id) => ValidOwner(owner) &&
        !string.IsNullOrWhiteSpace(id) && id.StartsWith(owner + ".", StringComparison.Ordinal) && ValidOwner(id);
    internal static bool Request(string id) => !string.IsNullOrWhiteSpace(id) && id.Length <= 256;
    internal static bool MayOpenReader(string owner, string id, string? heldOwner, string? heldId,
        bool visible, bool sameNativeItem) => !visible || sameNativeItem && owner == heldOwner && id == heldId;

    internal static OfficialDialogueScript Freeze(string owner, OfficialDialogueDefinition definition)
    {
        if (definition == null || !Owned(owner, definition.Id) || string.IsNullOrWhiteSpace(definition.Speaker) ||
            definition.Body == null || definition.Choices == null || definition.Choices.Count == 0 ||
            new[] { definition.ConfirmLabel, definition.BackLabel, definition.LeaveLabel,
                definition.LaterLabel, definition.FailureText, definition.NextPageLabel,
                definition.PreviousPageLabel }.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Invalid owned dialogue definition");
        var choices = definition.Choices.ToArray();
        if (choices.Any(c => c == null || !Request(c.Id) || string.IsNullOrWhiteSpace(c.Label) ||
                c.Preview == null || c.Confirm == null) || choices.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count() != choices.Length)
            throw new ArgumentException("Dialogue choice IDs must be unique with valid callbacks");
        return new(owner, definition with { Choices = Array.AsReadOnly(choices) });
    }
}

// Used by the native request ledger: no native objects or content save state.
internal sealed class OfficialStoryConversationRequests
{
    internal sealed record Entry(string Owner, string Request, string Actor, string Dialogue,
        OfficialPresentationStatus Status);
    private readonly Dictionary<(string Owner, string Request), Entry> _entries = new();
    internal Entry? Find(string owner, string request) => _entries.GetValueOrDefault((owner, request));
    internal OfficialPresentationStatus Reserve(string owner, string request, string actor, string dialogue)
    {
        if (Find(owner, request) is { } previous)
            return previous.Actor == actor && previous.Dialogue == dialogue ? previous.Status : OfficialPresentationStatus.Conflict;
        if (_entries.Values.Any(x => x.Owner == owner && x.Actor == actor &&
            x.Status is OfficialPresentationStatus.Queued or OfficialPresentationStatus.Arrived or OfficialPresentationStatus.Indeterminate))
            return OfficialPresentationStatus.Conflict;
        _entries.Add((owner, request), new(owner, request, actor, dialogue, OfficialPresentationStatus.Queued));
        return OfficialPresentationStatus.Queued;
    }
    internal void Set(string owner, string request, OfficialPresentationStatus status)
    {
        if (Find(owner, request) is { } entry) _entries[(owner, request)] = entry with { Status = status };
    }
    internal void Remove(string owner)
    { foreach (var key in _entries.Keys.Where(key => key.Owner == owner).ToArray()) _entries.Remove(key); }
    internal void Clear() => _entries.Clear();
}

internal sealed class OfficialStoryConversationSelection
{
    private bool _used;
    internal bool TryEnter(long captured, long current, bool currentVisitor)
    {
        if (_used || captured != current || !currentVisitor) return false;
        _used = true; return true;
    }
}

// DisplayChoice arms the timer; merely capturing/queuing a page does not.
internal sealed class OfficialStoryConversationAutomaticSelection(double delaySeconds)
{
    private bool _displayed, _used;
    private DateTime? _visibleSince;
    internal bool Pending => !_used;
    internal void Displayed(DateTime now)
    {
        if (_used) return;
        _displayed = true;
        _visibleSince ??= now;
    }
    internal bool TryEnter(bool visible, DateTime now)
    {
        if (_used || !_displayed) return false;
        if (!visible) { _visibleSince = null; return false; }
        _visibleSince ??= now;
        if ((now - _visibleSince.Value).TotalSeconds < delaySeconds) return false;
        _used = true;
        return true;
    }
}
