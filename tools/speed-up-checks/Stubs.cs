// Managed fixtures for pure regression checks. They do not execute Unity or load a save.
namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class HarmonyPatch : Attribute
    {
        public Type Type { get; }
        public string Method { get; }
        public HarmonyPatch(Type type, string method) { Type = type; Method = method; }
    }
}

public sealed class Dialogue
{
    public Action endAction;
    public bool isEndActionExecuted;
    public Dialogue nextDialogue;
    public readonly List<string> choices = new();
    public void ExecuteEndAction()
    {
        if (isEndActionExecuted || endAction == null) return;
        isEndActionExecuted = true;
        endAction.Invoke();
    }
}

public class DialogUIManager
{
    public static DialogUIManager Instance;
    public TextBubble clientBubble, playerBubble, secBubble;
    public Dialogue currentDialog, displayingUncompletedDialog;
    public bool HasClient = true;
    public static float GetTypewriterDelay() => 0;
    public void ContinueConversation() { }
}
public sealed class DialoguePhoneUIManager : DialogUIManager
{
    public new static DialoguePhoneUIManager Instance;
    public new void ContinueConversation() { }
}

public sealed class TextBubble
{
    public Febucci.UI.TypewriterByCharacter typewriter = new();
}

namespace Febucci.UI
{
    public sealed class TypewriterByCharacter { }
}

namespace DG.Tweening
{
    public sealed class Tween { public float timeScale = 1f; }
    public sealed class DOTweenPath { public Tween tween = new(); }
}

public sealed class StoreClientMono
{
    public DG.Tweening.DOTweenPath dOTweenPath = new(), pathRight = new();
    public void StartApprochingStore() { }
    public void StartLeavingStore() { }
    public void StartLeavingStoreRight() { }
}
public sealed class GenericClientMono
{
    public DG.Tweening.DOTweenPath dOTweenPath = new();
    public void StartArrive() { }
    public void StartLeave() { }
}
public sealed class StoreShutterButton
{
    public void ToggleShutterDoor() { }
    public void ForceImmediateClose() { }
}

// Persistence is in memory here. Native ModConfig supplies deserialization,
// ranges and disk writes; the tests exercise the Mod's settings operations.
public abstract class ConfigEntry
{
    public string Key, Section;
}
public sealed class ConfigEntry<T> : ConfigEntry
{
    public T Value { get; private set; }
    readonly ModConfig config;
    public ConfigEntry(string key, T value, string section, ModConfig config)
    { Key = key; Value = value; Section = section; this.config = config; }
    public void Set(T value) { Value = value; config.Saved[Key] = value; config.Writes++; }
}
public sealed class ModConfig
{
    readonly Dictionary<string, ConfigEntry> entries = new();
    public readonly Dictionary<string, object> Saved = new();
    public int Writes;
    public ConfigEntry Get(string key) => entries[key];
    public IEnumerable<ConfigEntry> Entries => entries.Values;
    ConfigEntry<T> Define<T>(string key, T value, string section)
    {
        var entry = new ConfigEntry<T>(key, Saved.TryGetValue(key, out var saved) ? (T)saved : value, section, this);
        entries[key] = entry;
        return entry;
    }
    public ConfigEntry<bool> Define(string key, bool value, string section, Func<string> label, Func<string> tooltip = null) => Define(key, value, section);
    public ConfigEntry<float> Define(string key, float value, string section, Func<string> label, float? min = null, float? max = null) => Define(key, value, section);
    public ConfigEntry<string> Define(string key, string value, string section, Func<string> label, string[] choices = null, Func<string> tooltip = null) => Define(key, value, section);
}
public static class ModHelper
{
    static readonly ModConfig config = new();
    public static ModConfig GetConfig(string id) => config;
    public static string GetLocalized(string id, string key, params object[] args) => key;
}
public sealed class ModLog { public void Log(string text) { } }
public static class KeyBindHelper { public static bool SuppressHotkeys; }
public static class StoreUIManager
{
    public static bool Editing;
    public static bool IsTextFieldFocused() => Editing;
}

namespace SpeedUp
{
    internal static class SpeedUpMod { internal const string Id = "nicokobo.speed-up"; internal static readonly ModLog Log = new(); }
    internal static class SpeedNotice { internal static void Show(string text) { } }
    internal static class GlobalSpeed { internal static void Update() { } }
}

namespace UnityEngine
{
    public static class Application { public static bool isFocused = true; }
    public static class Time { public static int frameCount; }
    public class MonoBehaviour { public bool isActiveAndEnabled = true; }
    public class GameObject
    {
        readonly Dictionary<Type, object> components = new();
        public T AddComponent<T>() where T : new() { var component = new T(); components[typeof(T)] = component; return component; }
        public T GetComponentInParent<T>() where T : class => components.TryGetValue(typeof(T), out var value) ? value as T : null;
    }
    public enum KeyCode
    {
        None = 0, Escape = 27, Space = 32, Plus = 43, Minus = 45, Equals = 61, A = 97,
        KeypadMinus = 269, KeypadPlus = 270, Home = 278, PageUp = 280, PageDown = 281,
        F8 = 289, F9 = 290, RightShift = 303, LeftShift = 304, RightControl = 305, LeftControl = 306,
        RightAlt = 307, LeftAlt = 308, RightCommand = 309, LeftCommand = 310, LeftWindows = 311,
        RightWindows = 312, Mouse0 = 323
    }
    public static class Input
    {
        public static string compositionString = "";
        internal static readonly HashSet<KeyCode> Held = new(), Down = new();
        public static bool GetKey(KeyCode key) => Held.Contains(key);
        public static bool GetKeyDown(KeyCode key) => Down.Contains(key);
    }
}
namespace UnityEngine.EventSystems
{
    public class EventSystem
    { public static EventSystem current; public UnityEngine.GameObject currentSelectedGameObject; }
}
namespace TMPro { public class TMP_InputField : UnityEngine.MonoBehaviour { public bool isFocused; } }
namespace UnityEngine.UI { public class InputField : UnityEngine.MonoBehaviour { public bool isFocused; } }
