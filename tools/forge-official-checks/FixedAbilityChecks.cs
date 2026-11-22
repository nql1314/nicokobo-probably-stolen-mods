extern alias clientA;
extern alias clientB;
using A = clientA::ForgeChecks.ClientProbe;
using B = clientB::ForgeChecks.ClientProbe;
using Nicokobo.ContentInterop;

internal static class FixedAbilityChecks
{
    internal static GameItem Run(Action<bool, string> check)
    {
        ModuleEffectHelper.moduleEffects.Add("native.affix", new() { displayName = "Native affix", description = "Native description" });
        var nativeIds = ModuleEffectHelper.moduleEffects.Keys.ToArray();
        A.FixedAbility();
        check(B.HasAbility() && ModuleEffectHelper.moduleEffects.Keys.SequenceEqual(nativeIds),
            "Cross-client fixed registration must leave the native random registry unchanged");
        bool collision = false; try { B.FixedAbility(); } catch (InvalidOperationException) { collision = true; }
        check(collision, "Fixed abilities must share one owner-checked catalog across client assemblies");
        var fresh = new GameItem { identifier = "fixed.node" }; B.AddAbility(fresh); B.AddAbility(fresh);
        check(fresh.GetTagReadonly("MODULE_EFFECT1_TAG").GetString() == "fixed.ability" &&
            fresh.GetTagReadonly("MODULE_EFFECT1_TAG").GetInt() == 25 && !fresh.IsTag("MODULE_EFFECT2_TAG"),
            "Binding a fixed ability must keep the existing save format and avoid duplicate slots");
        var foreign = new GameItem { identifier = "native.node" };
        bool rejected = false; try { B.AddAbility(foreign); } catch (InvalidOperationException) { rejected = true; }
        check(rejected && !foreign.IsTag("MODULE_EFFECT1_TAG"), "A fixed ability must not attach to unrelated native items");

        // A legacy save already contains these two enabled tags, without any new
        // marker or conversion pass. The real host must resolve that data directly.
        var legacy = new GameItem { identifier = "fixed.node" };
        legacy.ModifyTag("MODULE_EFFECT1_TAG", tag => tag.Enable().SetString("fixed.ability").SetInt(25));
        legacy.ModifyTag("MODULE_EFFECT2_TAG", tag => tag.Enable().SetString("native.affix"));
        FixedModuleActionPatch.Postfix(legacy, null, true);
        check(A.FixedCalls == 1 && legacy.GetTagReadonly("bonus").GetInt() == 25 && legacy.GetTagReadonly("MODULE_EFFECT1_TAG").GetInt() == 25,
            "A legacy fixed tag must route once to its original client callback without rewriting saved state");
        FixedModuleActionPatch.Postfix(legacy, null, false);
        check(A.FixedCalls == 1, "A skipped native calculation must not run the private callback");
        foreign.ModifyTag("MODULE_EFFECT1_TAG", tag => tag.Enable().SetString("fixed.ability"));
        FixedModuleActionPatch.Postfix(foreign, null, true);
        check(A.FixedCalls == 1, "Saved fixed tags on foreign items must not grant the fixed node's ability");

        var text = new RichTextBuilder();
        check(!FixedModuleTooltipPatch.Prefix(text, legacy) && text.Lines.SequenceEqual(new[]
            { "Fixed ability", "Fixed description", "Native affix", "Native description" }),
            "Mixed legacy fixed/native tooltip slots must display once and in their saved order");
        A.AbilityName = "Localized fixed ability"; text = new(); FixedModuleTooltipPatch.Prefix(text, legacy);
        check(text.Lines[0] == A.AbilityName, "Ability tooltip text must remain dynamic across language changes");
        legacy.shortDescription = "Fixed description"; text = new(); FixedModuleTooltipPatch.Prefix(text, legacy);
        check(text.Lines.SequenceEqual(new[] { A.AbilityName, "Native affix", "Native description" }),
            "A standalone item description must not be duplicated by its fixed ability block");
        var native = new GameItem(); native.ModifyTag("MODULE_EFFECT1_TAG", tag => tag.Enable().SetString("native.affix"));
        text = new(); check(FixedModuleTooltipPatch.Prefix(text, native) && text.Lines.Count == 0,
            "Native-only tooltips must continue through the original implementation");
        return legacy;
    }
}
