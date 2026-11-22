using static PsModI18n.I18n;
using Nicokobo.AutomatedLogistics.Domain;

namespace Nicokobo.AutomatedLogistics.Integration;

internal sealed partial class AutomatedLogisticsRuntime
{
    internal string RestockSourceSummary(GameItem machine)
    {
        if (Ready && MachineAccess.IsFeedDispenser(machine)) return FeedDispenserPlan(machine);
        if (!Ready || !MachineAccess.TryInventory(machine, out var inventory) || inventory == null) return PsModI18n.I18n.Get("source_1_machine_unavailable");
        if (!MachineAccess.HasRestockInput(machine)) return machine.identifier switch
        {
            "evaporator" => PsModI18n.I18n.Get("load_feed_manually_only_native_distillate_is"),
            "plant_dna_manipulator" => PsModI18n.I18n.Get("only_disks_already_in_the_native_eject"),
            _ => PsModI18n.I18n.Get("the_moisture_farm_needs_no_ingredients_restocked")
        };
        if (NativeModuleApi.FindInstalled(machine, Content.Feeder).Count == 0) return PsModI18n.I18n.Get("source_1_restocking_module_not_installed");
        var settings = Settings(machine);
        if (machine.identifier == "hydroponic" && settings.Hydroponic == null)
        {
            string feedStatus = HydroponicFeedStatus(machine);
            return (settings.FillEnabled ? "" : PsModI18n.I18n.Get("restocking_paused_2")) + feedStatus +
                PsModI18n.I18n.Get("remaining_nutrient_concentration_may_change_during_growth_2");
        }
        var solids = settings.PendingItems.Where(item => !item.Manual).Concat(ReadInputItems(inventory.ItemInput).Where(item => !item.Manual)
            .Select(item => new RestockItem { ItemId = item.ItemId, Purity = item.Purity, Quality = item.Quality, MaterialState = item.MaterialState })).ToArray();
        var liquids = machine.identifier == "hydroponic" && settings.Hydroponic is { } hydroponic
            ? new[] { hydroponic.Supply }
            : settings.PendingLiquids.Concat(ReadInputLiquids(inventory).Select(liquid => new RestockLiquid
        {
            SlotId = liquid.SlotId, VesselId = liquid.VesselId, WaterQuality = liquid.WaterQuality,
            Basis = liquid.Contents.Contents.Where(part => part.Parts > 0).Select(part => new RestockLiquidPart
            { LiquidId = part.LiquidId, Parts = part.Parts, Value = part.Value, QualityBasis = part.QualityBasis }).ToList()
        })).ToArray();
        var lines = new List<string>();
        string Location(RestockSource source) => source.Tier switch
        {
            0 => PsModI18n.I18n.Get("resource_network"), 2 => PsModI18n.I18n.Get("warehouse"),
            _ => source.Item.parentInventory?.GetParentItem() is { } container ? PsModI18n.I18n.Get("nearby_container") + Label(container) : PsModI18n.I18n.Get("nearby_containers_2")
        };
        foreach (var group in RestockSources(machine, false).Where(source => solids.Any(demand => MatchesConsumed(demand, source.Item)))
            .GroupBy(source => (source.Tier, source.Parent, source.Item.identifier)).OrderBy(group => group.Key.Tier).ThenBy(group => group.Min(source => source.Item.GetUniqueID())))
        {
            var first = group.First();
            lines.Add(PsModI18n.I18n.Get("source", lines.Count + 1, MachineAccess.DisplayName(first.Item), group.Sum(source => (long)source.Item.unitCount), Location(first)));
        }
        foreach (var source in RestockSources(machine, true).Where(source => liquids.Any(demand => MatchesConsumedLiquid(demand, source.Item))))
        {
            var liquid = NativeLiquidApi.Capture(source.Item)!;
            string name = NativeLiquidApi.WaterQuality(source.Item) switch
            {
                3 => LocHelper.GetLocalizedMechanic("mech_condition_pure_water"),
                2 => LocHelper.GetLocalizedMechanic("mech_condition_high_quality_water"),
                1 => LocHelper.GetLocalizedMechanic("mech_condition_basewater"),
                _ => MachineAccess.DisplayName(source.Item) + PsModI18n.I18n.Get("liquid")
            };
            lines.Add(PsModI18n.I18n.Get("source_ml", lines.Count + 1, name, liquid.TotalParts / 1000m, Location(source)));
        }
        if (lines.Count == 0) lines.Add(solids.Length == 0 && liquids.Length == 0 ? PsModI18n.I18n.Get("source_1_no_ingredients_loaded") : PsModI18n.I18n.Get("source_1_no_matching_items_available"));
        if (machine.identifier == "hydroponic")
        {
            string feedStatus = HydroponicFeedStatus(machine);
            lines.Insert(0, feedStatus.Length == 0 ? PsModI18n.I18n.Get("feed_specification_recorded_matching_the_sample_s") : feedStatus);
            lines.Add(PsModI18n.I18n.Get("remaining_nutrient_concentration_may_change_during_growth"));
        }
        if (!settings.FillEnabled) lines.Insert(0, PsModI18n.I18n.Get("restocking_paused"));
        return string.Join("\n", lines);
    }
}
