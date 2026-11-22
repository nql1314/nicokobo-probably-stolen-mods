using UnityEngine;
namespace AugMechanicalAscension.Integration;
internal static class OfficialStoryUi
{
    internal static bool AnyVisible => Nicokobo.ContentInterop.PresentationWindows.AnyVisible;
    internal static bool PointerOverWindow => Nicokobo.ContentInterop.PresentationWindows.PointerOverWindow;
    internal static CustomUIBuilder Create(string id, string title, float width, float height, Action? onClose = null) =>
        Nicokobo.ContentInterop.PresentationWindows.Create(AugMod.Id, id, title, width, height, onClose);
    internal static void Show(CustomUIBuilder builder) => Nicokobo.ContentInterop.PresentationWindows.Show(builder);
    internal static void Body(CustomUIBuilder builder, string text, string id) => Nicokobo.ContentInterop.PresentationWindows.Body(builder, text, id);
    internal static void SetBody(CustomUIWindow window, string id, string text) => Nicokobo.ContentInterop.PresentationWindows.SetBody(window, id, text);
    internal static void CloseAll() => Nicokobo.ContentInterop.PresentationWindows.CloseOwner(AugMod.Id);
}
