using System.Globalization;
using Nicokobo.FoodProcessing.Definitions;

namespace Nicokobo.FoodProcessing;

// Consumables use a value per remaining use / kcal, never a fixed full-item basis.
internal static class FoodStateValue
{
    internal const string RateTag = "NICOKOBO_FOOD_REMAINING_UNIT_VALUE_V1";
    internal const string UnitsTag = "NICOKOBO_FOOD_VALUE_UNITS_V1";
    internal sealed record Basis(decimal Value, int Units)
    {
        internal decimal ForUnits(int units) => Units == 0 ? 0 : Value * units / Units;
    }
    internal static bool Supported(GameItem item) => item.identifier is FoodDefinitions.LivestockImmuneSerumId or
        FoodDefinitions.LivestockSteroidSerumId or FoodDefinitions.IndustrialFishFeedId;
    internal static int Units(GameItem item) => Math.Max(0, item.GetTagReadonly(item.identifier == FoodDefinitions.IndustrialFishFeedId
        ? "CALORIE_VALUE_TAG" : "CURRENT_USE_COUNT_INT").GetInt());
    internal static bool HasRate(GameItem item) => item.IsTag(RateTag);
    internal static Basis Read(GameItem item) => HasRate(item)
        ? new(decimal.Parse(item.GetTagReadonly(RateTag).GetString()!, CultureInfo.InvariantCulture), item.GetTagReadonly(UnitsTag).GetInt())
        : new(item.unitValue, Units(item));
    internal static void Write(GameItem item, Basis basis)
    {
        item.ModifyTag(RateTag, tag => tag.Enable().SetString(basis.Value.ToString(CultureInfo.InvariantCulture)));
        item.ModifyTag(UnitsTag, tag => tag.Enable().SetInt(basis.Units));
        Refresh(item);
    }
    internal static void SetBasis(GameItem item, long value) => Write(item, new(value, Units(item)));
    internal static void Refresh(GameItem item)
    {
        if (Supported(item) && HasRate(item)) item.unitValue = (long)decimal.Floor(Read(item).ForUnits(Units(item)));
    }
    internal static void Transfer(GameItem source, GameItem target, int sourceBefore, int targetBefore, Basis sourceBasis, Basis targetBasis)
    {
        int moved = sourceBefore - Units(source);
        if (moved <= 0 || Units(target) != targetBefore + moved) return;
        Write(source, sourceBasis);
        Write(target, new(targetBasis.ForUnits(targetBefore) + sourceBasis.ForUnits(moved), Units(target)));
    }
}
