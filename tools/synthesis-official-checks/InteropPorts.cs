public static class ModHook
{
    public static event Action? OnGameLoadedLate, OnGoingSleepLate;
    public static void Sleep() => OnGoingSleepLate?.Invoke();
    public static event Action<string>? OnNewGameStarted;
    public static void Loaded() => OnGameLoadedLate?.Invoke();
    public static void NewGame(string scenario) => OnNewGameStarted?.Invoke(scenario);
}
public static class RNG
{
    public static Queue<int> Indices = new();
    public static int GetRandomIntExculsive(int min, int max) => Indices.Count == 0 ? min : Indices.Dequeue();
}
namespace UnityEngine
{
    public sealed class Sprite { }
    public static class Debug { public static void LogWarning(object message) { } }
}
public sealed class GameItemElement : GameItem
{
    public UnityEngine.Sprite? Icon;
    public UnityEngine.Sprite? ResolveSpriteByName(string name) => Icon;
}
public static class RenderHandler
{
    public static UnityEngine.Sprite? LoadFromAtlas(string atlas, string name) => null;
}
namespace MechcoreProtocol.ModuleMatrix
{
    // Provider lifecycle and module factories are covered by matrix-official-checks.
    // This executable links the real MatrixInterop predicates, registry and pool.
    internal static class MatrixMod
    {
        internal const string Id = Definitions.FixedModuleDefinitions.OwnerId;
        internal static bool Active = true;
        internal static readonly HashSet<string> Disabled = new();
        internal static bool Enabled(string id) => !Disabled.Contains(id);
    }
    internal static class MatrixMachines
    {
        internal static GameGridInventory? Modules(GameItem machine) => MachineHelper.GetBodyStorage(machine, 1) as GameGridInventory;
    }
}
