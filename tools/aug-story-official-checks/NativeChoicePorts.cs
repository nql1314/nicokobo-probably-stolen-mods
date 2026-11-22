// These ports exercise the production pagination algorithm without creating a game/Unity session.
public sealed class GameItem { public string name = ""; }
public sealed class Dialogue
{
    public string title = "", plainText = "";
    public Dialogue? nextDialogue;
    public readonly List<(GameItem, Dialogue?, Action?)> choices = [];
    public Dialogue SetText(string speaker, string text) { title = speaker; plainText = text; return this; }
    public Dialogue SetNextDialogue(Dialogue? next) { nextDialogue = next; return this; }
    public Dialogue AddChoice(GameItem item, string label, string description, Dialogue? choice, Action? action, Func<bool>? enabled)
    { item.name = label; choices.Add((item, choice, action)); return this; }
}
public static class ModHelper
{
    public static string GetLocalized(string modId, string key, params object[] args) => key;
    public static ModConfig GetConfig(string id) => throw new NotSupportedException("Mod configuration requires the game.");
}

public sealed class ModConfig
{
    public object? Get(string key) => null;
    public ConfigEntry<T> Define<T>(string key, T value, string section, Func<string> label) => new(value);
}

public sealed record ConfigEntry<T>(T Value);

namespace AugMechanicalAscension.Official { }
