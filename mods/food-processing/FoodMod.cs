using Nicokobo.FoodProcessing.Definitions;
using UnityEngine;
using Nicokobo.ContentInterop;

namespace Nicokobo.FoodProcessing;

public sealed class FoodMod : IMod
{
    private bool _enabled;
    public void Init(ModManifest manifest) { }

    public void OnEnable() => Nicokobo.ContentInterop.ForgeClient.Enable(FoodDefinitions.ModId, EnableContent, DisableContent, AssetsLoaded);
    public void OnDisable() => Nicokobo.ContentInterop.ForgeClient.Disable(DisableContent);

    private void EnableContent()
    {
        if (_enabled) return;
        _enabled = true;
        ContentRuntime.Attach(FoodDefinitions.ModId);
        ModHook.OnModItemDirectoryInit += RegisterItems;
        ModHook.OnLootTablesLoaded += RegisterLoot;
        ModHook.OnNewGameStarted += GiveOpeningGuide;
        Nicokobo.ContentInterop.NightShop.Register(FoodDefinitions.ModId, StockInventor);
        ModHook.OnCreateTooltipEarly += FoodItems.Tooltip;
    }

    private void DisableContent()
    {
        if (!_enabled) return;
        _enabled = false;
        ModHook.OnModItemDirectoryInit -= RegisterItems;
        ModHook.OnLootTablesLoaded -= RegisterLoot;
        ModHook.OnNewGameStarted -= GiveOpeningGuide;
        ModHook.OnCreateTooltipEarly -= FoodItems.Tooltip;
        FoodGuide.Close();
        FoodSelection.Close();
        FoodOutputPreview.CloseAll();
        ContentRuntime.Detach(FoodDefinitions.ModId);
    }

    private static void RegisterItems(ModItemDirectory directory)
    {
        var collision = FoodDefinitions.Items.FirstOrDefault(item => DirectoryMaster.Has<GameItem>(item.Id));
        if (collision != null) throw new InvalidOperationException("Food item ID already registered: " + collision.Id);
        foreach (var definition in FoodDefinitions.Items)
        {
            if (DirectoryMaster.Has<GameItem>(definition.Id))
                throw new InvalidOperationException("Food item ID already registered: " + definition.Id);
            if (!directory.Add(definition.Id, () => FoodItems.Create(definition)))
                throw new InvalidOperationException("Food item registration failed: " + definition.Id);
        }
        FoodInterop.Register();
    }

    private static void AssetsLoaded(LoadedMod mod)
    {
        if (mod.Manifest.ID != FoodDefinitions.ModId) return;
        RecipeConfiguration.Load();
        foreach (var item in FoodDefinitions.Items)
            if (!mod.Sprites.ContainsKey(FoodItems.SpriteKey(item)))
                throw new InvalidOperationException("Food sprite missing: " + item.Id);
        Debug.Log($"[FoodProcessing] {FoodDefinitions.Items.Count} items, {FoodDefinitions.Recipes.Count} recipes; official loader");
    }

    private static void RegisterLoot()
    {
        foreach (var item in FoodDefinitions.Items.Where(item => item.Kind is ItemKind.Food or ItemKind.Drink or ItemKind.Machine))
            LootRegistry.AddEntry(FoodDefinitions.ModId,
                item.Kind == ItemKind.Machine ? TableMaster.toolTable : TableMaster.packedFoodTable,
                item.Id, item.Kind == ItemKind.Machine
                    ? FoodNumbers.Distribution.MachineLootWeight : FoodNumbers.Distribution.FoodLootWeight);
    }

    private static void GiveOpeningGuide(string scenarioId)
    {
        var item = ModHelper.SpawnItem(FoodDefinitions.FoodProcessingGuideItem.Id);
        if (item == null) return;
        ModHelper.MoveItemToInventory(item);
        if (item.parentInventory == null) ModHelper.MoveItemToBackCounter(item);
        if (item.parentInventory == null)
        {
            item.Destroy();
            Warn("No space for the opening guide. Another copy can be bought from Jackson.");
        }
    }

    // This official hook also runs during daytime visits. The map identifies
    // Jackson's original night shop; daytime visits receive no extra stock.
    private static string[] StockInventor(GameItem[] ownedItems)
    {
        var stock = new List<string>();
        foreach (var item in FoodDefinitions.Items.Where(item => item.Kind is ItemKind.Machine or ItemKind.Guide))
        {
            if (item.Kind == ItemKind.Machine && !RNG.Roll(FoodNumbers.Distribution.MachineNightShopChancePercent)) continue;
            if (item.Kind == ItemKind.Guide && ownedItems.Any(owned => owned.identifier == item.Id)) continue;
            stock.Add(item.Id);
        }
        return stock.ToArray();
    }

    internal static void Warn(string text) => Debug.LogWarning("[FoodProcessing] " + text);
}
