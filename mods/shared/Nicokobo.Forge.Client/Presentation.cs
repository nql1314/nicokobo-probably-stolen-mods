namespace Nicokobo.ContentInterop;

public static class OfficialDialogueApi
{
    public static bool IsAvailable => ForgeClient.Call<bool>("presentation.available");
    public static OfficialDialogueScript Create(string owner, OfficialDialogueDefinition definition) => ForgeWire.Script(ForgeClient.Call<object[]>("dialogue.create", owner, ForgeWire.Dialogue(definition)));
}
public static class OfficialClientVisitApi
{
    public static bool IsAvailable => OfficialDialogueApi.IsAvailable;
    public static OfficialPresentationResult Queue(string owner, string request, string run, int slot, string actor, string sprite, OfficialDialogueScript script) =>
        ForgeWire.PresentationResult(ForgeClient.Call<object[]>("visit.queue", owner, request, run, slot, actor, sprite, ForgeWire.Script(script)));
    public static OfficialPresentationResult Status(string owner, string request) => ForgeWire.PresentationResult(ForgeClient.Call<object[]>("visit.status", owner, request));
}
public static class OfficialSupplierApi
{
    public static bool IsAvailable => OfficialDialogueApi.IsAvailable;
    public static OfficialPresentationResult Queue(string owner, string request, string run, int slot, string actor, string sprite, OfficialSupplierDefinition definition) =>
        ForgeWire.PresentationResult(ForgeClient.Call<object[]>("supplier.queue", owner, request, run, slot, actor, sprite, ForgeWire.Supplier(definition)));
}
public static class OfficialPhoneApi
{
    public static bool IsAvailable => ForgeClient.Call<bool>("phone.available");
    public static OfficialPresentationResult Register(string owner, OfficialPhoneContact contact) => ForgeWire.PresentationResult(ForgeClient.Call<object[]>("phone.register", owner, ForgeWire.Phone(contact)));
}
public static class OfficialReadingApi
{
    public static bool IsAvailable => OfficialDialogueApi.IsAvailable;
    public static OfficialPresentationResult Open(string owner, string id, string run, int slot, string title, string body, string signature) =>
        ForgeWire.PresentationResult(ForgeClient.Call<object[]>("reader.open", owner, id, run, slot, title, body, signature));
    public static OfficialPresentationResult Close(string owner, string id) => ForgeWire.PresentationResult(ForgeClient.Call<object[]>("reader.close", owner, id));
}
public static class OfficialWorkshopApi
{
    public static bool IsVisible => ForgeClient.Call<bool>("workshop.visible");
    public static SubmitResult RegisterChain(string owner, string id, string chinese, string english, Func<OfficialWorkshopSnapshot?> snapshot, Action<string> claim, Action? opened = null) =>
        RegisterChain(owner, id, chinese, english, snapshot, claim, opened, null);

    public static SubmitResult RegisterChain(string owner, string id, string chinese, string english,
        Func<OfficialWorkshopSnapshot?> snapshot, Action<string> claim, Action? opened, Func<string>? localizedTitle)
    {
        var v = ForgeClient.Call<object[]>("workshop.register", owner, id, chinese, english,
            (Func<object[]?>)(() => snapshot() is { } view ? ForgeWire.Workshop(view) : null), claim, opened!, localizedTitle!);
        return new((SubmitStatus)(int)v[0], (string)v[1]);
    }
    public static bool OpenChain(string id) => ForgeClient.Call<bool>("workshop.open", id);
    public static bool OpenDefault() => ForgeClient.Call<bool>("workshop.default");
    public static void CloseWindow() => ForgeClient.Call("workshop.close");
    public static void GrantOpeningCard() => ForgeClient.Call("workshop.card.grant");
}
public static class PresentationWindows
{
    public static bool AnyVisible => ForgeClient.Call<bool>("views.visible");
    public static bool PointerOverWindow => ForgeClient.Call<bool>("views.pointer");
    public static CustomUIBuilder Create(string owner, string id, string title, float width, float height, Action? onClose = null) => ForgeClient.Call<CustomUIBuilder>("views.create", owner, id, title, width, height, onClose!);
    public static void Show(CustomUIBuilder builder) => ForgeClient.Call("views.show", builder);
    public static void Body(CustomUIBuilder builder, string text, string id) => ForgeClient.Call("views.body", builder, text, id);
    public static void SetBody(CustomUIWindow window, string id, string text) => ForgeClient.Call("views.body.set", window, id, text);
    public static void CloseOwner(string owner) => ForgeClient.Call("views.close", owner);
    public static void Opened() => ForgeClient.Call("views.opened");
    public static void Closed() => ForgeClient.Call("views.closed");
}
