namespace Nicokobo.FoodProcessing.Domain;

public enum GlowberryGrade { Unknown, Bad, Unripe, Normal, Small }

public static class GlowberryDrinkRules
{
    public const string ConditionTag = "BERRY_CONDITION_INT";

    // InsBerryUI uses -2 for mold, -1 for both unripe and dried fruit,
    // 0/1 for normal fruit and 2 for small fruit. Retain the existing
    // interpretation of value 3 as small fruit.
    public static GlowberryGrade FromNativeCondition(int? condition) => condition switch
    {
        -2 => GlowberryGrade.Bad,
        -1 => GlowberryGrade.Unripe,
        0 or 1 => GlowberryGrade.Normal,
        2 or 3 => GlowberryGrade.Small,
        _ => GlowberryGrade.Unknown
    };

    public static bool CanUse(int? condition) => FromNativeCondition(condition) is
        GlowberryGrade.Unripe or GlowberryGrade.Normal or GlowberryGrade.Small;

}
