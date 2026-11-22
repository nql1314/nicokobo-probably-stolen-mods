namespace Nicokobo.ContentInterop;
internal static class RecipeQuery
{
    internal static string? LastItem;
    internal static void OpenItem(string id) => LastItem = id;
    internal static int Starts, Stops;
    internal static void Attach() => Starts++;
    internal static void Detach() => Stops++;
}
internal static class NativeSupplyHooks
{
    internal static void Install() { }
    internal static void Uninstall() { }
}

// Unity-owned service setup is outside this catalog/protocol fixture.
internal static class ForgeRuntime { internal static void Attach() { } internal static void Ready() { } internal static void Remove(string owner) { } internal static void Shutdown() { } }
internal static class ForgeServices { internal static object? Dispatch(string operation, object[] args) => throw new ArgumentException(operation); }
internal static class OfficialWorkshopApi { internal static void DefineConfig() { } }
internal static class ForgeDossier { internal static void DefineConfig() { } }
internal static class NativeLiquidValueRuntime
{
    internal static bool Install(Action<string> log) =>
        throw new NotSupportedException("Native liquid hook installation requires Unity.");
}
