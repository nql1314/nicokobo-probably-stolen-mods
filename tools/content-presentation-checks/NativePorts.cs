// Record item boundaries used by the production presentation code.
namespace UnityEngine
{
    public class Object { public static void Destroy(GameObject value) => value.Destroyed = true; }
    public sealed class GameObject { public bool Destroyed; }
    public sealed class Sprite { }
    public static class Debug { public static void LogWarning(string text) { } }
    public struct Vector3 { }
    public static class Time { public static int frameCount; }
    public static class Input
    {
        public static Vector3 mousePosition;
        public static bool MouseHeld, MouseReleased;
        public static bool GetMouseButton(int button) => button == 0 && MouseHeld;
        public static bool GetMouseButtonUp(int button) => button == 0 && MouseReleased;
    }
}

namespace HarmonyLib
{
    public sealed class Harmony(string id)
    {
        public string Id => id;
        public void Patch(System.Reflection.MethodInfo target, HarmonyMethod? prefix, HarmonyMethod? postfix) { }
        public void UnpatchAll(string owner) { }
    }
    public sealed class HarmonyMethod { public HarmonyMethod(System.Reflection.MethodInfo? method) { } }
    public static class AccessTools
    {
        public static System.Reflection.MethodInfo? Method(Type type, string name, Type[]? parameters = null) =>
            type.GetMethod(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance);
    }
}

namespace Nicokobo.ContentInterop
{
    internal static class OfficialStoryUi { internal static bool AnyVisible, PointerOverWindow; }
    internal static class OfficialWorkshopApi { internal static bool IsVisible, PointerOverWindow; }
    internal static class RecipeQuery { internal static bool IsVisible, PointerOverWindow; }
    internal static class OfficialStoryHooks
    {
        internal static bool ValidateTarget(System.Reflection.MethodInfo? target, out string reason)
        { reason = ""; return target != null; }
    }
}

public sealed class ItemMouseDragHandler
{
    public static ItemMouseDragHandler? current;
    public int Resets;
    public void UpdateReset() => Resets++;
}
public sealed class InputActionManager
{
    public static InputActionManager? current;
    public int Resets;
    public void Reset() => Resets++;
}
public sealed class CustomEventHandler
{
    public float doubleClickTimeThreshold;
    public static void ClearDoubleClickTarget() { }
}
public sealed class StoreUIManager { public bool hoveringStoreUI; public void CheckRaycast() { } }

public class GraphNodeStorage { public List<GraphNodeStorage> children = new(); }
public class GameInventory : GraphNodeStorage { }
public sealed class GameSlotInventory : GameInventory { }
public sealed class TreeNodeRender { public UnityEngine.GameObject gameObject = new(); }
public sealed class PixelWindow : GraphNodeStorage
{
    public List<GameItem> parentItems = new();
    public TreeNodeRender handler = new();
    public int HideCalls;
    public void Hide() => HideCalls++;
}
public static class GraphHandler { public static List<PixelWindow> windowGraphNodeRoots = new(); }
public class GameItem : GraphNodeStorage
{
    public string identifier = "", name = "", shortDescription = "", flavorText = "", spritePath = "", spriteAtlasPath = "";
    public bool useSpriteFromMod;
    public object? parentInventory;
    public int Destroys;
    public PixelWindow? contentWindow, examineWindow;
    public void SetContentWindow(PixelWindow? value)
    { contentWindow?.parentItems.Remove(this); contentWindow = value; value?.parentItems.Add(this); }
    public void SetExamineWindow(PixelWindow? value)
    { examineWindow?.parentItems.Remove(this); examineWindow = value; value?.parentItems.Add(this); }
    public string GetDisplayName() => name;
    public void Destroy() { Destroys++; contentWindow?.Hide(); }
}
public sealed class GameItemElement : GameItem
{
    public Func<string, UnityEngine.Sprite?> Resolve = _ => null;
    public UnityEngine.Sprite? ResolveSpriteByName(string key) => Resolve(key);
}
public static class RenderHandler
{
    public static readonly Dictionary<(string Atlas, string Key), UnityEngine.Sprite> Sprites = new();
    public static UnityEngine.Sprite? LoadFromAtlas(string atlas, string key) => Sprites.GetValueOrDefault((atlas, key));
}

public static class DirectoryMaster
{
    public static readonly Dictionary<string, Func<GameItem>> Factories = new();
    public static bool Has<T>(string id) => Factories.ContainsKey(id);
    public static T Create<T>(string id) => (T)(object)Factories[id]();
}

internal static class MechcorePackage
{
    internal const string Id = "nicokobo.mechcore_protocol";
}
