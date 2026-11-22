using System;
using System.Collections.Generic;
using System.Linq;

namespace NicoItemManager
{
    internal sealed class ModuleShape
    {
        internal string Atlas, Sprite, Label;
    }

    internal static class ModuleEditing
    {
        internal static readonly string[] Stats = { "BONUS_PERCENTAGE_PERFORMANCE_INT",
            "BONUS_PERCENTAGE_EFFICIENCY_INT", "BONUS_PERCENTAGE_QUALITY_INT" };
        internal static readonly string[] Learning = { "LEARNING_ALGO_MAX_PERFORMANCE_INT",
            "LEARNING_ALGO_MAX_EFFICIENCY_INT", "LEARNING_ALGO_MAX_QUALITY_INT" };
        internal static readonly string[] ManagedTags = Stats.Concat(Learning).Concat(new[] {
            "MODULE_EFFECT1_TAG", "MODULE_EFFECT2_TAG", "MODULE_TIER_INT", "TEMP_PERCENTAGE_PERFORMANCE_INT",
            "TEMP_PERCENTAGE_EFFICIENCY_INT", "TEMP_PERCENTAGE_QUALITY_INT" }).ToArray();

        internal static string Effect(GameItem item, int slot) => item.IsTag(EffectKey(slot))
            ? item.GetTagReadonly(EffectKey(slot)).GetString() ?? "" : "";
        static string EffectKey(int slot) => slot == 1 ? "MODULE_EFFECT1_TAG" : "MODULE_EFFECT2_TAG";

        internal static List<string> EffectsFor(GameItem item)
        {
            bool node = item.IsGameItemType("NODE");
            return new[] { "" }.Concat(ModuleEffectHelper.moduleEffects
                .Where(pair => pair.Value.effectType == ModuleEffectHelper.ModuleEffect.ModuleEffectType.Universal
                    || pair.Value.effectType == (node ? ModuleEffectHelper.ModuleEffect.ModuleEffectType.NodeOnly
                        : ModuleEffectHelper.ModuleEffect.ModuleEffectType.ModuleOnly))
                .OrderBy(pair => pair.Value.displayName, StringComparer.CurrentCulture).Select(pair => pair.Key))
                .Concat(new[] { Effect(item, 1), Effect(item, 2) }).Distinct(StringComparer.Ordinal).ToList();
        }

        internal static string EffectName(string id) => string.IsNullOrEmpty(id) ? Text.Get("none")
            : ModuleEffectHelper.moduleEffects.TryGetValue(id, out var effect) ? effect.displayName : id;

        internal static void SetEffect(GameItem item, int slot, string id)
        {
            if (slot != 1 && slot != 2) throw new ArgumentOutOfRangeException(nameof(slot));
            id = id ?? "";
            string previous = Effect(item, slot);
            if (previous == id) return;
            if (id.Length > 0)
            {
                if (!ModuleEffectHelper.moduleEffects.TryGetValue(id, out var effect))
                    throw new InvalidOperationException(Text.Get("this_module_effect_is_not_registered"));
                bool node = item.IsGameItemType("NODE");
                if (effect.effectType != ModuleEffectHelper.ModuleEffect.ModuleEffectType.Universal
                    && effect.effectType != (node ? ModuleEffectHelper.ModuleEffect.ModuleEffectType.NodeOnly
                        : ModuleEffectHelper.ModuleEffect.ModuleEffectType.ModuleOnly))
                    throw new InvalidOperationException(Text.Get("this_effect_does_not_apply_to_this"));
                string other = Effect(item, 3 - slot);
                if (other == id || effect.incompatibleWithEffects?.Contains(other) == true
                    || ModuleEffectHelper.moduleEffects.TryGetValue(other, out var otherEffect)
                        && otherEffect.incompatibleWithEffects?.Contains(id) == true)
                    throw new InvalidOperationException(Text.Get("the_two_effects_duplicate_or_conflict_with"));
            }
            if (previous.Length > 0)
            {
                if (previous == "MODULE_EFFECT_LEARNING_ALGORITHM")
                    for (int i = 0; i < Stats.Length; i++)
                    {
                        int maximum = item.GetTagReadonly(Learning[i]).GetInt();
                        if (maximum > 0 && item.GetTagReadonly(Stats[i]).GetInt() <= 0)
                            item.ModifyTag(Stats[i], tag => tag.SetInt(maximum / 2));
                        item.DisableTag(Learning[i], true);
                    }
                if (ModuleEffectHelper.moduleEffects.TryGetValue(previous, out var oldEffect))
                    oldEffect.onRemoveAction?.Invoke(item);
                item.ModifyTag(EffectKey(slot), tag => tag.SetString(""));
                item.DisableTag(EffectKey(slot), true);
            }
            if (id.Length > 0)
            {
                item.ModifyTag(EffectKey(slot), tag => tag.Enable().SetString(id));
                ModuleEffectHelper.moduleEffects[id].onCreateAction?.Invoke(item, slot);
            }
            item.ValidateShapeState();
            ModuleEffectHelper.ResetAllTempStat(item);
        }

        internal static List<ModuleShape> ReadShapes(GameItem item)
        {
            var result = new List<ModuleShape>();
            if (item.useSpriteFromMod || item.spriteAtlasPath != "Items/items_module") return result;
            string family = item.spritePath;
            string[] markers = { "_c", "_l", "_t", "_s", "_h" };
            string marker = markers.FirstOrDefault(suffix => family.EndsWith(suffix, StringComparison.Ordinal));
            if (marker != null) family = family.Substring(0, family.Length - marker.Length);
            if (family.EndsWith("_2", StringComparison.Ordinal) || family.EndsWith("_3", StringComparison.Ordinal))
                family = family.Substring(0, family.Length - 2);
            var paths = new[] { item.spritePath, family, family + "_2", family + "_3" }
                .Concat(new[] { "", "_2", "_3" }.SelectMany(size => markers.Select(suffix => family + size + suffix)))
                .Distinct(StringComparer.Ordinal);
            foreach (string path in paths)
            {
                if (RenderHandler.TryLoadFromAtlas(item.spriteAtlasPath, path) == null) continue;
                GridShapeBuilder shape = GameItem.BuildShapeFromSprite(item.spriteAtlasPath, path);
                int occupied = 0;
                for (int y = 0; y < shape.height; y++)
                    for (int x = 0; x < shape.width; x++) if (shape.GetLocal(x, y) == 1) occupied++;
                result.Add(new ModuleShape { Atlas = item.spriteAtlasPath, Sprite = path,
                    Label = Text.Get("cells", path, shape.width, shape.height, occupied) });
            }
            return result;
        }
    }

    internal static class LiquidEditing
    {
        static readonly Liquid[] pureLiquids = Liquid.Liquids.Where(liquid => liquid.identifier != "water")
            .OrderBy(liquid => liquid.identifier == "alcohol" ? 0 : liquid.identifier == "acid" ? 1
                : liquid.identifier == "base" ? 2 : 3).ToArray();

        internal static readonly Action<GameItem>[] Presets = new Action<GameItem>[] {
            WaterHelper.FillWithPureWater, WaterHelper.FillWithHighQualityWater, WaterHelper.FillWithBaseWater,
            WaterHelper.FillWithGhostwaterHighEnd, WaterHelper.FillWithGhostwater, WaterHelper.FillWithGhostwaterLowEnd,
            WaterHelper.FillWithRustWaterHighEnd, WaterHelper.FillWithRustWater, WaterHelper.FillWithRustWaterLowEnd,
            WaterHelper.FillWithGutterflow,
            item => WaterHelper.FillWithNutrientSolution(item, WaterHelper.GetTotalCapacityML(item) * 1000),
            item => WaterHelper.PourSpirit(item, WaterHelper.GetTotalCapacityML(item), WaterHelper.NUDKA_ALCOHOL_PERCENT)
        }.Concat(pureLiquids.Select(liquid => new Action<GameItem>(item => WaterHelper.AddMaxLiquid(item, liquid.identifier)))).ToArray();
        internal static void Refresh(GameItem item) => WaterFeatureHelper.InitWaterFeature(item, isExpose: true);
        internal static List<string> Labels()
        {
            string ghostwater = LocHelper.GetLocalizedMechanic("mech_condition_ghostwater");
            string rustwater = LocHelper.GetLocalizedMechanic("mech_condition_rustwater");
            return new List<string> {
                LocHelper.GetLocalizedMechanic("mech_condition_pure_water"),
                LocHelper.GetLocalizedMechanic("mech_condition_high_quality_water"),
                LocHelper.GetLocalizedMechanic("mech_condition_basewater"),
                Text.Get("liquid.high", ghostwater), Text.Get("liquid.normal", ghostwater), Text.Get("liquid.low", ghostwater),
                Text.Get("liquid.high", rustwater), Text.Get("liquid.normal", rustwater), Text.Get("liquid.low", rustwater),
                LocHelper.GetLocalizedMechanic("mech_condition_gutterflow"),
                LocHelper.GetLocalizedMechanic("mech_item_nutrient_solution"), LocHelper.GetLocalizedItem("item_nudka_name")
            }.Concat(pureLiquids.Select(liquid => Text.Get("liquid.concentration", liquid.displayName, 100))).ToList();
        }
    }
}
