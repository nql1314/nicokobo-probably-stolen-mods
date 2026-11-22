using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Nicokobo.ContentNativeProbe;

internal sealed partial class ProbeContext
{
    private object AugMod => Reflect.Get(ModLoader.Mods.Single(m => m.Manifest.ID == "nicokobo.mechcore_protocol").Instance, "_aug")
        ?? throw new InvalidOperationException("Mechcore Protocol Aug component is absent");
    private object? AugState()
    {
        object? bridge = Reflect.Get(AugMod, "Bridge");
        object? session = bridge == null ? null : Reflect.Get(bridge, "CurrentSession");
        return session == null ? null : Reflect.Get(session, "State");
    }
    private bool AugReady()
    {
        object? state = AugState();
        return state != null && Reflect.Str(state, "InitPhase") == "complete" && Reflect.Str(state, "FailureReason") == "none" &&
            Reflect.Str(Reflect.Get(state, "Opening")!, "Phase") == "committed" &&
            Reflect.Str(state, "GameRunId") == Store.runID && Reflect.Int(state, "SaveSlotId") == Store.saveSlotId;
    }
    internal IEnumerable<object?> Aug()
    {
        R.phase = "aug-normal-opening";
        object state = AugState()!;
        R.Check("aug.normal-initial-save-opening", new { cash = 500, rent = 250, health = 100, exposure = 20, security = -15, phase = "committed" },
            new { cash = Store.playerCash, rent = Store.rentValue, health = Reflect.Int(state, "Health"), exposure = Reflect.Int(state, "Exposure"),
                security = StoreReputation.GetSecReputation(), opening = Reflect.Json(Reflect.Get(state, "Opening")) },
            Store.playerCash == 500 && Store.rentValue == 250 && Reflect.Int(state, "Health") == 100 && Reflect.Int(state, "Exposure") == 20 && StoreReputation.GetSecReputation() == -15,
            "Registered official MODDED scenario, ordinary InitialSave, and committed Aug opening/session");
        // Day is the independent scene fixture. No health/exposure is assigned;
        // every resource transition below is performed by the real production
        // SaveGame prefix/DayReconciliationFlow and consumable transaction.
        int originalDay = StoreStation.GetDayCounter();
        for (int i = 0; i < 7; i++)
        {
            int beforeHealth = Reflect.Int(AugState()!, "Health"), beforeExposure = Reflect.Int(AugState()!, "Exposure");
            StoreStation.Instance.dayCounter = originalDay + i + 1;
            Store.SaveGame();
            int health = Reflect.Int(AugState()!, "Health"), exposure = Reflect.Int(AugState()!, "Exposure");
            R.Check("aug.day-native-save." + i, new { health = beforeHealth - 10, exposure = beforeExposure + 5 }, new { health, exposure },
                health == beforeHealth - 10 && exposure == beforeExposure + 5, "Legal next-day fixture -> actual native SaveGame production settlement; full EndNight is separately untested");
            Store.SaveGame();
            R.Check("aug.same-day-no-repeat." + i, new { health, exposure }, new { health = Reflect.Int(AugState()!, "Health"), exposure = Reflect.Int(AugState()!, "Exposure") },
                Reflect.Int(AugState()!, "Health") == health && Reflect.Int(AugState()!, "Exposure") == exposure);
            yield return null;
        }
        object consumables = Reflect.Get(Reflect.Type("AugMechanicalAscension.Integration.ConsumableRuntimeFlow"), "_active")
            ?? throw new InvalidOperationException("Actual Aug recovery flow unavailable");
        var counterfeit = PreBuiltItemHelper.CreateInjector(ItemFeatureList.InjectorConditionType.Genuine, ItemFeatureList.InjectorConditionType.Counterfeit);
        Retain(counterfeit); int uid = counterfeit.uniqueId;
        int healthBefore = Reflect.Int(AugState()!, "Health");
        int exposureBefore = Reflect.Int(AugState()!, "Exposure"), injectorUnits = counterfeit.unitCount;
        JToken journalBefore = Reflect.Json(Reflect.Get(AugState()!, "LastCommittedOperation"));
        Reflect.Call(consumables, "ConsumeRecovery", counterfeit);
        R.Check("aug.nonmedical-injector-refused", new { health = healthBefore, exposure = exposureBefore, uid, units = injectorUnits, journal = journalBefore },
            new { health = Reflect.Int(AugState()!, "Health"), exposure = Reflect.Int(AugState()!, "Exposure"), item = ItemState(counterfeit), journal = Reflect.Json(Reflect.Get(AugState()!, "LastCommittedOperation")) },
            Reflect.Int(AugState()!, "Health") == healthBefore && Reflect.Int(AugState()!, "Exposure") == exposureBefore &&
                counterfeit.unitCount == injectorUnits && AllItems().Any(i => i.uniqueId == uid) &&
                JToken.DeepEquals(journalBefore, Reflect.Json(Reflect.Get(AugState()!, "LastCommittedOperation"))),
            "Native genuine-labelled/real-counterfeit injector -> actual ConsumeRecovery; current medical rules exclude immunizers regardless of authenticity, so no recovery, consumption or committed journal change");
        var electronics = Enumerable.Range(0, 4).Select(_ => Spawn("common_electronic")).ToArray();
        foreach (var electronic in electronics) Retain(electronic);
        for (int i = 0; i < 3; i++)
        {
            var electronic = electronics[i];
            int itemUid = electronic.uniqueId;
            int exposure = Reflect.Int(AugState()!, "Exposure"), units = AugOwnedUnits(itemUid);
            Reflect.Call(consumables, "ConsumeRecovery", electronic);
            R.Check("aug.electronic-actual-consumption." + i, new { exposure = Math.Max(0, exposure - 25), units = units - 1 },
                new { exposure = Reflect.Int(AugState()!, "Exposure"), units = AugOwnedUnits(itemUid), uid = itemUid },
                units == 1 && Reflect.Int(AugState()!, "Exposure") == Math.Max(0, exposure - 25) && AugOwnedUnits(itemUid) == units - 1,
                "Separate native items; consumption is read from the live owned inventory, not unitCount on a destroyed wrapper");
        }
        var blockedElectronic = electronics[3];
        int blockedUid = blockedElectronic.uniqueId, blockedUnits = AugOwnedUnits(blockedUid);
        Reflect.Call(consumables, "ConsumeRecovery", blockedElectronic);
        R.Check("aug.no-exposure-no-consumption", new { exposure = 0, units = blockedUnits }, new { exposure = Reflect.Int(AugState()!, "Exposure"), units = AugOwnedUnits(blockedUid) },
            Reflect.Int(AugState()!, "Exposure") == 0 && AugOwnedUnits(blockedUid) == blockedUnits);
        var medical = Spawn("common_medical"); Retain(medical);
        int medicalUid = medical.uniqueId;
        int basicHealth = Reflect.Int(AugState()!, "Health"), medicalBefore = AugOwnedUnits(medicalUid);
        long nativeBaseValue = medical.unitValue;
        Reflect.Call(consumables, "ConsumeRecovery", medical);
        R.Check("aug.medical-native-base-value", new { health = Math.Min(100, basicHealth + nativeBaseValue), count = medicalBefore - 1 },
            new { health = Reflect.Int(AugState()!, "Health"), count = AugOwnedUnits(medicalUid), declaredNativeBaseValue = nativeBaseValue },
            medicalBefore == 1 && Reflect.Int(AugState()!, "Health") == Math.Min(100, basicHealth + nativeBaseValue) && AugOwnedUnits(medicalUid) == medicalBefore - 1);
        var finishingMedical = Spawn("common_medical"); Retain(finishingMedical);
        int finishingUid = finishingMedical.uniqueId, beforeFinishing = Reflect.Int(AugState()!, "Health");
        long finishingValue = finishingMedical.unitValue;
        Reflect.Call(consumables, "ConsumeRecovery", finishingMedical);
        R.Check("aug.medical-clamps-to-missing-health", Math.Min(100, beforeFinishing + finishingValue),
            new { health = Reflect.Int(AugState()!, "Health"), remaining = AugOwnedUnits(finishingUid) },
            Reflect.Int(AugState()!, "Health") == Math.Min(100, beforeFinishing + finishingValue) && AugOwnedUnits(finishingUid) == 0);
        var fullHealthMedical = Spawn("common_medical"); Retain(fullHealthMedical);
        int fullHealthUid = fullHealthMedical.uniqueId;
        JToken fullHealthJournal = Reflect.Json(Reflect.Get(AugState()!, "LastCommittedOperation"));
        Reflect.Call(consumables, "ConsumeRecovery", fullHealthMedical);
        R.Check("aug.full-health-no-consumption", new { health = 100, units = 1, journal = fullHealthJournal },
            new { health = Reflect.Int(AugState()!, "Health"), units = AugOwnedUnits(fullHealthUid), journal = Reflect.Json(Reflect.Get(AugState()!, "LastCommittedOperation")) },
            Reflect.Int(AugState()!, "Health") == 100 && AugOwnedUnits(fullHealthUid) == 1 && JToken.DeepEquals(fullHealthJournal, Reflect.Json(Reflect.Get(AugState()!, "LastCommittedOperation"))));
        yield return null;
        R.phase = "aug-seven-real-manufacturing-recipes";
        var productDefinitions = Reflect.Each(Reflect.Get(Reflect.Type("AugMechanicalAscension.Domain.CyberneticProductDefinitions"), "Products")).ToArray();
        R.Check("aug.seven-product-definitions", 7, productDefinitions.Length, productDefinitions.Length == 7);
        foreach (object definition in productDefinitions)
        {
            string recipeId = Reflect.Str(definition, "RecipeId");
            object view = Reflect.Each(Reflect.Call(MachineCatalog, "Recipes", "nicokobo.forge.machines.universal_manufacturing_terminal"))
                .Single(r => Reflect.Str(r, "Id") == recipeId);
            try
            {
                using var fixture = CreateRecipeFixture(view);
                ObserveBatch(fixture, recipeId + ":aug-start", true);
            }
            catch (FixtureUnavailable error) { R.Skip("aug.manufacturing." + recipeId, error.Message); }
            yield return null;
        }
        foreach (object? wait in Workshop()) yield return wait;
        foreach (object? wait in AugNaturalStory()) yield return wait;
        // Opening grants one interface; the three final workshop rewards grant
        // another 1 + 1 + 1 in the current reward catalog. Normal Update recalculates all owned bases once per
        // second. Saving immediately after those rewards captures mixed ages.
        const string neuralId = "nicokobo.aug.brain_computer_interface_module";
        GameItem[] OwnedInterfaces() => AllItems().Where(item => item.identifier == neuralId && GeneralHelper.IsItemOwned(item))
            .OrderBy(item => item.uniqueId).ToArray();
        GameItem[] interfaces = OwnedInterfaces();
        int[] interfaceUids = interfaces.Select(item => item.uniqueId).ToArray();
        var interfaceBefore = new JArray(interfaces.Select(ItemState));
        float interfaceDeadline = Time.realtimeSinceStartup + 5f;
        bool basesSettled;
        R.phase = "aug-wait-normal-owned-interface-refresh";
        while (true)
        {
            interfaces = OwnedInterfaces();
            basesSettled = interfaces.Length == 4 && interfaceUids.SequenceEqual(interfaces.Select(item => item.uniqueId)) &&
                interfaces.All(item => item.unitCount == 1 &&
                    item.GetTagReadonly("BONUS_PERCENTAGE_PERFORMANCE_INT").GetInt() == 32 &&
                    item.GetTagReadonly("BONUS_PERCENTAGE_EFFICIENCY_INT").GetInt() == 32 &&
                    item.GetTagReadonly("BONUS_PERCENTAGE_QUALITY_INT").GetInt() == 16);
            if (basesSettled || Time.realtimeSinceStartup >= interfaceDeadline) break;
            yield return null;
        }
        R.Check("aug.neural-shared-base-settled-before-save", new { count = 4, uids = interfaceUids, performance = 32, efficiency = 32, quality = 16 },
            new { before = interfaceBefore, after = new JArray(interfaces.Select(ItemState)) }, basesSettled,
            "Only ordinary Unity frames: actual ownership-driven Update must settle four interfaces to 4 x 8 / 4 x 8 / 4 x 4 before the final native save. No Refresh/Tick calls or tag writes.");
        if (!basesSettled) throw new InvalidOperationException("Owned neural-interface bases did not settle within five seconds; final-save baseline refused");
        R.persistence["augExpectedState"] = Reflect.Json(AugState());
        R.Skip("aug.full-endnight-and-natural-health-ui", "Daily resource transactions were reached by actual SaveGame after legal day fixtures; complete EndNight and mouse medical UI remain outside this mode", false);
    }
    private int AugOwnedUnits(int uid) => AllItems().Where(item => item.uniqueId == uid && GeneralHelper.IsItemOwned(item)).Sum(item => item.unitCount);
    private IEnumerable<object?> Workshop()
    {
        R.phase = "aug-real-workshop-proofs-and-rewards";
        var workshop = Reflect.Type("AugMechanicalAscension.Integration.NicoWorkshop");
        var defs = Reflect.Each(Reflect.Get(Reflect.Type("AugMechanicalAscension.Domain.WorkshopAchievements"), "Definitions")).ToArray();
        foreach (object definition in defs)
        {
            string id = Reflect.Str(definition, "Id");
            string proofId = Reflect.Str(definition, "ProofItemId");
            GameItem? proof = null;
            if (proofId.Length != 0) { proof = Spawn(proofId); Retain(proof); }
            // Eligibility is observed by the real page-open callback, not the
            // background Update. Reuse the same native chain as full stories.
            bool opened = (bool)Reflect.Call(FullStoryWorkshopUi, "OpenChain", "nicokobo.aug.workshop")!;
            yield return null;
            JObject pageState = Reflect.Json(Reflect.Get(workshop, "_state")) as JObject ?? new JObject();
            bool pageReady = opened && FullStoryWorkshopWindow()?.IsVisible == true &&
                (proof == null || (int?)pageState["Completed"]?[id]?["UniqueId"] == proof.uniqueId);
            R.Check("aug.workshop.actual-page-latches-proof." + id, "Visible native Aug chain; the page-open callback latches the original owned proof UID",
                new { opened, state = pageState }, pageReady);
            if (!pageReady) throw new InvalidOperationException("Real Aug workshop page did not latch " + id);
            Store.SaveGame();
            int cash = Store.playerCash;
            int[] proofBefore = proof == null ? Array.Empty<int>() : new[] { proof.uniqueId, proof.unitCount };
            var rewards = Reflect.Each(Reflect.Get(definition, "Rewards"))
                .ToDictionary(reward => Reflect.Str(reward, "ItemId"), reward => Reflect.Int(reward, "Count"), StringComparer.Ordinal);
            var before = rewards.Keys.ToDictionary(rewardId => rewardId, rewardId => AllItems().Where(i => i.identifier == rewardId).Sum(i => i.unitCount));
            Reflect.Call(workshop, "Claim", id);
            object? live = Reflect.Get(workshop, "_state");
            bool claimed = live != null && Reflect.Each(Reflect.Get(live, "Claimed")).Cast<string>().Contains(id);
            var after = rewards.Keys.ToDictionary(rewardId => rewardId, rewardId => AllItems().Where(i => i.identifier == rewardId).Sum(i => i.unitCount));
            R.Check("aug.workshop.claim." + id, new { rewards, cashUnchanged = cash, proofNotConsumed = proofBefore, claimed = true },
                new { claimed, rewardAdded = rewards.Keys.ToDictionary(rewardId => rewardId, rewardId => after[rewardId] - before[rewardId]), cash = Store.playerCash, state = Reflect.Json(live) },
                claimed && rewards.All(reward => after[reward.Key] - before[reward.Key] == reward.Value) && Store.playerCash == cash && (proof == null || proof.uniqueId == proofBefore[0] && proof.unitCount == proofBefore[1] && proof.parentInventory != null),
                "Real owned factory proof -> native chain page-open/Observe -> native SaveGame confirmation -> same private Claim used by normal workshop UI; no debug unlock");
            Reflect.Call(workshop, "Claim", id);
            var repeated = rewards.Keys.ToDictionary(rewardId => rewardId, rewardId => AllItems().Where(i => i.identifier == rewardId).Sum(i => i.unitCount));
            R.Check("aug.workshop.no-duplicate." + id, after, repeated, rewards.Keys.All(rewardId => after[rewardId] == repeated[rewardId]));
            Reflect.Call(FullStoryWorkshopUi, "CloseWindow");
            Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(proof!);
            yield return null;
        }
        object? final = Reflect.Get(workshop, "_state");
        R.persistence["workshopExpectedState"] = Reflect.Json(final);
        R.Check("aug.workshop.true-final-ascension", new { claimed = 13, debugAscensionUnlocked = false }, Reflect.Json(final),
            final != null && Reflect.Each(Reflect.Get(final, "Claimed")).Count() == 13 && !(bool)Reflect.Get(final, "DebugAscensionUnlocked")!);
        int h = Reflect.Int(AugState()!, "Health"), e = Reflect.Int(AugState()!, "Exposure");
        StoreStation.Instance.dayCounter++;
        Store.SaveGame();
        R.Check("aug.ascension-real-settlement-exemption", new { health = h, exposure = e }, new { health = Reflect.Int(AugState()!, "Health"), exposure = Reflect.Int(AugState()!, "Exposure") },
            Reflect.Int(AugState()!, "Health") == h && Reflect.Int(AugState()!, "Exposure") == e);
    }
    private IEnumerable<object?> AugNaturalStory()
    {
        R.phase = "aug-natural-story-availability";
        object? story = Reflect.Get(AugMod, "Story");
        if (story == null) { R.Skip("aug.story-normal-route", "Story flow was not installed", false); yield break; }
        Reflect.Call(story, "Update");
        R.environment["augStoryAtEnd"] = Reflect.Json(Reflect.Get(story, "State"));
        R.environment["graylineAtEnd"] = Reflect.Json(Reflect.Get(story, "_graylineState"));
        object? storyState = Reflect.Get(story, "State");
        string e02Choice = storyState == null ? "" : (string?)Reflect.Call(storyState, "Choice", "E02") ?? "";
        object? grayline = Reflect.Get(story, "_graylineState");
        R.environment["graylineRealPurchasePrerequisites"] = new { e02Choice, grayline = Reflect.Json(grayline),
            currentNativeClient = Store.currentClientInstance?.GetClientBlueprint()?.identifier,
            missing = "Actual completed E02 choice -> normal Tuesday appointment -> arrived Grayline native client -> real unowned counter offer and payable price" };
        R.Skip("aug.grayline.real-buyitem-regression", "Current normal fresh Aug fixture has no actual completed E02 choice. Production UpdateGrayline refuses unlock until StoryState.Choice(E02) is nonempty; the normal phone only reports schedule. Date fixtures cannot supply that prerequisite. No legitimate arrived Grayline counter/offer is available for BuyItem, duplicate BuyItemFromTable or bulk-buy-chain payment assertions. Debug/TestVisit, forged choices/receipts and busy simulations are not used.", false);
        R.Skip("aug.story-key-branches", "Natural multivisit dialogue routes remain outside this bounded fresh-start mode", false);
        yield return null;
    }
}
