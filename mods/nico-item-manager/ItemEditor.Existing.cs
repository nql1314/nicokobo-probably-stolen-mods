using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace NicoItemManager
{
    internal sealed partial class ItemEditor
    {
        internal GameItem Target { get; private set; }
        internal bool IsExisting => Target != null;
        long originalValue;
        bool valueEdited;
        ItemFeatureList.EquipmentCondition originalCondition;
        const BindingFlags FeatureFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static readonly FieldInfo FeaturePublicDisplay = typeof(ItemFeature).GetField("publicDisplay", FeatureFields);
        static readonly FieldInfo FeatureActualDisplay = typeof(ItemFeature).GetField("actualDisplay", FeatureFields);
        static readonly FieldInfo FeatureTool = typeof(ItemFeature).GetField("removedByTool", FeatureFields);
        static readonly FieldInfo FeatureValue = typeof(ItemFeature).GetField("valueModifier", FeatureFields);
        static readonly FieldInfo FeatureInitiallyShown = typeof(ItemFeature).GetField("initiallyShown", FeatureFields);
        static readonly FieldInfo FeatureExposed = typeof(ItemFeature).GetField("isFeatureExposed", FeatureFields);
        static readonly FieldInfo FeatureDiscovered = typeof(ItemFeature).GetField("isFeatureDiscovered", FeatureFields);
        static readonly FieldInfo FeaturePreExpose = typeof(ItemFeature).GetField("usePreExposeValue", FeatureFields);
        static readonly FieldInfo FeaturePreExposeValue = typeof(ItemFeature).GetField("preExposeValueModifier", FeatureFields);

        internal ItemEditor(GameItem source) : this(ExistingItems.RequireEditable(source).identifier, source) { }

        void LoadExisting(GameItem source)
        {
            Target = source;
            CopyState(source, Item);
            originalValue = Item.unitValue;
            if (IsGun) originalCondition = GunFrameHelper.GetCondition(Item);
        }

        internal static void CopyState(GameItem source, GameItem target)
        {
            // Retain the factory's identity, callbacks and windows. Reusing the
            // source's windows or delegates would bind the copy to the real item.
            target.state = source.state.Clone();
            target.itemFeatures = source.itemFeatures.Select(CopyFeature).ToList();
            foreach (ItemFeature feature in target.itemFeatures) feature.parentItemUniqueId = target.uniqueId;
            target.itemTypes = new List<string>(source.itemTypes);
            target.unitCount = source.unitCount;
            target.unitValue = source.unitValue;
            target.unitBaseValue = source.unitBaseValue;
            target.lateUnitValue = source.lateUnitValue;
            target.backupUnitValue = source.backupUnitValue;
            target.name = source.name;
            target.shortDescription = source.shortDescription;
            target.longDescription = source.longDescription;
            target.flavorText = source.flavorText;
            target.customText = source.customText;
            target.activateDefault = source.activateDefault;
            target.SetSprite(source.spriteAtlasPath, source.spritePath);
            target.useSpriteFromMod = source.useSpriteFromMod;
            target.shape = source.shape.Clone();
            target.ValidateShapeState();
        }

        static ItemFeature CopyFeature(ItemFeature source)
        {
            // These native records include private appraisal fields; rebuilding
            // them through public setters would lose hidden/exposed state.
            var copy = new ItemFeature
            {
                identifier = source.identifier, category = source.category, featureType = source.featureType, valueStage = source.valueStage,
                isFeatureMatch = source.isFeatureMatch, isExposing = source.isExposing, useCondition = source.useCondition,
                isExposable = source.isExposable, isPublicHidden = source.isPublicHidden, isDisabled = source.isDisabled,
                customIntValue1 = source.customIntValue1, realCondition = CopyCondition(source.realCondition), fakeCondition = CopyCondition(source.fakeCondition)
            };
            FeaturePublicDisplay.SetValue(copy, FeaturePublicDisplay.GetValue(source));
            FeatureActualDisplay.SetValue(copy, FeatureActualDisplay.GetValue(source));
            FeatureTool.SetValue(copy, FeatureTool.GetValue(source));
            FeatureValue.SetValue(copy, FeatureValue.GetValue(source));
            FeatureInitiallyShown.SetValue(copy, FeatureInitiallyShown.GetValue(source));
            FeatureExposed.SetValue(copy, FeatureExposed.GetValue(source));
            FeatureDiscovered.SetValue(copy, FeatureDiscovered.GetValue(source));
            FeaturePreExpose.SetValue(copy, FeaturePreExpose.GetValue(source));
            FeaturePreExposeValue.SetValue(copy, FeaturePreExposeValue.GetValue(source));
            return copy;
        }

        static ItemCondition CopyCondition(ItemCondition source) => source == null ? null : new ItemCondition
        {
            display = source.display, identifier = source.identifier, modValue = source.modValue, modValueDisplay = source.modValueDisplay,
            isTransformative = source.isTransformative, category = source.category, hiddenAsPublic = source.hiddenAsPublic,
            customValue = source.customValue, onActivateActionId = source.onActivateActionId, compareValue = source.compareValue
        };

        internal void ApplyExisting()
        {
            GameItem target = ExistingItems.RequireEditable(Target);
            Dictionary<string, TagState> changes = ReadChanges();
            bool valueChanged = valueEdited && Item.unitValue != originalValue;
            bool conditionChanged = GunConditionChanged && GunFrameHelper.GetCondition(Item) != originalCondition;
            if (changes.Count == 0 && !valueChanged && !conditionChanged) return;

            if (IsModule && changes.Keys.Any(key => key == "MODULE_EFFECT1_TAG" || key == "MODULE_EFFECT2_TAG"))
            {
                string effect1 = ModuleEditing.Effect(Item, 1), effect2 = ModuleEditing.Effect(Item, 2);
                for (int slot = 1; slot <= 2; slot++)
                    if (ModuleEditing.Effect(target, slot) != (slot == 1 ? effect1 : effect2)) ModuleEditing.SetEffect(target, slot, "");
                ModuleEditing.SetEffect(target, 1, effect1);
                ModuleEditing.SetEffect(target, 2, effect2);
            }
            // Generated copies need all managed fields. Existing items receive
            // only the delta; native helpers below recompute derived/live state.
            foreach (var pair in changes)
            {
                if (IsGun && GunDerived.Contains(pair.Key)) continue;
                if (pair.Value == null) target.state.dict.Remove(pair.Key);
                else target.state.dict[pair.Key] = pair.Value.Clone();
            }
            target.ValidateShapeState();
            ItemConfiguration.ApplyPurity(target, changes);
            if (IsModule && changes.Keys.Any(ModuleEditing.ManagedTags.Contains)) ModuleEffectHelper.ResetAllTempStat(target);
            if (IsLiquid && liquidEdited)
            {
                WaterHelper.RemoveExcessLiquid(target);
                LiquidEditing.Refresh(target);
            }
            if (valueChanged) target.SetValue(Item.unitValue);
            if (IsBattery && changes.Keys.Any(BatteryTags.Contains))
                PowerHelper.SetPowerSourceAt(target, Math.Min(target.GetTagReadonly("power_source_item_energy").GetInt(),
                    target.GetTagReadonly("power_source_item_max_energy").GetInt()));
            if (HasDurability && changes.Keys.Any(DurabilityTags.Contains))
                DurabilityHelper.SetDurability(target,
                    Math.Min(DurabilityHelper.GetRealDurability(target), DurabilityHelper.GetMaxDurability(target)),
                    Math.Min(DurabilityHelper.GetFakeDurability(target), DurabilityHelper.GetMaxDurability(target)),
                    DurabilityHelper.GetMaxDurability(target), DurabilityHelper.GetDefDurability(target));
            if (IsArmor && changes.Keys.Any(ArmorTags.Contains))
                target.ModifyTag("current_armor_value", tag => tag.SetInt(Math.Min(target.GetTagReadonly("current_armor_value").GetInt(),
                    target.GetTagReadonly("current_max_armor_value").GetInt())));
            if (IsGun && (conditionChanged || changes.Keys.Any(key => GunStats.Contains(key) || GunDerived.Contains(key))))
            {
                if (conditionChanged) GunHelper.SetEquipmentCondition(target, GunFrameHelper.GetCondition(Item));
                else GunHelper.UpdateCurrentGunStats(target);
            }
            if (HasUseCount && changes.Keys.Any(UseCountTags.Contains))
                UseCountHelper.SetCurrentUseCount(target, Math.Min(target.GetTagReadonly("CURRENT_USE_COUNT_INT").GetInt(),
                    target.GetTagReadonly("MAX_USE_COUNT_INT").GetInt()));
            if (valueChanged) target.SetValue(Item.unitValue);
            target.ValidateShapeState();
            OverlayHandler.current?.UpdateInventoryStateUI();
        }
    }
}
