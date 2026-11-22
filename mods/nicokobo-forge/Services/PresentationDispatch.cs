namespace Nicokobo.ContentInterop;

internal static partial class ForgeServices
{
    private static object? DispatchPresentation(string operation, object[] args)
    {
        switch (operation)
        {
            case "presentation.available": return OfficialDialogueApi.IsAvailable;
            case "dialogue.create": return ForgeWire.Script(OfficialDialogueApi.Create((string)args[0], ForgeWire.Dialogue((object[])args[1])));
            case "visit.queue": return ForgeWire.PresentationResult(OfficialClientVisitApi.Queue((string)args[0], (string)args[1], (string)args[2], (int)args[3], (string)args[4], (string)args[5], ForgeWire.Script((object[])args[6])));
            case "visit.status": return ForgeWire.PresentationResult(OfficialClientVisitApi.Status((string)args[0], (string)args[1]));
            case "supplier.queue": return ForgeWire.PresentationResult(OfficialSupplierApi.Queue((string)args[0], (string)args[1], (string)args[2], (int)args[3], (string)args[4], (string)args[5], ForgeWire.Supplier((object[])args[6])));
            case "phone.available": return OfficialPhoneApi.IsAvailable;
            case "phone.register": return ForgeWire.PresentationResult(OfficialPhoneApi.Register((string)args[0], ForgeWire.Phone((object[])args[1])));
            case "reader.open": return ForgeWire.PresentationResult(OfficialReadingApi.Open((string)args[0], (string)args[1], (string)args[2], (int)args[3], (string)args[4], (string)args[5], (string)args[6]));
            case "reader.close": return ForgeWire.PresentationResult(OfficialReadingApi.Close((string)args[0], (string)args[1]));
            case "workshop.visible": return OfficialWorkshopApi.IsVisible;
            case "workshop.register":
                var capture = (Func<object[]?>)args[4];
                var result = OfficialWorkshopApi.RegisterChain((string)args[0], (string)args[1], (string)args[2], (string)args[3],
                    () => capture() is { } view ? ForgeWire.Workshop(view) : null, (Action<string>)args[5],
                    args.Length > 6 ? (Action?)args[6] : null, args.Length > 7 ? (Func<string>?)args[7] : null);
                return new object[] { (int)result.Status, result.Reason };
            case "workshop.open": return OfficialWorkshopApi.OpenChain((string)args[0]);
            case "workshop.default": return OfficialWorkshopApi.OpenDefault();
            case "workshop.close": OfficialWorkshopApi.CloseWindow(); return null;
            case "workshop.card.grant": OfficialWorkshopApi.GrantOpeningCard(); return null;
            case "views.visible": return OfficialStoryUi.AnyVisible;
            case "views.pointer": return OfficialStoryUi.PointerOverWindow;
            case "views.create": ContentRuntime.RequireOwner((string)args[0]); return OfficialStoryUi.Create((string)args[0], (string)args[1], (string)args[2], (float)args[3], (float)args[4], (Action?)args[5]);
            case "views.show": OfficialStoryUi.Show((CustomUIBuilder)args[0]); return null;
            case "views.body": OfficialStoryUi.Body((CustomUIBuilder)args[0], (string)args[1], (string)args[2]); return null;
            case "views.body.set": OfficialStoryUi.SetBody((CustomUIWindow)args[0], (string)args[1], (string)args[2]); return null;
            case "views.close": OfficialStoryUi.CloseOwner((string)args[0]); return null;
            case "views.opened": PresentationInputGuard.Opened(); return null;
            case "views.closed": PresentationInputGuard.Closed(); return null;
            default: throw new ArgumentException("Unknown Forge protocol operation: " + operation);
        }
    }
}
