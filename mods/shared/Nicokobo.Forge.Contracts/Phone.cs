namespace Nicokobo.ContentInterop;
public sealed record OfficialPhoneContext(long Generation, string RunId, int SlotId);
public sealed record OfficialPhoneContact(string Id, long Number,
    Func<OfficialPhoneContext, bool> Visible, Func<OfficialPhoneContext, string> DisplayName,
    Func<OfficialPhoneContext, OfficialDialogueScript> Call);
