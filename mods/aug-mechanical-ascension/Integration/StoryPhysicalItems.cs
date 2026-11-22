using TMPro;
using AugMechanicalAscension.Official;
using AugMechanicalAscension.Domain;
using HarmonyLib;

namespace AugMechanicalAscension.Integration;

internal static class StoryPhysicalItems
{
    internal static Func<GameItem, StoryState?>? Resolve;
    internal static Action<GameItem>? ReadCompleted;
    internal static Action<InspectionUIManager, GameItem>? InspectCompleted;
    private static Action<string>? _log;
    internal static bool IsEvidence(string id) => id.StartsWith(StoryEvidence.Prefix, StringComparison.Ordinal);
    internal static bool Register(Action<string> log)
    {
        _log = log;
        StoryIconAtlas.Configure(log);
        foreach (var key in StoryEvidence.Keys)
        {
            var captured = key;
            var result = OfficialItemApi.RegisterItem("nicokobo.aug", StoryEvidence.Prefix + key, () => Create(captured));
            if (result.Status is not (SubmitStatus.Accepted or SubmitStatus.AlreadyPresent))
            { log("[Story] Evidence registration rejected: " + key); return false; }
        }
        return true;
    }
    internal static void InstallReadingHooks()
    {
        var guard = new HarmonyLib.Harmony("nicokobo.aug.story.reading");
        try
        {
            var read = AccessTools.Method(typeof(HandnoteUIManager), nameof(HandnoteUIManager.OpenPanel), [typeof(GameItem)]);
            var inspect = AccessTools.Method(typeof(InspectionUIManager), nameof(InspectionUIManager.OnInspectItem), [typeof(GameItem)]);
            var tooltip = AccessTools.Method(typeof(InspectableHelper), nameof(InspectableHelper.CreateInspectableTooltip), [typeof(RichTextBuilder), typeof(GameItem)]);
            foreach (var method in new[] { read, inspect, tooltip })
                if (method == null || !OfficialHookApi.TryValidateNativeTarget(method, out _))
                    throw new InvalidOperationException("Story reading native target unavailable");
            ModHook.OnNewGameStarted += NewGame;
            guard.Patch(read!, prefix: new HarmonyMethod(typeof(StoryPhysicalItems), nameof(ReadPrefix)),
                postfix: new HarmonyMethod(typeof(StoryPhysicalItems), nameof(ReadPostfix)));
            guard.Patch(inspect!, postfix: new HarmonyMethod(typeof(StoryPhysicalItems), nameof(InspectPostfix)));
            guard.Patch(tooltip!, prefix: new HarmonyMethod(typeof(StoryPhysicalItems), nameof(InspectableTooltipPrefix)));
        }
        catch { Uninstall(); guard.UnpatchAll(guard.Id); throw; }
    }
    // Hide the native tool hint while retaining the tag needed for magnifier targeting.
    private static bool InspectableTooltipPrefix(GameItem __1) => __1 == null || !IsEvidence(__1.identifier);
    private static GameItem Create(string key)
    {
        var item = DirectoryMaster.Create<GameItem>("handnote") ?? throw new InvalidOperationException("Handnote factory unavailable");
        try
        {
            item.identifier = StoryEvidence.Prefix + key;
            var size = StoryEvidence.Size(key);
            item.SetShape(new GridShapeBuilder().SetData(new string('1', size.Width * size.Height), size.Width).Build());
            StoryIconAtlas.AssignOrStage(item, key);
            if (key == StoryJournal.Key)
            {
                item.RemoveAllGameItemType();
                item.SetGameItemType("DOCUMENT");
            }
            if (key == "broken")
            {
                item.RemoveAllGameItemType();
                item.SetGameItemType("CYBERNETIC");
                item.SetGameItemType("DOCUMENT");
            }
            if (key is "broken" or "photo")
            {
                item.EnableTag("INSPECTABLE_TAG");
                if (!item.IsTag("INSPECTABLE_TAG")) throw new InvalidOperationException("Evidence inspection tag unavailable: " + key);
            }
            item.mayStackItemFunc = null;
            item.unitValue = 0; item.unitBaseValue = 0;
            Write(item, null);
            return item;
        }
        catch { item.Destroy(); throw; }
    }
    private static bool ReadPrefix(GameItem __0)
    {
        StoryJournalWindow.Close();
        if (__0 == null || !IsEvidence(__0.identifier)) return true;
        if (__0.identifier == StoryJournal.ItemId)
        {
            StoryJournalWindow.Open(__0);
            return false;
        }
        var state = Resolve?.Invoke(__0);
        if (state != null) Write(__0, state);
        return true;
    }
    private static void ReadPostfix(HandnoteUIManager __instance, GameItem __0, bool __runOriginal)
    {
        if (!__runOriginal || __0 == null || !IsEvidence(__0.identifier) || __instance.panel == null || !__instance.panel.activeInHierarchy) return;
        UnityEngine.Canvas.ForceUpdateCanvases();
        string body = __0.state.GetTag("HANDNOTE_BODY").valueString;
        string signature = __0.state.GetTag("HANDNOTE_SIGN").valueString;
        var labels = __instance.panel.GetComponentsInChildren<TextMeshProUGUI>(true);
        var text = labels.FirstOrDefault(x => x.text == body);
        var sign = labels.FirstOrDefault(x => x.text == signature);
        if (text != null)
        {
            var rect = text.rectTransform;
            float height = rect.rect.height;
            if (sign != null)
            {
                var signTop = sign.rectTransform.TransformPoint(new UnityEngine.Vector3(0, sign.rectTransform.rect.yMax, 0));
                height = Math.Min(height, rect.rect.yMax - rect.InverseTransformPoint(signTop).y - 12);
            }
            float width = rect.rect.width - text.margin.x - text.margin.z;
            float needed = text.GetPreferredValues(body, width, 0).y;
            if (needed > height - text.margin.y - text.margin.w)
            {
                StoryJournalWindow.Open(__0, body);
                if (StoryJournalWindow.ReadingVisible) ReadCompleted?.Invoke(__0);
                return;
            }
        }
        ReadCompleted?.Invoke(__0);
    }
    private static void InspectPostfix(InspectionUIManager __instance, GameItem __0, bool __runOriginal)
    {
        if (__runOriginal && __0 != null && ReferenceEquals(__instance.currentInspectingItem, __0) &&
            (__0.identifier == StoryEvidence.Prefix + "broken" || __0.identifier == StoryEvidence.Prefix + "photo") &&
            __instance.panel != null && __instance.panel.activeInHierarchy)
            InspectCompleted?.Invoke(__instance, __0);
    }
    internal static void Uninstall() => ModHook.OnNewGameStarted -= NewGame;
    private static void NewGame(string _)
    {
        var store = OfficialStoryGame.Store;
        if (store != null) InitialSavePrefix(store);
    }
    private static void InitialSavePrefix(PlayerStore __instance)
    {
        if (__instance == null || !StartDefinition.MatchesNativeStartType((int)__instance.startType)) return;
        GameItem? journal = null;
        try
        {
            if (OfficialStoryGame.Store == null || NativeObject.Handle(OfficialStoryGame.Store) != NativeObject.Handle(__instance)) return;
            if (OfficialInventoryApi.CaptureRunItems(__instance).Any(x => x.identifier == StoryJournal.ItemId && OfficialInventoryApi.IsPlayerOwned(x))) return;
            var inventory = (EmporiumEntry.Instance?.dossier?.contentWindow?.childElement as GameGridInventory);
            if (inventory == null) throw new InvalidOperationException("Opening journal dossier inventory unavailable");
            journal = DirectoryMaster.Item(StoryJournal.ItemId, true) ?? throw new InvalidOperationException("Opening journal factory unavailable");
            if (journal.identifier != StoryJournal.ItemId || journal.unitCount != 1 || journal.uniqueId <= 0 || journal.parentInventory != null)
                throw new InvalidOperationException("Opening journal factory identity mismatch");
            var plan = OfficialInventoryPlacementApi.PlanWholeGrid(inventory, [journal]);
            if (plan == null || !WorkshopAchievementDelivery.Apply(inventory, plan))
            {
                if (!WorkshopAchievementDelivery.Recall([journal])) throw new InvalidOperationException("Opening journal placement unconfirmed");
                throw new InvalidOperationException("Make a 2x3 space in the dossier for the story journal");
            }
            _log?.Invoke($"[Story] Opening journal granted; uid={journal.uniqueId}; destination=dossier; stagedForInitialSave=true");
        }
        catch (Exception ex) { _log?.Invoke("[ERROR] [Story] Opening journal: " + ex.Message); }
        finally
        {
            try { if (journal != null && journal.parentInventory == null) journal.Destroy(); }
            catch (Exception ex) { _log?.Invoke("[WARN] [Story] Detached journal cleanup: " + ex.Message); }
        }
    }
    internal static void Write(GameItem item, StoryState? state)
    {
        string key = item.identifier[StoryEvidence.Prefix.Length..];
        if (key == "broken")
        {
            item.RemoveGameItemType("JUNK");
            item.SetGameItemType("CYBERNETIC");
            item.SetGameItemType("DOCUMENT");
        }
        item.name = AugText.Get(item.identifier + ".name");
        if (key == "recording") item.name = state?.RecordingRepaired == true
            ? AugText.Get(item.identifier + ".repaired") : AugText.Get(item.identifier + ".damaged");
        item.shortDescription = StoryEvidence.Description(key, state, null);
        string body = StoryEvidence.Body(key, state, null);
        if (key == "copy" && state?.Choice("E07") == "withhold_name")
            body = body.Replace(AugText.Get("aug.grayline"), AugText.Get("aug.an_unnamed_supplier"));
        string appendix = state == null ? "" : StoryEvidence.OvernightAppendix(key, state, StoreStation.GetDayCounter(), null);
        if (appendix.Length > 0 && state?.EndingId == null && key == "investigation")
            body = body.Replace(AugText.Get("aug.the_result_hasn_t_been_read_yet"), "");
        if (appendix.Length > 0) body += "\n\n" + appendix;
        var entries = new[] { ("HANDNOTE_TITLE", item.name), ("HANDNOTE_BODY", body), ("HANDNOTE_SIGN", AugText.Get("aug.personal_evidence")) };
        foreach (var entry in entries)
        {
            if (item.state?.GetTag(entry.Item1) == null) throw new InvalidOperationException("Missing handnote tag");
            var value = entry.Item2;
            item.ModifyTag(entry.Item1, tag => { tag.Enable(); tag.valueString = value; });
        }
        item.SyncModifiedState();
        foreach (var entry in entries)
            if (item.state.GetTag(entry.Item1)?.valueString != entry.Item2)
                throw new InvalidOperationException("Evidence text readback mismatch: " + entry.Item1);
    }
}
