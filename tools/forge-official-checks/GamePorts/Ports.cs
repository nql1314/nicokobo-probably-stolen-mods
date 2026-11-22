// Native/UI boundary only. Catalogs, protocol, lifecycle and transactions are
// linked from production unchanged in the host and two client assemblies.
public interface IMod { void Init(ModManifest manifest); void OnEnable(); void OnDisable(); }
public sealed class ModManifest { public string ID = ""; }
public static class ModHelper
{
    public static string GetLocalized(string modId, string key, params object[] args) => key;
    public static UnityEngine.Coroutine StartCoroutine(string owner, System.Collections.IEnumerator routine) =>
        throw new NotSupportedException("Coroutines require Unity.");
    public static void StopCoroutine(string owner, UnityEngine.Coroutine routine) =>
        throw new NotSupportedException("Coroutines require Unity.");
}
public static class ModHook
{
    public static event Action<LoadedMod>? OnModAssetsLoaded;
    public static event Action? OnGameLoadedLate, OnGoingSleepLate;
    public static event Action<string>? OnNewGameStarted;
    public static void Assets(string owner)
    {
        if (OnModAssetsLoaded == null) return;
        foreach (Action<LoadedMod> handler in OnModAssetsLoaded.GetInvocationList())
        {
            try { handler(new() { Manifest = new() { ID = owner } }); }
            catch (Exception error) { UnityEngine.Debug.LogError(error); }
        }
    }
    public static void Night() => OnGoingSleepLate?.Invoke();
    public static void Loaded() => OnGameLoadedLate?.Invoke();
    public static void New() => OnNewGameStarted?.Invoke("test");
}
public class GraphNodeStorage { public List<GraphNodeStorage> children = new(); }
public sealed class TreeNodeRender { public UnityEngine.GameObject gameObject = new(); }
public sealed class PixelWindow : GraphNodeStorage
{
    public List<GameItem> parentItems = new();
    public TreeNodeRender handler = new();
    public void Hide() { }
}
public static class GraphHandler { public static List<PixelWindow> windowGraphNodeRoots = new(); }
public class GameItem : GraphNodeStorage
{
    public string identifier = "", shortDescription = "";
    public string spriteAtlasPath = "", spritePath = "";
    public bool useSpriteFromMod;
    public int unitCount = 1, WaterParts, Energy;
    public bool Owned = true;
    public GameInventory? parentInventory;
    public PixelWindow? contentWindow, examineWindow;
    public void SetContentWindow(PixelWindow? value)
    { contentWindow?.parentItems.Remove(this); contentWindow = value; value?.parentItems.Add(this); }
    public void SetExamineWindow(PixelWindow? value)
    { examineWindow?.parentItems.Remove(this); examineWindow = value; value?.parentItems.Add(this); }
    public object?[] Storages = new object?[5];
    private readonly Dictionary<string, TagState> _tags = new();
    public bool IsTag(string tag) => _tags.TryGetValue(tag, out var value) && value.Enabled;
    public TagState GetTagReadonly(string tag) => _tags.TryGetValue(tag, out var value) ? value : new();
    public void ModifyTag(string tag, Action<TagState> modify)
    { if (!_tags.TryGetValue(tag, out var value)) _tags.Add(tag, value = new()); modify(value); }
    public string GetDisplayName() => identifier;
    public void Destroy() { contentWindow?.Hide(); }
}
public sealed class GameItemElement : GameItem
{
    public UnityEngine.Sprite? ResolveSpriteByName(string name) => null;
}
public sealed class TagState
{
    public bool Enabled; public string Text = ""; public int Number;
    public TagState Enable() { Enabled = true; return this; }
    public TagState SetString(string value) { Text = value; return this; }
    public TagState SetInt(int value) { Number = value; return this; }
    public string GetString() => Text;
    public int GetInt() => Number;
}
public static class ModuleEffectHelper
{
    public sealed class ModuleEffect { public string displayName = "", description = ""; }
    public static readonly Dictionary<string, ModuleEffect> moduleEffects = new();
    public static void HandleModuleEffectAction(GameItem item, GameInventory? inventory) { }
    public static void CreateModuleEffectTooltip(RichTextBuilder builder, GameItem item) { }
}
public sealed class RichTextBuilder
{
    public List<string> Lines = new();
    public void AddLine(string text, bool ignoreEmpty = false, object? color = null, bool bold = false) => Lines.Add(text);
}
public static class RenderHandler
{
    public static UnityEngine.Sprite? LoadFromAtlas(string atlas, string name) => null;
    public static class ColorPalette { public static readonly object LightBlue = new(); }
}
public class GameInventory { public List<GameItem> childItems = new(); }
public sealed class GameSlotInventory : GameInventory { public GameItem? childItem => childItems.FirstOrDefault(); }
public static class WaterHelper { public static int GetTotalVolume(GameItem item) => item.WaterParts; }
public static class MachineHelper { public static object? GetBodyStorage(GameItem item, int index) => item.Storages[index]; }
public static class GeneralHelper { public static bool IsItemOwned(GameItem item) => item.Owned; }
public static class DirectoryMaster
{
    public static bool Has<T>(string id) => true;
    public static T Create<T>(string id) => (T)(object)new GameItem { identifier = id };
}
public static class StoreStation { public static int GetDayCounter() => 1; }
public sealed class PlayerStore
{
    public static bool HasInstance => true;
    public static PlayerStore Instance = new();
    public string runID = "test";
    public List<GameItem> Items = new();
    public List<GameItem> FindAllItem() => Items;
}
namespace UnityEngine
{
    public class Object { public static void Destroy(GameObject value) => value.Destroyed = true; }
    public sealed class GameObject { public bool Destroyed; }
    public sealed class Sprite { }
    public sealed class Coroutine { }
    public static class Debug
    {
        public static List<string> Errors = new();
        public static void LogError(object error) => Errors.Add(error.ToString()!);
        public static void LogWarning(object error) { }
        public static void Log(object message) { }
    }
}
namespace UnityEngine.Localization.Settings
{
    public static partial class LocalizationSettings
    {
        public static TestLocale? SelectedLocale => throw new NotSupportedException("Native locale selection requires Unity.");
        public static StringDatabasePort StringDatabase { get; } = new();
    }

    public sealed class StringDatabasePort
    {
        public LocaleOperation GetTableAsync(string name, TestLocale locale) =>
            throw new NotSupportedException("Native string tables require Unity.");
    }
}
namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class HarmonyPatch : Attribute { public HarmonyPatch(Type type, string method) { } }
    public sealed class Harmony(string id)
    {
        public string Id => id;
        public static List<string> Removed = new();
        public void Patch(System.Reflection.MethodInfo target, HarmonyMethod? prefix = null, HarmonyMethod? postfix = null) =>
            throw new NotSupportedException("Runtime patches require Unity.");
        public void UnpatchAll(string owner) => Removed.Add(owner);
    }

    public sealed class HarmonyMethod
    {
        public HarmonyMethod(Type type, string name) { }
    }

    public static class AccessTools
    {
        public static System.Reflection.MethodInfo Method(Type type, string name) => type.GetMethod(name)!;
    }
}
