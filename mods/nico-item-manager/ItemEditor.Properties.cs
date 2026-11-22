using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace NicoItemManager
{
    internal sealed partial class ItemEditor
    {
        internal bool IsBattery => Item.IsTag("power_source_item");
        internal bool HasDurability => Item.IsTag("DURABILITY_TAG")
            && Item.FindItemFeatureByID("DURABILITY_TAG") != null
            && DurabilityHelper.GetMaxDurability(Item) > 0 && DurabilityHelper.GetDefDurability(Item) > 0;
        internal bool IsArmor => Item.IsTag("armor_item");
        internal bool IsGun => Item.IsTag("ADV_GUN_TAG");
        internal bool HasUseCount => Item.IsTag("USE_COUNT_ITEM_TAG");
        internal bool HasCalories => Item.IsTag("CALORIE_VALUE_TAG");
        internal bool HasFreshness => Item.IsTag("FRESH_FOOD_TAG") || Item.IsTag("FRESHNESS_TAG");
        internal bool GunConditionChanged { get; private set; }
        internal int Int(string key) => Item.GetTagReadonly(key).GetInt();

        internal static readonly string[] BatteryTags = { "power_source_item_energy", "power_source_item_max_energy", "power_source_item_bandwidth" };
        internal static readonly string[] DurabilityTags = { "DURABILITY_TAG", "DURABILITY_FAKE_TAG", "MAX_DURABILITY_TAG", "DEFAULT_DURABILITY_TAG" };
        internal static readonly string[] ArmorTags = { "current_armor_value", "current_max_armor_value", "armor_protection", "armor_mitigation" };
        internal static readonly string[] GunStats = { "GUN_FIREPOWER_TAG", "GUN_ACCURACY_TAG", "GUN_RELIABILITY_TAG" };
        internal static readonly string[] GunDerived = { "GUN_CURRENT_FIREPOWER_TAG", "GUN_CURRENT_STYLE_TAG", "GUN_CURRENT_RELIABILITY_TAG",
            "GUN_CURRENT_RECOIL_TAG", "GUN_CURRENT_ERGONOMICS_TAG", "GUN_CURRENT_ACCURACY_TAG", "GUN_SUPPRESSED_TAG", "GUN_SIZE_TAG" };
        internal static readonly string[] UseCountTags = { "CURRENT_USE_COUNT_INT", "MAX_USE_COUNT_INT" };

        internal IEnumerable<string> ManagedPropertyTags()
        {
            if (IsBattery) foreach (string key in BatteryTags) yield return key;
            if (HasDurability) foreach (string key in DurabilityTags) yield return key;
            if (IsArmor) foreach (string key in ArmorTags) yield return key;
            if (IsGun)
            {
                foreach (string key in GunStats) yield return key;
                foreach (string key in GunDerived) yield return key;
            }
            if (HasUseCount) foreach (string key in UseCountTags) yield return key;
            if (HasCalories) yield return "CALORIE_VALUE_TAG";
            if (HasFreshness) yield return "FRESHNESS_TAG";
        }

        static void CheckRange(int value, int min, int max)
        {
            if (value < min || value > max)
                throw new InvalidOperationException(Text.Get("value_must_be", min, max));
        }

        static void RequireProperty(bool supported)
        {
            if (!supported) throw new InvalidOperationException(Text.Get("this_item_does_not_support_this_field"));
        }

        internal void SetBatteryEnergy(int value)
        {
            RequireProperty(IsBattery);
            CheckRange(value, 0, Int("power_source_item_max_energy"));
            PowerHelper.SetPowerSourceAt(Item, value);
        }

        internal void SetBatteryCapacity(int value)
        {
            RequireProperty(IsBattery);
            CheckRange(value, 1, 1000000);
            int energy = Math.Min(Int("power_source_item_energy"), value);
            Item.ModifyTag("power_source_item_max_energy", tag => tag.Enable().SetInt(value));
            PowerHelper.SetPowerSourceAt(Item, energy);
        }

        internal void SetBatteryOutput(int value)
        {
            RequireProperty(IsBattery);
            CheckRange(value, 0, 1000000);
            Item.ModifyTag("power_source_item_bandwidth", tag => tag.Enable().SetInt(value));
        }

        internal void SetRealDurability(int value)
        {
            RequireProperty(HasDurability);
            SetDurability(value, DurabilityHelper.GetFakeDurability(Item), DurabilityHelper.GetMaxDurability(Item));
        }

        internal void SetApparentDurability(int value)
        {
            RequireProperty(HasDurability);
            SetDurability(DurabilityHelper.GetRealDurability(Item), value, DurabilityHelper.GetMaxDurability(Item));
        }

        internal void SetMaximumDurability(int value)
        {
            RequireProperty(HasDurability);
            CheckRange(value, 1, 1000000);
            SetDurability(Math.Min(DurabilityHelper.GetRealDurability(Item), value),
                Math.Min(DurabilityHelper.GetFakeDurability(Item), value), value);
        }

        internal void RepairDurability()
        {
            RequireProperty(HasDurability);
            int maximum = DurabilityHelper.GetMaxDurability(Item);
            SetDurability(maximum, maximum, maximum);
        }

        void SetDurability(int real, int apparent, int maximum)
        {
            CheckRange(real, 0, maximum);
            CheckRange(apparent, 0, maximum);
            DurabilityHelper.SetDurability(Item, real, apparent, maximum, DurabilityHelper.GetDefDurability(Item));
        }

        internal void SetArmorCurrent(int value)
        {
            RequireProperty(IsArmor);
            CheckRange(value, 0, Int("current_max_armor_value"));
            Item.ModifyTag("current_armor_value", tag => tag.Enable().SetInt(value));
        }

        internal void SetArmorMaximum(int value)
        {
            RequireProperty(IsArmor);
            CheckRange(value, 1, 1000000);
            int current = Math.Min(Int("current_armor_value"), value);
            Item.ModifyTag("current_max_armor_value", tag => tag.Enable().SetInt(value));
            Item.ModifyTag("current_armor_value", tag => tag.Enable().SetInt(current));
        }

        internal void SetArmorProtection(int value)
        {
            RequireProperty(IsArmor);
            CheckRange(value, 0, 1000000);
            Item.ModifyTag("armor_protection", tag => tag.Enable().SetInt(value));
        }

        internal void SetArmorMitigation(int value)
        {
            RequireProperty(IsArmor);
            CheckRange(value, 0, 100);
            Item.ModifyTag("armor_mitigation", tag => tag.Enable().SetInt(value));
        }

        internal void RepairArmor()
        {
            RequireProperty(IsArmor);
            ArmorHelper.RepairArmorWithoutLoss(Item);
        }

        internal void SetGunStat(string key, int value)
        {
            RequireProperty(IsGun && GunStats.Contains(key));
            CheckRange(value, 0, 100000);
            Item.ModifyTag(key, tag => tag.Enable().SetInt(value));
            GunHelper.UpdateCurrentGunStats(Item);
        }

        internal void SetGunCondition(ItemFeatureList.EquipmentCondition condition)
        {
            RequireProperty(IsGun);
            if (!Enum.IsDefined(typeof(ItemFeatureList.EquipmentCondition), condition))
                throw new ArgumentOutOfRangeException(nameof(condition));
            GunHelper.SetEquipmentCondition(Item, condition);
            GunConditionChanged = true;
        }

        internal void SetUseCount(int value)
        {
            RequireProperty(HasUseCount);
            CheckRange(value, 0, Int("MAX_USE_COUNT_INT"));
            UseCountHelper.SetCurrentUseCount(Item, value);
        }

        internal void SetMaximumUseCount(int value)
        {
            RequireProperty(HasUseCount);
            CheckRange(value, 1, 1000000);
            int current = Math.Min(Int("CURRENT_USE_COUNT_INT"), value);
            UseCountHelper.ModMaxUseCount(Item, value - Int("MAX_USE_COUNT_INT"));
            UseCountHelper.SetCurrentUseCount(Item, current);
        }

        internal void SetFreshness(int value)
        {
            RequireProperty(HasFreshness);
            CheckRange(value, 0, 1000);
            FoodItemHelper.InitFreshFood(Item, value);
        }

        internal void SetCalories(int value)
        {
            RequireProperty(HasCalories);
            CheckRange(value, 0, 1000000);
            FoodItemHelper.SetCalories(Item, value);
        }
    }

    [JsonObject(MemberSerialization.OptIn)]
    internal sealed class ItemPropertyState
    {
        [JsonProperty] bool battery;
        [JsonProperty] bool durability;
        [JsonProperty] bool gun;
        [JsonProperty] bool useCount;
        [JsonProperty] int energy;
        [JsonProperty] int realDurability;
        [JsonProperty] int apparentDurability;
        [JsonProperty] int maximumDurability;
        [JsonProperty] int defaultDurability;
        [JsonProperty] int uses;
        [JsonProperty] ItemFeatureList.EquipmentCondition? condition;
        internal bool ConditionChanged => condition.HasValue;

        [JsonConstructor]
        ItemPropertyState() { }

        internal ItemPropertyState(ItemEditor editor)
        {
            battery = editor.IsBattery;
            durability = editor.HasDurability;
            gun = editor.IsGun;
            useCount = editor.HasUseCount;
            if (battery) energy = editor.Int("power_source_item_energy");
            if (durability)
            {
                realDurability = DurabilityHelper.GetRealDurability(editor.Item);
                apparentDurability = DurabilityHelper.GetFakeDurability(editor.Item);
                maximumDurability = DurabilityHelper.GetMaxDurability(editor.Item);
                defaultDurability = DurabilityHelper.GetDefDurability(editor.Item);
            }
            if (editor.GunConditionChanged) condition = GunFrameHelper.GetCondition(editor.Item);
            if (useCount) uses = editor.Int("CURRENT_USE_COUNT_INT");
        }

        internal void ApplyTo(GameItem target)
        {
            if (battery) PowerHelper.SetPowerSourceAt(target, energy);
            if (durability) DurabilityHelper.SetDurability(target, realDurability, apparentDurability, maximumDurability, defaultDurability);
            if (gun)
            {
                if (condition.HasValue) GunHelper.SetEquipmentCondition(target, condition.Value);
                else GunHelper.UpdateCurrentGunStats(target);
            }
            if (useCount) UseCountHelper.SetCurrentUseCount(target, uses);
        }
    }
}
