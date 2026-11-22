using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Nicokobo.ContentNativeProbe;

internal sealed partial class ProbeContext
{
    private readonly JArray _businessFixtures = new(), _businessActions = new(), _businessNights = new(), _businessTrades = new();
    private readonly Dictionary<int, (string Id, int Count, string Purpose)> _businessCommitments = new();
    private readonly Dictionary<int, string> _businessRetirements = new();
    private GameItem _businessVending = null!, _businessFountain = null!, _businessDeal = null!, _businessCoordinator = null!, _businessBay = null!, _businessTabTarget = null!;
    private bool _businessDealPassed;
    private int _businessSuccessfulNights;
    private static readonly string[] BusinessSaleInputs =
    { "common_electronic", "common_medical", "scrap_metal", "common_ore", "water_ration", "water_ration_small", "toilet_paper", "soap", "cloth", "screwdriver", "flashlight", "wine_berry" };

    // A normal APPRENTICE start. Only declared material/equipment preparation
    // uses native factories/Unlock helpers; all business effects use real paths.
    internal IEnumerable<object?> NativeBusinessDevices()
    {
        R.phase = "businessdevices.native-APPRENTICE-opening";
        R.environment["nativeBusinessFixtures"] = _businessFixtures;
        R.environment["nativeBusinessActions"] = _businessActions;
        R.environment["nativeBusinessNights"] = _businessNights;
        R.environment["nativeBusinessTrades"] = _businessTrades;
        R.environment["nativeBusinessCommitmentContract"] = "Fixed UID/ID/count denominator captured after legal admission, including native child trees. Only independently verified sales retire consumed UIDs. Old empty bottles remain active commitments; the live graph never defines a smaller denominator.";
        R.environment["nativeBusinessScope"] = "Build26317870 normal APPRENTICE; declared native UnlockVendingMachine/UnlockVendingFountain prerequisite fixtures, legal factory equipment/energy/stock/water/cassettes. Actual SlotMarker admission, generated native factory buyers, real shutters/bedroll/End Day/EndNight/auto-save, native MachineryButton.Select collection/activation/play/stop, native coordinator configuration and TogglePresets consumer. Input fixtures include service funds and generated client availability; no deposit/sales-complete/result assignments; no night callback invocation or mocked customer. Physical input gestures, economic acquisition/network purchase cost and audible quality are separate untested boundaries.";
        BusinessRequire("business.native-fresh-APPRENTICE", "Unmodified native APPRENTICE D1 normal difficulty; no mod scenario, perks, developer cheats or Steam", new
        {
            start = NewGameData.Instance.startType.ToString(), NewGameData.Instance.startScenarioId, day = StoreStation.GetDayCounter(),
            Store.playerCash, Store.devMode, steam = SteamManager.Initialized, Time.timeScale
        }, !O.Reload && NewGameData.Instance.startType == NewGameData.StartType.APPRENTICE && string.IsNullOrEmpty(NewGameData.Instance.startScenarioId) &&
            NewGameData.Instance.startingPerks.Count == 0 && StoreStation.GetDayCounter() == 1 && !Store.devMode && !SteamManager.Initialized && Time.timeScale == 1);
        Reflect.Call(Reflect.Type("SpeedUp.SpeedUpSettings", "SpeedUp"), "RestoreNormalSpeed");
        BusinessRequire("business.fixture-unlock-precondition", "Fresh native vending devices are not already unlocked", new { Store.isVendingMachineUnlocked, Store.isVendingFountainUnlocked },
            !Store.isVendingMachineUnlocked && !Store.isVendingFountainUnlocked);
        int cash = Store.playerCash;
        BusinessAction("declared-native-Unlock-prerequisite", new { helpers = new[] { "VendingMachineHelper.UnlockVendingMachine()", "VendingFountainHelper.UnlockVendingFountain()" }, economicPurchaseCovered = false });
        VendingMachineHelper.UnlockVendingMachine();
        VendingFountainHelper.UnlockVendingFountain();
        _businessVending = EmporiumEntry.Instance.vendingMachineElement.childItem;
        _businessFountain = EmporiumEntry.Instance.vendingFountainElement.childItem;
        BusinessRequire("business.native-unlock-identity", "Native helpers create owned fixed machines plus real cores, with unchanged cash", new
        {
            cash, actualCash = Store.playerCash, vending = BusinessItem(_businessVending), fountain = BusinessItem(_businessFountain),
            Store.isVendingMachineUnlocked, Store.isVendingFountainUnlocked
        }, Store.playerCash == cash && Store.isVendingMachineUnlocked && Store.isVendingFountainUnlocked &&
            _businessVending.identifier == "vending_machine" && _businessFountain.identifier == "vending_fountain" &&
            GeneralHelper.IsItemOwned(_businessVending) && GeneralHelper.IsItemOwned(_businessFountain) &&
            _businessVending.FindAllChildItems().Count(i => i.identifier == "vending_machine_core") == 1 &&
            _businessFountain.FindAllChildItems().Count(i => i.identifier == "vending_fountain_core") == 1);
        BusinessCommit(_businessVending, "actual native Unlock vending machine and original core");
        BusinessCommit(_businessFountain, "actual native Unlock fountain and original core");
        BusinessCommit(EmporiumEntry.Instance.cassettePlayer, "original fixed native cassette player and native starting tapes");
        BusinessPower(_businessVending, 100);
        BusinessPower(_businessFountain, 100);
        _businessDeal = BusinessFixture("deal_maker", "native unmodified machine fixture; no modules");
        Place(EmporiumEntry.Instance.invElement, _businessDeal, "business-dealmaker-main-floor");
        BusinessPower(_businessDeal, 250, true);
        _businessCoordinator = BusinessFixture("storage_coordinator", "native coordinator configuration input device");
        Place(EmporiumEntry.Instance.invElement, _businessCoordinator, "business-coordinator-main-floor");
        _businessBay = BusinessFixture("smuggler_bay_mod", "explicit native secure storage fixture for idle deal maker and its original battery across real nights");
        Place(EmporiumEntry.Instance.invElement, _businessBay, "business-safe-storage-main-floor");
        _businessTabTarget = BusinessFixture("auto_butcher", "native two-tab storage target for coordinator; no food processing or powered production claim");
        Place(EmporiumEntry.Instance.invElement, _businessTabTarget, "business-real-tab-target-main-floor");
        Retain(_businessDeal); Retain(_businessCoordinator); Retain(_businessBay); Retain(_businessTabTarget);
        foreach (var item in new[] { _businessDeal, _businessCoordinator, _businessBay, _businessTabTarget })
            BusinessCommit(item, "original legally admitted native equipment fixture and its original children");
        BusinessAudit("business.commitments.after-preparation");
        foreach (object? wait in BusinessCoordinator()) yield return wait;

        // Two real restock/sale/collection cycles with directly generated buyers.
        // A third bounded cycle retains the native sale-outcome check.
        for (int attempt = 0; attempt < 3 && (_businessSuccessfulNights < 2 || !_businessDealPassed); attempt++)
        {
            int day = StoreStation.GetDayCounter();
            using var night = new NightObservation(this, Array.Empty<RecipeFixture>(), day);
            _businessNights.Add(night.Evidence);
            if (_businessDeal.parentInventory != EmporiumEntry.Instance.invElement)
                GraylineTransfer(_businessDeal, EmporiumEntry.Instance.invElement, "business-dealmaker-ready-D" + day);
            foreach (object? wait in GraylineOpen()) yield return wait;
            foreach (object? wait in BusinessServeDay(night)) yield return wait;
            var stock = BusinessRestock(day);
            // No protection flags are written: the original machine/battery
            // moves through real admission into an actual smuggler bay.
            SlotMarker? secure = _businessBay.TryFindOneValidInventorySlot(_businessDeal);
            BusinessRequire("business.legal-secure-machine-slot.D" + day, "Actual native bay can admit the original deal maker", new { slot = secure?.inventory?.identifier, valid = secure?.IsValid(), uid = _businessDeal.uniqueId },
                secure?.IsValid() == true);
            Place(secure!.inventory, _businessDeal, "business-original-dealmaker-to-native-bay-D" + day);
            var critical = new[] { _businessDeal, _businessCoordinator, _businessTabTarget, _businessVending, _businessFountain, BusinessBattery(_businessDeal) };
            var candidates = Store.GetAllRepoEligibleItems().Select(i => i.uniqueId).ToHashSet();
            BusinessRequire("business.native-night-storage.D" + day, "Original prepared devices/batteries remain owned and are absent from the actual native burglary candidate list", critical.Select(i => new
            { i.uniqueId, i.identifier, owned = GeneralHelper.IsItemOwned(i), smuggler = i.IsRootedInSmugglerBay(), nativeEligible = Store.IsEligibleForRepo(i), candidate = candidates.Contains(i.uniqueId) }).ToArray(),
                _businessDeal.IsRootedInSmugglerBay() && critical.All(i => GeneralHelper.IsItemOwned(i) && !candidates.Contains(i.uniqueId)));
            // Fixed vending power slots remain under normal native burglary
            // rules. Record their eligibility rather than pretending that the
            // fixed machines' IMPORTANT tag recursively protects every child.
            // Native skip-intro grace covers the first two required nights.
            BusinessAction("native-fixed-slot-burglary-risk.D" + day, new[] { _businessVending, _businessFountain }.Select(i => new
            { machine = i.identifier, battery = BusinessBattery(i).uniqueId, eligible = Store.IsEligibleForRepo(BusinessBattery(i)), candidate = candidates.Contains(BusinessBattery(i).uniqueId) }).ToArray());
            BusinessAudit("business.commitments.before-real-night.D" + day);
            foreach (object? wait in BusinessEndNight(day, night)) yield return wait;
            BusinessCheckNight(stock, day);
            foreach (object? wait in BusinessCollect(_businessVending, false, "vending.D" + day)) yield return wait;
            foreach (object? wait in BusinessCollect(_businessFountain, true, "fountain.D" + day)) yield return wait;
            BusinessAudit("business.commitments.after-real-collections.D" + day);
            _businessSuccessfulNights++;
        }
        BusinessRequire("business.two-real-restock-sale-collection-cycles", 2, _businessSuccessfulNights, _businessSuccessfulNights >= 2);
        if (!_businessDealPassed)
        {
            R.Skip("business.dealmaker-natural-markup-paid-sale", "Three fixture cycles did not produce an eligible generated buyer with accepted markup and a fully paid native sale. No customer, budget, random outcome or bargain state was manufactured.");
            throw new InvalidOperationException("Required deal-maker natural paid sale remains unexecuted after three generated-buyer cycles");
        }
        foreach (object? wait in BusinessCassette()) yield return wait;
        foreach (object? wait in BusinessCoordinator()) yield return wait;
        BusinessAudit("business.commitments.before-final-native-save");
        R.persistence["nativeBusinessDevices"] = BusinessSnapshot();
        R.Skip("business.audible-perception", "Native AudioSource playback/pause/unpause/stop and track configuration are checked; audible output quality requires a listening test.", false);
        R.Skip("business.fixture-economic-acquisition-and-physical-gestures", "Native factories/Unlock helpers prepare materials/prerequisites. Network purchase cost, economic acquisition, pointer aiming, coordinator target picking/key capture and physical hotkey delivery are not executed. SavePresets/TogglePresets are the real native configuration/consumer paths.", false);
    }

    internal IEnumerable<object?> NativeBusinessDevicesReload(JObject baseline)
    {
        R.phase = "businessdevices.cross-process-native-readback";
        BusinessRequire("business.reload-original-slot-only", "Independent original-slot reload; no copied-slot identity claim", new { O.Reload, O.UseCopy },
            O.Reload && !O.UseCopy);
        var expected = baseline["persistence"]?["nativeBusinessDevices"] as JObject
            ?? throw new InvalidOperationException("Missing native business-device baseline");
        _businessVending = BusinessFind((int)expected["vending"]!["uid"]!, "vending_machine");
        _businessFountain = BusinessFind((int)expected["fountain"]!["uid"]!, "vending_fountain");
        _businessDeal = BusinessFind((int)expected["dealMaker"]!["uid"]!, "deal_maker");
        _businessCoordinator = BusinessFind((int)expected["coordinator"]!["uid"]!, "storage_coordinator");
        _businessBay = BusinessFind((int)expected["secureBay"]!["uid"]!, "smuggler_bay_mod");
        _businessTabTarget = BusinessFind((int)expected["tabTarget"]!["uid"]!, "auto_butcher");
        var commitments = expected["commitments"] as JArray ?? throw new InvalidOperationException("Missing fixed native business commitment denominator");
        foreach (var row in commitments)
        {
            int uid = (int)row["uid"]!;
            if (_businessCommitments.ContainsKey(uid)) throw new InvalidOperationException("Duplicate UID in business baseline commitments");
            _businessCommitments.Add(uid, ((string)row["id"]!, (int)row["count"]!, (string)row["purpose"]!));
            string retired = (string?)row["retiredByVerifiedSale"] ?? "";
            if (retired.Length != 0) _businessRetirements.Add(uid, retired);
        }
        BusinessAudit("business.commitments.independent-native-reload");
        JObject actual = BusinessSnapshot();
        BusinessRequire("business.reload-UID-cash-water-power-deposits-presets-cassette", expected, actual, JToken.DeepEquals(expected, actual),
            "Normal native LoadGame/ES3 decoding, no fixture replay, SavePresets, playback or collection on reload. Fixed cassette-player negative UID and positive cassette UIDs are included explicitly.");
        foreach (GameItem machine in new[] { _businessVending, _businessFountain })
            BusinessRequire("business.reload-zero-collected-deposit." + machine.identifier, 0, machine.GetTagReadonly("VENDING_CASH").GetInt(),
                machine.GetTagReadonly("VENDING_CASH").GetInt() == 0);
        string savePath = SaveFiles.AbsolutePath(SaveFiles.Slot(Store.saveSlotId));
        string saveHash = Reflect.HashFile(savePath); int cash = Store.playerCash;
        var loadedPresets = MachineStorageCoordinator.LoadPresets(_businessCoordinator);
        var targets = new[] { _businessTabTarget, _businessDeal };
        foreach (var item in targets) item.CloseContentWindow();
        MachineStorageCoordinator.TogglePresets(loadedPresets);
        yield return null;
        BusinessRequire("business.reload-presets-actual-consumer", "Previously saved UID targets open with the saved nondefault tab selection", targets.Select(i => new
        { i.uniqueId, visible = i.contentWindow.IsVisible(), selected = i.GetContentTabsSelection(), expected = loadedPresets[0].GetTabs(i.uniqueId) }).ToArray(),
            targets.All(i => i.contentWindow.IsVisible() && BusinessPresetTabsMatch(i, loadedPresets[0].GetTabs(i.uniqueId))) &&
            BusinessTabs(_businessTabTarget).Any(t => t.count > 1) && BusinessTabs(_businessTabTarget).Where(t => t.count > 1).All(t => t.selected == 1));
        MachineStorageCoordinator.TogglePresets(loadedPresets);
        yield return null;
        BusinessRequire("business.reload-consumer-no-save-or-money", new { saveHash, cash }, new { saveHash = Reflect.HashFile(savePath), cash = Store.playerCash },
            Reflect.HashFile(savePath) == saveHash && Store.playerCash == cash && targets.All(i => !i.contentWindow.IsVisible() && GeneralHelper.IsItemOwned(i)));
        BusinessAudit("business.commitments.after-reload-consumer");
        R.persistence["nativeBusinessDevices"] = actual;
        yield return null;
    }

    private JObject BusinessRestock(int day)
    {
        R.phase = "businessdevices.real-restock.D" + day;
        BusinessAudit("business.commitments.before-next-restock.D" + day);
        var inventory = VendingMachineHelper.GetVendingInventory(_businessVending);
        BusinessRequire("business.empty-before-restock.D" + day, "Empty vending stock and zero collected deposits; default15 attractiveness", new
        {
            stock = inventory.childItems.Count, vendingCash = _businessVending.GetTagReadonly("VENDING_CASH").GetInt(),
            fountainCash = _businessFountain.GetTagReadonly("VENDING_CASH").GetInt(),
            vendingAttractiveness = _businessVending.GetTagReadonly("VENDING_ATTRACTIVENESS").GetInt(),
            fountainAttractiveness = _businessFountain.GetTagReadonly("VENDING_ATTRACTIVENESS").GetInt()
        }, inventory.childItems.Count == 0 && _businessVending.GetTagReadonly("VENDING_CASH").GetInt() == 0 &&
            _businessFountain.GetTagReadonly("VENDING_CASH").GetInt() == 0 &&
            _businessVending.GetTagReadonly("VENDING_ATTRACTIVENESS").GetInt() == 15 &&
            _businessFountain.GetTagReadonly("VENDING_ATTRACTIVENESS").GetInt() == 15);
        var ration = BusinessFixture("water_ration_small", "one native shelf-stable consumable stock input; guaranteed ordinary <=50 sale, not an economically acquired product");
        BusinessRequire("business.independent-vending-input.D" + day, "Default15-credit single ration, no price modifiers/children; normal positive final vending price <=50", BusinessItem(ration),
            ration.unitValue == 15 && ration.unitCount == 1 && ration.lateUnitValue == 0 && ration.FindAllChildItems().Count == 0 &&
            ration.itemFeatures.All(f => f.isDisabled || f.GetCurrentValueMod() == 0) && !ration.IsGameItemType("LUXURY_ITEM"));
        int markup = Store.retailMarkup;
        int goodsPrice = checked((int)Math.Round(15 * (1.0 + (markup + 10) / 100.0)));
        BusinessRequire("business.guaranteed-sale-contract.D" + day, "One nonluxury native consumable <=50, independently expected price positive", new { goodsPrice, markup, ration.uniqueId },
            goodsPrice > 0 && goodsPrice <= 50);
        Place(inventory, ration, "business-real-vending-stock-D" + day);
        BusinessCommit(ration, "legally admitted original single vending stock D" + day);
        var baseSlot = (GameSlotInventory)MachineHelper.GetSlot(_businessFountain, 1);
        var highSlot = (GameSlotInventory)MachineHelper.GetSlot(_businessFountain, 2);
        foreach (var slot in new[] { baseSlot, highSlot })
        {
            if (slot.childItem == null) continue;
            BusinessRequire("business.prior-water-source-empty.D" + day + "." + slot.identifier, 0, WaterHelper.GetTotalVolume(slot.childItem),
                WaterHelper.GetTotalVolume(slot.childItem) == 0);
            GraylineTransfer(slot.childItem, EmporiumEntry.Instance.invElement, "business-retain-empty-water-source-D" + day);
        }
        var baseWater = BusinessFixture("bottled_water", "explicit native750ml base-water fixture, filled by WaterHelper.FillWithBaseWater");
        var highWater = BusinessFixture("small_bottled_water", "explicit native450ml high-quality-water fixture, filled by WaterHelper.FillWithHighQualityWater");
        WaterHelper.FillWithBaseWater(baseWater);
        WaterHelper.FillWithHighQualityWater(highWater);
        Place(baseSlot, baseWater, "business-real-base-water-restock-D" + day);
        Place(highSlot, highWater, "business-real-high-water-restock-D" + day);
        BusinessCommit(baseWater, "original base-water bottle D" + day + "; remains committed after emptying and later storage");
        BusinessCommit(highWater, "original high-quality-water bottle D" + day + "; remains committed after emptying and later storage");
        BusinessRequire("business.native-water-preparation.D" + day, "750000/450000 liquid parts and real base/high-quality purity", new
        {
            baseVolume = WaterHelper.GetTotalVolume(baseWater), highVolume = WaterHelper.GetTotalVolume(highWater),
            basePurity = WaterHelper.GetWaterPurity(baseWater), highPurity = WaterHelper.GetWaterPurity(highWater),
            baseUid = baseWater.uniqueId, highUid = highWater.uniqueId
        }, WaterHelper.GetTotalVolume(baseWater) == 750000 && WaterHelper.GetTotalVolume(highWater) == 450000 &&
            WaterHelper.GetWaterPurity(baseWater) >= 9800 && WaterHelper.GetWaterPurity(baseWater) < 9900 &&
            WaterHelper.GetWaterPurity(highWater) >= 9900 && WaterHelper.GetWaterPurity(highWater) < 9990 &&
            ((GameSlotInventory)MachineHelper.GetSlot(_businessFountain, 3)).childItem == null);
        // Independent Build26317870 formula: 40 credits/litre, water innate
        // modifiers base0%/HQ50%, bottle value5/4; retail+vending10% market.
        int basePrice = checked((int)Math.Round((30 + 5) * (1.0 + (markup + 10) / 100.0)));
        int highPrice = checked((int)Math.Round((18 * 1.5 + 4) * (1.0 + (markup + 10) / 100.0)));
        var row = new JObject
        {
            ["day"] = day, ["rationUid"] = ration.uniqueId, ["baseUid"] = baseWater.uniqueId, ["highUid"] = highWater.uniqueId,
            ["goodsPrice"] = goodsPrice, ["basePrice"] = basePrice, ["highPrice"] = highPrice, ["markup"] = markup,
            ["vendingEnergy"] = BusinessEnergy(BusinessBattery(_businessVending)),
            ["fountainEnergy"] = BusinessEnergy(BusinessBattery(_businessFountain)),
            ["formula"] = "Round(15*(1+(retail+10)/100)); Round((30+5)*market)+Round((18*1.5+4)*market); 750000+450000 parts consumed; 2kJ each"
        };
        BusinessAction("actual-native-restock-D" + day, row);
        return row;
    }

    private void BusinessCheckNight(JObject stock, int day)
    {
        var baseWater = ((GameSlotInventory)MachineHelper.GetSlot(_businessFountain, 1)).childItem;
        var highWater = ((GameSlotInventory)MachineHelper.GetSlot(_businessFountain, 2)).childItem;
        BusinessRequire("business.complete-normal-night-output.D" + day, stock, new
        {
            vendingDeposit = _businessVending.GetTagReadonly("VENDING_CASH").GetInt(),
            fountainDeposit = _businessFountain.GetTagReadonly("VENDING_CASH").GetInt(),
            stockRemaining = VendingMachineHelper.GetVendingInventory(_businessVending).childItems.Count,
            rationOwned = BusinessOwnedUnits((int)stock["rationUid"]!), baseWater = BusinessItem(baseWater), highWater = BusinessItem(highWater),
            baseVolume = WaterHelper.GetTotalVolume(baseWater), highVolume = WaterHelper.GetTotalVolume(highWater),
            vendingEnergy = BusinessEnergy(BusinessBattery(_businessVending)), fountainEnergy = BusinessEnergy(BusinessBattery(_businessFountain)), Store.retailMarkup
        }, _businessVending.GetTagReadonly("VENDING_CASH").GetInt() == (int)stock["goodsPrice"]! &&
            _businessFountain.GetTagReadonly("VENDING_CASH").GetInt() == (int)stock["basePrice"]! + (int)stock["highPrice"]! &&
            VendingMachineHelper.GetVendingInventory(_businessVending).childItems.Count == 0 && BusinessOwnedUnits((int)stock["rationUid"]!) == 0 &&
            baseWater != null && highWater != null && baseWater.uniqueId == (int)stock["baseUid"]! && highWater.uniqueId == (int)stock["highUid"]! &&
            GeneralHelper.IsItemOwned(baseWater) && GeneralHelper.IsItemOwned(highWater) &&
            WaterHelper.GetTotalVolume(baseWater) == 0 && WaterHelper.GetTotalVolume(highWater) == 0 &&
            BusinessEnergy(BusinessBattery(_businessVending)) == (int)stock["vendingEnergy"]! - 2 &&
            BusinessEnergy(BusinessBattery(_businessFountain)) == (int)stock["fountainEnergy"]! - 2 && Store.retailMarkup == (int)stock["markup"]!,
            "Only the real EndNight native cycle callbacks may generate deposits; the harness never calls HandleNight/SimulateWaterSale or writes VENDING_CASH.");
        BusinessRetire((int)stock["rationUid"]!, "independently verified real vending sale D" + day);
        BusinessAudit("business.commitments.after-verified-night-output.D" + day);
    }

    private IEnumerable<object?> BusinessCollect(GameItem machine, bool fountain, string label)
    {
        if (fountain) StoreVendingFountain.Instance.OnClick(); else StoreVendingMachine.Instance.OnClick();
        yield return null;
        var button = BusinessButtons(machine).Single(b => b.text == LocHelper.GetLocalizedMechanic("mech_vending_btn_transfer"));
        int cash = Store.playerCash, deposit = machine.GetTagReadonly("VENDING_CASH").GetInt(), uid = machine.uniqueId;
        int[] children = machine.FindAllChildItems().Select(i => i.uniqueId).OrderBy(i => i).ToArray();
        BusinessRequire("business.collect-visible-positive-deposit." + label, "Actual visible enabled machine collection button and positive generated deposit", new { uid, cash, deposit, button.text },
            machine.contentWindow.IsVisible() && deposit > 0 && !button.isDisabled && !button.isHidden);
        BusinessClick(machine, button, "native-collection." + label);
        yield return null;
        BusinessRequire("business.collect-exact-cash-UID." + label, new { cash = cash + deposit, deposit = 0, uid, children }, new
        {
            cash = Store.playerCash, deposit = machine.GetTagReadonly("VENDING_CASH").GetInt(), uid = machine.uniqueId,
            children = machine.FindAllChildItems().Select(i => i.uniqueId).OrderBy(i => i).ToArray()
        }, Store.playerCash == cash + deposit && machine.GetTagReadonly("VENDING_CASH").GetInt() == 0 && machine.uniqueId == uid &&
            children.SequenceEqual(machine.FindAllChildItems().Select(i => i.uniqueId).OrderBy(i => i)) && GeneralHelper.IsItemOwned(machine));
        BusinessClick(machine, button, "repeat-empty-collection." + label);
        yield return null;
        BusinessRequire("business.repeat-collection-no-money." + label, new { cash = cash + deposit, deposit = 0 }, new { cash = Store.playerCash, deposit = machine.GetTagReadonly("VENDING_CASH").GetInt() },
            Store.playerCash == cash + deposit && machine.GetTagReadonly("VENDING_CASH").GetInt() == 0);
        machine.CloseContentWindow();
    }

    private IEnumerable<object?> BusinessCoordinator()
    {
        R.phase = "businessdevices.native-coordinator-presets";
        int cash = Store.playerCash;
        var targets = new[] { _businessTabTarget, _businessDeal };
        foreach (GameItem item in targets)
        {
            BusinessOpen(item);
            var tabs = BusinessTabs(item);
            BusinessRequire("business.coordinator-real-content." + item.identifier, "Owned unlocked native target; tab selection is required on the actual auto-butcher target", tabs.Select(t => new { t.count, t.selected }).ToArray(),
                item.contentWindow.IsVisible() && (item != _businessTabTarget || tabs.Any(t => t.count > 1)));
            foreach (var tab in tabs.Where(t => t.count > 1)) tab.Select(1);
            if (item == _businessTabTarget)
                BusinessRequire("business.coordinator-independent-selected-one", "Every actual multi-page target tab selected1", tabs.Select(t => new { t.count, t.selected }).ToArray(),
                    tabs.Any(t => t.count > 1) && tabs.Where(t => t.count > 1).All(t => t.selected == 1));
        }
        var preset = new MachineStorageCoordinator.Preset { name = "Native business devices", keyCode = (int)KeyCode.F8, itemIds = targets.Select(t => t.uniqueId).ToList() };
        foreach (var item in targets) preset.SetTabs(item.uniqueId, item.GetContentTabsSelection());
        var one = new List<MachineStorageCoordinator.Preset> { preset };
        BusinessAction("native-SavePresets-input", new { coordinator = _businessCoordinator.uniqueId, preset.name, preset.keyCode, preset.itemIds, preset.itemTabs });
        MachineStorageCoordinator.SavePresets(_businessCoordinator, one);
        var first = JToken.FromObject(MachineStorageCoordinator.LoadPresets(_businessCoordinator));
        BusinessRequire("business.coordinator-save-load", JToken.FromObject(one), first, JToken.DeepEquals(JToken.FromObject(one), first));
        preset.name = "Native business devices saved";
        var second = new MachineStorageCoordinator.Preset { name = "Deal maker only", keyCode = (int)KeyCode.F9, itemIds = new List<int> { _businessDeal.uniqueId } };
        second.SetTabs(_businessDeal.uniqueId, preset.GetTabs(_businessDeal.uniqueId));
        var group = new List<MachineStorageCoordinator.Preset> { preset, second };
        MachineStorageCoordinator.SavePresets(_businessCoordinator, group);
        var loaded = MachineStorageCoordinator.LoadPresets(_businessCoordinator);
        BusinessRequire("business.coordinator-replace-and-multiple-presets", JToken.FromObject(group), JToken.FromObject(loaded),
            loaded.Count == 2 && JToken.DeepEquals(JToken.FromObject(group), JToken.FromObject(loaded)));
        foreach (var item in targets)
        {
            foreach (var tab in BusinessTabs(item).Where(t => t.count > 1)) tab.Select(0);
            if (item == _businessTabTarget)
                BusinessRequire("business.coordinator-independent-reset-zero", "Actual target page0 differs from independently verified saved page1", new
                { reset = item.GetContentTabsSelection(), saved = preset.GetTabs(item.uniqueId), tabs = BusinessTabs(item).Select(t => new { t.count, t.selected }).ToArray() },
                    BusinessTabs(item).Any(t => t.count > 1) && BusinessTabs(item).Where(t => t.count > 1).All(t => t.selected == 0) &&
                    item.GetContentTabsSelection() != preset.GetTabs(item.uniqueId));
            item.CloseContentWindow();
        }
        BusinessAction("native-TogglePresets-open-and-apply", new { input = "persisted native presets", targets = targets.Select(i => i.uniqueId).ToArray() });
        MachineStorageCoordinator.TogglePresets(loaded);
        yield return null;
        BusinessRequire("business.coordinator-apply-nondefault-tabs", "Both actual content windows opened with persisted nondefault tab selections", targets.Select(t => new
        { t.uniqueId, visible = t.contentWindow.IsVisible(), selected = t.GetContentTabsSelection(), expected = preset.GetTabs(t.uniqueId) }).ToArray(),
            targets.All(t => t.contentWindow.IsVisible() && BusinessPresetTabsMatch(t, preset.GetTabs(t.uniqueId))) &&
            BusinessTabs(_businessTabTarget).Any(t => t.count > 1) && BusinessTabs(_businessTabTarget).Where(t => t.count > 1).All(t => t.selected == 1));
        MachineStorageCoordinator.TogglePresets(loaded);
        yield return null;
        BusinessRequire("business.coordinator-toggle-close", "All target windows close, original UIDs and cash remain", new { cash = Store.playerCash, targets = targets.Select(t => new { t.uniqueId, visible = t.contentWindow.IsVisible(), owned = GeneralHelper.IsItemOwned(t) }).ToArray() },
            Store.playerCash == cash && targets.All(t => !t.contentWindow.IsVisible() && GeneralHelper.IsItemOwned(t)));
        MachineStorageCoordinator.OpenWindow(_businessCoordinator);
        yield return null;
        BusinessRequire("business.coordinator-real-configuration-window", "Actual native configuration UI is shown for original coordinator", new { owner = (Reflect.Get(typeof(MachineStorageCoordinator), "openItem") as GameItem)?.uniqueId, exists = CustomUIManager.Instance.GetWindow("storage_coordinator") != null },
            ReferenceEquals(Reflect.Get(typeof(MachineStorageCoordinator), "openItem"), _businessCoordinator) && CustomUIManager.Instance.GetWindow("storage_coordinator")?.IsAlive == true);
        MachineStorageCoordinator.OpenWindow(_businessCoordinator); // native toggle closes the same UI
        yield return null;
    }

    private IEnumerable<object?> BusinessCassette()
    {
        R.phase = "businessdevices.native-cassette-playback";
        GameItem machine = EmporiumEntry.Instance.cassettePlayer;
        var deck = MachineCassettePlayer.GetCassetteInventory();
        GameItem tape = BusinessFixture("cassette4d", "one declared native tape input admitted into the real fixed cassette-player deck");
        Place(deck, tape, "business-real-cassette-deck");
        BusinessCommit(tape, "original legal cassette fixture admitted into fixed native deck");
        Retain(tape);
        BusinessOpen(machine);
        yield return null;
        // Stop any native starting track through the real stop button before
        // constructing the expected first track from actual deck order.
        var play = BusinessButtons(machine).Single(b => (Reflect.Get(b, "faceNormal") as Sprite)?.name == "play");
        var stop = BusinessButtons(machine).Single(b => (Reflect.Get(b, "faceNormal") as Sprite)?.name == "pause");
        BusinessClick(machine, stop, "cassette.initial-stop");
        string[] tracks = deck.childItems.Where(i => i.IsTag("CASSETTE_TAG")).Select(i => i.GetTagReadonly("CASSETTE_CONTENT").GetString()).ToArray();
        int[] uids = deck.childItems.Select(i => i.uniqueId).ToArray(); int cash = Store.playerCash;
        BusinessRequire("business.cassette-real-playable-input", "Actual deck contains owned tape(s) and a native playable first track", new { tracks, uids, tapeUid = tape.uniqueId },
            tracks.Length > 0 && tracks.All(t => !string.IsNullOrEmpty(t)) && AudioManager.Instance.CanPlay(tracks[0]) &&
            deck.childItems.Contains(tape) && GeneralHelper.IsItemOwned(tape));
        BusinessClick(machine, play, "cassette.native-play");
        foreach (object? wait in BusinessWait("business.cassette-actual-audio-source-playing", () => BusinessAudioPlaying(), 5)) yield return wait;
        BusinessRequire("business.cassette-native-track-and-index", new { state = 1, index = 0, track = tracks[0], uids }, BusinessCassetteState(),
            machine.GetTagReadonly("CASETTE_PLAYER_STATE_INT").GetInt() == 1 &&
            machine.GetTagReadonly("CURRENT_PLAYING_CASSETTE_INDEX_TAG").GetInt() == 0 &&
            machine.GetTagReadonly("CURRENT_PLAYING_TRACK_ID_TAG").GetString() == tracks[0] &&
            Reflect.Get(Reflect.Get(AudioManager.Instance, "audioSourceMusic")!, "clip") != null && deck.childItems.Select(i => i.uniqueId).SequenceEqual(uids));
        BusinessClick(machine, play, "cassette.native-pause");
        yield return null;
        BusinessRequire("business.cassette-native-pause", "Native state2 and AudioSource paused", BusinessCassetteState(),
            machine.GetTagReadonly("CASETTE_PLAYER_STATE_INT").GetInt() == 2 && !BusinessAudioPlaying());
        BusinessClick(machine, play, "cassette.native-unpause");
        foreach (object? wait in BusinessWait("business.cassette-actual-audio-source-resumed", () => BusinessAudioPlaying(), 5)) yield return wait;
        BusinessClick(machine, stop, "cassette.native-stop");
        yield return null;
        BusinessRequire("business.cassette-native-stop-preserves-config", "Stopped audio with original deck UIDs, track/index and unchanged cash", BusinessCassetteState(),
            machine.GetTagReadonly("CASETTE_PLAYER_STATE_INT").GetInt() == 0 && !BusinessAudioPlaying() &&
            machine.GetTagReadonly("CURRENT_PLAYING_TRACK_ID_TAG").GetString() == tracks[0] &&
            machine.GetTagReadonly("CURRENT_PLAYING_CASSETTE_INDEX_TAG").GetInt() == 0 &&
            deck.childItems.Select(i => i.uniqueId).SequenceEqual(uids) && Store.playerCash == cash && deck.childItems.All(GeneralHelper.IsItemOwned));
        BusinessClick(machine, stop, "cassette.repeat-stop");
        yield return null;
        BusinessRequire("business.cassette-repeat-stop-no-consumption", uids, deck.childItems.Select(i => i.uniqueId).ToArray(),
            machine.GetTagReadonly("CASETTE_PLAYER_STATE_INT").GetInt() == 0 && !BusinessAudioPlaying() &&
            deck.childItems.Select(i => i.uniqueId).SequenceEqual(uids) && Store.playerCash == cash);
        machine.CloseContentWindow();
    }

    private IEnumerable<object?> BusinessServeDay(NightObservation night)
    {
        FixtureClearVisitors("generated native buyer for commercial-device regression");
        var buyer = StoreClientListUnused.CreateDebugClient2();
        _businessFixtures.Add(JObject.FromObject(new { purpose = "Shipped native test-buyer factory; no natural customer wait", buyer.identifier, budget = buyer.GetBudget() }));
        Store.storeClientManager.AddNextClient(buyer);
        foreach (object? wait in BusinessWait("business.fixture.bell-ready", () => Store.CanNextClient() && Store.lockNextBellFlag == 0 && DialogUIManager.Instance.currentDialog == null, 30f)) yield return wait;
        GraylineRingBell();
        foreach (object? wait in BusinessWait("business.fixture.arrived", () => ReferenceEquals(Store.currentClientInstance?.GetClientBlueprint(), buyer) && Store.isClientArrived, 30f)) yield return wait;
        foreach (object? wait in BusinessClient(buyer)) { night.SampleClock(); yield return wait; }
        foreach (object? wait in FixturePrepareNight()) { night.SampleClock(); yield return wait; }
    }

    private IEnumerable<object?> BusinessClient(StoreClient client)
    {
        float deadline = Time.realtimeSinceStartup + 180; bool rent = false, mortgage = false, vending = false, traded = false, rang = false;
        while (ReferenceEquals(Store.currentClientInstance?.GetClientBlueprint(), client))
        {
            if (Time.realtimeSinceStartup >= deadline) throw new InvalidOperationException("Native business client timed out: " + client.identifier);
            GraylineAdvanceDialogue();
            if (GraylineChoicesVisible())
            {
                string? key = client.identifier switch
                {
                    "landlord" when !rent => "dialog_landlord_player_pay",
                    "loan_shark2" when !mortgage => "dialog_loan_shark2_player_decline",
                    "landlordVending" when !vending => "dialog_landlord_vending_player_decline",
                    _ => null
                };
                if (key != null)
                {
                    int before = Store.playerCash, charge = client.identifier == "landlord" ? Store.GetRentValue() : 0;
                    string label = client.identifier == "landlord" ? LocHelper.GetLocalizedDialogue(key, charge) : LocHelper.GetLocalizedDialogue(key);
                    BusinessRequire("business.known-mandatory-choice." + client.identifier, "Exact real visible affordable rent/explicit optional-offer decline", new { label, charge, before },
                        GraylineChoice(label, label) != null && (charge == 0 || Store.CanPayRent()));
                    GraylineClickChoice(label, label, "business-" + client.identifier);
                    if (client.identifier == "landlord")
                    {
                        rent = true;
                        BusinessRequire("business.actual-rent-payment", before - charge, Store.playerCash, Store.playerCash == before - charge);
                    }
                    if (client.identifier == "loan_shark2") mortgage = true;
                    if (client.identifier == "landlordVending") vending = true;
                }
                else if (!Store.IsClientDimissable()) throw new InvalidOperationException("Unknown mandatory choice: " + client.identifier);
            }
            bool idle = Reflect.Get(DialogUIManager.Instance, "displayingUncompletedDialog") == null && !DialogUIManager.Instance.isWaitingForManualAdvance && !GraylineChoicesVisible();
            if (idle && !traded && Store.IsClientDimissable() && client.identifier != "landlord" && (!_businessDealPassed || Store.playerCash < 500))
            {
                traded = true;
                foreach (object? wait in BusinessTrade(client)) yield return wait;
            }
            if (idle && !rang && Store.IsClientDimissable() && Store.lockNextBellFlag == 0)
            { GraylineRingBell(); rang = true; }
            yield return null;
        }
        BusinessRequire("business.dealmaker-native-customer-reset." + client.identifier, "Native dismissal clears activation/bonus", new { Store.dealMakerBargainBonus, Store.dealMakerActivatedThisCustomer },
            Store.dealMakerBargainBonus == 0 && !Store.dealMakerActivatedThisCustomer);
        GraylineStowOwnedCounter();
    }

    private IEnumerable<object?> BusinessTrade(StoreClient client)
    {
        foreach (string id in BusinessSaleInputs)
        {
            if (!ReferenceEquals(Store.currentClientInstance?.GetClientBlueprint(), client) || !client.IsClientBuying() || client.isAug) yield break;
            if (!DirectoryMaster.Has<GameItem>(id)) continue;
            GameItem item = Spawn(id);
            bool willing = client.IsClientBuyingThisItem(item) && !client.CanClientExposeAnyFeatureDeep(item) &&
                (!client.useClientBudget || client.HasBudgetLeftToBuy(item)) && !ContrabandHelper.IsContraband(item) && !StolenHelper.IsStolenItem(item);
            if (!willing) { Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(item); continue; }
            _businessFixtures.Add(new JObject { ["purpose"] = "explicit legal native input for an actually arrived buyer; no economic acquisition claim", ["item"] = BusinessItem(item) });
            Place(EmporiumEntry.Instance.backInvinvElement, item, "business-natural-customer-counter-" + item.uniqueId);
            BusinessCommit(item, "legitimate factory input admitted onto actual natural customer's counter");
            foreach (object? wait in BusinessDialogueIdle("business.actual-counter-inspection")) yield return wait;
            if (!Store.CanSellThisItem(item) || !NegociationUIManager.Instance.GetSellableItem().Contains(item))
            { GraylineTransfer(item, EmporiumEntry.Instance.invElement, "business-unsold-guarded-fixture"); continue; }
            bool useDeal = !_businessDealPassed && !client.alwaysRejectBadBargain && BargainUIManager.Instance.CanOfferMarkup();
            int percent = 5, chanceBefore = 0, chanceAfter = 0, expectedBefore = 0, expectedAfter = 0, powerBefore = 0;
            long beforeQuote = item.GetNegociatedValue();
            BusinessRequire("business.independent-pre-bargain-quote." + item.uniqueId, BusinessIndependentPrice(item), beforeQuote,
                beforeQuote > 0 && BusinessIndependentPrice(item) == beforeQuote && item.FindAllChildItems().Count == 0 && item.GetComplexeItemValue() == 0);
            if (useDeal)
            {
                BusinessOpen(_businessDeal); yield return null;
                var battery = BusinessBattery(_businessDeal);
                var activate = BusinessButtons(_businessDeal).Single(b => b.text == "Activate");
                int faction = StoreReputation.GetStoreReputation(client.clientFaction)?.GetReputation() ?? 0;
                int repBonus = faction / 6; if (repBonus < 0) repBonus *= 2;
                int eventBonus = string.IsNullOrEmpty(client.eventSourceId) ? 0 : 15;
                if (90 + repBonus + eventBonus >= 100) percent = 20;
                expectedBefore = Math.Max(0, Math.Min(100, 100 - percent * 2 + repBonus + eventBonus));
                expectedAfter = Math.Max(0, Math.Min(100, 100 - percent * 2 + repBonus + eventBonus + 10));
                chanceBefore = BargainUIManager.Instance.GetBargainSuccessChance(BargainType.Markup, percent);
                powerBefore = BusinessEnergy(battery);
                BusinessRequire("business.dealmaker-native-ready." + item.uniqueId, "Unmodified machine, real present buyer, no prior effect, enough actual25kJ and independent chance before activation", new
                {
                    client = client.identifier, faction, repBonus, eventBonus, percent, expectedBefore, chanceBefore, powerBefore,
                    power = MachineryHelper.GetMachinePowerUsage(_businessDeal), quality = ModuleHelper.GetAjustedQuality(_businessDeal),
                    Store.dealMakerActivatedThisCustomer, Store.dealMakerBargainBonus
                }, MachineDealMaker.CanActivate(_businessDeal, MachineHelper.GetBatterySlot(_businessDeal)) && !Store.dealMakerActivatedThisCustomer &&
                    Store.dealMakerBargainBonus == 0 && ModuleHelper.GetAjustedQuality(_businessDeal) == 0 &&
                    MachineryHelper.GetMachinePowerUsage(_businessDeal) == 25 && powerBefore >= 25 && expectedAfter > expectedBefore && chanceBefore == expectedBefore);
                BusinessClick(_businessDeal, activate, "dealmaker.actual-activate." + item.uniqueId);
                yield return null;
                chanceAfter = BargainUIManager.Instance.GetBargainSuccessChance(BargainType.Markup, percent);
                BusinessRequire("business.dealmaker-power-and-real-chance." + item.uniqueId, new { energy = powerBefore - 25, bonus = 10, chance = expectedAfter }, new
                { energy = BusinessEnergy(battery), Store.dealMakerBargainBonus, Store.dealMakerActivatedThisCustomer, chanceAfter },
                    BusinessEnergy(battery) == powerBefore - 25 && Store.dealMakerBargainBonus == 10 && Store.dealMakerActivatedThisCustomer && chanceAfter == expectedAfter);
                // The real control may disable itself; either an actual repeated
                // callback or the native disabled control must consume no power.
                if (!activate.isDisabled) BusinessClick(_businessDeal, activate, "dealmaker.repeat-activate." + item.uniqueId);
                BusinessRequire("business.dealmaker-once-per-customer." + item.uniqueId, powerBefore - 25, BusinessEnergy(battery),
                    BusinessEnergy(battery) == powerBefore - 25 && Store.dealMakerBargainBonus == 10 && !MachineDealMaker.CanActivate(_businessDeal, MachineHelper.GetBatterySlot(_businessDeal)));
                _businessDeal.CloseContentWindow();
                BargainUIManager.Instance.OpenUI();
                BusinessAction("actual-BargainUI-OfferMarkup", new { uid = item.uniqueId, client = client.identifier, percent, chanceBefore, chanceAfter, expectedBefore, expectedAfter });
                BargainUIManager.Instance.OfferMarkup(percent);
                foreach (object? wait in BusinessDialogueIdle("business.actual-markup-reply")) yield return wait;
                BargainUIManager.Instance.CloseUI();
                if (!ReferenceEquals(Store.currentClientInstance?.GetClientBlueprint(), client)) yield break;
            }
            var markup = item.FindItemFeatureByID("bargainMarkup");
            bool accepted = useDeal && markup != null && !markup.isDisabled && markup.GetCurrentValueMod() == percent && markup.GetValueStage() == ItemFeature.ValueStage.Final;
            if (!Store.CanSellThisItem(item) || !NegociationUIManager.Instance.GetSellableItem().Contains(item))
            { GraylineTransfer(item, EmporiumEntry.Instance.invElement, "business-legitimate-post-bargain-refusal"); yield break; }
            int cash = Store.playerCash, budget = client.GetBudget(), uid = item.uniqueId;
            long quote = BusinessIndependentPrice(item);
            BusinessRequire("business.real-positive-affordable-sale." + uid, quote, item.GetNegociatedValue(),
                quote > 0 && quote <= int.MaxValue && quote == item.GetNegociatedValue() && (!client.useClientBudget || budget >= quote));
            BusinessAction("native-NegociationUIManager-SellMultiple", new { uid, id, client = client.identifier, quote, accepted, chanceBefore, chanceAfter });
            NegociationUIManager.Instance.SellMultiple(new List<GameItem> { item });
            yield return null;
            BusinessRequire("business.actual-paid-sale-ownership." + uid, new { cash = cash + quote, budget = client.useClientBudget ? budget - quote : budget, owned = 0 }, new
            { cash = Store.playerCash, budget = client.GetBudget(), owned = BusinessOwnedUnits(uid), nativeOwned = GeneralHelper.IsItemOwned(item) },
                Store.playerCash == cash + quote && (!client.useClientBudget || client.GetBudget() == budget - quote) &&
                BusinessOwnedUnits(uid) == 0 && !GeneralHelper.IsItemOwned(item));
            BusinessRetire(uid, "independently verified paid native SellMultiple transaction to " + client.identifier + " D" + StoreStation.GetDayCounter());
            BusinessAudit("business.commitments.after-verified-paid-trade." + uid);
            _businessTrades.Add(new JObject { ["uid"] = uid, ["client"] = client.identifier, ["day"] = StoreStation.GetDayCounter(), ["dealActivated"] = useDeal, ["markupAccepted"] = accepted,
                ["percent"] = percent, ["chanceBefore"] = chanceBefore, ["chanceAfter"] = chanceAfter, ["quoteBefore"] = beforeQuote, ["quotePaid"] = quote,
                ["cashBefore"] = cash, ["cashAfter"] = Store.playerCash, ["budgetBefore"] = budget, ["budgetAfter"] = client.GetBudget() });
            if (accepted && quote > beforeQuote)
            {
                BusinessRequire("business.dealmaker-natural-markup-paid-sale", "Actual natural buyer accepted markup after independently verified+10 chance; final feature participated in positive fully paid sale", new { uid, percent, beforeQuote, quote, chanceBefore, chanceAfter },
                    useDeal && chanceAfter == expectedAfter && chanceAfter > chanceBefore && quote > beforeQuote && Store.dealMakerActivatedThisCustomer);
                _businessDealPassed = true;
            }
            foreach (object? wait in BusinessDialogueIdle("business.actual-sale-reply")) yield return wait;
            yield break;
        }
    }

    private IEnumerable<object?> BusinessEndNight(int day, NightObservation observation)
    {
        BusinessRequire("business.close-native-eligibility.D" + day, "Actual native CanEndDay with no locks/cheats", GraylineUiState(),
            Store.CanEndDay() && Store.lockShutterButtonFlag == 0 && !Store.devMode && !Store.isClientBeingArrested && !Store.isHandlingShootout);
        BusinessAction("real-shutter-close.D" + day);
        StoreShutterButton.Instance.OnClick();
        if (StoreUIManager.Instance.closeShutterConfirmationPanel.activeSelf) StoreShutterButton.Instance.EndAndClose();
        foreach (object? wait in NightWait("business.actual-closed.D" + day, () => Store.storeState == PlayerStore.StoreState.CLOSED && Store.currentClientInstance == null &&
            StoreShutterButton.Instance.shutterGameObject.transform.position == StoreShutterButton.Instance.startPosition &&
            StoreShutterButton.Instance.blockerGameObject.transform.localPosition == StoreShutterButton.Instance.blockerStartPosition, observation)) yield return wait;
        var beds = UnityEngine.Object.FindObjectsOfType<StoreBedroll>().Where(b => b.gameObject.activeInHierarchy).ToArray();
        BusinessRequire("business.actual-bedroll.D" + day, 1, beds.Length, beds.Length == 1);
        beds[0].OnClick();
        foreach (object? wait in NightWait("business.actual-end-day-ui.D" + day, () => EndOfDayUIManager.Instance.IsOpen &&
            Reflect.Get(EndOfDayUIManager.Instance, "endDayButton") is TabletButton, observation)) yield return wait;
        var end = (TabletButton)Reflect.Get(EndOfDayUIManager.Instance, "endDayButton")!;
        observation.Invoice = new JArray(Store.storeService.Where(s => s.unlocked && s.isUsing).Select(s => JObject.FromObject(new { s.id, s.cost, s.totalCooldown })));
        observation.InvoiceTotal = Store.storeService.Where(s => s.unlocked && s.isUsing).Sum(s => s.cost);
        observation.Evidence["invoice"] = observation.Invoice;
        BusinessRequire("business.affordable-native-end-day.D" + day, "Actual enabled button and affordable invoice", new { Store.playerCash, observation.InvoiceTotal, end.Interactable },
            end.gameObject.activeInHierarchy && end.Interactable && end.button != null && end.button.IsInteractable() && Store.playerCash >= observation.InvoiceTotal);
        observation.Armed = true;
        BusinessAction("native-end-day-button.D" + day, observation.Invoice);
        end.button!.onClick.Invoke();
        foreach (object? wait in NightWait("business.actual-next-morning.D" + day, () => StoreStation.GetDayCounter() == day + 1 &&
            Store.storeState == PlayerStore.StoreState.MORNING && !StoreUIManager.Instance.blackscreen.gameObject.activeSelf &&
            !StoreUIManager.Instance.savingTMP.gameObject.activeSelf && !EndOfDayUIManager.Instance.IsOpen &&
            AdvCalendarUIManager.Instance.UIPanel.activeSelf && AdvCalendarUIManager.Instance.nightlyReportSection.activeSelf &&
            observation.Events.Count(e => (string?)e["name"] == "OnWakingUpLate") == 1, observation, 60)) yield return wait;
        observation.Armed = false;
        BusinessRequire("business.complete-real-EndNight-and-ES3.D" + day, "One actual EndNight/BeginDay/SaveGame, ten ordered native lifecycle events, normal clock and read-back auto-save", observation.Evidence,
            new[] { "EndNight", "BeginDay", "SaveGame" }.All(n => observation.Entered.GetValueOrDefault(n) == 1 && observation.Returned.GetValueOrDefault(n) == 1) &&
            observation.Events.Select(e => (string)e["name"]!).SequenceEqual(NightEvents) &&
            observation.Events.All(e => (int)e["day"]! == ((string)e["name"]! is "OnWakingUpEarly" or "OnWakingUpLate" ? day + 1 : day)) &&
            observation.AutoSave != null && (bool?)observation.AutoSave["valid"] == true &&
            observation.ObserverErrors.Count == 0 && observation.Errors.Count == 0 && observation.MinimumTimeScale == 1 && observation.MaximumTimeScale == 1);
        var sleep = observation.Events.Single(e => (string?)e["name"] == "OnGoingSleepEarly");
        var charged = observation.Events.Single(e => (string?)e["name"] == "OnHandlingNightlyServicesEarly");
        BusinessRequire("business.actual-night-service-cash.D" + day, observation.InvoiceTotal, (int)sleep["cash"]! - (int)charged["cash"]!,
            (int)sleep["cash"]! - (int)charged["cash"]! == observation.InvoiceTotal);
    }

    private IEnumerable<object?> BusinessDialogueIdle(string name)
    {
        float deadline = Time.realtimeSinceStartup + 60; R.phase = name;
        while (Reflect.Get(DialogUIManager.Instance, "displayingUncompletedDialog") != null || DialogUIManager.Instance.isWaitingForManualAdvance || GraylineChoicesVisible())
        {
            if (Time.realtimeSinceStartup >= deadline) throw new InvalidOperationException(name + " timed out after60s");
            if (GraylineChoicesVisible()) throw new InvalidOperationException(name + ": unexpected real dialogue choice");
            GraylineAdvanceDialogue(); yield return null;
        }
    }
    private IEnumerable<object?> BusinessWait(string name, Func<bool> condition, float timeout)
    {
        float deadline = Time.realtimeSinceStartup + timeout; R.phase = name;
        while (!condition())
        {
            if (Time.realtimeSinceStartup >= deadline) { BusinessRequire(name, "Actual native condition within" + timeout + "s", BusinessCassetteState(), false); }
            yield return null;
        }
        R.Check(name, "Actual native condition reached", BusinessCassetteState(), true);
    }
    private void BusinessRequire(string name, object? expected, object? observed, bool pass, string evidence = "")
    {
        R.Check(name, expected, observed, pass, evidence);
        if (!pass) throw new InvalidOperationException("Required native business assertion failed: " + name);
    }
    private void BusinessAction(string name, object? value = null)
    {
        _businessActions.Add(new JObject { ["name"] = name, ["day"] = StoreStation.GetDayCounter(), ["frame"] = Time.frameCount, ["cash"] = Store.playerCash,
            ["detail"] = value == null ? null : (JToken?)EvidenceJson.Snapshot(value) });
    }
    private GameItem BusinessFixture(string id, string purpose)
    {
        GameItem item = Spawn(id);
        _businessFixtures.Add(new JObject { ["purpose"] = purpose, ["item"] = BusinessItem(item) });
        return item;
    }
    private void BusinessPower(GameItem machine, int energy, bool extended = false)
    {
        GameItem battery = extended ? PreBuiltItemHelper.EnergyCreditExt(energy) : PreBuiltItemHelper.EnergyCredit(energy);
        _businessFixtures.Add(new JObject { ["purpose"] = "explicit native PreBuilt energy fixture, no earned energy claim", ["item"] = BusinessItem(battery), ["preparedEnergy"] = energy });
        Place(MachineHelper.GetBatterySlot(machine), battery, "business-native-power-" + machine.identifier);
        BusinessCommit(battery, "original native energy fixture admitted into " + machine.identifier);
        BusinessRequire("business.native-power-admission." + machine.identifier, energy, BusinessEnergy(battery),
            BusinessEnergy(battery) == energy && GeneralHelper.IsItemOwned(battery));
    }
    private static GameItem BusinessBattery(GameItem machine) => MachineHelper.GetBatterySlot(machine).childItem ?? throw new InvalidOperationException("Original native battery missing: " + machine.identifier);
    private static int BusinessEnergy(GameItem item) => item.GetTagReadonly("power_source_item_energy").GetInt();
    private static JObject BusinessItem(GameItem? item) => item == null ? new JObject { ["missing"] = true } : new JObject
    {
        ["uid"] = item.uniqueId, ["id"] = item.identifier, ["count"] = item.unitCount, ["owned"] = GeneralHelper.IsItemOwned(item),
        ["parent"] = item.parentInventory?.identifier == null ? JValue.CreateNull() : new JValue(item.parentInventory.identifier), ["value"] = item.unitValue, ["energy"] = BusinessEnergy(item)
    };
    private int BusinessOwnedUnits(int uid) => Store.FindAllItem().Where(i => i.uniqueId == uid && GeneralHelper.IsItemOwned(i)).Sum(i => i.unitCount);
    private static MachineryButton[] BusinessButtons(GameItem machine) => machine.contentWindow.handler.GetComponentsInChildren<TreeNodeRender>(true)
        .Select(n => n.element).OfType<MachineryButton>().Distinct().ToArray();
    private static MachineryTabs[] BusinessTabs(GameItem machine) => machine.contentWindow.handler.GetComponentsInChildren<TreeNodeRender>(true)
        .Select(n => n.element).OfType<MachineryTabs>().Distinct().ToArray();
    private static bool BusinessPresetTabsMatch(GameItem machine, string? saved)
    {
        string actual = machine.GetContentTabsSelection();
        return BusinessTabs(machine).Length == 0
            ? saved == null && actual == ""
            : !string.IsNullOrEmpty(saved) && actual == saved;
    }
    private void BusinessOpen(GameItem machine)
    {
        ItemMouseDoubleClickHandler.current.OpenContentAction(machine, new Vector2(Screen.width * .5f, Screen.height * .5f));
        BusinessRequire("business.visible-native-content." + machine.identifier, "Actual owned unlocked content window", new { machine.uniqueId, visible = machine.contentWindow.IsVisible(), machine.contentWindow.isLocked },
            GeneralHelper.IsItemOwned(machine) && machine.contentWindow.IsVisible() && !machine.contentWindow.isLocked);
    }
    private void BusinessClick(GameItem machine, MachineryButton button, string name)
    {
        bool activeRender = button.handler != null && button.handler.gameObject.activeInHierarchy && ReferenceEquals(button.handler.element, button) &&
            machine.contentWindow.handler.GetComponentsInChildren<TreeNodeRender>(false).Any(n => ReferenceEquals(n, button.handler) && ReferenceEquals(n.element, button));
        BusinessRequire("business.enabled-native-control." + name, "Actual active matching native button render, visible window, enabled/nonhidden and no hold requirement", new
        { visible = machine.contentWindow.IsVisible(), button.text, button.isDisabled, button.isHidden, button.requiresHold, activeRender },
            machine.contentWindow.IsVisible() && activeRender && !button.isDisabled && !button.isHidden && !button.requiresHold);
        BusinessAction(name, new { machine = machine.identifier, machine.uniqueId, button = button.text, face = (Reflect.Get(button, "faceNormal") as Sprite)?.name });
        bool selected = button.Select();
        BusinessRequire("business.native-select-dispatched." + name, true, selected, selected, "SelectableElement.Select -> MachineryButton.OnSelect -> actual native callback; no callback replacement or direct invocation.");
    }
    private static long BusinessIndependentPrice(GameItem item)
    {
        long innate = 0, market = 0; double final = 1;
        foreach (var feature in item.itemFeatures.Where(f => !f.isDisabled))
        {
            int mod = feature.GetCurrentValueMod();
            switch (feature.GetValueStage())
            {
                case ItemFeature.ValueStage.Market: market += mod; break;
                case ItemFeature.ValueStage.Final: final *= Math.Max(0, 1 + mod / 100.0); break;
                default: innate += mod; break;
            }
        }
        // Funding candidates are ordinary non-water, childless, non-power
        // items; the guard above rejects other economic components.
        return Math.Max(0, (long)Math.Round((Math.Max(0, item.unitValue * (1 + innate / 100.0)) * Math.Max(0, 1 + market / 100.0) + item.lateUnitValue) * final));
    }
    private GameItem BusinessFind(int uid, string id)
    {
        var candidates = Store.FindAllItem().Concat(new[] { EmporiumEntry.Instance.vendingMachineElement.childItem, EmporiumEntry.Instance.vendingFountainElement.childItem, EmporiumEntry.Instance.cassettePlayer })
            .Where(i => i != null && i.uniqueId == uid).Distinct().ToArray();
        BusinessRequire("business.loaded-original-UID." + id, new { uid, id, count = 1 }, candidates.Select(BusinessItem).ToArray(),
            candidates.Length == 1 && candidates[0].identifier == id && GeneralHelper.IsItemOwned(candidates[0]));
        return candidates[0];
    }
    private IEnumerable<GameItem> BusinessGraphItems()
    {
        var fixedRoots = new[] { EmporiumEntry.Instance.cassettePlayer, EmporiumEntry.Instance.vendingMachineElement.childItem, EmporiumEntry.Instance.vendingFountainElement.childItem };
        return Store.FindAllItem().Concat(fixedRoots.Where(i => i != null).SelectMany(i => new[] { i }.Concat(i.FindAllChildItems()))).Distinct();
    }
    private void BusinessCommit(GameItem root, string purpose)
    {
        foreach (var item in new[] { root }.Concat(root.FindAllChildItems()).Distinct())
        {
            BusinessRequire("business.commit-original-admitted-UID." + item.uniqueId, "Legally admitted owned native UID with positive count", BusinessItem(item),
                item.uniqueId != 0 && item.unitCount > 0 && item.parentInventory != null && GeneralHelper.IsItemOwned(item));
            if (_businessCommitments.TryGetValue(item.uniqueId, out var previous))
                BusinessRequire("business.commitment-not-redefined." + item.uniqueId, new { previous.Id, previous.Count }, BusinessItem(item),
                    !_businessRetirements.ContainsKey(item.uniqueId) && previous.Id == item.identifier && previous.Count == item.unitCount);
            else _businessCommitments.Add(item.uniqueId, (item.identifier, item.unitCount, purpose));
        }
    }
    private void BusinessRetire(int uid, string verifiedSale)
    {
        BusinessRequire("business.retire-only-original-consumed-UID." + uid, "Previously committed UID, not already retired, now no owned instance after independent sale ledger passed", new
        { uid, verifiedSale, committed = _businessCommitments.ContainsKey(uid), retired = _businessRetirements.ContainsKey(uid), owned = BusinessGraphItems().Count(i => i.uniqueId == uid && GeneralHelper.IsItemOwned(i)) },
            _businessCommitments.ContainsKey(uid) && !_businessRetirements.ContainsKey(uid) &&
            BusinessGraphItems().All(i => i.uniqueId != uid || !GeneralHelper.IsItemOwned(i)));
        _businessRetirements.Add(uid, verifiedSale);
    }
    private JArray BusinessCommitmentSnapshot() => new(_businessCommitments.OrderBy(pair => pair.Key).Select(pair => new JObject
    {
        ["uid"] = pair.Key, ["id"] = pair.Value.Id, ["count"] = pair.Value.Count, ["purpose"] = pair.Value.Purpose,
        ["retiredByVerifiedSale"] = _businessRetirements.TryGetValue(pair.Key, out string reason) ? reason : ""
    }));
    private void BusinessAudit(string name)
    {
        var graph = BusinessGraphItems().ToArray(); var rows = new JArray(); bool valid = _businessCommitments.Count > 0;
        foreach (var pair in _businessCommitments.OrderBy(p => p.Key))
        {
            var matches = graph.Where(i => i.uniqueId == pair.Key && GeneralHelper.IsItemOwned(i)).ToArray();
            bool retired = _businessRetirements.ContainsKey(pair.Key);
            bool itemValid = retired ? matches.Length == 0 : matches.Length == 1 && matches[0].identifier == pair.Value.Id && matches[0].unitCount == pair.Value.Count;
            valid &= itemValid;
            rows.Add(new JObject { ["uid"] = pair.Key, ["retired"] = retired, ["valid"] = itemValid, ["ownedInstances"] = new JArray(matches.Select(BusinessItem)) });
        }
        BusinessRequire(name, BusinessCommitmentSnapshot(), rows, valid,
            "Fixed immutable UID/ID/count denominator from legal admission, including original core/battery/cassette child trees and every old empty bottle. Retired UIDs require earlier independent cash/sale/ownership proof. Never derive expected inventory from current live items.");
    }
    private static bool BusinessAudioPlaying() => (bool)Reflect.Get(Reflect.Get(AudioManager.Instance, "audioSourceMusic")!, "isPlaying")!;
    private static JObject BusinessCassetteState()
    {
        var item = EmporiumEntry.Instance.cassettePlayer;
        var deck = MachineCassettePlayer.GetCassetteInventory();
        return new JObject
        {
            ["uid"] = item.uniqueId, ["state"] = item.GetTagReadonly("CASETTE_PLAYER_STATE_INT").GetInt(),
            ["index"] = item.GetTagReadonly("CURRENT_PLAYING_CASSETTE_INDEX_TAG").GetInt(), ["track"] = item.GetTagReadonly("CURRENT_PLAYING_TRACK_ID_TAG").GetString(),
            ["deck"] = new JArray(deck.childItems.Select(i => new JObject { ["uid"] = i.uniqueId, ["id"] = i.identifier, ["track"] = i.GetTagReadonly("CASSETTE_CONTENT").GetString(), ["owned"] = GeneralHelper.IsItemOwned(i) }))
        };
    }
    private JObject BusinessSnapshot()
    {
        JObject Machine(GameItem item) => new()
        {
            ["uid"] = item.uniqueId, ["id"] = item.identifier, ["owned"] = GeneralHelper.IsItemOwned(item),
            ["battery"] = BusinessItem(BusinessBattery(item)), ["deposit"] = item.GetTagReadonly("VENDING_CASH").GetInt()
        };
        var fountain = Machine(_businessFountain);
        fountain["water"] = new JArray(new[] { 1, 2, 3 }.Select(index =>
        {
            GameItem? item = ((GameSlotInventory)MachineHelper.GetSlot(_businessFountain, index)).childItem;
            return new JObject { ["slot"] = index, ["item"] = BusinessItem(item), ["volume"] = item == null ? 0 : WaterHelper.GetTotalVolume(item), ["purity"] = item == null ? 0 : WaterHelper.GetWaterPurity(item) };
        }));
        return new JObject
        {
            ["day"] = StoreStation.GetDayCounter(), ["runId"] = Store.runID, ["slot"] = Store.saveSlotId, ["cash"] = Store.playerCash,
            ["vending"] = Machine(_businessVending), ["fountain"] = fountain, ["dealMaker"] = Machine(_businessDeal),
            ["coordinator"] = new JObject { ["uid"] = _businessCoordinator.uniqueId, ["owned"] = GeneralHelper.IsItemOwned(_businessCoordinator),
                ["raw"] = _businessCoordinator.GetTagReadonly("STORAGE_COORDINATOR_PRESETS_STRING").GetString(),
                ["presets"] = JToken.FromObject(MachineStorageCoordinator.LoadPresets(_businessCoordinator)) },
            ["commitments"] = BusinessCommitmentSnapshot(), ["secureBay"] = BusinessItem(_businessBay), ["tabTarget"] = new JObject { ["uid"] = _businessTabTarget.uniqueId, ["id"] = _businessTabTarget.identifier,
                ["owned"] = GeneralHelper.IsItemOwned(_businessTabTarget), ["rememberedTabs"] = _businessTabTarget.GetTagReadonly("CONTENT_TABS_STRING").GetString() }, ["cassette"] = BusinessCassetteState()
        };
    }
}

