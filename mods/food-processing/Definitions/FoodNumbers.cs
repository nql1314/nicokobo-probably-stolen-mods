namespace Nicokobo.FoodProcessing;
internal static class FoodNumbers
{
    internal static class Machines
    {
        internal const int DefaultWaterSourceSlots = 0, DefaultPowerCost = 10, PerformancePerExtraBatch = 100;
    }
    internal static class ProductionMarkups { internal const int K04 = 25, K06 = 0; }
    internal static class Distribution
    {
        internal const int MachineNightShopChancePercent = 40;
        internal const float MachineLootWeight = 0.05f, FoodLootWeight = 0.1f;
    }
    // Fixed stock references. Real production overwrites these with its inputs.
    internal static int ReferenceCalories(string code) => code switch
    {
        "D02" => 1350, "D04" => 900, "D05" => 0, "D09" => 150,
        "E01" => 1300, "E05" => 1100, "E07" => 1100, "E08" => 800,
        "E14" => 1175, "E15" => 1100, "E17" => 990, "E18" => 1100,
        "E19" => 1950, "E20" => 412, "E21" => 125, "E22" => 550,
        _ => Definitions.FoodDefinitions.Items.Single(item => item.Code == code).Calories
    };
    internal static bool IsMeat(string code) => code is
        "E01" or "E05" or "E07" or "E08" or "E14" or "E15" or "E18" or "E20" or "E22" ||
        Definitions.FoodDefinitions.Items.Any(item => item.Code == code && item.IsMeat);
}
