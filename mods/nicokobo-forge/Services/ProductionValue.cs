using System.Globalization;
using HarmonyLib;

namespace Nicokobo.ContentInterop;

public static class ProductionValue
{
    public const string BasisTag = "NICOKOBO_FORGE_PRODUCTION_BASIS_V1";
    private delegate void Accumulate(GameItem item, ref long innate, ref long market, ref double final, bool skipEvents);
    private delegate long Compose(long raw, long innate, long market, double final, int misc, int containerPrice);
    private static class Pricing
    {
        internal static readonly Accumulate Stages = (Accumulate)AccessTools.Method(typeof(GameItem), "AccumulateFeatureStages").CreateDelegate(typeof(Accumulate));
        internal static readonly Compose Value = (Compose)AccessTools.Method(typeof(GameItem), "ComposeStagedValue").CreateDelegate(typeof(Compose));
    }
    private static (long Innate, double Final) Stages(GameItem item)
    {
        long innate = 0, market = 0; double final = 1;
        Pricing.Stages(item, ref innate, ref market, ref final, true);
        return (innate, final);
    }
    public static decimal InnateFactor(GameItem item)
    { var stages = Stages(item); return Math.Max(0m, 100m + stages.Innate) / 100m * (decimal)stages.Final; }
    public static decimal InputValue(GameItem item)
    {
        decimal value = 0;
        foreach (var current in item.FindAllChildItems().Concat(new[] { item }))
        {
            RestoreBasis(current);
            var stages = Stages(current);
            value += Math.Max(0, Pricing.Value(checked(current.unitValue + PowerHelper.GetPowerSourceItemValue(current)),
                stages.Innate, 0, stages.Final, checked((int)current.lateUnitValue), WaterHelper.GetContainerPrice(current)));
        }
        return value;
    }
    public static long ComposeRealValue(long basis, long innate, double final) => Math.Max(0, Pricing.Value(basis, innate, 0, final, 0, 0));
    public static void SetBasis(GameItem item, long basis)
    { item.ModifyTag(BasisTag, tag => tag.Enable().SetString(basis.ToString(CultureInfo.InvariantCulture))); item.unitValue = basis; }
    public static long ReadBasis(GameItem item, long fallback)
    {
        if (!item.IsTag(BasisTag)) return fallback;
        string? saved = item.GetTagReadonly(BasisTag)?.valueString;
        if (string.IsNullOrEmpty(saved)) return fallback;
        return long.TryParse(saved, NumberStyles.None, CultureInfo.InvariantCulture, out long value)
            ? value : throw new InvalidOperationException("Invalid production basis: " + item.identifier);
    }
    public static void RestoreBasis(GameItem item) { if (item.IsTag(BasisTag)) item.unitValue = ReadBasis(item, item.unitValue); }
}

[HarmonyPatch]
internal static class ProductionBasisPatch
{
    private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(GameItem), nameof(GameItem.GetCurrentValue));
        yield return AccessTools.Method(typeof(GameItem), nameof(GameItem.GetRefreshedValue));
        yield return AccessTools.Method(typeof(GameItem), nameof(GameItem.GetBaseValue));
    }
    private static void Prefix(GameItem __instance) => ProductionValue.RestoreBasis(__instance);
}
