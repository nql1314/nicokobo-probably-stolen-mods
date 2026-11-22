using AugMechanicalAscension.Domain;
using Nicokobo.ModUtilities;
using ProbablyStolen.ModSupport;

namespace AugMechanicalAscension.Integration;

// Aug owns these cybernetic products. Synthesis supplies the stable K05 terminal;
// Synthesis owns the moved machine, sleep and AI products.
internal static class CyberneticManufacturing
{
    private const string OwnerId = "nicokobo.aug";
    // This package contributes recipes without registering a second terminal.
    internal const string MachineId = OfficialMachineTarget.MachineId;
    private static IReadOnlyList<CyberneticProductDefinition> Products => CyberneticProductDefinitions.Products;

    private static bool _itemsStaged;
    private static bool _recipesStaged;

    internal static bool Install(Action<string> log)
    {
        _itemsStaged = true;
        foreach (var product in Products)
        {
            var result = OfficialItemApi.RegisterItem(OwnerId, product.Id,
                () => Create(product), new NativeItemOptions(
                    Name: new(product.Id + ".name"),
                    ShortDescription: new(product.Id + ".description"),
                    FlavorText: new(product.Id + ".flavor")));
            _itemsStaged &= result.Status is SubmitStatus.Accepted or
                SubmitStatus.AlreadyPresent;
            log($"[Manufacturing] product={product.Id}; stage={result.Status}; " +
                $"reason={result.Reason}");
        }
        _itemsStaged &= BionicRepairGelManufacturing.Install(log);
        return _itemsStaged;
    }

    // All mods have submitted initial declarations by this loader callback.
    // One registration replaces per-frame polling, snapshots and reflection.
    internal static void RegisterRecipes()
    {
        if (!_itemsStaged || _recipesStaged) return;
        foreach (var product in Products)
        {
            var ingredients = product.Ingredients.Select(ingredient =>
            {
                bool requiresFullPerfume = product.Suffix == "mimetic_skin" &&
                    ingredient.ItemId == "neuroactive_perfume";
                return new Nicokobo.ContentInterop.IngredientView(ingredient.ItemId, ingredient.Quantity, item =>
                {
                    if (item.identifier != ingredient.ItemId) return false;
                    if (!requiresFullPerfume) return true;
                    int maximum = item.GetTagReadonly(UseCountHelper.MAX_USE_COUNT_INT).GetInt();
                    return item.IsTag(UseCountHelper.USE_COUNT_ITEM_TAG) && maximum > 0 &&
                        item.GetTagReadonly(UseCountHelper.CURRENT_USE_COUNT_INT).GetInt() == maximum;
                }, requiresFullPerfume ? AugText.GetFor("aug.full_perfume", false) : ingredient.ItemId,
                    requiresFullPerfume ? AugText.GetFor("aug.full_perfume", true) : ingredient.ItemId)
                {
                    LocalizedLabel = requiresFullPerfume
                        ? () => AugText.Get("aug.ingredient_full_uses", NativeItemText.Name(ingredient.ItemId, ingredient.ItemId))
                        : null
                };
            }).ToList();
            if(product.MusicCassetteCount>0) ingredients.Add(new("",
                product.MusicCassetteCount,
                item=>CyberneticProductDefinitions.IsMusicCassette(item.IsTag("CASSETTE_TAG"),item.IsGameItemType("ART")||item.IsGameItemType("LUXURY_ITEM"),item.GetTagReadonly("CASSETTE_CONTENT")?.valueString),
                Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "aug.any_music_cassette", "zh"),
                Nicokobo.ModUtilities.ModText.GetFor("nicokobo.mechcore_protocol", "aug.any_music_cassette", "en"))
            { LocalizedLabel = () => ModText.Get(MechcorePackage.Id, "aug.any_music_cassette") });
            Nicokobo.ContentInterop.ExtraRecipes.Register(OwnerId,new(product.RecipeId,MachineId,ingredients,product.Id,product.Chinese,product.English,()=>OfficialItemApi.Exists(product.Id),()=>product.Id)
            { LocalizedOutputName = () => AugText.Get(product.Id + ".name") });
        }
        _recipesStaged=true;
        BionicRepairGelManufacturing.RegisterRecipes();
    }

    private static GameItem Create(CyberneticProductDefinition product)
    {
        var item = DirectoryMaster.Create<GameItem>(product.TemplateId);
        if (item == null || NativeObject.Handle(item) == IntPtr.Zero ||
            item.identifier != product.TemplateId || item.unitCount != 1)
            throw new InvalidOperationException($"Product template unavailable: {product.Id}");
        var shape = new GridShapeBuilder()
            .SetData(new string('1', checked(product.Width * product.Height)), product.Width).Build();
        if (shape == null || NativeObject.Handle(shape) == IntPtr.Zero)
            throw new InvalidOperationException($"Product shape unavailable: {product.Id}");
        item.SetSpriteAndShapeFromMod(MechcorePackage.Id, ItemArtwork.Cybernetic(product.Suffix));
        // Artwork transparency must not change the product's declared footprint.
        item.SetShape(shape);
        ProbablyStolen.ModSupport.ModItemTypeRules.ApplyTemplateTypes(item, cybernetic: true);
        item.identifier = product.Id;
        item.name = AugText.Get(product.Id + ".name");
        item.shortDescription = AugText.Get(product.Id + ".description");
        item.flavorText = AugText.Get(product.Id + ".flavor");
        item.unitBaseValue = 0;
        item.unitValue = product.BaseValue;
        item.mayStackItemFunc = null;
        item.mayActivateSlotItemFunc = null;
        item.canActivateSlotItemFunc = null;
        item.onActivateSlotItemFunc = null;
        item.onLoaded = null;
        item.forceDisableActivate = false;
        NativeContrabandStatus.Apply(item, product.ContrabandLevel);
        if (item.identifier != product.Id || item.unitCount != 1 ||
            item.unitValue != product.BaseValue || item.shape?.width != product.Width ||
            item.shape?.height != product.Height ||
            item.modifiedShape?.width != product.Width || item.modifiedShape?.height != product.Height)
            throw new InvalidOperationException($"Product factory mismatch: {product.Id}");
        return item;
    }
}
