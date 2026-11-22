using System.Reflection;
using HarmonyLib;

namespace Nicokobo.ContentInterop;

internal sealed class SupplyCallbackSpec
{
    internal readonly Type Root;
    internal readonly string Factory;
    internal readonly StockCategory Categories;
    internal readonly StockSupplier Supplier;
    internal readonly bool Miner, BothBranches, RepeatPoolDraw;
    internal SupplyCallbackSpec(Type root, string factory, StockCategory categories,
        StockSupplier supplier = StockSupplier.Any, bool miner = false, bool bothBranches = false, bool repeatPoolDraw = false)
    { Root = root; Factory = factory; Categories = categories; Supplier = supplier; Miner = miner; BothBranches = bothBranches; RepeatPoolDraw = repeatPoolDraw; }
}

internal static class NativeSupplyHooks
{
    internal const string HarmonyId = "nicokobo.content_interop.npc_supply";
    private const StockCategory Technical = StockCategory.Material | StockCategory.Module | StockCategory.Machine | StockCategory.Household;
    private const StockCategory Materials = StockCategory.Material | StockCategory.Ore | StockCategory.Module;
    internal static readonly IReadOnlyList<SupplyCallbackSpec> Callbacks = Array.AsReadOnly(new[]
    {
        new SupplyCallbackSpec(typeof(StoreClientList), "CreateMiner", StockCategory.Ore, miner: true),
        new SupplyCallbackSpec(typeof(StoreClientList), "CreateJunker", Materials),
        new SupplyCallbackSpec(typeof(StoreClientList), "CreateJunkerSellOnly", Materials),
        new SupplyCallbackSpec(typeof(StoreClientList), "CreateScrapper", Technical),
        new SupplyCallbackSpec(typeof(StoreClientList), "CreateLowerLevelRareMerchant", Technical),
        new SupplyCallbackSpec(typeof(StoreClientList), "CreateThief", StockCategory.All, StockSupplier.Thief),
        new SupplyCallbackSpec(typeof(StoreClientList), "CreatePettyThief", StockCategory.All),
        new SupplyCallbackSpec(typeof(StoreClientList), "CreateBrokeUpperLevel", StockCategory.All),
        new SupplyCallbackSpec(typeof(StoreClientList), "CreateFoodThief", StockCategory.Food),
        new SupplyCallbackSpec(typeof(StoreClientList), "CreateShadyPharmacist", StockCategory.Medical),
        new SupplyCallbackSpec(typeof(StoreClientList), "CreateRareLowerLevelChemist", StockCategory.Medical),
        new SupplyCallbackSpec(typeof(StoreClientList), "CreateLowerLevelChemist", StockCategory.Medical),
        new SupplyCallbackSpec(typeof(StoreClientList), "CreateScavGeneral", StockCategory.All),
        new SupplyCallbackSpec(typeof(StoreClientList), "CreateScavCrate", StockCategory.All, repeatPoolDraw: true),
        new SupplyCallbackSpec(typeof(StoreClientListMinor), "CreateScavHaul", Materials),
        new SupplyCallbackSpec(typeof(StoreClientListMinor), "CreateSalvagePilot", Materials),
        new SupplyCallbackSpec(typeof(StoreClientListMinor), "CreateNurse1", StockCategory.Medical),
        new SupplyCallbackSpec(typeof(StoreClientListRev), "CreateRevRaider", StockCategory.All),
        new SupplyCallbackSpec(typeof(StoreClientListRev), "CreateRevQuartermaster", StockCategory.All),
        new SupplyCallbackSpec(typeof(StoreClientListSurplus), "CreateFoodSurplusClient", StockCategory.Food, bothBranches: true),
        new SupplyCallbackSpec(typeof(StoreClientListSurplus), "CreateMedicalSurplusClient", StockCategory.Medical, bothBranches: true),
        new SupplyCallbackSpec(typeof(StoreClientListSurplus), "CreateMaterialSurplusClient", Materials, bothBranches: true),
        new SupplyCallbackSpec(typeof(StoreClientListEvent), "CreateScavengerHouseholdClient", StockCategory.Household)
    });

    [ThreadStatic] private static SupplySession? Session;
    private static Harmony? _harmony;
    private static readonly List<WeakReference<Dialogue>> Wrapped = new();
    private sealed class ScopeFrame { internal SupplySession? Previous; }

    internal static bool MatchesName(SupplyCallbackSpec spec, string name) =>
        name.StartsWith("<" + spec.Factory + ">b__", StringComparison.Ordinal) &&
        (name.EndsWith("_0", StringComparison.Ordinal) || name.EndsWith("b__0", StringComparison.Ordinal) ||
            spec.BothBranches && (name.EndsWith("_1", StringComparison.Ordinal) || name.EndsWith("b__1", StringComparison.Ordinal)));
    private sealed class SupplyAction
    {
        internal readonly Action Original;
        private readonly SupplyCallbackSpec _spec;
        internal SupplyAction(Action original, SupplyCallbackSpec spec) { Original = original; _spec = spec; }
        internal void Invoke()
        {
            if (_harmony == null) { Original(); return; }
            Open(_spec.Categories, _spec.Supplier, _spec.Miner, false, out var frame, _spec.RepeatPoolDraw);
            try { Original(); } finally { Session = frame.Previous; }
        }
    }

    // The official generation hook supplies the actual client graph. Wrap only
    // its known stock actions; do not discover or patch private compiler types.
    internal static void AttachCustomers(StoreClientManager manager)
    {
        Wrapped.RemoveAll(reference => !reference.TryGetTarget(out _));
        var visited = new HashSet<Dialogue>();
        foreach (var client in manager.clientStack.Concat(manager.chemicalFeedbackStack))
        {
            Visit(client.mainDialogue); Visit(client.acceptDealDialogue); Visit(client.acceptDealDialogueLastLine);
            Visit(client.placedWrongItemWhenSellingToDialogue); Visit(client.placeRightItemWhenSellingToDialogue);
            Visit(client.allDoneDialogue); Visit(client.repeatDialogue); Visit(client.interogationDialogue);
            Visit(client.glasseDialogue); Visit(client.customOnArrestDialogue); Visit(client.resumeAfterConfrontDialogue);
        }
        void Visit(Dialogue? dialogue)
        {
            if (dialogue == null || !visited.Add(dialogue)) return;
            if (dialogue.endAction != null)
            {
                bool changed = false;
                Action? replacement = null;
                foreach (Action action in dialogue.endAction.GetInvocationList())
                {
                    var root = action.Method.DeclaringType;
                    while (root?.DeclaringType != null) root = root.DeclaringType;
                    var spec = action.Target is SupplyAction ? null : Callbacks.FirstOrDefault(candidate =>
                        candidate.Root == root && MatchesName(candidate, action.Method.Name));
                    replacement += spec == null ? action : new SupplyAction(action, spec).Invoke;
                    changed |= spec != null;
                }
                if (changed) { dialogue.endAction = replacement; Wrapped.Add(new(dialogue)); }
            }
            Visit(dialogue.nextDialogue);
            foreach (var choice in dialogue.choices) Visit(choice.Item2);
        }
    }

    internal static void Install()
    {
        if (_harmony != null) return;
        var harmony = new Harmony(HarmonyId);
        try
        {
            Patch(harmony, AccessTools.Method(typeof(StoreClientList), nameof(StoreClientList.PlaceInventorInventory), new[] { typeof(bool) }), nameof(BeginInventor), finalizer: nameof(EndScope));
            Patch(harmony, AccessTools.Method(typeof(StoreClientList), nameof(StoreClientList.PlaceSupplierInventory), Type.EmptyTypes), nameof(BeginGeneral), finalizer: nameof(EndScope));
            Patch(harmony, AccessTools.Method(typeof(ItemSpawner), nameof(ItemSpawner.Spawn), new[] { typeof(string) }), postfix: nameof(Spawned));
            // The current public void wrapper delegates to this bool-returning
            // method, which owns capacity checks and unplaced-item destruction.
            Patch(harmony, AccessTools.Method(typeof(PlayerStore), nameof(PlayerStore.TryAddDirectSellingItemToTable), new[] {
                typeof(GameItem), typeof(bool), typeof(bool), typeof(bool), typeof(int) }), nameof(Place), finalizer: nameof(PlacementEnded));
            _harmony = harmony;
            ModHook.OnGenerateCustomerVeryLate += AttachCustomers;
            ModHook.OnLootRolled += LootRolled;
            if (PlayerStore.HasInstance) AttachCustomers(PlayerStore.Instance.storeClientManager);
        }
        catch
        {
            _harmony = harmony; Uninstall();
            throw;
        }
    }
    internal static void Uninstall()
    {
        ModHook.OnGenerateCustomerVeryLate -= AttachCustomers;
        ModHook.OnLootRolled -= LootRolled;
        foreach (var reference in Wrapped)
        {
            if (!reference.TryGetTarget(out var dialogue) || dialogue.endAction == null) continue;
            Action? original = null;
            foreach (Action action in dialogue.endAction.GetInvocationList())
                original += action.Target is SupplyAction wrapper ? wrapper.Original : action;
            dialogue.endAction = original;
        }
        Wrapped.Clear();
        _harmony?.UnpatchAll(HarmonyId); _harmony = null; Session = null;
    }
    private static void Patch(Harmony harmony, MethodBase method, string? prefix = null, string? postfix = null, string? finalizer = null)
    {
        HarmonyMethod? Hook(string? name) => name == null ? null : new HarmonyMethod(typeof(NativeSupplyHooks), name);
        harmony.Patch(method, Hook(prefix), Hook(postfix), finalizer: Hook(finalizer));
    }
    private static void Open(StockCategory categories, StockSupplier supplier, bool miner, bool night, out ScopeFrame __state, bool repeatPoolDraw = false)
    {
        __state = new() { Previous = Session };
        Session = new(categories, supplier, miner, night, repeatPoolDraw);
    }
    private static void BeginInventor(bool __0, out ScopeFrame __state) =>
        Open(StockCategory.Module, StockSupplier.Inventor, false, !__0, out __state);
    private static void BeginGeneral(out ScopeFrame __state) => Open(StockCategory.All, StockSupplier.Any, false, false, out __state);
    private static Exception? EndScope(ScopeFrame? __state, Exception? __exception)
    { if (__state != null) Session = __state.Previous; return __exception; }
    private static void LootRolled(LootRollContext context)
    {
        if (Session == null || Session.Miner || (SupplyPolicy.TableCategories(context.tableId) & Session.Categories) == 0) return;
        context.itemId = SupplyEngine.Roll(Session, context.tableId, context.itemId, LootRegistry.GetTableEntries(context.tableId));
        SupplyEngine.Rolled(Session, context.tableId, context.itemId);
    }
    private static void Spawned(string __0, GameItem __result) { if (Session != null) SupplyEngine.Spawned(Session, __0, __result); }
    private static bool Place(ref GameItem __0, bool __1, ref bool __result, out GameItem? __state)
    {
        __state = null;
        if (Session == null) return true;
        var previous = __0;
        bool run = SupplyEngine.Replace(Session, ref __0, __1);
        if (__0 != previous) __state = __0;
        if (!run) __result = false;
        return run;
    }
    private static Exception? PlacementEnded(GameItem? __state, Exception? __exception)
    {
        // Normal false returns already destroy rejected items natively. Only an
        // exception before native cleanup needs an explicit detached-item cleanup.
        if (__exception != null && __state?.parentInventory == null && __state != null) SupplyEngine.Destroy(__state);
        return __exception;
    }
}
