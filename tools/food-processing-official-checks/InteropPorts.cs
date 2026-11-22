// Native storage, save, RNG and logging boundaries only. Production, receipts,
// recipes and supply policy are source-linked from the actual shared SDK.
public static class MachineHelper
{
    public static GraphNodeStorage GetBodyStorage(GameItem machine, int index) =>
        machine.Storages.TryGetValue(index, out var storage) ? storage : null!;
}

public static class DirectoryUtils
{
    public static (InventoryWindow, GameInventory) CreateInventoryWindow(int width, int height)
    {
        var inventory = new GameGridInventory().SetShape(width, height);
        var window = new InventoryWindow { childElement = inventory };
        window.children.Add(inventory);
        return (window, inventory);
    }

    public sealed class InventoryWindow : PixelWindow
    {
        public override List<GraphNodeStorage> children { get; } = new();
        public bool isLocked;
    }
}

public sealed class GenericConfirmationUIManager
{
    public static GenericConfirmationUIManager Instance { get; } = new();
    public void OpenPanelSingleButton(string body, string confirm) { }
}

public static class AudioHelper
{
    public static void InitPackagedAudio(GameItem item) { }
}

public sealed class EmporiumEntry
{
    public static EmporiumEntry Instance { get; } = new();
    public GameGridInventory invElement = new();
}

public sealed class PlayerStore
{
    public static PlayerStore? Current;
    public static bool HasInstance => Current != null;
    public static PlayerStore Instance => Current ??= new();
    public string runID = "food-checks";
    public List<GameItem> Items = new();
    public List<GameItem> FindAllItem(bool isOwn = true) => Items.SelectMany(item =>
        new[] { item }.Concat(item.FindAllChildItems())).Where(item => !isOwn || GeneralHelper.IsItemOwned(item)).Distinct().ToList();
}

public static class StoreStation
{
    public static int Day = 25;
    public static int GetDayCounter() => Day;
}

public static class RNG
{
    public static double GetRandomDouble(double min, double max) => min;
}

public sealed class LootEntry
{
    public string id, sourceModId, modifiedByModId = "";
    public float weight;
    public LootEntry(string id, float weight, string? sourceModId = null)
    { this.id = id; this.weight = weight; this.sourceModId = sourceModId ?? ""; }
}

public static class ModHook
{
    public static event Action? OnGameLoadedLate, OnGoingSleepLate;
    public static void Sleep() => OnGoingSleepLate?.Invoke();
    public static event Action<string>? OnNewGameStarted;
    public static void Loaded() => OnGameLoadedLate?.Invoke();
    public static void NewGame(string scenario) => OnNewGameStarted?.Invoke(scenario);
}

namespace UnityEngine
{
    public readonly record struct Vector2(float x, float y);
    public static class Debug { public static void LogWarning(object text) { } }
}

namespace Nicokobo.ContentInterop
{
    internal static class ForgeTransactions
    {
        internal static NativeTransactionResult Run(IReadOnlyList<NativeTransactionStep> steps)
        {
            var result = MachineTransaction.Run(steps.Select(step => new MachineTransactionStep(step.Name, step.Apply, step.Restore)).ToArray());
            return new(result.Committed, result.Restored, result.Status);
        }
    }

    internal static class NativeInventoryFreezeApi
    {
        internal static bool IsFrozen(GameItem item) => item.IsTag("fixture_frozen");
    }
}
