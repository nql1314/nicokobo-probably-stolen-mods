using System.Reflection;
using DefinitelyStolen.Domain;
using DefinitelyStolen.Persistence;
using Nicokobo.ModUtilities;

namespace DefinitelyStolen.Integration;

internal sealed class NativeItems(Catalog catalog)
{
    private static readonly string[] WaterIds = { "mini_bottle", "small_bottled_water", "bottled_water", "large_bottled_water" };
    internal static GameItem Preview(ItemState candidate) => !candidate.Available
        ? throw new InvalidDataException("Candidate native state was not saved")
        : CandidateState.NeedsNativeState(candidate.Id) && candidate.Graph.Length != 0
            ? NativeGraph.Decode(candidate.Graph, frozenCandidate: true)
            : CandidateState.IsFish(candidate.Id) ? CreateBase(candidate.Id) : DirectoryMaster.Item(candidate.Id);
    internal ItemState CreateCandidate(string id, int tier, StableRandom random) => Create(id, tier, random, captureGraph: CandidateState.NeedsNativeState(id));
    private static GameItem CreateBase(string id)
    {
        var item = id switch
        {
            "large_purple_injector" => PreBuiltItemHelper.CreateRealGenuineInjector(),
            "water_test_container" => PreBuiltItemHelper.FullWaterTestStripBox(),
            "water_tablet_container" => PreBuiltItemHelper.FullWaterPurificationTabletBox(),
            _ => DirectoryMaster.Item(id)
        };
        if (CandidateState.IsFish(id))
        {
            // Some native juvenile templates start with zero meat. Keep the
            // fish juvenile while seeding its native growth and meat state.
            var species = AquacultureHelper.GetSpecies(item);
            item.state.GetTag("FISH_AGE_TAG").Enable().SetInt(species.stageStartDays[(int)FishStage.Adolescent] - 1);
            AquacultureHelper.SeedKcalEatenForAge(item, species);
            AquacultureHelper.UpdateNameAndSprite(item);
        }
        return item;
    }
    internal ItemState Create(string id, int tier, StableRandom random, int? batteryStep = null, int? waterGrade = null, bool genuine = false, bool captureGraph = true)
    {
        if (!catalog.DropIds.Contains(id) && !OpeningRules.Fixed.Contains(id)) throw new InvalidDataException("Item outside whitelist");
        GameItem item = CreateBase(id);
        try
        {
            if (item.identifier != id) throw new InvalidDataException("Prebuilt item identity changed");
            if (id is "energy_credit" or "energy_credit_ext" or "energy_credit_breeder")
            {
                int max = item.GetTagReadonly("power_source_item_max_energy").valueInt;
                int step = batteryStep ?? ChargeStep(random.Unit());
                if (step is < 0 or > 4 || max <= 0) throw new InvalidDataException("Battery state unavailable");
                PowerHelper.SetPowerSourceAt(item, max * step / 4);
            }
            if (WaterIds.Contains(id))
            {
                int grade = waterGrade ?? WaterGrade(tier, random.Unit());
                WaterHelper.EmptyContainer(item);
                // Native AddWater's range parameters are constituent ranges,
                // not a percentage of purity. Native grade filling owns chemistry.
                WaterHelper.AddWater(item, grade, -1, false, 0, 1, true);
                int purity = WaterHelper.GetWaterPurity(item);
                if (WaterHelper.GetFreeCapacity(item) != 0)
                    throw new InvalidDataException("Native water container was not filled");
                if (grade == 2 && purity is < 9800 or >= 9900 ||
                    grade == 1 && purity is < 9900 or >= 9990 || grade == 0 && purity is < 9990 or > 10000)
                    throw new InvalidDataException("Native water grade outside agreed purity range");
            }
            if (id == "packet_red_cigarette")
            {
                bool real = genuine || random.Index(101) <= (Native.Store?.isVisualInspectionIntroduced == true ? 39 : 30);
                SetCigarette(item, real);
            }
            var described = Describe(item, captureGraph); described.Heat = tier * 25; return described;
        }
        finally { ItemGraphCleanup.Destroy(item); }
    }
    internal ItemState Describe(GameItem item, bool captureGraph = true)
    {
        var value = TrueValue(item);
        var result = new ItemState { Id = item.identifier, UniqueId = item.GetUniqueID(), Value = value,
            Graph = captureGraph ? NativeGraph.Encode(item) : "" };
        if (CandidateState.NeedsNativeState(item.identifier)) result.NativeStateVersion = 1;
        if (item.identifier is "energy_credit" or "energy_credit_ext" or "energy_credit_breeder")
        { result.MaxCharge = item.GetTagReadonly("power_source_item_max_energy").valueInt; result.Charge = PowerHelper.GetAvailableEnergyFromItem(item, false); }
        if (WaterIds.Contains(item.identifier))
        {
            // Liquid tags and GetTotalVolume use microlitres. The native ML
            // accessor returns filled volume; capacity is a separate tag.
            result.VolumeMl = WaterHelper.GetCurrentCapacityML(item);
            result.CapacityMl = item.GetTagReadonly("LIQUID_CONTAINER_CAPACITY").valueInt / 1000;
            result.Purity = WaterHelper.GetWaterPurity(item);
            result.LiquidParts = new(StringComparer.Ordinal);
            foreach (var entry in item.state.dict)
                if (entry.Key.StartsWith("CURRENT_PART_", StringComparison.Ordinal) && entry.Value.valueInt > 0)
                    result.LiquidParts.Add(entry.Key, entry.Value.valueInt);
        }
        if (item.identifier == "packet_red_cigarette") result.Condition = value >= 100 ? "genuine" : "counterfeit";
        if (item.identifier == "large_purple_injector") result.Condition = "genuine";
        if (item.identifier == "bottled_water_premium") result.Condition = "sealed";
        if (CandidateState.IsFish(item.identifier)) result.Condition = item.GetTagReadonly("FISH_SEX_TAG").valueString;
        TheftRules.Classify(result); return result;
    }
    internal static long TrueValue(GameItem item)
    {
        long innate = 0;
        foreach (var feature in item.itemFeatures)
            if (!feature.isDisabled && feature.GetValueStage() == ItemFeature.ValueStage.Innate)
                innate += feature.GetActualValueModifier();
        // Same native composition and rounding, with actual conditions and
        // without current market/client/stolen/falsified-label multipliers.
        return (long)Math.Round(Math.Max(0d, (item.unitValue + PowerHelper.GetPowerSourceItemValue(item)) * (1d + innate / 100d))
            + WaterHelper.GetContainerPrice(item) + item.GetItemMiscValue(false, true, false));
    }
    internal GameItem Decode(ItemState state, PlayerStore store)
    {
        var ids = NativeGraph.RunItems(store).Where(x => x.uniqueId > 0).Select(x => x.uniqueId).ToHashSet();
        var item = state.Graph.Length == 0 ? MaterializeCandidate(state, ids) : NativeGraph.Decode(state.Graph, ids,
            frozenCandidate: state.NativeStateVersion != null);
        try
        {
            if (item.identifier != state.Id || item.GetUniqueID() != state.UniqueId || item.unitCount != 1) throw new InvalidDataException("Saved item identity mismatch");
            if (TrueValue(item) != state.Value) throw new InvalidDataException("Saved intrinsic value changed");
            return item;
        }
        catch { ItemGraphCleanup.Destroy(item); throw; }
    }
    private static GameItem MaterializeCandidate(ItemState state, HashSet<int> existingIds)
    {
        CandidateState.Validate(state);
        if (!state.Available) throw new InvalidDataException("Candidate native state was not saved");
        if (existingIds.Contains(state.UniqueId)) throw new InvalidDataException("Candidate already exists in player inventory");
        var item = CreateBase(state.Id);
        try
        {
            // Candidate identity also anchors intelligence. Native children get
            // fresh IDs only when this detached instance is actually needed.
            item.uniqueId = state.UniqueId;
            foreach (var feature in item.itemFeatures) feature.parentItemUniqueId = state.UniqueId;
            if (state.Charge is int charge)
            {
                if (item.GetTagReadonly("power_source_item_max_energy").valueInt != state.MaxCharge)
                    throw new InvalidDataException("Candidate battery capacity changed");
                PowerHelper.SetPowerSourceAt(item, charge);
            }
            if (state.LiquidParts != null)
            {
                // Initialize native water features, then restore the exact
                // constituent quantities rather than rolling another purity.
                int grade = state.Purity < 9900 ? 2 : state.Purity < 9990 ? 1 : 0;
                WaterHelper.EmptyContainer(item); WaterHelper.AddWater(item, grade, -1, false, 0, 1, true);
                foreach (var entry in item.state.dict)
                    if (entry.Key.StartsWith("CURRENT_PART_", StringComparison.Ordinal)) entry.Value.SetInt(0);
                foreach (var part in state.LiquidParts) item.state.GetTag(part.Key).Enable().SetInt(part.Value);
                WaterHelper.UpdateContainerGoodType(item); SpriteHelper.UpdateWaterContainerSprite(item);
                if (WaterHelper.GetCurrentCapacityML(item) != state.VolumeMl || WaterHelper.GetWaterPurity(item) != state.Purity ||
                    item.GetTagReadonly("LIQUID_CONTAINER_CAPACITY").valueInt / 1000 != state.CapacityMl)
                    throw new InvalidDataException("Candidate liquid state changed");
            }
            if (state.Id == "packet_red_cigarette") SetCigarette(item, state.Condition == "genuine");
            if (CandidateState.IsFish(state.Id))
            {
                // Native fish templates start at the same juvenile stage, but
                // roll sex on creation. Preserve the candidate's original roll.
                item.state.GetTag("FISH_SEX_TAG").Enable().SetString(state.Condition);
                AquacultureHelper.UpdateNameAndSprite(item);
            }
            return item;
        }
        catch { ItemGraphCleanup.Destroy(item); throw; }
    }
    private static void SetCigarette(GameItem item, bool genuine)
    {
        foreach (var feature in item.itemFeatures.ToArray().Where(x => x.category == "CATEGORY_GENUINE_CIGARETTE")) item.RemoveItemFeatureByID(feature.identifier);
        if (genuine) { InsCigaretteHelper.SetGenuine(item); item.AddItemFeature(ItemFeatureList.CigaretteAuthenticity()); }
        else { InsCigaretteHelper.SetConterfeit(item); item.AddItemFeature(ItemFeatureList.CigaretteFakeAuthenticity()); }
    }
    internal static void MarkLoot(GameItem item, int heat)
    {
        StolenHelper.InitStolenItem(item, heat); StolenHelper.SetHeat(item, heat);
        item.state.EnableTag(Identity.CustodyTag);
        // Discovery chance can be modified by perks; validate the stored heat itself.
        if (!StolenHelper.IsStolenItem(item) || item.GetTagReadonly("STOLEN_VALUE_INT").valueInt != heat) throw new InvalidOperationException("Stolen heat not applied");
    }
    internal string CatalogName(string id)
    {
        return NativeItemText.Name(id, CatalogText.GetFor("item." + id + ".name"));
    }

    internal string Name(ItemState item, bool revealQuality = false)
    {
        string name = CatalogName(item.Id);
        if (item.Charge != null) name += $" ({item.Charge}/{item.MaxCharge} kJ)";
        if (item.Purity != null)
        {
            string purity = revealQuality
                ? $", {LocHelper.GetLocalizedMechanic("item_feature_category_water_purity")} {item.Purity / 100d:0.00}%"
                : "";
            name += $" ({item.VolumeMl} ml{purity})";
        }
        if (item.Condition == "sealed") name += " (" + LocHelper.GetLocalizedMechanic("mech_condition_factory_sealed") + ")";
        return name;
    }

    internal string SnapshotName(ItemState item, bool english)
    {
        var meta = catalog.Item(item.Id);
        string name = english ? meta.English : meta.Name;
        if (item.Charge != null) name += CatalogText.GetFor("energy", english, item.Charge.Value, item.MaxCharge?.ToString() ?? "");
        if (item.Purity != null) name += english ? $" ({item.VolumeMl} ml)" : $"（{item.VolumeMl} ml）";
        if (item.Condition == "sealed") name += CatalogText.GetFor("sealed", english);
        return name;
    }
    internal static IReadOnlyList<NativePlacement>? Plan(GameGridInventory grid, IReadOnlyList<GameItem> items) => NativePlacementApi.PlanWholeGrid(grid, items);
    internal static void Place(GameGridInventory grid, IReadOnlyList<NativePlacement> plan)
    {
        var inserted = new List<GameItem>();
        try
        {
            foreach (var entry in plan)
            {
                var item = entry.Item;
                var marker = grid.TryInventorySlot(item, 1, entry.Shape, null);
                if (marker == null || !marker.IsValid() || marker.targetItem != null || marker.numTransfer != 1) throw new InvalidOperationException("Inventory proposal changed");
                marker.TryAcceptOnce(1);
                if (item.parentInventory == null || !ReferenceEquals(item.parentInventory, grid) || item.unitCount != 1) throw new InvalidOperationException("Native placement failed");
                inserted.Add(item);
            }
        }
        catch { foreach (var item in inserted.AsEnumerable().Reverse()) Detach(item); throw; }
    }
    internal static void Detach(GameItem item)
    {
        if (item.parentInventory != null && !item.parentInventory.Expel(item)) throw new InvalidOperationException("Native detach rejected");
        if (item.parentInventory != null) throw new InvalidOperationException("Native detach failed");
    }
    internal string Portrait(RoleDefinition role, StableRandom random)
    {
        return DefinitelyStolen.UI.SceneLayout.PortraitName(role.Id, random.Index(3));
    }
    private static int ChargeStep(double roll) => roll < .15 ? 0 : roll < .30 ? 1 : roll < .60 ? 2 : roll < .85 ? 3 : 4;
    private static int WaterGrade(int tier, double roll) => tier switch
    { 1 => roll < .75 ? 2 : roll < .95 ? 1 : 0, 2 => roll < .50 ? 2 : roll < .90 ? 1 : 0, _ => roll < .20 ? 2 : roll < .70 ? 1 : 0 };
}
