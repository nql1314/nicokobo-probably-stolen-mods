
namespace Nicokobo.ContentInterop;


/// <summary>Managed branching-dialogue definitions. Native controls and delegates
/// are created and retained by OfficialClientVisitApi; content owns all consequences.</summary>
public static class OfficialDialogueApi
{
    public static bool IsAvailable => OfficialStoryPresentation.IsAvailable;
    public static OfficialDialogueScript Create(string ownerId, OfficialDialogueDefinition definition) =>
        OfficialStoryConversationContract.Freeze(ownerId, definition);
}


/// <summary>One-session native dialogue visitors. Queue success is not story
/// completion or persistence. After load, content must issue a new request.</summary>
public static class OfficialClientVisitApi
{
    public static bool IsAvailable => OfficialStoryPresentation.IsAvailable;
    public static OfficialPresentationResult Queue(string ownerId, string requestId,
        string runId, int slotId, string actorId, string spriteName, OfficialDialogueScript script) =>
        OfficialStoryPresentation.Queue(ownerId, requestId, runId, slotId, actorId, spriteName, script);
    public static OfficialPresentationResult Status(string ownerId, string requestId) =>
        OfficialStoryPresentation.Status(ownerId, requestId);
}


/// <summary>Repeatable purchases in one native visitor. Each refreshed menu has
/// a new confirmation gate; stale menus and duplicate clicks cannot buy twice.</summary>
public static class OfficialSupplierApi
{
    public static bool IsAvailable => OfficialStoryPresentation.IsAvailable;
    public static OfficialPresentationResult Queue(string ownerId, string requestId,
        string runId, int slotId, string actorId, string spriteName,
        OfficialSupplierDefinition definition)
    {
        try { OfficialStorySupplierContract.Validate(ownerId, definition); }
        catch (ArgumentException ex) { return new(OfficialPresentationStatus.Invalid, ex.Message); }
        var placeholder = OfficialDialogueApi.Create(ownerId, new(definition.Id, definition.Speaker, "",
            [new("supplier", definition.ContinueLabel, "", _ => definition.FailureText)],
            definition.ConfirmLabel, definition.BackLabel, definition.LeaveLabel,
            definition.LeaveLabel, definition.FailureText));
        return OfficialStoryPresentation.Queue(ownerId, requestId, runId, slotId, actorId,
            spriteName, placeholder, definition);
    }
}

internal static class OfficialReadingApi
{
    internal static bool IsAvailable => OfficialStoryPresentation.IsAvailable;
    internal static OfficialPresentationResult Open(string owner, string id, string run, int slot,
        string title, string body, string signature) => OfficialStoryUi.OpenDocument(owner, id, run, slot, title, body, signature);
    internal static OfficialPresentationResult Close(string owner, string id) => OfficialStoryUi.CloseDocument(owner, id);
}
