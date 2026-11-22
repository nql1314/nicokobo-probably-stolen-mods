using MechcoreProtocol.ModuleMatrix.Definitions;
using MechcoreProtocol.ModuleMatrix.Domain;
using UnityEngine;
using Nicokobo.ContentInterop;

namespace MechcoreProtocol.ModuleMatrix;

public sealed class MatrixMod
{
    internal const string Id = FixedModuleDefinitions.OwnerId;
    private readonly MechanicLocalizedPatches _modulePatches = new(MechcorePackage.Id, "module-effects", MatrixEffects.Patch);
    internal static bool Active { get; private set; }
    internal static MatrixSettings Settings { get; private set; } = new(true, true, true, true, true);
    internal static PlayerStore? Store => PlayerStore.HasInstance ? PlayerStore.Instance : null;
    internal static void Warn(string message) => Debug.LogWarning("[ModuleMatrix] " + message);
    internal static bool Enabled(string id) => FixedModuleDefinitions.All.Any(item => item.Id == id) ? Settings.Fixed :
        ContinuousModuleDefinitions.ById.ContainsKey(id) ? Settings.Continuous :
        NodeItemDefinitions.All.Any(item => item.Id == id) ? Settings.Nodes :
        MachineModuleDefinitions.All.Any(item => item.Id == id && !item.Retired) && Settings.Machines;

    public void Init(ModManifest manifest) => Settings = MatrixSettings.Load(manifest.ID);

    internal void EnableContent()
    {
        if (Active) return;
        FixedModuleAbilities.RequireVersion(1);
        Active = true;
        ContentRuntime.Attach(Id);
        ModHook.OnModItemDirectoryInit += Register;
        ModHook.OnLootTablesLoaded += Loot;
        Nicokobo.ContentInterop.NightShop.Register(Id, NightShop);
        ModHook.OnGameLoadedLate += Loaded;
        ModHook.OnGoingSleepLate += Night;
        ModHook.OnCreateTooltipLate += Tooltip;
        ModHook.OnCreateTooltipEarly += MatrixText.Tooltip;
        _modulePatches.Start();
    }
    internal void DisableContent()
    {
        if (!Active) return;
        Active = false;
        _modulePatches.Stop();
        ModHook.OnModItemDirectoryInit -= Register;
        ModHook.OnLootTablesLoaded -= Loot;
        ModHook.OnGameLoadedLate -= Loaded;
        ModHook.OnGoingSleepLate -= Night;
        ModHook.OnCreateTooltipLate -= Tooltip;
        ModHook.OnCreateTooltipEarly -= MatrixText.Tooltip;
        MatrixText.Assets = null;
        MatrixEffects.Unregister();
        ContentRuntime.Detach(Id);
    }
    private static void Register(ModItemDirectory directory)
    {
        var collision = ModuleIconDefinitions.All.FirstOrDefault(icon => DirectoryMaster.Has<GameItem>(icon.ItemId));
        if (collision != null) throw new InvalidOperationException("Matrix item ID already registered: " + collision.ItemId);
        MatrixEffects.Register();
        foreach (var icon in ModuleIconDefinitions.All)
            if (!directory.Add(icon.ItemId, () => MatrixItems.Create(icon.ItemId)))
                throw new InvalidOperationException("Matrix registration failed: " + icon.ItemId);
        MatrixInterop.Register();
    }
    internal static void Assets(LoadedMod mod)
    {
        if (mod.Manifest.ID != MechcorePackage.Id) return;
        MatrixText.Assets = mod;
        MatrixEffects.RefreshText();
        foreach (var icon in ModuleIconDefinitions.All)
            if (!mod.Sprites.ContainsKey(icon.SpriteKey)) throw new InvalidOperationException("Missing Matrix sprite: " + icon.SpriteKey);
    }
    private static void Loot()
    {
        if (!Settings.Fixed) return;
        foreach (var item in FixedModuleDefinitions.Scavenging)
        foreach (string table in new[] { TableMaster.allModuleTable, TableMaster.t1moduleTable })
            LootRegistry.AddEntry(Id, table, item.Id, ModuleMatrixNumbers.Distribution.FixedModuleLootWeight);
    }
    private static string[] NightShop(GameItem[] owned)
    {
        var stock = new List<string>();
        var available = ModuleIconDefinitions.All.Where(icon => Enabled(icon.ItemId) && DirectoryMaster.Has<GameItem>(icon.ItemId))
            .Select(icon => icon.ItemId).ToHashSet(StringComparer.Ordinal);
        foreach (string code in NightShopModuleSelection.Draw(available, count => RNG.GetRandomIntExculsive(0, count)))
        {
            string id = ModuleTierDefinitions.All.Single(item => item.IsMatrix && MatrixItems.Code(item.Id) == code).Id;
            stock.Add(id);
        }
        return stock.ToArray();
    }
    private static void Loaded()
    {
        if (Store == null) return;
        var items = Store.FindAllItem();
        foreach (var item in items) { MatrixItems.Reprice(item); MatrixMachines.Configure(item); }
        foreach (var machine in items.Where(item => MatrixMachines.Modules(item) != null)) MatrixMachines.Recompute(machine);
    }
    private static void Night()
    {
        MatrixGrowth.Decay();
        MatrixMachines.Market();
    }
    private static void Tooltip(RichTextBuilder builder, GameItem item)
    {
        MatrixItems.Reprice(item);
        if (!MatrixEffects.Enabled(item) && !MachineModuleDefinitions.All.Any(def => def.Id == item.identifier && def.Retired))
            builder.AddLine(MatrixText.Get("matrix.category_disabled_item_and_growth_are_preserved"));
        if (ModuleTierDefinitions.Find(item.identifier) is { } tier)
            builder.AddLine(ModuleTierDefinitions.Label(tier.Tier));
        if (ContinuousModuleDefinitions.ById.TryGetValue(item.identifier, out var continuous))
            builder.AddLine(MatrixTurbo.ContinuousDescription(item, continuous));
        MatrixMachines.Tooltip(builder, item);
    }
}
