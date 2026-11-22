namespace AugMechanicalAscension.Domain;

internal enum GraylineProductKind { Material, Machine, BasicNode, RareNode, BasicModule, RareModule }
internal sealed record GraylineProduct(string ItemId, string NameKey,
    int Quantity, int BaseValue, GraylineProductKind Kind = GraylineProductKind.Material);

internal static class GraylineSupplyRules
{
    internal const string MaterialPrefix = "nicokobo.mechcore.synthesis.";
    internal const int CurrentCatalogVersion = 5;
    // Module draws match ModuleMatrixNumbers.Distribution's night-shop tier weights.
    private const int BasicTierWeight = 2, RareTierWeight = 1;
    private const string NodePrefix = "nicokobo.mechcore_protocol.module_matrix.";
    // Native Common Electronic value is 33 in the current installed catalog.
    // The five Synthesis values come from BuildConfig/SynthesisExpansion.cs.
    internal static readonly GraylineProduct[] LegacyProducts =
    [
        new("common_electronic", "aug.electronic_parts_common", 3, 33),
        new(MaterialPrefix + "precision_parts", "aug.precision_parts", 2, 140),
        new(MaterialPrefix + "titanium_alloy", "aug.titanium_alloy", 2, 153),
        new(MaterialPrefix + "advanced_circuit", "aug.advanced_circuit", 1, 185),
        new(MaterialPrefix + "control_chip", "aug.control_chip", 1, 427),
        new(MaterialPrefix + "power_regulator", "aug.power_regulator", 1, 387)
    ];
    private static readonly GraylineProduct[] TwoQuartzProducts =
    [
        new(MaterialPrefix + "quartz", "aug.quartz_feedstock", 2, 20),
        new(MaterialPrefix + "titanium_ore", "aug.titanium_ore", 2, 45),
        new(MaterialPrefix + "material_refiner", "aug.material_refiner", 1, 150,
            GraylineProductKind.Machine),
        new(MaterialPrefix + "synthesizer", "aug.electromechanical_workbench", 1, 200,
            GraylineProductKind.Machine),
        new(MaterialPrefix + "precision_assembler", "aug.precision_assembler", 1, 300,
            GraylineProductKind.Machine),
        new(MaterialPrefix + "liquid_infusion_tower", "aug.liquid_bottler", 1, 150,
            GraylineProductKind.Machine),
        new(OfficialManufacturingTerminal.ItemId, OfficialManufacturingTerminal.ItemId + ".name",
            1, OfficialManufacturingTerminal.BaseValue, GraylineProductKind.Machine),
        new(MaterialPrefix + "food_processor", "aug.food_processor", 1, 150,
            GraylineProductKind.Machine),
        new("node_small", "aug.node_small", 1, 60,
            GraylineProductKind.BasicNode),
        new("node_medium", "aug.node_medium", 1, 70,
            GraylineProductKind.BasicNode),
        new(NodePrefix + "performance_conversion_node", "aug.performance_conversion_node", 1, 130,
            GraylineProductKind.RareNode),
        new(NodePrefix + "efficiency_conversion_node", "aug.efficiency_conversion_node", 1, 130,
            GraylineProductKind.RareNode),
        new(NodePrefix + "quality_conversion_node", "aug.quality_conversion_node", 1, 130,
            GraylineProductKind.RareNode),
        new(NodePrefix + "isolation_node", "aug.isolation_node", 1, 130,
            GraylineProductKind.RareNode),
        new(NodePrefix + "learning_acceleration_node", "aug.accelerated_learning_node", 1, 160,
            GraylineProductKind.RareNode)
    ];
    // Match the night shop's native basic/rare modules and Matrix modules.
    // Machine-specific Matrix modules participate on the rare side.
    private static readonly GraylineProduct[] ModuleProducts =
    [
        new("system_module_performance", "aug.performance_module", 1, 70,
            GraylineProductKind.BasicModule),
        new("system_module_efficiancy", "aug.efficiency_module", 1, 70,
            GraylineProductKind.BasicModule),
        new("system_module_quality", "aug.quality_module", 1, 70,
            GraylineProductKind.BasicModule),
        new("system_module_overclock", "aug.overclock_module", 1, 130,
            GraylineProductKind.RareModule),
        new("system_module_eco", "aug.eco_module", 1, 130,
            GraylineProductKind.RareModule),
        new("system_module_fineness", "aug.refinement_module", 1, 130,
            GraylineProductKind.RareModule),
        new("system_module_streamlining", "aug.streamlining_module", 1, 130,
            GraylineProductKind.RareModule),
        new(NodePrefix + "balanced_module", "aug.balanced_module", 1, 70,
            GraylineProductKind.BasicModule),
        new(NodePrefix + "assembly_line_module", "aug.assembly_line_module", 1, 70,
            GraylineProductKind.BasicModule),
        new(NodePrefix + "precision_module", "aug.precision_module", 1, 70,
            GraylineProductKind.BasicModule),
        new(NodePrefix + "steady_state_module", "aug.steady_state_module", 1, 70,
            GraylineProductKind.BasicModule),
        new(NodePrefix + "inspection_module", "aug.inspection_module", 1, 70,
            GraylineProductKind.BasicModule),
        new(NodePrefix + "high_power_module", "aug.high_power_module", 1, 200,
            GraylineProductKind.RareModule),
        new(NodePrefix + "continuous_acceleration_module", "aug.continuous_acceleration_module", 1, 130,
            GraylineProductKind.RareModule),
        new(NodePrefix + "continuous_efficiency_module", "aug.continuous_efficiency_module", 1, 130,
            GraylineProductKind.RareModule),
        new(NodePrefix + "continuous_calibration_module", "aug.continuous_calibration_module", 1, 150,
            GraylineProductKind.RareModule),
        new(NodePrefix + "osmosis_module", "aug.osmosis_module", 1, 220,
            GraylineProductKind.RareModule),
        new(NodePrefix + "auto_water_supply_module", "aug.automatic_water_supply_module", 1, 160,
            GraylineProductKind.RareModule),
        new(NodePrefix + "market_tracking_module", "aug.market_tracking_module", 1, 200,
            GraylineProductKind.RareModule),
        new(NodePrefix + "decoding_cache_module", "aug.decoding_cache_module", 1, 220,
            GraylineProductKind.RareModule),
        new(NodePrefix + "batch_smelting_module", "aug.batch_smelting_module", 1, 110,
            GraylineProductKind.RareModule)
    ];
    private static readonly GraylineProduct[] NativeSmallNodeProducts = TwoQuartzProducts.Where(x => x.ItemId != "node_medium")
        .Concat(ModuleProducts).ToArray();
    internal static readonly GraylineProduct[] Products = NativeSmallNodeProducts
        .Where(x => x.Kind != GraylineProductKind.BasicNode).ToArray();
    private static readonly GraylineProduct[] FourQuartzProducts = TwoQuartzProducts.Select(x =>
        x.ItemId == MaterialPrefix + "quartz" ? x with { Quantity = 4 } : x).ToArray();
    internal static GraylineProduct[] Catalog(int version) => version switch
    {
        1 => LegacyProducts,
        2 => FourQuartzProducts,
        3 => TwoQuartzProducts,
        4 => NativeSmallNodeProducts,
        CurrentCatalogVersion => Products,
        _ => throw new InvalidDataException("Unsupported Grayline catalog")
    };
    internal static GraylineProduct? Product(string id, int version = CurrentCatalogVersion) =>
        Catalog(version).FirstOrDefault(x => x.ItemId == id);

    // Build 25382790 StoreCalendar.GetDayOfWeek uses day % 7:
    // Sunday=0, Monday=1, Tuesday=2. Natural weeks here start on Monday.
    internal static int NextNaturalTuesday(int day)
    {
        if (day < 0) throw new ArgumentOutOfRangeException(nameof(day));
        int mondayBasedWeekday = (int)(((long)day + 6) % 7);
        return checked(day + 8 - mondayBasedWeekday);
    }
    internal static int NextTuesday(int day)
    {
        if (day < 0) throw new ArgumentOutOfRangeException(nameof(day));
        return checked(day + (9 - day % 7) % 7);
    }
    internal static int Quote(int actualUnitValue)
    {
        if (actualUnitValue <= 0) throw new ArgumentOutOfRangeException(nameof(actualUnitValue));
        return checked((int)(((long)actualUnitValue * 5 + 3) / 4));
    }
    internal static GraylineSupplyState Schedule(GraylineSupplyState state, int day,
        string appointmentId, IReadOnlyDictionary<string, int> availableValues, Func<int, int>? nextIndex = null) =>
        Schedule(state, day, appointmentId, () => availableValues, nextIndex);

    internal static GraylineSupplyState Schedule(GraylineSupplyState state, int day,
        string appointmentId, Func<IReadOnlyDictionary<string, int>?> readValues, Func<int, int>? nextIndex = null)
    {
        if (!state.Unlocked || state.Pending != null) throw new InvalidOperationException("Supply is unavailable");
        int due = NextTuesday(day);
        if (state.HasAppointment)
            return state.SchemaVersion == 1 && state.Receipts.Length == 0 && state.DebugVisitDay == null
                ? state with { SchemaVersion = 2, BookedDay = day, DueDay = due, Revision = checked(state.Revision + 1) }
                : state;
        int lastVisit = state.LastVisitDay;
        if (lastVisit < 0 && state.VisitState == "closed")
            lastVisit = Math.Max(state.PurchaseStartDay, state.Receipts.Select(x => x.Day).DefaultIfEmpty(-1).Max());
        if (lastVisit >= day) return state;
        if (!Guid.TryParseExact(appointmentId, "D", out _)) throw new ArgumentException("Invalid appointment ID");
        // The native valuation callback creates temporary products. Only read it for a new shipment.
        var availableValues = readValues();
        if (availableValues == null) return state;
        nextIndex ??= ManagedRandom.Shared.Next;
        var available = Products.Where(x => availableValues.ContainsKey(x.ItemId)).ToArray();
        var selected = available.Where(x => x.Kind == GraylineProductKind.Material).ToList();
        var machines = available.Where(x => x.Kind == GraylineProductKind.Machine).ToList();
        int Index(int count)
        {
            int index = nextIndex(count);
            return (uint)index < (uint)count ? index : throw new ArgumentOutOfRangeException(nameof(nextIndex));
        }
        void Draw(List<GraylineProduct> pool)
        { int index = Index(pool.Count); selected.Add(pool[index]); pool.RemoveAt(index); }
        for (int i = 0; i < 2 && machines.Count > 0; i++) Draw(machines);
        void DrawTiers(GraylineProductKind basicKind, GraylineProductKind rareKind)
        {
            var basic = available.Where(x => x.Kind == basicKind).ToList();
            var rare = available.Where(x => x.Kind == rareKind).ToList();
            for (int i = 0; i < 2 && basic.Count + rare.Count > 0; i++)
            {
                var pool = basic.Count == 0 ? rare : rare.Count == 0 ? basic :
                    Index(BasicTierWeight + RareTierWeight) < BasicTierWeight ? basic : rare;
                Draw(pool);
            }
        }
        var nodes = available.Where(x => x.Kind == GraylineProductKind.RareNode).ToList();
        for (int i = 0; i < 2 && nodes.Count > 0; i++) Draw(nodes);
        DrawTiers(GraylineProductKind.BasicModule, GraylineProductKind.RareModule);
        var stock = selected.Select(x =>
            new GraylineStock(x.ItemId, x.Quantity, x.Quantity, Quote(availableValues[x.ItemId]))).ToArray();
        if (stock.Length == 0) throw new InvalidOperationException("No registered supply products");
        return state with { SchemaVersion = 2, CatalogVersion = CurrentCatalogVersion, LastVisitDay = lastVisit,
            AppointmentId = appointmentId, BookedDay = day, DueDay = due,
            DebugVisitDay = null, VisitState = "waiting", Stock = stock, Receipts = [], Pending = null, Revision = checked(state.Revision + 1) };
    }
    internal static GraylineSupplyState Expedite(GraylineSupplyState state, int day)
    {
        if (!state.HasAppointment || state.Pending != null || day < state.BookedDay)
            throw new InvalidOperationException("Immediate visit is unavailable");
        return state.DebugVisitDay != null ? state : state with { DebugVisitDay = day, Revision = state.Revision + 1 };
    }
    internal static bool IsDue(GraylineSupplyState state, int day, bool open) =>
        state.HasAppointment && state.Pending == null && open && day >= state.PurchaseStartDay &&
        (state.DebugVisitDay != null || day % 7 == 2);
    internal static string? PurchaseDenial(GraylineSupplyState state, string appointmentId,
        string operationId, string itemId, int quantity, int quotedPrice, int cash, bool nativeTrade = false)
    {
        if (!state.HasAppointment || state.AppointmentId != appointmentId || state.VisitState != "arrived") return "visit";
        if (state.Pending != null) return "pending";
        if (!Guid.TryParseExact(operationId, "D", out _) || state.Receipts.Any(x => x.OperationId == operationId)) return "duplicate";
        var stock = state.Stock.FirstOrDefault(x => x.ItemId == itemId);
        if (stock == null || Product(itemId, state.CatalogVersion) == null || quantity <= 0 || quantity > stock.Remaining) return "stock";
        if (quotedPrice < 0 || !nativeTrade && (quotedPrice == 0 || quotedPrice != stock.UnitPrice)) return "price";
        if (cash < 0 || (long)quantity * quotedPrice > cash) return "cash";
        return null;
    }
    internal static GraylineSupplyState Prepare(GraylineSupplyState state, string operationId,
        string itemId, int quantity, int price, int day, int cash, int[] outputUids, bool nativeTrade = false)
    {
        if (PurchaseDenial(state, state.AppointmentId ?? "", operationId, itemId, quantity, price, cash, nativeTrade) is { } denial)
            throw new InvalidOperationException("Supply purchase rejected: " + denial);
        if (day < state.PurchaseStartDay || outputUids.Length != quantity || outputUids.Any(x => x <= 0) ||
            outputUids.Distinct().Count() != quantity || state.Receipts.SelectMany(x => x.OutputUids).Intersect(outputUids).Any())
            throw new InvalidOperationException("Invalid supply output identities");
        return state with { Pending = new(operationId, itemId, quantity, price, day, cash, outputUids.ToArray(), nativeTrade), Revision = checked(state.Revision + 1) };
    }
    internal static GraylineSupplyState Commit(GraylineSupplyState state)
    {
        var p = state.Pending ?? throw new InvalidOperationException("No pending supply purchase");
        int after = checked(p.CashBefore - p.Quantity * p.UnitPrice);
        return state with
        {
            Stock = state.Stock.Select(x => x.ItemId == p.ItemId ? x with { Remaining = checked(x.Remaining - p.Quantity) } : x).ToArray(),
            Receipts = [.. state.Receipts, new(p.OperationId, p.ItemId, p.Quantity, p.UnitPrice, p.Day, p.CashBefore, after, p.OutputUids.ToArray(), p.NativeTrade)],
            Pending = null, Revision = checked(state.Revision + 1)
        };
    }
    internal static GraylineSupplyState Close(GraylineSupplyState state, int day)
    {
        if (!state.HasAppointment) return state;
        if (day < state.PurchaseStartDay || state.Pending?.Day > day || state.Receipts.Any(x => x.Day > day))
            throw new ArgumentOutOfRangeException(nameof(day));
        // An interrupted purchase never debits shipment stock or replays the native sale.
        return state with { VisitState = "closed", LastVisitDay = day, Pending = null, Revision = checked(state.Revision + 1) };
    }
}
