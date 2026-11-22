extern alias aug;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

internal static class RuntimeBoundaryChecks
{
    internal static void Run()
    {
        AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            string path = Path.Combine(@"F:\SteamLibrary\steamapps\common\Probably Stolen\Probably Stolen_Data\Managed", name.Name + ".dll");
            return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
        };
        var client = typeof(aug::AugMechanicalAscension.AugMod).Assembly.GetType("Nicokobo.ContentInterop.ForgeClient", true)!;
        var service = client.GetField("_service", BindingFlags.Static | BindingFlags.NonPublic)!;
        object? previous = service.GetValue(null);
        try
        {
            // Exercise the shipped value implementation behind the client transport.
            service.SetValue(null, (Func<string, object[], object?>)((operation, arguments) => operation switch
            {
                "value.read" => Nicokobo.ContentInterop.ProductionValue.ReadBasis((GameItem)arguments[0], (long)arguments[1]),
                _ => throw new InvalidOperationException("Unexpected transport call: " + operation)
            }));
            CheckEntrypoints();
        }
        finally { service.SetValue(null, previous); }
    }

    private static void CheckEntrypoints()
    {
        var assembly = typeof(aug::AugMechanicalAscension.AugMod).Assembly;
        var failures = new List<string>();
        int count = 0;
        void Check(bool result, string message)
        {
            count++;
            if (!result) failures.Add(message);
        }
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        var flowType = assembly.GetType("AugMechanicalAscension.Integration.ConsumableRuntimeFlow", true)!;
        var flow = RuntimeHelpers.GetUninitializedObject(flowType);
        var medical = flowType.GetMethod("IsMedical", hidden)!;
        var itemTypes = typeof(GameItem).GetField("itemTypes", hidden | BindingFlags.Public)!;
        foreach (string id in new[] { "common_medical", "bandage_item", "hemostatic_bandage_item",
                     "topical_bandage_item", "salve", "blood_bag", "blue_blood_bag", "tilapia_skin_graft",
                     "nicokobo.aug.bionic_repair_gel", "nicokobo.mechcore.synthesis.compressed_nutrient_liquid" })
        {
            var item = (GameItem)RuntimeHelpers.GetUninitializedObject(typeof(GameItemElement));
            item.identifier = id;
            itemTypes.SetValue(item, new List<string> { "MEDICAL" });
            Check((bool)medical.Invoke(flow, [item])!, "Recovery entry accepts " + id);
        }
        foreach (string id in new[] { "surgery_tool", "pink_alt_injector", "large_purple_injector",
                     "restricted_medical", "rubbing_alcohol", "nicokobo.aug.cybernetic.artificial_heart",
                     "nicokobo.aug.cybernetic.mimetic_skin", "nicokobo.aug.cybernetic.synthetic_vocal_cords" })
        {
            var item = (GameItem)RuntimeHelpers.GetUninitializedObject(typeof(GameItemElement));
            item.identifier = id;
            itemTypes.SetValue(item, new List<string> { "MEDICAL", "CYBERNETIC" });
            Check(!(bool)medical.Invoke(flow, [item])!, "Recovery entry rejects " + id);
        }
        var medicalValue = flowType.GetMethod("MedicalValue", hidden)!;
        foreach (long savedBasis in new long[] { 50, 62, 93 })
        {
            var item = (GameItem)RuntimeHelpers.GetUninitializedObject(typeof(GameItemElement));
            item.identifier = "nicokobo.aug.bionic_repair_gel";
            item.unitValue = 999;
            item.itemFeatures = new List<ItemFeature>();
            item.modifiedState = new TagSystem();
            const string tagName = "NICOKOBO_FORGE_PRODUCTION_BASIS_V1";
            var basisTag = new TagState(tagName, tagName);
            basisTag.Enable().SetString(savedBasis.ToString(System.Globalization.CultureInfo.InvariantCulture));
            item.modifiedState.dict[tagName] = basisTag;
            Check((long)medicalValue.Invoke(flow, [item])! == savedBasis,
                "Recovery reads the saved batch basis instead of the factory or displayed value: " + savedBasis);
        }

        foreach (string coldId in new[] { "chilled", "frozen", "iceCold" })
        {
            var item = (GameItem)RuntimeHelpers.GetUninitializedObject(typeof(GameItemElement));
            item.identifier = "nicokobo.mechcore.synthesis.compressed_nutrient_liquid";
            item.unitValue = 100;
            item.modifiedState = new TagSystem();
            var cold = new ItemFeature(ItemFeature.FeatureType.Normale)
            { identifier = coldId, category = coldId, valueStage = ItemFeature.ValueStage.Innate,
                isFeatureMatch = true, isExposable = false };
            // The native fridge writes the pre-exposure sale modifier, not valueModifier.
            cold.SetPreExposedValueMod(coldId == "chilled" ? 20 : 25);
            Check(cold.GetCurrentValueMod() == (coldId == "chilled" ? 20 : 25) && cold.GetActualValueModifier() == 0,
                "Native cold features affect current sale value, not actual recovery modifiers: " + coldId);
            item.itemFeatures = new List<ItemFeature> { cold };
            Check((long)medicalValue.Invoke(flow, [item])! == 100,
                "Cold sale feature must not increase nutrient-liquid recovery: " + coldId);
            var quality = new ItemFeature(ItemFeature.FeatureType.Normale)
            { identifier = "checks.quality", valueStage = ItemFeature.ValueStage.Innate };
            quality.SetValueModifier(15);
            item.itemFeatures.Add(quality);
            Check((long)medicalValue.Invoke(flow, [item])! == 115,
                "Cold sale bonuses must not mask actual quality: " + coldId);
        }

        var storyType = assembly.GetType("AugMechanicalAscension.Integration.StoryRuntimeFlow", true)!;
        var story = RuntimeHelpers.GetUninitializedObject(storyType);
        var registered = storyType.GetMethod("GraylineRegistered", hidden)!;
        var directories = typeof(DirectoryMaster).GetField("singletonInstance", BindingFlags.Static | BindingFlags.NonPublic)!;
        object? previous = directories.GetValue(null);
        try
        {
            // Populate the real game directory without creating Unity objects or invoking factories.
            var directory = (ModItemDirectory)RuntimeHelpers.GetUninitializedObject(typeof(ModItemDirectory));
            typeof(Directory<GameItem>).GetProperty("factoryDictionary")!.SetValue(directory,
                new Dictionary<string, Func<GameItem>>
                {
                    ["nicokobo.mechcore.synthesis.quartz"] = () => throw new Exception("Factory must not run"),
                    ["system_module_performance"] = () => throw new Exception("Factory must not run")
                });
            directories.SetValue(null, new Dictionary<Type, List<object>> { [typeof(GameItem)] = [directory] });
            Check((bool)registered.Invoke(story, ["nicokobo.mechcore.synthesis.quartz"])!,
                "Grayline accepts a registered external product without a local Aug declaration");
            Check((bool)registered.Invoke(story, ["system_module_performance"])!, "Grayline accepts registered native stock");
            Check(!(bool)registered.Invoke(story, ["nicokobo.mechcore.synthesis.missing"])!, "Grayline rejects a missing provider item");
        }
        finally { directories.SetValue(null, previous); }

        CheckSaveAfterRejectedRebind(assembly, Check);

        var client = assembly.GetType("Nicokobo.ContentInterop.ForgeClient", true)!;
        var service = client.GetField("_service", BindingFlags.Static | BindingFlags.NonPublic)!;
        object? previousService = service.GetValue(null);
        var gel = assembly.GetType("AugMechanicalAscension.Integration.BionicRepairGelManufacturing", true)!;
        var staged = gel.GetField("_staged", BindingFlags.Static | BindingFlags.NonPublic)!;
        var recipesStaged = gel.GetField("_recipesStaged", BindingFlags.Static | BindingFlags.NonPublic)!;
        object? previousStaged = staged.GetValue(null), previousRecipesStaged = recipesStaged.GetValue(null);
        var cybernetics = assembly.GetType("AugMechanicalAscension.Integration.CyberneticManufacturing", true)!;
        var cyberneticsStaged = cybernetics.GetField("_itemsStaged", BindingFlags.Static | BindingFlags.NonPublic)!;
        var cyberneticRecipesStaged = cybernetics.GetField("_recipesStaged", BindingFlags.Static | BindingFlags.NonPublic)!;
        object? previousCyberneticsStaged = cyberneticsStaged.GetValue(null),
            previousCyberneticRecipesStaged = cyberneticRecipesStaged.GetValue(null);
        var recipes = new List<object[]>();
        try
        {
            service.SetValue(null, (Func<string, object[], object?>)((operation, arguments) =>
            {
                if (operation == "extra.register") recipes.Add((object[])arguments[1]);
                else throw new Exception("Unexpected transport call: " + operation);
                return null;
            }));
            staged.SetValue(null, true);
            recipesStaged.SetValue(null, false);
            gel.GetMethod("RegisterRecipes", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null);
            Check(recipes.Count == 2, "Both repair-gel routes are declared by the actual Aug provider");
            Check(recipes.Any(recipe => (string)recipe[0] == "nicokobo.aug.recipe.bionic_repair_gel.skin_graft" &&
                ((object[][])recipe[2]).Any(input => (string)input[0] == "tilapia_skin_graft" && (int)input[1] == 1)),
                "Skin-graft route uses one actual graft instead of a new product identity");

            var recipeDirectory = (ModItemDirectory)RuntimeHelpers.GetUninitializedObject(typeof(ModItemDirectory));
            var recipeFactories = new Dictionary<string, Func<GameItem>>
            {
                ["nicokobo.aug.bionic_repair_gel"] = () => throw new Exception("Factory must not run")
            };
            typeof(Directory<GameItem>).GetProperty("factoryDictionary")!.SetValue(recipeDirectory, recipeFactories);
            object? beforeRecipeDirectories = directories.GetValue(null);
            try
            {
                directories.SetValue(null, new Dictionary<Type, List<object>> { [typeof(GameItem)] = [recipeDirectory] });
                Check(recipes.All(recipe => !((Func<bool>)((object[])recipe[7])[2])()),
                    "Repair-gel recipes stay unavailable when optional Food Processing K04 is absent");
                recipeFactories.Add("nicokobo.mechcore.synthesis.liquid_infusion_tower",
                    () => throw new Exception("Factory must not run"));
                Check(recipes.All(recipe => ((Func<bool>)((object[])recipe[7])[2])()),
                    "The same repair-gel recipes become available after K04 is registered");
                recipeFactories.Remove("nicokobo.aug.bionic_repair_gel");
                Check(recipes.All(recipe => !((Func<bool>)((object[])recipe[7])[2])()),
                    "An available K04 cannot offer repair gel without the actual output provider");
            }
            finally { directories.SetValue(null, beforeRecipeDirectories); }

            cyberneticsStaged.SetValue(null, true);
            cyberneticRecipesStaged.SetValue(null, false);
            cybernetics.GetMethod("RegisterRecipes", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null);
            var skinRecipe = recipes.Single(recipe => (string)recipe[0] == "nicokobo.aug.recipe.cybernetic.mimetic_skin");
            var skinInputs = (object[][])skinRecipe[2];
            var perfumeInput = skinInputs.Single(input => (string)input[0] == "neuroactive_perfume");
            var acceptsPerfume = (Func<GameItem, bool>)perfumeInput[2];
            var perfume = (GameItem)RuntimeHelpers.GetUninitializedObject(typeof(GameItemElement));
            perfume.identifier = "neuroactive_perfume";
            perfume.modifiedState = new TagSystem();
            perfume.modifiedState.dict[UseCountHelper.USE_COUNT_ITEM_TAG] =
                new TagState(UseCountHelper.USE_COUNT_ITEM_TAG, UseCountHelper.USE_COUNT_ITEM_TAG).Enable().SetBool(true);
            perfume.modifiedState.dict[UseCountHelper.MAX_USE_COUNT_INT] =
                new TagState(UseCountHelper.MAX_USE_COUNT_INT, UseCountHelper.MAX_USE_COUNT_INT).Enable().SetInt(5);
            var remaining = new TagState(UseCountHelper.CURRENT_USE_COUNT_INT, UseCountHelper.CURRENT_USE_COUNT_INT).Enable();
            perfume.modifiedState.dict[UseCountHelper.CURRENT_USE_COUNT_INT] = remaining;
            for (int uses = 5; uses >= 0; uses--)
            {
                remaining.SetInt(uses);
                Check(acceptsPerfume(perfume) == (uses == 5),
                    "Mimetic skin accepts only a full native perfume bottle: " + uses + "/5");
            }
            perfume.modifiedState.dict.Clear();
            Check(!acceptsPerfume(perfume), "Perfume without native use-count state is not a full bottle");
            perfume.identifier = "self_refilling_neuroactive_perfume";
            Check(!acceptsPerfume(perfume), "The full-bottle rule does not admit a different perfume item");
            perfume.identifier = "tilapia_skin_graft";
            Check(((Func<GameItem, bool>)skinInputs.Single(input => (string)input[0] == "tilapia_skin_graft")[2])(perfume),
                "The full-bottle rule does not add use-count requirements to skin grafts");
            Check(((string)perfumeInput[3]).Contains("满") && ((string)perfumeInput[4]).Contains("full"),
                "Both recipe languages disclose the full-bottle requirement");
        }
        finally
        {
            service.SetValue(null, previousService);
            staged.SetValue(null, previousStaged);
            recipesStaged.SetValue(null, previousRecipesStaged);
            cyberneticsStaged.SetValue(null, previousCyberneticsStaged);
            cyberneticRecipesStaged.SetValue(null, previousCyberneticRecipesStaged);
        }

        CheckMergedPackage(assembly, Check);
        LogisticsRuntimeChecks.Run(Check);
        if (failures.Count > 0) throw new Exception(string.Join(Environment.NewLine, failures));
        Console.WriteLine($"Aug runtime boundary checks passed: {count}");
    }

    // These contracts were not covered by the separate component suites: saved
    // atlas owners and native config values must survive the new package identity.
    private static void CheckSaveAfterRejectedRebind(Assembly assembly, Action<bool, string> check)
    {
        const BindingFlags hidden = BindingFlags.Static | BindingFlags.NonPublic;
        var fork = assembly.GetType("AugMechanicalAscension.Official.OfficialRunForkApi", true)!;
        var store = (PlayerStore)RuntimeHelpers.GetUninitializedObject(typeof(PlayerStore));
        store.startScenarioId = AugMechanicalAscension.Domain.StartDefinition.Id;
        var nativeObject = assembly.GetType("AugMechanicalAscension.Official.NativeObject", true)!;
        var blocked = fork.GetField("_blockedStore", hidden)!;
        object? previous = blocked.GetValue(null);
        try
        {
            var address = nativeObject.GetMethod("Handle", hidden)!.Invoke(null, [store]);
            blocked.SetValue(null, address);
            fork.GetMethod("SavePrefix", hidden)!.CreateDelegate<Action<PlayerStore>>()(store);
            check(Equals(blocked.GetValue(null), address),
                "normal saving can proceed while the failed Mod rebind still guards future forks");
        }
        finally { blocked.SetValue(null, previous); }
    }

    private static void CheckMergedPackage(Assembly assembly, Action<bool, string> check)
    {
        const string packageId = "nicokobo.mechcore_protocol";
        var spritePatch = assembly.GetType("MechcoreProtocol.PreviousSpriteOwnerPatch", true)!
            .GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic)!;
        foreach (string owner in new[] { "nicokobo.mechcore.synthesis", "nicokobo.mechcore_protocol.module_matrix",
                     "nicokobo.aug", packageId, "nicokobo.food_processing", "native_atlas" })
        {
            object[] arguments = [owner];
            spritePatch.Invoke(null, arguments);
            check((string)arguments[0] == (owner is "nicokobo.food_processing" or "native_atlas" ? owner : packageId),
                "Merged sprite patch preserves or remaps the actual saved owner: " + owner);
        }

        // Seed the real native cache in memory so For/Define deserialize saved
        // values without touching Unity paths or writing any user config files.
        const BindingFlags hidden = BindingFlags.Static | BindingFlags.NonPublic;
        var cache = (Dictionary<string, ModConfig>)typeof(ModConfig).GetField("configs", hidden)!.GetValue(null)!;
        string[] owners = ["nicokobo.mechcore_protocol.module_matrix", "nicokobo.aug", packageId];
        var previous = owners.ToDictionary(id => id, id => cache.TryGetValue(id, out var value) ? value : null);
        string[] keys = ["EnableP1FixedModules", "EnableP2PlacementSlice", "EnableP4ContinuousModules", "EnableNodeItems", "EnableMachineModules"];
        try
        {
            foreach (string id in owners)
                cache[id] = (ModConfig)Activator.CreateInstance(typeof(ModConfig), BindingFlags.Instance | BindingFlags.NonPublic,
                    null, [id], null)!;
            Dictionary<string, string> Raw(string id) => (Dictionary<string, string>)typeof(ModConfig)
                .GetField("rawValues", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(cache[id])!;
            foreach (string key in keys) Raw(owners[0])[key] = "false";
            Raw(owners[1])["StoryEnabled"] = "false";
            Raw(packageId)["EnableNodeItems"] = "true";
            var settings = assembly.GetType("MechcoreProtocol.ModuleMatrix.MatrixSettings", true)!
                .GetMethod("Load", hidden)!.Invoke(null, [packageId])!;
            foreach (string property in new[] { "Fixed", "Placement", "Continuous", "Nodes", "Machines" })
                check((bool)settings.GetType().GetProperty(property)!.GetValue(settings)! == (property == "Nodes"),
                    "Merged native settings retain previous values and prefer a saved new-package override: " + property);
            var previousBoolean = assembly.GetType("MechcorePackage", true)!.GetMethod("PreviousBoolean", hidden)!;
            check(!(bool)previousBoolean.Invoke(null, [owners[1], "StoryEnabled", true])!,
                "Merged story default reads the saved native component setting");
            check((bool)previousBoolean.Invoke(null, [owners[1], "NewMissingSetting", true])!,
                "Missing component setting uses its declared default");
        }
        finally
        {
            foreach (string id in owners)
                if (previous[id] is { } value) cache[id] = value; else cache.Remove(id);
        }
    }
}
