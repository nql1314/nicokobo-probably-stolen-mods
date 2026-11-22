using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Nicokobo.ContentNativeProbe;

internal sealed partial class ProbeContext
{
    private static readonly string[] EpBlendIds =
    {
        "nutrifruit","coffee_bean","dream_cap","meal_cap","nightmare_cap","water_tablet","nutrient_tablet",
        "feces_small","feces_medium","feces_large","birth_control_pill","fanta_pill","zyanide_pill",
        "dream_dust","nightmare_dust","caffeine_pill","pink_alt_injector"
    };
    private static readonly string[] EpSolubleDefaultIds = EpBlendIds.Where(id => id is not ("nutrifruit" or "coffee_bean" or "dream_cap" or "meal_cap" or "nightmare_cap")).ToArray();
    private readonly JArray _epInputs = new(), _epControls = new(), _epNights = new(), _epVacuum = new(), _epFixtures = new();
    private readonly List<GameItem> _epSafes = new();
    private readonly HashSet<int> _epNaturalManure = new(), _epFood = new();
    private GameItem _epBlender = null!, _epReactor = null!, _epCell = null!, _epCage = null!, _epRat = null!, _epRobot = null!;
    private GameGridInventory _epReactorGrid = null!, _epCageGrid = null!, _epRobotStorage = null!;
    private bool _epCollected, _epRefused;
    private int _epWasteUid;

    // Family routing and process supervision are supplied by the runner.
    internal IEnumerable<object?> NativeExtendedProduction()
    {
        R.phase = "extendedproduction-native-setup";
        GapCheckTagProjectionLong();
        R.environment["extendedProductionScope"] = "Build26317870 normal APPRENTICE.17 registered default blendable IDs,12 default soluble IDs plus the actually soluble native PreBuilt CreateNutrifruitLarge form of the same nutrifruit ID:30 ID/entry cases. Native default blender35% loss and actual soluble Target, no direct conversion callback. One ordinary native rat's actual Poop provenance, real vacuum late-cycle collection, declared native-feces capacity fillers for full-box refusal, generated reactor progress108, then three complete native cycles of unupgraded18 decay ->162 accumulated progress ->one real nuclear_waste and remainder2. Actual electronic upgrade only after the threshold proof. Fixed original UID/ID/count with verified consumption receipts, all36 Parts and value/basis readback, explicit ambient26C persistence inputs followed by all-tag float-bit-exact ES3 and independent original-slot reload. Dynamic trait/size/concentration phenotypes are explicitly outside this finite input-ID denominator.";
        R.environment["extendedProductionInputs"] = _epInputs; R.environment["extendedProductionNights"] = _epNights;
        R.environment["extendedProductionConversionControls"] = _epControls;
        R.environment["extendedProductionVacuum"] = _epVacuum; R.environment["extendedProductionFixtures"] = _epFixtures;
        ForgeRequire("extendedproduction.normal-fresh-APPRENTICE", "Native APPRENTICE D1, no scenario/perks/dev cheats/Steam", new
        { NewGameData.Instance.startType, NewGameData.Instance.startScenarioId, day = StoreStation.GetDayCounter(), Store.playerCash, Store.devMode, steam = SteamManager.Initialized },
            !O.Reload && NewGameData.Instance.startType == NewGameData.StartType.APPRENTICE && string.IsNullOrEmpty(NewGameData.Instance.startScenarioId) &&
            NewGameData.Instance.startingPerks.Count == 0 && StoreStation.GetDayCounter() == 1 && !Store.devMode && !SteamManager.Initialized);
        Reflect.Call(Reflect.Type("SpeedUp.SpeedUpSettings", "SpeedUp"), "RestoreNormalSpeed");
        EpDiscoverDefaultInputs();
        _epBlender = EpBorn("blender", EmporiumEntry.Instance.invElement, "native default blender, no modules and no battery system");
        foreach (string id in EpBlendIds) foreach (object? wait in EpConvert(id, false, false)) yield return wait;
        foreach (string id in EpSolubleDefaultIds) foreach (object? wait in EpConvert(id, true, false)) yield return wait;
        // This actual native helper sets SOLUBLE on a large nutrifruit; the
        // default registered nutrifruit is blendable only. Do not miss the ID.
        foreach (object? wait in EpConvert("nutrifruit", true, true)) yield return wait;
        ForgeRequire("extendedproduction.finite-30-input-entries-complete", 30, _epInputs.Count,
            _epInputs.Count == 30 && _epInputs.All(row => (bool?)row["passed"] == true));
        foreach (object? wait in EpConversionControls()) yield return wait;
        EpSetupReactor();
        _epReactor.ModifyTag("REACTOR_WASTE_PROGRESS_INT", tag => tag.Enable().SetInt(108));
        _epFixtures.Add(JObject.FromObject(new { purpose = "Explicit progress108 threshold input; three real18-decay cycles must create waste and remainder2", uid = _epReactor.uniqueId }));
        EpSetupVacuum();
        using (var observer = new EpObservation(this))
        {
            R.environment["extendedProductionNativeObservation"] = observer.Evidence;
            for (int number = 1; number <= 3; number++)
            {
                int day = StoreStation.GetDayCounter();
                R.phase = "extendedproduction.real-night-" + number;
                EpRepairReactor(number);
                EpPrepareFood();
                if (number == 2) EpFillVacuum();
                GapAudit("extendedproduction.fixed-original-UIDs.before-night-" + number);
                int integrity = ReactorHelper.GetIntegrity(_epReactor), progress = _epReactor.GetTagReadonly("REACTOR_WASTE_PROGRESS_INT").GetInt(), energy = Energy(_epCell);
                int[] wastes = Store.FindAllItem().Where(i => i.identifier == "nuclear_waste").Select(i => i.uniqueId).ToArray();
                int reactorCalls = observer.ReactorCalls;
                observer.Night = number;
                using (var night = new NightObservation(this, Array.Empty<RecipeFixture>(), day))
                {
                    _epNights.Add(new JObject { ["number"] = number, ["beforeIntegrity"] = integrity, ["beforeWasteProgress"] = progress,
                        ["beforeEnergy"] = energy, ["nativeNight"] = night.Evidence });
                    try { foreach (object? wait in NightDay(day, "extendedproduction.N" + number, night, ensureInsurance: true)) yield return wait; }
                    finally { observer.Night = 0; }
                }
                int afterIntegrity = Math.Max(0, integrity - 18), expectedWaste = (progress + integrity - afterIntegrity) / 160;
                int expectedProgress = (progress + integrity - afterIntegrity) % 160;
                var newWaste = Store.FindAllItem().Where(i => i.identifier == "nuclear_waste" && !wastes.Contains(i.uniqueId)).ToArray();
                ForgeRequire("extendedproduction.reactor-real-night-" + number, new
                { integrity = afterIntegrity, progress = expectedProgress, charge = afterIntegrity > 25 ? 1 : 0, energy = energy + (afterIntegrity > 25 ? 1 : 0), newWaste = expectedWaste, reactorCalls = 1 }, new
                { integrity = ReactorHelper.GetIntegrity(_epReactor), progress = _epReactor.GetTagReadonly("REACTOR_WASTE_PROGRESS_INT").GetInt(), energy = Energy(_epCell),
                    newWaste = newWaste.Select(GapItem).ToArray(), reactorCalls = observer.ReactorCalls - reactorCalls, observer.Errors },
                    StoreStation.GetDayCounter() == day + 1 && ReactorHelper.GetUpgrades(_epReactor) == 0 &&
                    ReactorHelper.GetIntegrity(_epReactor) == afterIntegrity && _epReactor.GetTagReadonly("REACTOR_WASTE_PROGRESS_INT").GetInt() == expectedProgress &&
                    Energy(_epCell) == energy + (afterIntegrity > 25 ? 1 : 0) && newWaste.Length == expectedWaste &&
                    observer.ReactorCalls - reactorCalls == 1 && observer.Errors.Count == 0);
                foreach (var waste in newWaste)
                {
                    ForgeRequire("extendedproduction.actual-produced-waste", "One original owned count1 nuclear waste from the real160 crossing", GapItem(waste),
                        number == 3 && waste.unitCount == 1 && GeneralHelper.IsItemOwned(waste));
                    _epWasteUid = waste.uniqueId; GapCommit(waste, "actual native ReactorHelper.OnNight160-threshold output, not a factory reward");
                    EpPack(waste);
                }
                EpReadVacuumNight(observer, number);
                EpReadFeeding(observer, number);
                GapAudit("extendedproduction.fixed-original-UIDs.after-night-" + number);
                if (number == 2)
                    foreach (var item in _epRobotStorage.childItems.ToArray()) EpPack(item);
            }
            ForgeRequire("extendedproduction.reactor160-and-vacuum-required", new { realNights = 3, nuclearWaste = 1, remainder = 2, collected = true, refused = true }, new
            { nights = _epNights.Count, _epWasteUid, progress = _epReactor.GetTagReadonly("REACTOR_WASTE_PROGRESS_INT").GetInt(), _epCollected, _epRefused },
                _epNights.Count == 3 && _epWasteUid > 0 && _epReactor.GetTagReadonly("REACTOR_WASTE_PROGRESS_INT").GetInt() == 2 && _epCollected && _epRefused);
        }
        // Exercise a genuine upgrade after, so it cannot alter the independent
        // 108+3*18 progress denominator that produced the first waste.
        var electronic = EpBorn("common_electronic", EpSafeGrid(), "real reactor upgrade input after actual waste proof");
        int electronicUid = electronic.uniqueId, cash = Store.playerCash;
        ForgeRequire("extendedproduction.real-upgrade-guards", true, new { may = electronic.MayTarget(_epReactor), can = electronic.CanTarget(_epReactor) },
            electronic.MayTarget(_epReactor) && electronic.CanTarget(_epReactor) && ForgeOwnedUnits(electronicUid) == 1);
        electronic.Target(_epReactor);
        ForgeRequire("extendedproduction.real-electronic-upgrade-consumed", new { upgrades = 1, consumed = 0, decay = 17, cash }, new
        { upgrades = ReactorHelper.GetUpgrades(_epReactor), consumed = ForgeOwnedUnits(electronicUid), decay = ReactorHelper.GetDecay(_epReactor), cash = Store.playerCash },
            ReactorHelper.GetUpgrades(_epReactor) == 1 && ReactorHelper.GetDecay(_epReactor) == 17 && ForgeOwnedUnits(electronicUid) == 0 && Store.playerCash == cash);
        GapRetire(electronicUid, "actual owned electronic consumed by native reactor Target, upgrades0->1 after the completed160-threshold proof");
        // Persistence is a separate fixture stage. Conversion quantities/value
        // have already been asserted; prepare an exact ambient input rather
        // than waiting for asymptotic temperature convergence.
        foreach (var pair in _gapCommitments.Where(pair => !_gapConsumed.ContainsKey(pair.Key)))
        {
            var item = GapFind(pair.Key, pair.Value.Id);
            if (!item.IsTag("CONTAINER_TEMP_TAG")) continue;
            var parts = VcParts(item); long value = item.unitValue;
            double before = item.GetTagReadonly("CONTAINER_TEMP_TAG").GetDouble();
            item.ModifyTag("CONTAINER_TEMP_TAG", tag => tag.Enable().SetDouble(26));
            _epFixtures.Add(JObject.FromObject(new { purpose = "Explicit 26C persistence input after real conversion/night assertions; no parts/value change", uid = item.uniqueId, before, after = 26.0 }));
            ForgeRequire("extendedproduction.persistence-temperature-fixture-keeps-resources." + item.uniqueId, parts, VcParts(item),
                JToken.DeepEquals(parts, VcParts(item)) && item.unitValue == value && item.GetTagReadonly("CONTAINER_TEMP_TAG").GetDouble() == 26);
        }
        foreach (object? wait in EpSave()) yield return wait;
        R.Skip("extendedproduction.dynamic-plant-and-size-phenotypes", "Finite native registered/default input ID entries plus the actual soluble large nutrifruit are covered. Arbitrary PlantTrait-added chemistry, genetic yields, concentrations and every same-ID size/purity phenotype are not exhaustively generated. Their eligibility still follows actual BLENDABLE/SOLUBLE flags; no flags are assigned by this probe.", false);
        R.Skip("extendedproduction.vacuum-natural-medium-large-and-incineration", "Ordinary rat-produced manure collection and full-box rejection are required. The30 input routes use declared factory feces as raw ingredients; they do not prove a naturally >=600g/1000g animal produced medium/large manure. Incinerator destruction has its separate native family; no mature-animal/age/weight fixture is substituted.", false);
    }

    internal IEnumerable<object?> NativeExtendedProductionReload(JObject baseline)
    {
        R.phase = "extendedproduction.independent-native-ES3-load";
        GapCheckTagProjectionLong();
        ForgeRequire("extendedproduction.reload-original-process-slot", "Independent original-slot load", new { O.Reload, O.UseCopy }, O.Reload && !O.UseCopy);
        var saved = baseline["persistence"]?["extendedProduction"] as JObject ?? throw new InvalidOperationException("Missing extended-production baseline");
        foreach (var row in (JArray)saved["commitments"]!)
        {
            int uid = (int)row["uid"]!; if (_gapCommitments.ContainsKey(uid)) throw new InvalidOperationException("Duplicate promised UID in baseline");
            _gapCommitments.Add(uid, ((string)row["id"]!, (int)row["count"]!, (string)row["purpose"]!));
            string reason = (string?)row["consumedByVerifiedNativeAction"] ?? ""; if (reason.Length != 0) _gapConsumed.Add(uid, reason);
        }
        _epReactor = GapFind((int)saved["reactorUid"]!, "makeshift_reactor"); _epCell = GapFind((int)saved["cellUid"]!, "energy_credit");
        _epRobot = GapFind((int)saved["robotUid"]!, "vacuum_robot"); _epRat = GapFind((int)saved["ratUid"]!, "rat");
        _epCage = GapFind((int)saved["cageUid"]!, "animal_cage"); _epBlender = GapFind((int)saved["blenderUid"]!, "blender");
        _epCageGrid = AnimalCage.GetCageInventory(_epCage);
        _epRobotStorage = (GameGridInventory)MachineHelper.GetBodyStorage(_epRobot, 1);
        GapAudit("extendedproduction.reload-fixed-original-denominator");
        var actual = EpSnapshot();
        ForgeRequire("extendedproduction.reload-all-UIDs-raw-tags-float-bits-36parts-value-basis", saved["snapshot"], actual, JToken.DeepEquals(GapReadbackProjection(saved["snapshot"]!), GapReadbackProjection(actual)),
            "Actual native LoadGame/ES3. No consumed UID or missing product is filtered, no floating tolerance, no extra night, repair, output, manure source or converted material is recreated.");
        R.persistence["extendedProductionReload"] = actual;
        yield return null;
    }

    private void EpDiscoverDefaultInputs()
    {
        HashSet<string> blend = new(), soluble = new();
        foreach (string directory in new[] { "AmenitiesItemDirectory", "MedsItemDirectory", "HydroponicDirectory", "HusbandryDirectory" })
            foreach (string id in DirectoryMaster.GetIdentifierList<GameItem>(directory).Distinct())
            {
                GameItem? sample = null;
                try
                {
                    ForgeRequire("extendedproduction.native-factory-owner." + id, "Actual vanilla registered factory", DirectoryMaster.GetFactory<GameItem>(id)?.Method.DeclaringType?.Assembly.GetName().Name,
                        DirectoryMaster.GetFactory<GameItem>(id)?.Method.DeclaringType?.Assembly == typeof(GameItem).Assembly);
                    sample = DirectoryMaster.Create<GameItem>(id);
                    if (sample.IsTag(ChemicalHelper.BLENDABLE_TAG)) blend.Add(id);
                    if (sample.IsTag(ChemicalHelper.SOLUBLE_TAG)) soluble.Add(id);
                }
                finally { Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(sample!); }
            }
        ForgeRequire("extendedproduction.default-registration-denominator", new { blend = EpBlendIds.OrderBy(x => x).ToArray(), soluble = EpSolubleDefaultIds.OrderBy(x => x).ToArray() }, new
        { blend = blend.OrderBy(x => x).ToArray(), soluble = soluble.OrderBy(x => x).ToArray() },
            blend.SetEquals(EpBlendIds) && soluble.SetEquals(EpSolubleDefaultIds));
        ForgeRequire("extendedproduction.36-native-liquid-species", VcSpecies, Liquid.Liquids.Select(l => l.identifier).ToArray(),
            VcSpecies.Length == 36 && Liquid.Liquids.Select(l => l.identifier).OrderBy(x => x).SequenceEqual(VcSpecies.OrderBy(x => x)));
    }
    private IEnumerable<object?> EpConvert(string id, bool soluble, bool largeNutrifruit)
    {
        string key = (soluble ? "dissolve." : "blend.") + id + (largeNutrifruit ? ".native-large-form" : "");
        R.phase = "extendedproduction." + key;
        var row = (MachineryPanelRow)_epBlender.contentWindow.childElement;
        var input = (GameGridInventory)MachineHelper.GetBodyStorage(_epBlender, 1);
        var outputSlot = (GameSlotInventory)MachineHelper.GetBodyStorage(_epBlender, 2);
        var button = (MachineryButton)row.GetPanel("control").content;
        GameItem raw = largeNutrifruit ? PreBuiltItemHelper.CreateNutrifruitLarge() : Spawn(id);
        _epFixtures.Add(new JObject { ["purpose"] = "actual native raw input preparation, not proof that this raw material was economically acquired", ["id"] = id, ["uid"] = raw.uniqueId, ["largeNativeForm"] = largeNutrifruit });
        Place(soluble ? EpRoomFor(raw) : input, raw, "extendedproduction.raw." + key);
        GapCommit(raw, "original raw material before native " + key);
        var vessel = EpBorn(id == "nutrifruit" ? "bottled_water" : "100ml_cylinder", outputSlot, "empty sufficient-capacity native receiver for " + key);
        JObject parts = VcParts(raw); int uid = raw.uniqueId; JObject resources = EpResources();
        var expected = new JObject(VcSpecies.OrderBy(s => s).Select(s => new JProperty(s,
            soluble ? (int)parts[s]! : (int)Math.Round((int)parts[s]! * .65, MidpointRounding.AwayFromZero))));
        ForgeRequire("extendedproduction.normal-input." + key, "Count1 original eligible material, empty sufficient-capacity original receiver, default35% native blender", new
        { raw = GapItem(raw), parts, capacity = WaterHelper.GetFreeCapacity(vessel), loss = MachineBlender.GetLoss(_epBlender) },
            raw.unitCount == 1 && GeneralHelper.IsItemOwned(raw) && raw.IsTag(soluble ? ChemicalHelper.SOLUBLE_TAG : ChemicalHelper.BLENDABLE_TAG) &&
            WaterHelper.GetTotalVolume(vessel) == 0 && WaterHelper.GetFreeCapacity(vessel) >= parts.Properties().Sum(p => (int)p.Value) &&
            MachineBlender.GetLoss(_epBlender) == 35 && ForgeOwnedUnits(uid) == 1);
        _epBlender.contentWindow.Show();
        if (soluble)
        {
            ForgeRequire("extendedproduction.actual-soluble-target-guards." + key, true, new { may = raw.MayTarget(vessel), can = raw.CanTarget(vessel) }, raw.MayTarget(vessel) && raw.CanTarget(vessel));
            raw.Target(vessel);
        }
        else
        {
            EpClickBlend(button, key);
        }
        // Same-frame comparison precedes genuine ambient reaction ticks. The
        // original receiver UID remains live through subsequent real nights.
        bool valid = ForgeOwnedUnits(uid) == 0 && JToken.DeepEquals(expected, VcParts(vessel)) && JToken.DeepEquals(resources, EpResources()) &&
            ReferenceEquals(vessel.parentInventory, outputSlot) && GeneralHelper.IsItemOwned(vessel);
        _epInputs.Add(new JObject { ["entry"] = key, ["rawUid"] = uid, ["receiverUid"] = vessel.uniqueId, ["before36"] = parts, ["expected36"] = expected,
            ["actual36"] = VcParts(vessel), ["actualValueBasis"] = FlLiquid(vessel), ["passed"] = valid });
        ForgeRequire("extendedproduction.actual-conversion-and-consumption." + key, new { expected, consumed = 1, noCashOrEnergy = true }, _epInputs.Last, valid);
        GapRetire(uid, "verified real native " + key + " material consumption, exact original36-component invoice");
        EpPack(vessel); _epBlender.CloseContentWindow();
        yield return null;
    }
    private IEnumerable<object?> EpConversionControls()
    {
        R.phase = "extendedproduction.actual-conversion-refusal-controls";
        var input = (GameGridInventory)MachineHelper.GetBodyStorage(_epBlender, 1);
        var slot = (GameSlotInventory)MachineHelper.GetBodyStorage(_epBlender, 2);
        var button = (MachineryButton)((MachineryPanelRow)_epBlender.contentWindow.childElement).GetPanel("control").content;
        _epBlender.contentWindow.Show();
        var raw = EpBorn("birth_control_pill", input, "one original eligible control input; refused controls must not consume it");
        ForgeRequire("extendedproduction.control-no-container-precondition", "Eligible input and actual missing receiver", new { count = input.childItems.Count, receiver = slot.childItem?.uniqueId },
            input.childItems.Count == 1 && slot.childItem == null && raw.IsTag(ChemicalHelper.BLENDABLE_TAG));
        JObject before = EpControlLedger(_epBlender, raw);
        EpClickBlend(button, "control.no-container");
        EpRefusal("blender.no-container", before, EpControlLedger(_epBlender, raw));

        var full = EpBorn("100ml_cylinder", slot, "declared full-capacity pure-water negative fixture, not manufactured product");
        WaterHelper.AddLiquid(full, "water", WaterHelper.GetFreeCapacity(full));
        _epFixtures.Add(new JObject { ["purpose"] = "native AddLiquid water capacity preparation; no produced-liquid claim", ["uid"] = full.uniqueId, ["parts"] = VcParts(full) });
        ForgeRequire("extendedproduction.control-full-blender-precondition", "Actual receiver is full and original eligible material remains", new { full = WaterHelper.IsFull(full), rawOwned = ForgeOwnedUnits(raw.uniqueId) },
            WaterHelper.IsFull(full) && WaterHelper.GetFreeCapacity(full) == 0 && ForgeOwnedUnits(raw.uniqueId) == 1);
        before = EpControlLedger(_epBlender, raw, full);
        EpClickBlend(button, "control.full-container");
        EpRefusal("blender.full-container", before, EpControlLedger(_epBlender, raw, full));

        Place(EpRoomFor(raw), raw, "extendedproduction.retain-refused-original-raw"); EpPack(full);
        var invalid = EpBorn("scrap_metal", input, "actual ordinary non-BLENDABLE input control, kept original after refusal");
        var empty = EpBorn("100ml_cylinder", slot, "actual empty receiver for nonblendable-input control");
        ForgeRequire("extendedproduction.control-ineligible-precondition", "Exactly one nonblendable input with an actual empty available receiver", new
        { blendable = invalid.IsTag(ChemicalHelper.BLENDABLE_TAG), count = input.childItems.Count, empty = WaterHelper.IsEmpty(empty) },
            !invalid.IsTag(ChemicalHelper.BLENDABLE_TAG) && input.childItems.Count == 1 && WaterHelper.IsEmpty(empty));
        before = EpControlLedger(_epBlender, invalid, empty);
        EpClickBlend(button, "control.nonblendable-input");
        EpRefusal("blender.ineligible-input", before, EpControlLedger(_epBlender, invalid, empty));
        Place(EpRoomFor(invalid), invalid, "extendedproduction.retain-refused-nonblendable"); EpPack(empty);

        before = EpControlLedger(raw, invalid);
        bool may = raw.MayTarget(invalid), can = raw.CanTarget(invalid);
        ForgeRequire("extendedproduction.soluble-wrong-target-real-guards", "Actual non-liquid target rejected by both native guards", new { may, can },
            !may && !can && raw.IsTag(ChemicalHelper.SOLUBLE_TAG) && !invalid.IsTag("LIQUID_CONTAINER_TAG"));
        EpRefusal("soluble.non-liquid-target", before, EpControlLedger(raw, invalid));

        // SOLUBLE full-container behavior is replacement, not refusal.
        // For a full pure-water receiver, native removal of total raw volume
        // followed by raw addition has a simple independent exact oracle.
        JObject material = VcParts(raw); int amount = material.Properties().Sum(p => (int)p.Value), capacity = WaterHelper.GetTotalVolume(full);
        ForgeRequire("extendedproduction.soluble-full-replacement-precondition", "Full pure-water fixture, actual soluble guards allow original raw", new { amount, capacity, may = raw.MayTarget(full), can = raw.CanTarget(full), parts = VcParts(full) },
            WaterHelper.IsFull(full) && (int)VcParts(full)["water"]! == capacity && amount > 0 && amount <= capacity &&
            raw.MayTarget(full) && raw.CanTarget(full) && ForgeOwnedUnits(raw.uniqueId) == 1);
        JObject expected = new(VcSpecies.OrderBy(s => s).Select(s => new JProperty(s, (s == "water" ? capacity - amount : 0) + (int)material[s]!)));
        JObject resources = EpResources(); raw.Target(full);
        bool replaced = ForgeOwnedUnits(raw.uniqueId) == 0 && JToken.DeepEquals(expected, VcParts(full)) && JToken.DeepEquals(resources, EpResources()) && WaterHelper.IsFull(full);
        _epControls.Add(new JObject { ["entry"] = "soluble.full-is-real-replacement", ["rawUid"] = raw.uniqueId, ["receiverUid"] = full.uniqueId,
            ["expected36"] = expected, ["actual36"] = VcParts(full), ["passed"] = replaced });
        ForgeRequire("extendedproduction.soluble-full-actual-replacement", expected, _epControls.Last, replaced);
        GapRetire(raw.uniqueId, "real full-container SOLUBLE replacement consumed original control raw, exact pure-water removal plus36-component addition proved");
        ForgeRequire("extendedproduction.fixed-conversion-control-matrix", "Four real refusals plus native full-soluble replacement", _epControls,
            _epControls.Count == 5 && _epControls.All(row => (bool?)row["passed"] == true));
        _epBlender.CloseContentWindow(); GapAudit("extendedproduction.after-real-conversion-controls");
        yield return null;
    }
    private void EpClickBlend(MachineryButton button, string key)
    {
        bool render = button.handler.gameObject.activeInHierarchy && ReferenceEquals(button.handler.element, button);
        ForgeRequire("extendedproduction.actual-blend-button." + key, "Active matching native button, no hidden/disabled/hold bypass", new
        { render, button.isHidden, button.isDisabled, button.requiresHold },
            _epBlender.contentWindow.IsVisible() && render && !button.isDisabled && !button.isHidden && !button.requiresHold);
        bool selected = button.Select(); ForgeRequire("extendedproduction.blend-dispatched." + key, true, selected, selected);
    }
    private JObject EpControlLedger(params GameItem[] items) => new()
    {
        ["resources"] = EpResources(), ["items"] = new JArray(items.OrderBy(i => i.uniqueId).Select(i => new JObject
        { ["state"] = GapItem(i), ["parts36"] = VcParts(i), ["ownedUnits"] = ForgeOwnedUnits(i.uniqueId), ["parent"] = i.parentInventory?.identifier }))
    };
    private void EpRefusal(string key, JObject before, JObject after)
    {
        bool same = JToken.DeepEquals(before, after);
        _epControls.Add(new JObject { ["entry"] = key, ["before"] = before, ["after"] = after, ["passed"] = same });
        ForgeRequire("extendedproduction.actual-refusal-unchanged." + key, before, after, same,
            "Real active native Blend button or native MayTarget/CanTarget guards; original UID/count/ownership, all raw tags/36parts and cash/energy remain identical. Full SOLUBLE is tested separately as replacement.");
    }
    private void EpSetupReactor()
    {
        var safe = EpNewSafe("reactor original native adjacency stage");
        _epReactorGrid = (GameGridInventory)safe.contentWindow.childElement;
        _epReactor = EpBorn("makeshift_reactor", _epReactorGrid, "unmodified native100 integrity/0 upgrades/0 waste reactor");
        _epCell = PreBuiltItemHelper.EnergyCredit(0);
        Place(_epReactorGrid, _epCell, "extendedproduction.original-empty-adjacent-cell"); GapCommit(_epCell, "original declared native empty100-capacity battery receiving actual reactor charge");
        ForgeRequire("extendedproduction.reactor-native-geometry", "Same actual8x8 grid, original reactor/cell adjacent by native geometry", new
        { reactor = GapItem(_epReactor), cell = GapItem(_epCell) }, NdxAt(_epReactorGrid, _epReactor, 0, 0) &&
            NdxAt(_epReactorGrid, _epCell, _epReactor.modifiedShape.width, 0) && _epReactor.FindAdjacentType<GameItem>().Contains(_epCell) &&
            ReactorHelper.GetIntegrity(_epReactor) == 100 && ReactorHelper.GetMaxIntegrity(_epReactor) == 100 &&
            ReactorHelper.GetUpgrades(_epReactor) == 0 && _epReactor.GetTagReadonly("REACTOR_WASTE_PROGRESS_INT").GetInt() == 0 &&
            Energy(_epCell) == 0 && _epReactor.IsRootedInSmugglerBay() && _epCell.IsRootedInSmugglerBay());
    }
    private void EpRepairReactor(int night)
    {
        int integrity = ReactorHelper.GetIntegrity(_epReactor);
        if (integrity - 18 > 25) return;
        var scrap = EpBorn("scrap_metal", EpSafeGrid(), "actual native reactor repair input before night" + night);
        int uid = scrap.uniqueId, cash = Store.playerCash;
        ForgeRequire("extendedproduction.real-scrap-target-guards.N" + night, true, new { may = scrap.MayTarget(_epReactor), can = scrap.CanTarget(_epReactor) },
            scrap.MayTarget(_epReactor) && scrap.CanTarget(_epReactor) && ForgeOwnedUnits(uid) == 1);
        scrap.Target(_epReactor);
        ForgeRequire("extendedproduction.real-scrap-repair.N" + night, new { integrity = Math.Min(100, integrity + 60), consumed = 0, cash }, new
        { integrity = ReactorHelper.GetIntegrity(_epReactor), consumed = ForgeOwnedUnits(uid), cash = Store.playerCash },
            ReactorHelper.GetIntegrity(_epReactor) == Math.Min(100, integrity + 60) && ForgeOwnedUnits(uid) == 0 && Store.playerCash == cash);
        GapRetire(uid, "actual scrap_metal Target repair+" + (ReactorHelper.GetIntegrity(_epReactor) - integrity) + " before real night" + night);
    }
    private void EpSetupVacuum()
    {
        _epCage = EpBorn("animal_cage", EmporiumEntry.Instance.invElement, "ordinary native unmodified cage");
        _epCageGrid = AnimalCage.GetCageInventory(_epCage);
        _epRobot = EpBorn("vacuum_robot", _epCageGrid, "ordinary native collector, no incinerator upgrade or fake battery");
        _epRobotStorage = (GameGridInventory)MachineHelper.GetBodyStorage(_epRobot, 1);
        _epRat = EpBorn("rat", _epCageGrid, "ordinary native5g age0 factory rat with unmodified genetics, not a mature-manure-output fixture");
        ForgeRequire("extendedproduction.real-default-vacuum-and-rat", "Age0 live rat, empty8x4 storage, incinerator off, no direct battery fixture", new
        { age = _epRat.GetTagReadonly("ANIMAL_AGE_TAG").GetInt(), dead = HusbandryHelper.IsAnimalDead(_epRat), incinerator = HusbandryHelper.IsIncineratorOn(_epRobot), count = _epRobotStorage.childItems.Count },
            _epRat.GetTagReadonly("ANIMAL_AGE_TAG").GetInt() == 0 && !HusbandryHelper.IsAnimalDead(_epRat) &&
            !HusbandryHelper.IsIncineratorOn(_epRobot) && _epRobotStorage.childItems.Count == 0 &&
            MachineHelper.GetBatterySlot(_epRobot).childItem == null);
    }
    private void EpPrepareFood()
    {
        if (_epCageGrid.childItems.Any(i => i.IsTag("CALORIE_VALUE_TAG"))) return;
        var food = EpBorn("wine_berry", _epCageGrid, "actual native animal-feed input, only observed TryFeed disappearance may retire it");
        _epFood.Add(food.uniqueId);
    }
    private void EpFillVacuum()
    {
        ForgeRequire("extendedproduction.positive-collection-before-full-control", true, _epCollected, _epCollected);
        for (int index = 0; index < 64; index++)
        {
            var filler = Spawn("feces_small");
            var slot = _epRobotStorage.TryFindOneValidInventorySlot(filler);
            if (slot?.IsValid() != true)
            { Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(filler); break; }
            Place(_epRobotStorage, filler, "extendedproduction.declared-capacity-filler-" + index);
            GapCommit(filler, "declared native feces capacity-only negative fixture; not natural production provenance");
            _epFixtures.Add(new JObject { ["purpose"] = "factory capacity-only filler, never counted as natural rat manure", ["uid"] = filler.uniqueId });
        }
        var probe = Spawn("feces_small");
        bool rejected = _epRobotStorage.TryFindOneValidInventorySlot(probe)?.IsValid() != true;
        Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(probe);
        ForgeRequire("extendedproduction.actual-small-manure-full-box", "Real native storage cannot admit another ordinary small manure", new { rejected, count = _epRobotStorage.childItems.Count }, rejected);
    }
    private void EpReadVacuumNight(EpObservation observer, int night)
    {
        var rows = observer.Poops.Where(r => (int?)r["night"] == night && (int?)r["growth"] >= 30).ToArray();
        if (night <= 2)
            ForgeRequire("extendedproduction.actual-growth-and-manure-source.N" + night, "Actual ordinary live rat native growth>=30 produced exactly one owned manure UID", rows,
                rows.Length == 1 && (JArray)rows[0]["newUids"]! is { Count: 1 } && (bool?)rows[0]["originalRan"] == true);
        foreach (var row in rows)
            foreach (int uid in ((JArray)row["newUids"]!).Values<int>())
            {
                var item = Store.FindAllItem().Single(i => i.uniqueId == uid);
                ForgeRequire("extendedproduction.native-manure-identity." + uid, (string)row["expectedId"]!, item.identifier,
                    item.identifier == (string)row["expectedId"]! && GeneralHelper.IsItemOwned(item) && item.unitCount == 1);
                _epNaturalManure.Add(uid); GapCommit(item, "real ordinary rat Poop during observed native EndNight" + night);
                var gates = observer.Admissions.Where(g => (int?)g["night"] == night && (int?)g["uid"] == uid).ToArray();
                if (night == 1)
                {
                    bool pass = ReferenceEquals(item.parentInventory, _epRobotStorage) && gates.Any(g => (bool?)g["accepted"] == true && (bool?)g["originalRan"] == true);
                    ForgeRequire("extendedproduction.real-vacuum-collection-same-UID", "Real source UID moved from actual cage into actual ordinary collector after native CanAccept", new { uid, parent = item.parentInventory?.identifier, gates }, pass);
                    _epCollected = pass;
                }
                if (night == 2)
                {
                    bool pass = ReferenceEquals(item.parentInventory, _epCageGrid) && gates.Any(g => (bool?)g["accepted"] == false && (bool?)g["originalRan"] == true);
                    ForgeRequire("extendedproduction.real-full-box-collector-refusal", "New real source UID remains in cage; native collector attempted actual CanAccept and refused full box", new { uid, parent = item.parentInventory?.identifier, gates }, pass);
                    _epRefused = pass;
                }
                _epVacuum.Add(new JObject { ["uid"] = uid, ["night"] = night, ["source"] = row.DeepClone(), ["gates"] = new JArray(gates.Select(g => g.DeepClone())), ["collected"] = ReferenceEquals(item.parentInventory, _epRobotStorage) });
            }
        ForgeRequire("extendedproduction.collector-no-fake-power.N" + night, "Native unpowered ordinary robot, unchanged original identity, no incineration", GapItem(_epRobot),
            MachineHelper.GetBatterySlot(_epRobot).childItem == null && !HusbandryHelper.IsIncineratorOn(_epRobot));
    }
    private void EpReadFeeding(EpObservation observer, int night)
    {
        foreach (int uid in _epFood.Where(uid => !_gapConsumed.ContainsKey(uid)).ToArray())
        {
            if (ForgeOwnedUnits(uid) != 0) continue;
            bool consumed = observer.Feeds.Any(row => (int?)row["night"] == night && (bool?)row["originalRan"] == true && ((JArray)row["consumedUids"]!).Values<int>().Contains(uid));
            ForgeRequire("extendedproduction.real-feed-consumption." + uid, "Native TryFeed observed the original calorie input disappear during this real night", observer.Feeds, consumed);
            GapRetire(uid, "actual observed native TryFeed calorie consumption during real night" + night);
        }
    }

    private GameItem EpBorn(string id, GameInventory inventory, string purpose)
    {
        var item = Spawn(id);
        if (_epSafes.Any(s => ReferenceEquals(s.contentWindow.childElement, inventory)) && inventory.TryFindOneValidInventorySlot(item)?.IsValid() != true)
            inventory = EpRoomFor(item);
        Place(inventory, item, "extendedproduction." + id + "." + item.uniqueId); GapCommit(item, purpose);
        _epFixtures.Add(new JObject { ["purpose"] = purpose, ["item"] = GapItem(item) }); return item;
    }
    private GameItem EpNewSafe(string purpose)
    {
        var item = Spawn("smuggler_bay_mod"); Place(EmporiumEntry.Instance.invElement, item, "extendedproduction.safe-" + _epSafes.Count); _epSafes.Add(item);
        GapCommit(item, "actual native8x8 safe storage preparation: " + purpose); return item;
    }
    private GameInventory EpSafeGrid() => _epSafes.Count == 0 ? (GameInventory)EpNewSafe("native input/output proof retention").contentWindow.childElement : (GameInventory)_epSafes[0].contentWindow.childElement;
    private GameInventory EpRoomFor(GameItem item)
    {
        foreach (var safe in _epSafes)
        {
            var grid = (GameInventory)safe.contentWindow.childElement;
            if (ReferenceEquals(grid, _epReactorGrid)) continue;
            if (grid.TryFindOneValidInventorySlot(item)?.IsValid() == true) return grid;
        }
        ForgeRequire("extendedproduction.bounded-native-input-room", "At most10 actual safe bays, no artificial grid", _epSafes.Count, _epSafes.Count < 10);
        return (GameInventory)EpNewSafe("additional lawful actual native input/output capacity").contentWindow.childElement;
    }
    private void EpPack(GameItem item)
    {
        foreach (var safe in _epSafes)
        {
            var inventory = (GameInventory)safe.contentWindow.childElement;
            if (ReferenceEquals(inventory, _epReactorGrid) && item.identifier != "nuclear_waste") continue;
            if (inventory.TryFindOneValidInventorySlot(item)?.IsValid() != true) continue;
            Place(inventory, item, "extendedproduction.retain-original-output-" + item.uniqueId);
            ForgeRequire("extendedproduction.native-output-storage." + item.uniqueId, "Same original UID rooted in an actual smuggler bay", item.IsRootedInSmugglerBay(), item.IsRootedInSmugglerBay()); return;
        }
        ForgeRequire("extendedproduction.bounded-native-proof-storage", "At most10 actual safe bays; no resized or fabricated inventory", _epSafes.Count, _epSafes.Count < 10);
        var next = EpNewSafe("additional actual output capacity");
        Place((GameInventory)next.contentWindow.childElement, item, "extendedproduction.new-safe-output-" + item.uniqueId);
    }
    private JObject EpResources() => new() { ["cash"] = Store.playerCash, ["energy"] = new JArray(Store.FindAllItem().Where(i => i.IsTag("power_source_item")).OrderBy(i => i.uniqueId)
        .Select(i => new JObject { ["uid"] = i.uniqueId, ["energy"] = Energy(i) })) };
    private JObject EpSnapshot() => new()
    {
        ["run"] = Store.runID, ["slot"] = Store.saveSlotId, ["day"] = StoreStation.GetDayCounter(), ["cash"] = Store.playerCash,
        ["proofs"] = GapLiveProofs(), ["commitments"] = GapCommitmentSnapshot(),
        ["liquids"] = new JArray(_gapCommitments.Where(p => !_gapConsumed.ContainsKey(p.Key)).OrderBy(p => p.Key)
            .Select(p => Store.FindAllItem().Single(i => i.uniqueId == p.Key)).Where(i => i.IsTag("LIQUID_CONTAINER_TAG"))
            .Select(i => new JObject { ["uid"] = i.uniqueId, ["ledger36ValueBasis"] = FlLiquid(i) })),
        ["reactor"] = GapItem(_epReactor), ["cell"] = GapItem(_epCell), ["rat"] = GapItem(_epRat), ["robot"] = GapItem(_epRobot)
    };
    private IEnumerable<object?> EpWaitForNaturalStableEvidence()
    {
        R.phase = "extendedproduction.native-output-stability-before-strict-ES3";
        float deadline = Time.realtimeSinceStartup + 60, next = Time.realtimeSinceStartup + 1.2f;
        JArray previous = GapLiveProofs(); int stable = 0;
        while (stable < 3)
        {
            if (Time.realtimeSinceStartup >= deadline)
                ForgeRequire("extendedproduction.natural-output-stability", "Three unchanged complete native proof snapshots1.2s apart within60s; no reaction/temperature callback disabled", new { stable, previous, actual = GapLiveProofs() }, false);
            if (Time.realtimeSinceStartup < next) { yield return null; continue; }
            JArray current = GapLiveProofs(); stable = JToken.DeepEquals(previous, current) ? stable + 1 : 0;
            previous = current; next = Time.realtimeSinceStartup + 1.2f;
            yield return null;
        }
        ForgeRequire("extendedproduction.natural-output-stability", "Three unchanged complete native proof snapshots1.2s apart before strict save", stable, stable == 3,
            "Original produced receivers remain live with their real36 Parts and native ambient/reaction callbacks. No drain, parts/temperature writes, timer replacement or float tolerance is used. Persistent reactivity is a required blocker, not a silent projection.");
    }
    private IEnumerable<object?> EpSave()
    {
        GapAudit("extendedproduction.before-real-save");
        Store.SaveGame(); string path = SaveFiles.AbsolutePath(SaveFiles.Slot(Store.saveSlotId)); var settings = new ES3Settings(path, ES3.Location.File);
        var saved = (PlayerStore)Reflect.Call(typeof(SaveFiles), "ReadStoreForPreview", settings)!;
        ForgeRequire("extendedproduction.actual-ES3-carrier", "Native actual run/slot/cash/day", new { saved.runID, saved.saveSlotId, saved.playerCash, day = ES3.Load<StoreStation>("storeStation", settings).dayCounter },
            saved.runID == Store.runID && saved.saveSlotId == Store.saveSlotId && saved.playerCash == Store.playerCash &&
            ES3.Load<StoreStation>("storeStation", settings).dayCounter == StoreStation.GetDayCounter());
        var nodes = new[] { saved.mainInvJSON, saved.backInvJSON }.SelectMany(json => JsonUtility.FromJson<SaveState>(json).saveItems).ToArray();
        var disk = new JArray();
        foreach (var pair in _gapCommitments.OrderBy(p => p.Key))
        {
            var matches = nodes.Where(n => n.uniqueId == pair.Key).ToArray(); bool consumed = _gapConsumed.ContainsKey(pair.Key);
            ForgeRequire("extendedproduction.actual-ES3-fixed-UID." + pair.Key, consumed ? 0 : 1, matches.Length,
                consumed ? matches.Length == 0 : matches.Length == 1 && matches[0].identifier == pair.Value.Id && matches[0].unitCount == pair.Value.Count);
            if (!consumed) disk.Add(GapStoredItem(matches[0].identifier, matches[0].uniqueId, matches[0].unitCount, matches[0].unitValue, matches[0].itemState));
        }
        var live = GapLiveProofs();
        var decoded = GapProveSerializedTemperatures(live, disk, saved.mainInvJSON, saved.backInvJSON);
        ForgeRequire("extendedproduction.actual-ES3-all-tags-and-float-bits", live, disk, JToken.DeepEquals(GapReadbackProjection(decoded), GapReadbackProjection(disk)), "Only default disabled tag omission and separately proven original-disk/Unity-decoded temperatures follow the actual native serialization contract. Raw live and disk snapshots remain recorded.");
        var snapshot = EpSnapshot(); snapshot["proofs"] = disk.DeepClone();
        R.persistence["extendedProduction"] = new JObject { ["snapshot"] = snapshot, ["commitments"] = GapCommitmentSnapshot(),
            ["inputs"] = _epInputs.DeepClone(), ["conversionControls"] = _epControls.DeepClone(), ["nights"] = _epNights.DeepClone(), ["vacuum"] = _epVacuum.DeepClone(),
            ["reactorUid"] = _epReactor.uniqueId, ["cellUid"] = _epCell.uniqueId, ["ratUid"] = _epRat.uniqueId, ["cageUid"] = _epCage.uniqueId,
            ["robotUid"] = _epRobot.uniqueId, ["blenderUid"] = _epBlender.uniqueId, ["wasteUid"] = _epWasteUid, ["savePath"] = path, ["nativeHash"] = Reflect.HashFile(path) };
        yield return null;
    }

    // Every production hook below is void and read-only. __state is Harmony
    // bookkeeping, never an original argument/result or business flag.
    private sealed class EpObservation : IDisposable
    {
        private static EpObservation? Active;
        private readonly ProbeContext _context; private readonly Harmony _harmony; private readonly List<MethodBase> _methods = new();
        internal int Night, ReactorCalls;
        internal readonly JArray Poops = new(), Admissions = new(), Feeds = new(), FoodDestruction = new(), Errors = new();
        internal readonly JObject Evidence = new();
        internal EpObservation(ProbeContext context)
        {
            if (Active != null) throw new InvalidOperationException("Extended observer already active");
            _context = context; _harmony = new Harmony("nicokobo.content_native_probe.extended." + Guid.NewGuid().ToString("N"));
            Evidence["poops"] = Poops; Evidence["admissions"] = Admissions; Evidence["feeds"] = Feeds; Evidence["foodDestruction"] = FoodDestruction; Evidence["errors"] = Errors;
            Active = this;
            try
            {
                Hook(typeof(ReactorHelper), "OnNight", Type.EmptyTypes, null, nameof(AfterReactor));
                Hook(typeof(HusbandryHelper), "Poop", new[] { typeof(GameItem), typeof(int) }, nameof(BeforePoop), nameof(AfterPoop));
                Hook(typeof(HusbandryHelper), "TryFeed", new[] { typeof(GameItem), typeof(GameInventory) }, nameof(BeforeFeed), nameof(AfterFeed));
                Hook(typeof(GameItemElement), nameof(GameItem.Destroy), Type.EmptyTypes, nameof(BeforeFoodDestroy), null);
                Hook(typeof(GraphUtils), "CanAccept", new[] { typeof(GraphNodeStorage), typeof(GameItem) }, null, nameof(AfterAdmission));
            }
            catch { Dispose(); throw; }
        }
        private void Hook(Type type, string name, Type[] args, string? prefix, string? postfix)
        {
            var method = AccessTools.Method(type, name, args) ?? throw new MissingMethodException(type.FullName, name);
            _harmony.Patch(method, prefix: prefix == null ? null : new HarmonyMethod(typeof(EpObservation), prefix),
                postfix: postfix == null ? null : new HarmonyMethod(typeof(EpObservation), postfix)); _methods.Add(method);
        }
        private void Observe(Action action) { try { action(); } catch (Exception error) { Errors.Add(error.ToString()); } }
        private static void AfterReactor(bool __runOriginal)
        {
            var o = Active; if (o == null || o.Night == 0) return;
            o.Observe(() => { if (!__runOriginal) throw new InvalidOperationException("Real Reactor.OnNight original was skipped"); o.ReactorCalls++; });
        }
        private static void BeforePoop(GameItem __0, int __1, out int[] __state)
        {
            __state = Array.Empty<int>(); var o = Active;
            if (o == null || o.Night == 0 || !ReferenceEquals(__0, o._context._epRat)) return;
            int[] before = Array.Empty<int>();
            o.Observe(() => before = __0.parentInventory?.childItems.Where(i => i.IsTag("ANIMAL_FECES_TAG")).Select(i => i.uniqueId).ToArray() ?? Array.Empty<int>());
            __state = before;
        }
        private static void AfterPoop(GameItem __0, int __1, int[] __state, bool __runOriginal)
        {
            var o = Active; if (o == null || o.Night == 0 || !ReferenceEquals(__0, o._context._epRat)) return;
            o.Observe(() =>
            {
                float weight = __0.GetTagReadonly("ANIMAL_WEIGHT_TAG").GetFloat();
                o.Poops.Add(new JObject { ["night"] = o.Night, ["ratUid"] = __0.uniqueId, ["growth"] = __1, ["weight"] = (double)weight,
                    ["weightBits"] = BitConverter.SingleToInt32Bits(weight).ToString("X8"), ["age"] = __0.GetTagReadonly("ANIMAL_AGE_TAG").GetInt(),
                    ["originalRan"] = __runOriginal, ["expectedId"] = weight >= 1000 ? "feces_large" : weight >= 600 ? "feces_medium" : "feces_small",
                    ["newUids"] = new JArray((__0.parentInventory?.childItems ?? new List<GameItem>()).Where(i => i.IsTag("ANIMAL_FECES_TAG") && !__state.Contains(i.uniqueId)).Select(i => i.uniqueId)) });
            });
        }
        private static void BeforeFeed(GameItem __0, GameInventory __1, out int[] __state)
        {
            __state = Array.Empty<int>(); var o = Active;
            if (o == null || o.Night == 0 || !ReferenceEquals(__0, o._context._epRat)) return;
            int[] before = Array.Empty<int>();
            o.Observe(() => before = __1.childItems.Where(i => i.IsTag("CALORIE_VALUE_TAG")).Select(i => i.uniqueId).ToArray());
            __state = before;
        }
        private static void AfterFeed(GameItem __0, GameInventory __1, int[] __state, bool __runOriginal)
        {
            var o = Active; if (o == null || o.Night == 0 || !ReferenceEquals(__0, o._context._epRat)) return;
            o.Observe(() => o.Feeds.Add(new JObject { ["night"] = o.Night, ["ratUid"] = __0.uniqueId, ["originalRan"] = __runOriginal,
                ["consumedUids"] = new JArray(__state.Where(uid => o._context.ForgeOwnedUnits(uid) == 0)) }));
        }
        private static void BeforeFoodDestroy(GameItemElement __instance)
        {
            var o = Active; if (o == null || !o._context._epFood.Contains(__instance.uniqueId)) return;
            o.Observe(() => o.FoodDestruction.Add(new JObject { ["night"] = o.Night, ["uid"] = __instance.uniqueId,
                ["id"] = __instance.identifier, ["parentUid"] = __instance.parentInventory?.GetParentItem()?.uniqueId,
                ["stack"] = Environment.StackTrace }));
        }
        private static void AfterAdmission(GraphNodeStorage __0, GameItem __1, bool __result, bool __runOriginal)
        {
            var o = Active; if (o == null || o.Night == 0 || !ReferenceEquals(__0, o._context._epRobotStorage)) return;
            o.Observe(() => o.Admissions.Add(new JObject { ["night"] = o.Night, ["uid"] = __1.uniqueId, ["id"] = __1.identifier, ["accepted"] = __result, ["originalRan"] = __runOriginal }));
        }
        public void Dispose()
        {
            if (ReferenceEquals(Active, this)) Active = null;
            foreach (var method in _methods) _harmony.Unpatch(method, HarmonyPatchType.All, _harmony.Id);
        }
    }
}

