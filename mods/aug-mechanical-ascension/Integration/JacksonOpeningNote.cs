using AugMechanicalAscension.Official;
using ProbablyStolen.ModSupport;
using HarmonyLib;
using AugMechanicalAscension.Domain;

namespace AugMechanicalAscension.Integration;

// One Mod introduction for every new run. Keep its native handnote identity
// and reading action; choose the guidance from the current start type.
internal static class JacksonOpeningNote
{
    internal const string ItemId = "nicokobo.aug.jackson_note";
    private const string TemplateId = "handnote";
    private static readonly LocalizedItemText Description = new("aug.an_introduction_to_this_start_s_mod");
    private static readonly LocalizedItemText Flavor = new("aug.at_least_this_piece_of_paper_is");
    private static bool _registrationStaged;
    private static Action<string>? _log;
    private static UnityEngine.GameObject? _readingPanel;

    internal static bool Install(HarmonyLib.Harmony harmony, Action<string> log)
    {
        _log = log;
        JacksonNoteIconRuntime.Configure(log);
        try
        {
            var read = AccessTools.Method(typeof(HandnoteUIManager),
                nameof(HandnoteUIManager.OpenPanel), [typeof(GameItem)])
                ?? throw new MissingMethodException(nameof(HandnoteUIManager), nameof(HandnoteUIManager.OpenPanel));
            var registration = OfficialItemApi.RegisterItem("nicokobo.aug", ItemId,
                Create, new NativeItemOptions(Name: new("aug.hint"),
                    ShortDescription: Description, FlavorText: Flavor));
            if (registration.Status is not (SubmitStatus.Accepted or SubmitStatus.AlreadyPresent))
                throw new InvalidOperationException($"Opening hint registration rejected: {registration.Status}; {registration.Reason}");
            ModHook.OnNewGameStarted += NewGame;
            ModHook.OnGameLoadedLate += Loaded;
            harmony.Patch(read,
                prefix: new HarmonyMethod(typeof(JacksonOpeningNote), nameof(ReadPrefix)),
                postfix: new HarmonyMethod(typeof(JacksonOpeningNote), nameof(ReadPostfix)));
            _registrationStaged = true;
            log($"[P3] Opening hint registered={registration.Status}; every new start; ordinary/Aug guidance; complete factory text");
            return true;
        }
        catch (Exception ex)
        {
            Uninstall();
            log($"[ERROR] [P3] Opening hint hook failed: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    internal static void Update() => JacksonNoteIconRuntime.TryInstall();

    internal static void LateUpdate() => ReadingWindowDrag.KeepCursor(_readingPanel);

    private static GameItem Create()
    {
        var item = DirectoryMaster.Create<GameItem>(TemplateId);
        if (item == null || item.identifier != TemplateId || item.unitCount != 1)
            throw new InvalidOperationException("Native handnote template unavailable");
        // Match the 32x48 paper icon instead of retaining the template footprint.
        var shape = new GridShapeBuilder().SetData("111111", 2).Build();
        if (shape == null || NativeObject.Handle(shape) == IntPtr.Zero)
            throw new InvalidOperationException("Opening hint shape unavailable");
        item.SetShape(shape);
        item.RemoveAllGameItemType();
        item.SetGameItemType("DOCUMENT");
        item.identifier = ItemId;
        item.mayStackItemFunc = null;
        WriteContents(item, OfficialStoryGame.Store);
        JacksonNoteIconRuntime.AssignOrStage(item);
        return item;
    }

    internal static void Uninstall()
    {
        ModHook.OnNewGameStarted -= NewGame;
        ModHook.OnGameLoadedLate -= Loaded;
        _readingPanel = null; _registrationStaged = false;
    }
    private static void NewGame(string _)
    { var store = OfficialStoryGame.Store; if (store != null) InitialSavePrefix(store); }
    private static void Loaded()
    { var store = OfficialStoryGame.Store; if (store != null) LoadPostfix(store); }
    private static void InitialSavePrefix(PlayerStore __instance)
    {
        if (!_registrationStaged) return;
        try
        {
            var current = OfficialStoryGame.Store;
            var entry = EmporiumEntry.Instance;
            if (current == null || NativeObject.Handle(current) != NativeObject.Handle(__instance) ||
                entry?.invElement == null || NativeObject.Handle(entry.invElement) == IntPtr.Zero)
                throw new InvalidOperationException("New-run inventory unavailable");
            var note = FindOwnedNote(__instance);
            bool granted = note == null;
            if (note == null)
            {
                if (!DirectoryMaster.Has<GameItem>(ItemId))
                    throw new InvalidOperationException("Opening hint blueprint unavailable");
                note = DirectoryMaster.Item(ItemId, true);
                if (note == null || note.identifier != ItemId || note.unitCount != 1)
                    throw new InvalidOperationException("Opening hint factory failed");
                GeneralHelper.SetItemOwned(note, true);
                GraphUtils.EmporiumTryAdd(note);
                if (note.parentInventory == null || !GeneralHelper.IsItemOwned(note))
                    throw new InvalidOperationException("Opening hint was not placed");
            }
            WriteContents(note, __instance);
            _log?.Invoke($"[P3] Opening hint granted={granted}; units=1; startType={(int)__instance.startType}; item={note.uniqueId}; stagedForInitialSave=true");
        }
        catch (Exception ex)
        {
            _log?.Invoke($"[ERROR] [P3] Opening hint grant failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void LoadPostfix(PlayerStore __instance)
    {
        try
        {
            var note = FindOwnedNote(__instance);
            if (note != null) WriteContents(note, __instance);
        }
        catch (Exception ex)
        {
            _log?.Invoke($"[ERROR] [P3] Opening hint refresh failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void ReadPrefix(GameItem __0)
    {
        if (__0?.identifier != ItemId) return;
        try { WriteContents(__0, OfficialStoryGame.Store); }
        catch (Exception ex)
        {
            _log?.Invoke($"[ERROR] [P3] Opening hint text failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void ReadPostfix(HandnoteUIManager __instance, GameItem __0)
    {
        _readingPanel = __0?.identifier == ItemId ? __instance.panel : null;
        if (_readingPanel != null) ReadingWindowDrag.Attach(_readingPanel);
    }

    private static GameItem? FindOwnedNote(PlayerStore store)
    {
        var items = store.FindAllItem(true);
        if (items == null) return null;
        for (int index = 0; index < items.Count; index++)
        {
            var item = items[index];
            if (item != null && NativeObject.Handle(item) != IntPtr.Zero && item.identifier == ItemId &&
                item.unitCount == 1 && GeneralHelper.IsItemOwned(item)) return item;
        }
        return null;
    }

    private static void WriteContents(GameItem note, PlayerStore? store)
    {
        bool aug = store != null && StartDefinition.MatchesNativeStartType((int)store.startType);
        note.name = AugText.Get(ItemId + ".name");
        note.shortDescription = Description.For();
        note.flavorText = Flavor.For();
        var contents = new[]
        {
            (Key: "HANDNOTE_TITLE", Text: note.name),
            (Key: "HANDNOTE_BODY", Text: Body(aug)),
            (Key: "HANDNOTE_SIGN", Text: AugText.Get("aug.jackson"))
        };
        foreach (var (key, text) in contents)
        {
            if (note.state?.GetTag(key) == null)
                throw new InvalidOperationException("Handnote tag missing: " + key);
            note.ModifyTag(key, tag => { tag.Enable(); tag.valueString = text; });
        }
        note.SyncModifiedState();
        foreach (var (key, text) in contents)
            if (note.GetTagReadonly(key)?.valueString != text)
                throw new InvalidOperationException("Handnote text readback mismatch: " + key);
    }

    private static string Body(bool aug) =>
        (aug ? AugText.Get("aug.jackson.aug", AugNumbers.OpeningAndSurvival.DailyHealthLoss,
            AugNumbers.OpeningAndSurvival.DailyExposureGain, AugNumbers.OpeningAndSurvival.MaxExposure,
            AugNumbers.AugInteractions.ThreatExposureGain, AugNumbers.AugInteractions.KillExposureGain,
            AugNumbers.AugInteractions.MinimumPresence, AugNumbers.AugInteractions.MaximumPresence,
            AugNumbers.OpeningAndSurvival.ExposureRepairPerElectronic) : AugText.Get("aug.jackson.ordinary"))
        + "\n\n" + AugText.Get("aug.jackson.common");
}
