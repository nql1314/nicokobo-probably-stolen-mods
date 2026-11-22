using HarmonyLib;

namespace Nicokobo.ContentInterop;

// Fixed abilities retain their saved MODULE_EFFECT tags, but never enter the
// native registry: that registry is also the native random-affix candidate pool.
public static class FixedModuleAbilities
{
    public const int ApiVersion = 1;
    private sealed class Definition(string owner, Func<GameItem, bool> appliesTo,
        Func<string> name, Func<string> description, Action<GameItem, GameInventory?>? compute)
    {
        internal readonly string Owner = owner;
        internal readonly Func<GameItem, bool> AppliesTo = appliesTo;
        internal readonly Func<string> Name = name, Description = description;
        internal readonly Action<GameItem, GameInventory?>? Compute = compute;
    }
    private static readonly Dictionary<string, Definition> Definitions = new(StringComparer.Ordinal);
    private static readonly string[] Slots = { "MODULE_EFFECT1_TAG", "MODULE_EFFECT2_TAG" };

    public static void RequireVersion(int version)
    {
        if (version != ApiVersion) throw new InvalidOperationException("Unsupported Forge fixed-module ability API: " + version);
    }

    public static void Register(string owner, string id, Func<GameItem, bool> appliesTo,
        Func<string> name, Func<string> description, Action<GameItem, GameInventory?>? compute = null)
    {
        ContentRuntime.RequireOwner(owner);
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Empty fixed-module ability ID", nameof(id));
        if (appliesTo == null || name == null || description == null) throw new ArgumentNullException("Fixed-module ability callbacks");
        if (Definitions.ContainsKey(id) || ModuleEffectHelper.moduleEffects.ContainsKey(id))
            throw new InvalidOperationException("Module ability ID collision: " + id);
        Definitions.Add(id, new(owner, appliesTo, name, description, compute));
    }

    public static bool Contains(string id) => Definitions.ContainsKey(id);

    public static void Add(GameItem item, string id, int parameter = 0)
    {
        if (!Definitions.TryGetValue(id, out var definition) || !definition.AppliesTo(item))
            throw new InvalidOperationException("Fixed module ability does not apply to " + item.identifier + ": " + id);
        foreach (string slot in Slots)
            if (item.IsTag(slot) && item.GetTagReadonly(slot).GetString() == id) return;
        string target = Slots.FirstOrDefault(slot => !item.IsTag(slot))
            ?? throw new InvalidOperationException("No free module effect slot on " + item.identifier);
        item.ModifyTag(target, tag => tag.Enable().SetString(id).SetInt(parameter));
    }

    internal static void Compute(GameItem item, GameInventory? inventory)
    {
        foreach (string slot in Slots)
            if (item.IsTag(slot) && Definitions.TryGetValue(item.GetTagReadonly(slot).GetString(), out var definition)
                && definition.AppliesTo(item)) definition.Compute?.Invoke(item, inventory);
    }

    internal static bool CreateTooltip(RichTextBuilder builder, GameItem item)
    {
        if (!Slots.Any(slot => item.IsTag(slot) && Definitions.ContainsKey(item.GetTagReadonly(slot).GetString()))) return false;
        foreach (string slot in Slots)
        {
            if (!item.IsTag(slot)) continue;
            string id = item.GetTagReadonly(slot).GetString();
            string name, description;
            if (Definitions.TryGetValue(id, out var definition)) { name = definition.Name(); description = definition.Description(); }
            else { var native = ModuleEffectHelper.moduleEffects[id]; name = native.displayName; description = native.description; }
            builder.AddLine(name, ignoreEmpty: true, color: RenderHandler.ColorPalette.LightBlue, bold: true);
            // Native item tooltips append shortDescription after the module block.
            if (description != item.shortDescription)
                builder.AddLine(description, ignoreEmpty: true, color: RenderHandler.ColorPalette.LightBlue);
        }
        return true;
    }

    public static void Remove(string owner)
    { foreach (string id in Definitions.Where(pair => pair.Value.Owner == owner).Select(pair => pair.Key).ToArray()) Definitions.Remove(id); }
    internal static void Clear() => Definitions.Clear();

    internal static void Patch(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(ModuleEffectHelper), nameof(ModuleEffectHelper.HandleModuleEffectAction)),
            postfix: new HarmonyMethod(typeof(FixedModuleActionPatch), nameof(FixedModuleActionPatch.Postfix)));
        harmony.Patch(AccessTools.Method(typeof(ModuleEffectHelper), nameof(ModuleEffectHelper.CreateModuleEffectTooltip)),
            prefix: new HarmonyMethod(typeof(FixedModuleTooltipPatch), nameof(FixedModuleTooltipPatch.Prefix)));
    }
}

internal static class FixedModuleActionPatch
{
    internal static void Postfix(GameItem __0, GameInventory? __1, bool __runOriginal)
    { if (__runOriginal) FixedModuleAbilities.Compute(__0, __1); }
}

// Native tooltip lookup indexes its random registry directly. Resolve private
// abilities only when present; mixed native/private slots keep their saved order.
internal static class FixedModuleTooltipPatch
{
    internal static bool Prefix(RichTextBuilder __0, GameItem __1) => !FixedModuleAbilities.CreateTooltip(__0, __1);
}
