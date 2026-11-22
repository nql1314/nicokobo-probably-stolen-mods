using System.Globalization;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Nicokobo.ContentNativeProbe;

internal sealed partial class ProbeContext
{
    // Run alone in a fresh ordinary pickpocket career process (Family=theft).
    // This scenario owns no fake RunState, loot algorithm or UI replacement.
    // Its three awards use BeginTheft/StopTheft; discards use the actual button.
    internal IEnumerable<object?> ReviewTheftLootSelection()
    {
        R.phase = "TT01-real-theft-panel-loot-selection";
        object run = TheftRun();
        ToolsRequire("TT01.normal-qualified-career", true, Reflect.Get(TheftState()!, "Qualified"),
            (bool)Reflect.Get(TheftState()!, "Qualified")! && Store.startScenarioId == "nicokobo.definitely_stolen.pickpocket");
        ToolsRequire("TT01.normal-night-generation", true, Reflect.Call(run, "EnsureNight"), Reflect.Get(TheftState()!, "Night") != null);
        ToolsRequire("TT01.fresh-no-loot", 0, ToolsLootIds(), ToolsLootIds().Length == 0);
        if (Store.storeState != PlayerStore.StoreState.CLOSED) StoreShutterButton.Instance.EndAndClose();
        foreach (object? wait in ToolsAwait(() => Store.storeState == PlayerStore.StoreState.CLOSED, "normal shutter close")) yield return wait;
        var location = ModHelper.GetAfterhourLocation("nicokobo.definitely_stolen.lower_night_market")
            ?? throw new InvalidOperationException("TT01 registered lower-night-market is missing");
        MapUIManager.Instance.Travel(location);
        foreach (object? wait in ToolsAwait(() => MapUIManager.Instance.Current?.Id == location.Id && (bool)Reflect.Get(run, "Browsing")!, "normal location arrival")) yield return wait;
        object panel = Reflect.Get(ModLoader.Mods.Single(m => m.Manifest.ID == "nicokobo.definitely_stolen").Instance, "Panel")!;
        Reflect.Call(panel, "Refresh");
        var emptyDropdown = ToolsLootDropdown(panel);
        var emptyButton = ToolsAbandonButton(panel);
        ToolsRequire("TT01.empty-list-placeholder", new { options = 1, abandon = false },
            new { options = emptyDropdown.dropdown.options.Select(o => o.text).ToArray(), abandon = emptyButton.button.interactable },
            emptyDropdown.dropdown.options.Count == 1 && !emptyButton.button.interactable && ToolsPanelLootIds(panel).Length == 0);
        int revision = Reflect.Int(TheftState()!, "Revision");
        emptyButton.button.onClick.Invoke();
        ToolsRequire("TT01.empty-button-no-op", revision, Reflect.Int(TheftState()!, "Revision"), Reflect.Int(TheftState()!, "Revision") == revision);

        string[] targets = Npcs().Where(n => Reflect.Int(n, "Region") == 0).OrderBy(n => Reflect.Int(n, "Position"))
            .Select(n => Reflect.Str(n, "Key")).ToArray();
        var mode = Enum.Parse(Reflect.Type("DefinitelyStolen.Domain.TheftMode"), "Timing");
        Reflect.Call(run, "SelectMode", mode);
        var awards = new List<GameItem>();
        foreach (string key in targets)
        {
            int[] before = ToolsLootIds();
            bool begun = (bool)Reflect.Call(run, "BeginTheft", key)!;
            ToolsRequire("TT01.begin." + key, true, begun, begun);
            // The first attempt for every independent target has its ordinary
            // success zone centered at .5; no probabilities/config are changed.
            Reflect.Call(run, "StopTheft", .5d);
            object receipt = Reflect.Get(TheftState()!, "LastAction")!;
            int uid = Reflect.Int(receipt, "ItemUniqueId");
            GameItem? award = ToolsLiveItems().SingleOrDefault(item => item.uniqueId == uid);
            int[] after = ToolsLootIds();
            ToolsRequire("TT01.native-success." + key, new { outcome = "Success", newLoot = 1, nativeObject = true },
                new { receipt = Reflect.Json(receipt), before, after, item = award == null ? null : ItemState(award) },
                Reflect.Str(receipt, "Outcome") == "Success" && !(bool)Reflect.Get(receipt, "Debug")! &&
                    after.Length == before.Length + 1 && after.Except(before).SequenceEqual(new[] { uid }) && award != null);
            awards.Add(award!);
            yield return null;
        }
        Reflect.Call(panel, "Refresh");
        int[] initialIds = ToolsLootIds();
        ToolsRequire("TT01.three-distinct-real-loot-uids", 3, initialIds, initialIds.Length == 3 && initialIds.Distinct().Count() == 3);
        var dropdown = ToolsLootDropdown(panel);
        dropdown.SetIndex(1, notify: true);
        int selectedUid = initialIds[1];
        ToolsRequire("TT01.non-first-selection", selectedUid, ToolsSelectedLootId(panel), ToolsSelectedLootId(panel) == selectedUid);
        float selectedAt = Time.realtimeSinceStartup;
        foreach (object? wait in ToolsAwait(() => Time.realtimeSinceStartup - selectedAt > .35f, "two periodic refreshes", 3f)) yield return wait;
        Reflect.Call(panel, "Refresh");
        ToolsRequire("TT01.periodic-refresh-keeps-uid", selectedUid,
            new { uid = ToolsSelectedLootId(panel), elapsed = Time.realtimeSinceStartup - selectedAt, index = dropdown.GetIndex() },
            ToolsSelectedLootId(panel) == selectedUid && dropdown.GetIndex() == 1,
            "Actual production Tick runs during >0.35s wait, followed by actual production Refresh; no copied dropdown algorithm.");

        // Remove an unselected first item through the production transaction.
        // The selected UID moves from index one to zero and must stay selected.
        Reflect.Call(run, "Abandon", initialIds[0]);
        Reflect.Call(panel, "Refresh");
        ToolsRequire("TT01.other-item-removal-keeps-uid", selectedUid,
            new { selected = ToolsSelectedLootId(panel), ids = ToolsLootIds(), firstDestroyed = ToolsDestroyed(awards[0]) },
            ToolsSelectedLootId(panel) == selectedUid && ToolsLootIds().SequenceEqual(new[] { selectedUid, initialIds[2] }) && ToolsDestroyed(awards[0]));

        // Destroy only the actual window, leaving the location/stage and run
        // intact, then rebuild through native OpenLocationPanel/PanelObject.
        var oldWindow = (CustomUIWindow)Reflect.Get(panel, "_window")!;
        var oldDropdown = ToolsLootDropdown(panel);
        oldWindow.Close();
        Reflect.Call(MapUIManager.Instance, "OpenLocationPanel", location);
        Reflect.Call(panel, "Refresh");
        dropdown = ToolsLootDropdown(panel);
        ToolsRequire("TT01.control-rebuild-populates-options", ToolsLootIds(),
            new { ids = ToolsPanelLootIds(panel), options = dropdown.dropdown.options.Select(o => o.text).ToArray(), rebuilt = !ReferenceEquals(oldDropdown, dropdown) },
            !ReferenceEquals(oldDropdown, dropdown) && dropdown.dropdown.options.Count == 2 && ToolsPanelLootIds(panel).SequenceEqual(ToolsLootIds()));
        dropdown.SetIndex(1, notify: true);
        selectedUid = initialIds[2];
        selectedAt = Time.realtimeSinceStartup;
        foreach (object? wait in ToolsAwait(() => Time.realtimeSinceStartup - selectedAt > .35f, "rebuilt periodic refreshes", 3f)) yield return wait;
        Reflect.Call(panel, "Refresh");
        ToolsRequire("TT01.rebuilt-periodic-refresh-keeps-uid", selectedUid, ToolsSelectedLootId(panel), ToolsSelectedLootId(panel) == selectedUid);

        int survivorUid = initialIds[1];
        var abandon = ToolsAbandonButton(panel);
        ToolsRequire("TT01.real-abandon-button-enabled", true, abandon.button.interactable, abandon.button.interactable);
        GameItem[] selectedGraph = new[] { awards[2] }.Concat(awards[2].FindAllChildItems()).Distinct().ToArray();
        GameItem[] preserved = ToolsLiveItems().Except(selectedGraph).ToArray();
        int[] expectedLiveUids = preserved.Select(item => item.uniqueId).Where(uid => uid > 0).Distinct().OrderBy(uid => uid).ToArray();
        abandon.button.onClick.Invoke();
        Reflect.Call(panel, "Refresh");
        int[] remaining = ToolsLootIds();
        int[] actualLiveUids = ToolsLiveItems().Select(item => item.uniqueId).Where(uid => uid > 0).Distinct().OrderBy(uid => uid).ToArray();
        int[] abandoned = Reflect.Each(Reflect.Get(TheftNight(), "Abandoned")).Select(item => Reflect.Int(item, "UniqueId")).ToArray();
        ToolsRequire("TT01.button-destroys-only-selected-uid", new { destroyed = selectedUid, survivor = survivorUid },
            new { remaining, abandoned, selectedGraphUids = selectedGraph.Select(i => i.uniqueId).ToArray(), expectedLiveUids, actualLiveUids,
                selectedDestroyed = ToolsDestroyed(awards[2]), survivorDestroyed = ToolsDestroyed(awards[1]) },
            remaining.SequenceEqual(new[] { survivorUid }) && abandoned.Contains(selectedUid) && !abandoned.Contains(survivorUid) &&
                ToolsDestroyed(awards[2]) && !ToolsDestroyed(awards[1]) && ToolsLiveItems().Count(i => i.uniqueId == survivorUid) == 1 && ToolsLiveItems().All(i => i.uniqueId != selectedUid),
            "Actual button.onClick -> production RunController.Abandon -> Commit -> ItemGraphCleanup.Destroy; survivor checked by independent native UID.");
        ToolsRequire("TT01.abandon-preserves-every-unrelated-native-item", expectedLiveUids, actualLiveUids,
            expectedLiveUids.SequenceEqual(actualLiveUids) && selectedGraph.All(ToolsDestroyed) && preserved.All(item => !ToolsDestroyed(item)));

        // The selected item is now gone. A subsequent click must target the
        // sole remaining real UID, then empty-list clicks must be harmless.
        ToolsRequire("TT01.removed-selection-falls-back-safely", survivorUid, ToolsSelectedLootId(panel), ToolsSelectedLootId(panel) == survivorUid);
        ToolsAbandonButton(panel).button.onClick.Invoke();
        Reflect.Call(panel, "Refresh");
        revision = Reflect.Int(TheftState()!, "Revision");
        ToolsAbandonButton(panel).button.onClick.Invoke();
        ToolsRequire("TT01.last-removal-and-empty-no-op", new { loot = 0, abandon = false, revision },
            new { loot = ToolsLootIds(), abandon = ToolsAbandonButton(panel).button.interactable, revision = Reflect.Int(TheftState()!, "Revision") },
            ToolsLootIds().Length == 0 && ToolsPanelLootIds(panel).Length == 0 && !ToolsAbandonButton(panel).button.interactable &&
                ToolsLootDropdown(panel).dropdown.options.Count == 1 && ToolsDestroyed(awards[1]) && Reflect.Int(TheftState()!, "Revision") == revision);
        R.persistence["toolsReviewLootSelection"] = new JObject { ["initialUids"] = new JArray(initialIds), ["abandonedUids"] = new JArray(Reflect.Each(Reflect.Get(TheftNight(), "Abandoned")).Select(i => Reflect.Int(i, "UniqueId"))) };
        R.Skip("TT01.physical-dropdown-pointer-and-locale-change", "This scenario uses native controls and real transactions programmatically; physical mouse selection and changing the saved game language are outside this function.", false);
    }

    private static CustomUIElement ToolsLootDropdown(object panel) => (CustomUIElement)Reflect.Get(panel, "_loot")!;
    private static CustomUIElement ToolsAbandonButton(object panel) => (CustomUIElement)Reflect.Get(panel, "_drop")!;
    private static int[] ToolsPanelLootIds(object panel) => ((string[])Reflect.Get(panel, "_lootIds")!).Select(id => int.Parse(id, CultureInfo.InvariantCulture)).ToArray();
    private static int ToolsSelectedLootId(object panel)
    {
        int index = ToolsLootDropdown(panel).GetIndex();
        int[] ids = ToolsPanelLootIds(panel);
        return index >= 0 && index < ids.Length ? ids[index] : 0;
    }
    private int[] ToolsLootIds() => Reflect.Each(Reflect.Get(TheftNight(), "Loot")).Select(item => Reflect.Int(item, "UniqueId")).ToArray();
    private GameItem[] ToolsLiveItems() => Reflect.Each(Reflect.Call(TheftRun(), "CaptureItems")).Cast<GameItem>().ToArray();
    private static bool ToolsDestroyed(GameItem item) => item is GameItemElement element && element.IsDestroyed();
    private void ToolsRequire(string name, object? expected, object? actual, bool passed, string evidence = "")
    {
        R.Check(name, expected, actual, passed, evidence);
        if (!passed) throw new InvalidOperationException(name + ": expected " + Reflect.Json(expected) + "; actual " + Reflect.Json(actual));
    }
    private static IEnumerable<object?> ToolsAwait(Func<bool> ready, string name, float seconds = 45f)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        while (!ready())
        {
            if (Time.realtimeSinceStartup >= deadline) throw new TimeoutException("TT01 timed out waiting for " + name);
            yield return null;
        }
    }
}
