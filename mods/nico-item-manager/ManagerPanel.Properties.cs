using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace NicoItemManager
{
    internal sealed partial class ManagerPanel
    {
        readonly Dictionary<CustomUIElement, Func<string>> propertyValues = new Dictionary<CustomUIElement, Func<string>>();
        CustomUIElement gunConditionInput;

        void PropertyNumber(CustomUIBuilder builder, string title, string key, Action<int> change)
        {
            NumberField(builder, title, value => change(ParseInt(value)), out CustomUIElement field);
            propertyValues.Add(field, () => editor.Int(key).ToString(CultureInfo.InvariantCulture));
        }

        void PropertyInfo(CustomUIBuilder builder, Func<string> value)
        {
            builder.AddLabel("");
            Label(builder.Last, 16, true);
            propertyValues.Add(builder.Last, value);
        }

        static void PropertyTitle(CustomUIBuilder builder, string title)
        {
            builder.AddLabel(title);
            Label(builder.Last, 19);
        }

        void TagParameterLabel(CustomUIBuilder builder, string key)
        {
            ItemTagDescription description = ItemTagDescriptions.Find(editor.Item, key);
            builder.AddLabel(description?.Name ?? key);
            Fixed(builder.Last, 300, 32);
            Label(builder.Last, 16);
            builder.Last.SetTooltip(description?.Tooltip(key) ?? Text.Get("this_tag_s_meaning_on_this_item") + "\nTag: " + key);
        }

        void BuildItemPropertyFields(CustomUIBuilder builder)
        {
            if (editor.IsBattery) BuildBatteryFields(builder);
            if (editor.HasDurability) BuildDurabilityFields(builder);
            if (editor.IsArmor) BuildArmorFields(builder);
            if (editor.IsGun) BuildGunFields(builder);
            if (editor.HasUseCount) BuildUseCountFields(builder);
            if (editor.HasCalories || editor.HasFreshness) BuildFoodFields(builder);
        }

        void BuildNumericTagFields(CustomUIBuilder builder)
        {
            List<TagEdit> fields = editor.ReadNumericTags();
            if (fields.Count == 0) return;
            PropertyTitle(builder, Text.Get("item_values"));
            builder.AddLabel(Text.Get("edit_using_each_value_s_existing_type"));
            Label(builder.Last, 16, true);
            foreach (TagEdit edit in fields)
            {
                builder.BeginRow(10, TextAnchor.MiddleLeft);
                TagParameterLabel(builder, edit.Key);
                AddTextInput(builder, ItemTags.KindLabel(edit.Kind), edit.Text, value =>
                {
                    ApplyEditor(() =>
                    {
                        TagEdit current = editor.ReadNumericTags().Single(tag => tag.Key == edit.Key);
                        current.Text = value;
                        editor.ApplyTag(current);
                    });
                });
                builder.Last.SetFixedWidth(260);
                propertyValues.Add(builder.Last, () => ItemTags.ValueText(editor.Item.GetTagReadonly(edit.Key), edit.Kind));
                builder.End();
            }
            builder.AddSeparator();
        }

        void BuildBatteryFields(CustomUIBuilder builder)
        {
            PropertyTitle(builder, Text.Get("battery"));
            builder.BeginRow(10);
            PropertyNumber(builder, Text.Get("energy_kj"), "power_source_item_energy", editor.SetBatteryEnergy);
            PropertyNumber(builder, Text.Get("capacity_kj"), "power_source_item_max_energy", editor.SetBatteryCapacity);
            PropertyNumber(builder, Text.Get("output_kw"), "power_source_item_bandwidth", editor.SetBatteryOutput);
            builder.End();
            builder.BeginRow(8);
            builder.AddButton(Text.Get("charge_fully"), () => ApplyEditor(() => editor.SetBatteryEnergy(editor.Int("power_source_item_max_energy"))));
            builder.Last.SetFixedWidth(150);
            builder.AddButton(Text.Get("discharge"), () => ApplyEditor(() => editor.SetBatteryEnergy(0)));
            builder.Last.SetFixedWidth(150);
            builder.End();
            builder.AddSeparator();
        }

        void BuildDurabilityFields(CustomUIBuilder builder)
        {
            PropertyTitle(builder, Text.Get("durability"));
            builder.BeginRow(10);
            PropertyNumber(builder, Text.Get("real_durability"), "DURABILITY_TAG", editor.SetRealDurability);
            PropertyNumber(builder, Text.Get("apparent_durability"), "DURABILITY_FAKE_TAG", editor.SetApparentDurability);
            PropertyNumber(builder, Text.Get("maximum_durability"), "MAX_DURABILITY_TAG", editor.SetMaximumDurability);
            builder.End();
            builder.BeginRow(8);
            builder.AddButton(Text.Get("repair_fully"), () => ApplyEditor(editor.RepairDurability));
            builder.Last.SetFixedWidth(150);
            builder.AddLabel(Text.Get("apparent_durability_affects_the_displayed_condition_and"));
            Label(builder.Last, 16, true);
            builder.End();
            builder.AddSeparator();
        }

        void BuildArmorFields(CustomUIBuilder builder)
        {
            PropertyTitle(builder, Text.Get("armor"));
            builder.BeginRow(10);
            PropertyNumber(builder, Text.Get("current_armor"), "current_armor_value", editor.SetArmorCurrent);
            PropertyNumber(builder, Text.Get("maximum_armor"), "current_max_armor_value", editor.SetArmorMaximum);
            PropertyNumber(builder, Text.Get("protection"), "armor_protection", editor.SetArmorProtection);
            builder.End();
            builder.BeginRow(10);
            PropertyNumber(builder, Text.Get("mitigation"), "armor_mitigation", editor.SetArmorMitigation);
            builder.AddButton(Text.Get("repair_armor"), () => ApplyEditor(editor.RepairArmor));
            builder.Last.SetFixedWidth(150);
            builder.End();
            builder.AddSeparator();
        }

        void BuildGunFields(CustomUIBuilder builder)
        {
            PropertyTitle(builder, Text.Get("firearm"));
            builder.BeginRow(10);
            builder.AddLabel(Text.Get("condition"));
            builder.Last.SetFixedWidth(130);
            builder.AddDropdown(new List<string> { Text.Get("new"), Text.Get("excellent"),
                Text.Get("good"), Text.Get("fair"), Text.Get("poor") },
                (int)GunFrameHelper.GetCondition(editor.Item),
                index => ApplyEditor(() => editor.SetGunCondition((ItemFeatureList.EquipmentCondition)index)));
            gunConditionInput = builder.Last.SetFixedWidth(250);
            builder.End();
            builder.BeginRow(10);
            PropertyNumber(builder, Text.Get("base_firepower"), "GUN_FIREPOWER_TAG", value => editor.SetGunStat("GUN_FIREPOWER_TAG", value));
            PropertyNumber(builder, Text.Get("base_accuracy"), "GUN_ACCURACY_TAG", value => editor.SetGunStat("GUN_ACCURACY_TAG", value));
            PropertyNumber(builder, Text.Get("base_reliability"), "GUN_RELIABILITY_TAG", value => editor.SetGunStat("GUN_RELIABILITY_TAG", value));
            builder.End();
            PropertyInfo(builder, () => Text.Get("effective_firepower_accuracy_reliability", editor.Int("GUN_CURRENT_FIREPOWER_TAG"), editor.Int("GUN_CURRENT_ACCURACY_TAG"), editor.Int("GUN_CURRENT_RELIABILITY_TAG")));
            builder.AddSeparator();
        }

        void BuildUseCountFields(CustomUIBuilder builder)
        {
            PropertyTitle(builder, Text.Get("uses"));
            builder.BeginRow(10);
            PropertyNumber(builder, Text.Get("remaining_uses"), "CURRENT_USE_COUNT_INT", editor.SetUseCount);
            PropertyNumber(builder, Text.Get("maximum_uses"), "MAX_USE_COUNT_INT", editor.SetMaximumUseCount);
            builder.AddButton(Text.Get("refill"), () => ApplyEditor(() => editor.SetUseCount(editor.Int("MAX_USE_COUNT_INT"))));
            builder.Last.SetFixedWidth(150);
            builder.End();
            builder.AddSeparator();
        }

        void BuildFoodFields(CustomUIBuilder builder)
        {
            PropertyTitle(builder, Text.Get("food"));
            builder.BeginRow(10);
            if (editor.HasCalories) PropertyNumber(builder, Text.Get("calories"), "CALORIE_VALUE_TAG", editor.SetCalories);
            if (editor.HasFreshness) PropertyNumber(builder, Text.Get("freshness"), "FRESHNESS_TAG", editor.SetFreshness);
            builder.End();
            if (editor.HasFreshness)
            {
                builder.BeginRow(8);
                int[] values = { 13, 10, 7, 3, 0 };
                string[] labels = { Text.Get("fresh"), Text.Get("normal"), Text.Get("stale"),
                    Text.Get("spoiling"), Text.Get("rotten") };
                for (int i = 0; i < values.Length; i++)
                {
                    int freshness = values[i];
                    builder.AddButton(labels[i], () => ApplyEditor(() => editor.SetFreshness(freshness)));
                    builder.Last.SetFixedWidth(140);
                }
                builder.End();
                PropertyInfo(builder, () => Text.Get("current_freshness", FoodItemHelper.GetFreshnessString(editor.Item) switch
                    {
                        "fresh" => Text.Get("fresh"),
                        "stale" => Text.Get("stale"),
                        "spoiling" => Text.Get("spoiling"),
                        "rotten" => Text.Get("rotten"),
                        _ => Text.Get("normal")
                    }));
            }
            builder.AddSeparator();
        }
    }
}
