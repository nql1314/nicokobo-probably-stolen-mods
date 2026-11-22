namespace Nicokobo.ContentInterop;

// No IMod or automatic PatchAll in this dependency. Entry points explicitly
// attach/detach their own registrations; the shared native supply adapter has one owner.
public static class ContentRuntime
{
    private static readonly HashSet<string> Owners = new(StringComparer.Ordinal);
    internal static void Shutdown()
    { foreach (string owner in Owners.ToArray()) Detach(owner); }
    internal static event Action<string>? Detaching;
    public static bool IsAttached(string owner) => Owners.Contains(owner);
    internal static void RequireOwner(string owner)
    { if (!Owners.Contains(owner)) throw new InvalidOperationException("Content provider is not attached: " + owner); }
    public static void Attach(string owner)
    {
        if (string.IsNullOrWhiteSpace(owner)) throw new ArgumentException("Empty content owner", nameof(owner));
        if (!Owners.Add(owner)) return;
        if (Owners.Count != 1) return;
        try
        {
            RecipeQuery.Attach();
            ModHook.OnGameLoadedLate += Ready;
            ModHook.OnNewGameStarted += NewGame;
            ModHook.OnGoingSleepLate += ProductionHooks.FlushNight;
        }
        catch
        {
            ModHook.OnGameLoadedLate -= Ready;
            ModHook.OnNewGameStarted -= NewGame;
            ModHook.OnGoingSleepLate -= ProductionHooks.FlushNight;
            Owners.Remove(owner);
            RecipeQuery.Detach();
            throw;
        }
    }
    private static void NewGame(string scenario) => Ready();
    private static void Ready() { if (Owners.Count != 0) NativeSupplyHooks.Install(); }
    public static void Detach(string owner)
    {
        try { if (Owners.Contains(owner)) Detaching?.Invoke(owner); }
        finally
        {
            ExtraRecipes.Remove(owner); MachineCatalog.Remove(owner); ProductionHooks.Remove(owner); SupplyCatalog.Remove(owner); DevicePower.Remove(owner);
            if (Owners.Remove(owner) && Owners.Count == 0)
            {
                ModHook.OnGameLoadedLate -= Ready;
                ModHook.OnNewGameStarted -= NewGame;
                ModHook.OnGoingSleepLate -= ProductionHooks.FlushNight;
                try { RecipeQuery.Detach(); } finally { NativeSupplyHooks.Uninstall(); }
            }
        }
    }
}
