using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Nicokobo.ContentNativeProbe;

internal sealed partial class ProbeContext
{
    internal IEnumerable<object?> Dossier()
    {
        R.phase = "dossier-real-opening-and-paid-native-upgrade";
        R.environment["dossierScope"] = "Native new run in the selected ordinary/Aug/Theft family; live opening documents and grid; native WildNetworkUI slot/Use button events; native inventory transfer; separate-process ES3 reload. Programmatic UI events, not mouse interaction. No cash, dimensions, upgrade state or save JSON edits.";
        var entry = EmporiumEntry.Instance;
        var dossier = entry.dossier;
        var openingGrid = dossier.contentWindow?.childElement as GameGridInventory;
        string expectedScenario = O.Family switch
        {
            "dossier" => "", "dossieraug" => "nicokobo.aug.mechanical_ascension.start",
            "dossiertheft" => "nicokobo.definitely_stolen.pickpocket", _ => throw new InvalidOperationException("Unsupported dossier scenario family: " + O.Family)
        };
        var expectedStart = expectedScenario.Length == 0 ? NewGameData.StartType.APPRENTICE : NewGameData.StartType.MODDED;
        DossierRequire("dossier.native-opening", new { freshRun = true, startType = expectedStart.ToString(), scenario = expectedScenario, uid = -101, width = 16, height = 16 }, new
        {
            O.Reload, startType = NewGameData.Instance.startType.ToString(), scenario = NewGameData.Instance.startScenarioId, Store.runID, Store.saveSlotId,
            uid = dossier.uniqueId, grid = openingGrid == null ? null : DossierGridState(openingGrid)
        }, !O.Reload && NewGameData.Instance.startType == expectedStart && NewGameData.Instance.startScenarioId == expectedScenario &&
            dossier.uniqueId == -101 && openingGrid != null && openingGrid.inventoryShape.width == 16 && openingGrid.inventoryShape.height == 16);
        var grid = openingGrid!;
        var initialGrid = DossierGridState(grid);
        var initialDocuments = DossierDocuments(grid);
        var ownedDocuments = Store.FindAllItem().Where(item => item.IsGameItemType("DOCUMENT")).ToArray();
        var allItems = Store.FindAllItem(false);
        var mainDocuments = entry.invElement.childItems.Where(item => GeneralHelper.IsItemOwned(item) && item.IsGameItemType("DOCUMENT")).ToArray();
        // Forge only moves direct main-counter children. Native HandleSkipIntro
        // puts tutorial pages inside tutorial_book before storing the whole book.
        var nestedItems = ownedDocuments.Where(item => item.parentInventory?.GetParentItem()?.uniqueId > 0).ToArray();
        var initialNestedDocuments = DossierNestedDocuments();
        DossierRequire("dossier.opening-documents-collected-once", "Unique owned top-level documents in dossier -101, none left directly on the main counter; nested documents retain a real owning item",
            new { documents = initialDocuments, nestedDocuments = initialNestedDocuments,
                ownedDocumentUids = ownedDocuments.Select(item => item.uniqueId).OrderBy(uid => uid).ToArray(),
                mainDocumentUids = mainDocuments.Select(item => item.uniqueId).ToArray() },
            initialDocuments.Count > 0 && mainDocuments.Length == 0 && ownedDocuments.Length == initialDocuments.Count + nestedItems.Length &&
            ownedDocuments.All(item => item.uniqueId > 0 && item.unitCount > 0 && allItems.Count(other => other.uniqueId == item.uniqueId) == 1) &&
            grid.childItems.All(item => item.parentInventory == grid && GeneralHelper.IsItemOwned(item) && item.IsGameItemType("DOCUMENT")) &&
            nestedItems.All(item => allItems.Contains(item.parentInventory.GetParentItem()) &&
                GraphUtils.FindAllChildrenType<GameItem>(item.parentInventory.GetParentItem()).Contains(item)),
            "Forge OpeningDocumentsPatch enumerates invElement.childItems, not all descendants. Native PlayerStore.HandleSkipIntro creates tutorial_book and inserts its tutorial_page children before putting the book in dossier.");

        bool foundUpgrade = Store.networkUpgrade.TryGetValue("DOSSIER_UPGRADE", out var upgrade);
        int cashBefore = Store.playerCash, favorBefore = Store.wildFavor;
        int cost = foundUpgrade ? upgrade!.GetCost() : -1;
        DossierRequire("dossier.native-upgrade-affordable", "Native DOSSIER_UPGRADE ready, locked, no favor, cost 300 covered by real starting cash", new
        {
            foundUpgrade, ready = upgrade?.IsReady(), unlocked = upgrade?.IsUnlocked(), prerequisites = upgrade?.IsHavePrerequisite(),
            cost, cash = cashBefore, favor = favorBefore
        }, foundUpgrade && upgrade!.IsReady() && !upgrade.IsUnlocked() && upgrade.IsHavePrerequisite() && cost == 300 && cashBefore >= cost && favorBefore == 0);

        WildNetworkUI.Open();
        yield return null;
        var openedFrame = Reflect.Get(typeof(WildNetworkUI), "frame") as WildNetworkFrame;
        DossierRequire("dossier.native-network-window", "Native visible WildNetworkUI with its real frame and EventSystem", new
        {
            open = WildNetworkUI.IsOpen, frame = openedFrame != null, eventSystem = EventSystem.current != null
        }, WildNetworkUI.IsOpen && openedFrame != null && openedFrame.gameObject.activeInHierarchy && EventSystem.current != null);
        var frame = openedFrame!;
        try
        {
            WildNetworkSlot? target = frame.gridContent.GetComponentsInChildren<WildNetworkSlot>().SingleOrDefault(slot => slot.id == "DOSSIER_UPGRADE");
            foreach (var tab in frame.tabBar.GetComponentsInChildren<Button>())
            {
                if (target != null) break;
                if (tab.interactable) tab.onClick.Invoke();
                // BuildGrid destroys the previous tab's objects at frame end.
                yield return null;
                target = frame.gridContent.GetComponentsInChildren<WildNetworkSlot>().SingleOrDefault(slot => slot.id == "DOSSIER_UPGRADE");
            }
            DossierRequire("dossier.native-upgrade-slot", "Visible native DOSSIER_UPGRADE slot", target == null ? null : new { target.id, active = target.gameObject.activeInHierarchy },
                target != null && target.gameObject.activeInHierarchy);
            target!.OnPointerClick(new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left });
            yield return null;
            string selectedId = Reflect.Str(typeof(WildNetworkUI), "selectedId");
            DossierRequire("dossier.native-purchase-button", "Selected DOSSIER_UPGRADE and active, interactable native Use button", new
            {
                selectedId, active = frame.useButton.gameObject.activeInHierarchy, frame.useButton.interactable,
                title = frame.infoTitle.text, price = frame.usePrice.text
            }, selectedId == "DOSSIER_UPGRADE" && frame.useButton.gameObject.activeInHierarchy && frame.useButton.interactable);
            frame.useButton.onClick.Invoke();
            yield return null;
        }
        finally { WildNetworkUI.Close(); }

        var upgradedGrid = DossierGridState(grid);
        var expectedGrid = (JObject)initialGrid.DeepClone();
        expectedGrid["width"] = 17;
        DossierRequire("dossier.paid-upgrade-preserves-height-and-grid", expectedGrid, upgradedGrid,
            JToken.DeepEquals(expectedGrid, upgradedGrid) && entry.dossier.contentWindow.childElement == grid,
            "Actual WildNetworkUI.OnUseClicked -> payment -> NetworkUpgrade.Unlock -> native DOSSIER_UPGRADE action -> installed Forge patch.");
        DossierRequire("dossier.native-payment", new { cash = cashBefore - cost, favor = favorBefore, unlocked = true },
            new { cash = Store.playerCash, favor = Store.wildFavor, unlocked = upgrade!.IsUnlocked() },
            Store.playerCash == cashBefore - 300 && Store.wildFavor == favorBefore && upgrade.IsUnlocked());
        var afterUpgrade = DossierDocuments(grid);
        DossierRequire("dossier.upgrade-preserves-documents-and-coordinates", initialDocuments, afterUpgrade,
            JToken.DeepEquals(initialDocuments, afterUpgrade) && grid.childItems.All(item => Store.FindAllItem(false).Count(other => other.uniqueId == item.uniqueId) == 1),
            "Read back actual UID, identifier, count, parent and complete item transform after the paid upgrade.");
        var afterUpgradeNested = DossierNestedDocuments();
        DossierRequire("dossier.upgrade-preserves-nested-documents", initialNestedDocuments, afterUpgradeNested,
            JToken.DeepEquals(initialNestedDocuments, afterUpgradeNested));

        var transferable = grid.childItems.OrderBy(item => item.modifiedShape.width * item.modifiedShape.height).ThenBy(item => item.uniqueId)
            .Select(item => new { item, slot = entry.invElement.TryFindOneValidInventorySlot(item) })
            .FirstOrDefault(candidate => candidate.slot != null && candidate.slot.IsValid() && candidate.slot.inventory == entry.invElement &&
                candidate.slot.targetItem == null && candidate.slot.numTransfer >= candidate.item.unitCount);
        DossierRequire("dossier.real-document-external-slot", "An existing dossier document can move whole to a valid main-counter slot without stacking", transferable == null ? null : new
        {
            transferable.item.identifier, transferable.item.uniqueId, transferable.item.unitCount, destination = transferable.slot.inventory.identifier,
            transferable.slot.numTransfer
        }, transferable != null);
        var transfer = transferable!;
        var movedItem = transfer.item;
        int movedUid = movedItem.uniqueId, movedCount = movedItem.unitCount;
        int transferred = transfer.slot.TryAcceptOnce(movedCount);
        var movedDocument = DossierDocumentState(movedItem);
        DossierRequire("dossier.legitimate-document-move-out", new { uid = movedUid, count = movedCount, transferred = movedCount, outsideDossier = true },
            new { document = movedDocument, transferred },
            transferred == movedCount && movedItem.uniqueId == movedUid && movedItem.unitCount == movedCount && movedItem.parentInventory == entry.invElement &&
            !grid.childItems.Contains(movedItem) && Store.FindAllItem(false).Count(item => item.uniqueId == movedUid) == 1,
            "Native GameGridInventory.TryFindOneValidInventorySlot -> SlotMarker.TryAcceptOnce; no Expel/UncheckedAccept or parent edits in the probe.");
        var remaining = DossierDocuments(grid);
        var expectedRemaining = new JArray(initialDocuments.Where(item => (int)item["uid"]! != movedUid).Select(item => item.DeepClone()));
        DossierRequire("dossier.unmoved-documents-preserved", expectedRemaining, remaining, JToken.DeepEquals(expectedRemaining, remaining));
        var nestedDocuments = DossierNestedDocuments();
        DossierRequire("dossier.move-preserves-nested-documents", initialNestedDocuments, nestedDocuments,
            JToken.DeepEquals(initialNestedDocuments, nestedDocuments));
        R.persistence["dossier"] = new
        {
            Store.runID, initialGrid, upgradedGrid, initialDocuments, remainingDocuments = remaining, movedDocument,
            initialNestedDocuments, nestedDocuments, cashBefore, cost, cashAfter = Store.playerCash, favorAfter = Store.wildFavor, upgradeState = upgrade.state
        };
        R.phase = "dossier-complete-before-native-save";
        // The runner's existing Save() must follow this scenario and bind the
        // actual ES3 bytes; this receipt is only the live pre-save observation.
    }

    internal IEnumerable<object?> DossierReload(JObject baseline)
    {
        R.phase = "dossier-independent-process-native-reload";
        var receipt = baseline["persistence"]?["dossier"] as JObject;
        DossierRequire("dossier.reload-baseline", "Passed " + O.Family + "-save receipt from this real game run", receipt,
            (string?)baseline["mode"] == O.Family + "-save" && (bool?)baseline["passed"] == true && receipt != null &&
            (string?)receipt["runID"] == Store.runID && receipt["movedDocument"] != null && receipt["remainingDocuments"] is JArray && receipt["nestedDocuments"] is JArray);
        var expected = receipt!;
        var loadedGrid = EmporiumEntry.Instance.dossier.contentWindow?.childElement as GameGridInventory;
        DossierRequire("dossier.reload-upgraded-grid", expected["upgradedGrid"], loadedGrid == null ? null : DossierGridState(loadedGrid),
            loadedGrid != null && EmporiumEntry.Instance.dossier.uniqueId == -101 && JToken.DeepEquals(expected["upgradedGrid"], DossierGridState(loadedGrid)));
        var grid = loadedGrid!;
        var upgrade = Store.networkUpgrade["DOSSIER_UPGRADE"];
        DossierRequire("dossier.reload-paid-upgrade", new { cash = expected["cashAfter"], favor = expected["favorAfter"], state = expected["upgradeState"] },
            new { cash = Store.playerCash, favor = Store.wildFavor, state = upgrade.state },
            Store.playerCash == (int)expected["cashAfter"]! && Store.wildFavor == (int)expected["favorAfter"]! && upgrade.IsUnlocked() && upgrade.state == (int)expected["upgradeState"]!);
        var remaining = DossierDocuments(grid);
        DossierRequire("dossier.reload-unmoved-documents-and-coordinates", expected["remainingDocuments"], remaining,
            JToken.DeepEquals(expected["remainingDocuments"], remaining));
        var nestedDocuments = DossierNestedDocuments();
        DossierRequire("dossier.reload-nested-documents-and-original-parents", expected["nestedDocuments"], nestedDocuments,
            JToken.DeepEquals(expected["nestedDocuments"], nestedDocuments),
            "Actual nested documents, including tutorial pages, must retain their original owning item UID, quantity and internal position after native LoadGame.");
        int movedUid = (int)expected["movedDocument"]!["uid"]!;
        var moved = Store.FindAllItem(false).Where(item => item.uniqueId == movedUid).ToArray();
        JToken? movedState = moved.Length == 1 ? DossierDocumentState(moved[0]) : null;
        DossierRequire("dossier.reload-does-not-recollect-opening-document", expected["movedDocument"], new { matches = moved.Length, document = movedState },
            moved.Length == 1 && moved[0].parentInventory == EmporiumEntry.Instance.invElement &&
            !grid.childItems.Contains(moved[0]) && JToken.DeepEquals(expected["movedDocument"], movedState),
            "Fresh-process native LoadGame must leave the deliberately removed original document outside dossier -101, with the same UID, quantity and position.");
        var initialUids = ((JArray)expected["initialDocuments"]!).Concat((JArray)expected["initialNestedDocuments"]!)
            .Select(item => (int)item["uid"]!).OrderBy(uid => uid).ToArray();
        var currentOriginals = Store.FindAllItem(false).Where(item => initialUids.Contains(item.uniqueId)).OrderBy(item => item.uniqueId).ToArray();
        DossierRequire("dossier.reload-original-uids-unique", initialUids, currentOriginals.Select(item => item.uniqueId).ToArray(),
            initialUids.SequenceEqual(currentOriginals.Select(item => item.uniqueId)));
        R.persistence["dossier"] = new
        {
            Store.runID, upgradedGrid = DossierGridState(grid), remainingDocuments = remaining, nestedDocuments, movedDocument = movedState,
            cashAfter = Store.playerCash, favorAfter = Store.wildFavor, upgradeState = upgrade.state
        };
        yield return null;
    }

    private static JObject DossierGridState(GameGridInventory grid) => JObject.FromObject(new
    {
        grid.inventoryShape.minX, grid.inventoryShape.minY, grid.inventoryShape.width, grid.inventoryShape.height,
        grid.inventoryShape.orientation, grid.inventoryShape.flipped
    });
    private static JObject DossierDocumentState(GameItem item) => JObject.FromObject(new
    {
        id = item.identifier, uid = item.uniqueId, count = item.unitCount, owned = GeneralHelper.IsItemOwned(item),
        parentUid = item.parentInventory?.GetParentItem()?.uniqueId ?? 0, parentId = item.parentInventory?.GetParentItem()?.identifier ?? "",
        parentStorage = item.parentInventory?.identifier ?? "",
        item.modifiedShape.minX, item.modifiedShape.minY, item.modifiedShape.width, item.modifiedShape.height,
        item.modifiedShape.orientation, item.modifiedShape.flipped
    });
    private static JArray DossierDocuments(GameGridInventory grid) => new(grid.childItems.OrderBy(item => item.uniqueId).Select(DossierDocumentState));
    private JArray DossierNestedDocuments() => new(Store.FindAllItem()
        .Where(item => item.IsGameItemType("DOCUMENT") && item.parentInventory?.GetParentItem()?.uniqueId > 0)
        .OrderBy(item => item.uniqueId).Select(DossierDocumentState));
    private void DossierRequire(string name, object? expected, object? observed, bool passed, string evidence = "")
    {
        R.Check(name, expected, observed, passed, evidence);
        if (!passed) throw new InvalidOperationException(name + " failed; stop before generating a successful save baseline");
    }
}
