using MechcoreProtocol.SynthesisExpansion.Definitions;
using UnityEngine;
using Nicokobo.ContentInterop;

namespace MechcoreProtocol.SynthesisExpansion;

public sealed class SynthesisMod
{
    private bool _enabled;
    private bool _addedCyberneticType;
    internal static bool Active { get; private set; }
    internal static PlayerStore? Store => PlayerStore.HasInstance ? PlayerStore.Instance : null;
    internal static LoadedMod? Assets { get; private set; }
    public void Init(ModManifest manifest) { }

    internal void EnableContent()
    {
        if (_enabled) return;
        _enabled = true;
        Active = true;
        if (!TypeHelper.ALL_TYPES_LIST.Contains("CYBERNETIC"))
        { TypeHelper.ALL_TYPES_LIST.Add("CYBERNETIC"); _addedCyberneticType = true; }
        ContentRuntime.Attach(SynthesisDefinitions.OwnerId);
        ResetRuntime();
        ModHook.OnModItemDirectoryInit += RegisterItems;
        ModHook.OnLootTablesLoaded += RegisterLoot;
        ModHook.OnNewGameStarted += OpeningGuides;
        Nicokobo.ContentInterop.NightShop.Register(SynthesisDefinitions.OwnerId, StockInventor);
        ModHook.OnGameLoadedInit += ResetRuntime;
        ModHook.OnShutterClosedLate += ManufacturedEffectsRuntime.Closed;
        ModHook.OnGoingSleepEarly += Night;
        ModHook.OnCreateTooltipLate += Tooltip;
        ModHook.OnCreateTooltipEarly += SynthesisItems.Tooltip;
    }
    internal void DisableContent()
    {
        if (!_enabled) return;
        _enabled = false;
        Active = false;
        if (_addedCyberneticType) { TypeHelper.ALL_TYPES_LIST.Remove("CYBERNETIC"); _addedCyberneticType = false; }
        ModHook.OnModItemDirectoryInit -= RegisterItems;
        ModHook.OnLootTablesLoaded -= RegisterLoot;
        ModHook.OnNewGameStarted -= OpeningGuides;
        ModHook.OnGameLoadedInit -= ResetRuntime;
        ModHook.OnShutterClosedLate -= ManufacturedEffectsRuntime.Closed;
        ModHook.OnGoingSleepEarly -= Night;
        ModHook.OnCreateTooltipLate -= Tooltip;
        ModHook.OnCreateTooltipEarly -= SynthesisItems.Tooltip;
        ResetRuntime(); Assets = null;
        ContentRuntime.Detach(SynthesisDefinitions.OwnerId);
        SynthesisGuide.Close();
    }

    private static void RegisterItems(ModItemDirectory directory)
    {
        var collision = ProductionCatalog.Items.FirstOrDefault(item => DirectoryMaster.Has<GameItem>(item.Id));
        if (collision != null) throw new InvalidOperationException("Synthesis item ID already registered: " + collision.Id);
        foreach (var item in ProductionCatalog.Items)
            if (!directory.Add(item.Id, () => SynthesisItems.Create(item)))
                throw new InvalidOperationException("Synthesis registration failed: " + item.Id);
        SynthesisInterop.Register();
    }

    internal static void AssetsLoaded(LoadedMod mod)
    {
        if (mod.Manifest.ID != MechcorePackage.Id) return;
        Assets = mod;
        RecipeConfiguration.Load();
        foreach (var item in ProductionCatalog.Items)
            if (!mod.Sprites.ContainsKey(ProductionCatalog.SpriteKey(item)))
                throw new InvalidOperationException("Missing synthesis sprite: " + item.Id);
        foreach (string state in new[] { "scavenging_robot_broken", "mining_drill_upgraded" })
            if (!mod.Sprites.ContainsKey(state)) throw new InvalidOperationException("Missing synthesis state sprite: " + state);
        Debug.Log($"[MechcoreSynthesis] {ProductionCatalog.Items.Count} production items, {ProductionCatalog.AllRecipes.Count()} recipes; official loader");
    }

    private static void RegisterLoot()
    {
        foreach (var item in ProductionCatalog.Items.Where(item => item.Kind == ItemKind.Machine ||
                     item.Id == SynthesisDefinitions.Quartz.Id || item.Id == SynthesisDefinitions.TitaniumOre.Id))
            LootRegistry.AddEntry(SynthesisDefinitions.OwnerId,
                item.Kind == ItemKind.Machine ? TableMaster.toolTable : TableMaster.materialTable,
                item.Id, item.Kind == ItemKind.Machine ? SynthesisNumbers.Distribution.MachineLootWeight : SynthesisNumbers.Distribution.OreLootWeight);
    }

    private static void OpeningGuides(string scenarioId)
    {
        ResetRuntime();
        foreach (var definition in ProductionCatalog.Items.Where(item => item.Kind == ItemKind.Guide))
        {
            if (!SynthesisGuide.HasContent(definition.Id)) continue;
            var item = ModHelper.SpawnItem(definition.Id);
            if (item == null) continue;
            ModHelper.MoveItemToInventory(item);
            if (item.parentInventory == null) ModHelper.MoveItemToBackCounter(item);
            if (item.parentInventory != null) continue;
            item.Destroy();
            Warn("No space for opening guide: " + definition.Id + ". Another copy can be bought from Jackson.");
        }
    }

    private static string[] StockInventor(GameItem[] ownedItems)
    {
        var stock = new List<string>();
        foreach (var definition in ProductionCatalog.Items.Where(item => item.Kind is ItemKind.Machine or ItemKind.Guide ||
                     item.Id == SynthesisDefinitions.Quartz.Id || item.Id == SynthesisDefinitions.TitaniumOre.Id ||
                     item.Id == SynthesisDefinitions.Silicon.Id))
        {
            if (definition.Kind == ItemKind.Machine && !RNG.Roll(SynthesisNumbers.Distribution.MachineNightShopChancePercent)) continue;
            if (definition.Kind == ItemKind.Guide &&
                (!SynthesisGuide.HasContent(definition.Id) || ownedItems.Any(item => item.identifier == definition.Id))) continue;
            if (definition.Id == SynthesisDefinitions.Silicon.Id &&
                (StoreStation.GetDayCounter() < SynthesisNumbers.Distribution.RefinedMaterialMinimumStockDay ||
                 ownedItems.Any(item => item.identifier == definition.Id))) continue;
            stock.Add(definition.Id);
        }
        return stock.ToArray();
    }

    internal static void Warn(string message) => Debug.LogWarning("[MechcoreSynthesis] " + message);

    private static void ResetRuntime()
    {
        ManufacturedEffectsRuntime.Reset(); CyberneticTrading.Reset();
        SynthesisRobots.Reset(); SynthesisCyberEye.Reset(); SynthesisPower.Reset();
    }
    private static void Night()
    {
        ManufacturedEffectsRuntime.Night();
        SynthesisRobots.Night();
    }
    private static void Tooltip(RichTextBuilder builder, GameItem item)
    {
        ManufacturedEffectsRuntime.BeforeRead(item);
        ManufacturedEffectsRuntime.Tooltip(builder, item);
        RobotAppearance.Tooltip(builder, item);
        SynthesisCyberEye.Tooltip(builder, item);
    }
}
