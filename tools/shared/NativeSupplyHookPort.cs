// Headless checks exercise the shared catalogs and production callbacks. Native
// Harmony installation is checked against the game DLL by content-supply-checks.
namespace Nicokobo.ContentInterop;
internal static class NativeSupplyHooks
{
    internal static void Install() { }
    internal static void Uninstall() { }
}

// The recipe window needs a live Unity UI; catalog/transaction tests stay headless.
internal static class RecipeQuery
{
    internal static void Attach() { }
    internal static void Detach() { }
}