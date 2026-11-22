

namespace Nicokobo.ContentInterop;

// Shared native lifetime, not a story engine. No file IO, modData or SaveGame calls.
internal static partial class OfficialStoryPresentation
{
    private static readonly HarmonyLib.AccessTools.FieldRef<DialogUIManager, Dialogue> DisplayingDialogue =
        HarmonyLib.AccessTools.FieldRefAccess<DialogUIManager, Dialogue>("displayingUncompletedDialog");
    private const string HookId = "nicokobo.forge.presentation";
    private static Action<string>? _log;
    private static bool _installed, _loading;
    private static long _generation;
    private static IntPtr _storePointer;
    private static string? _run;
    private static int _slot;
    private static readonly OfficialStoryConversationRequests Requests = new();
    private static readonly List<Visit> Visits = [];
    private static DateTime _nextUpdate;
    internal static bool IsAvailable => _installed;
    internal static bool ReadingAvailable => _installed;
    private sealed class Visit(string owner, string request, string actor, OfficialDialogueScript script,
        long generation, string run, int slot, OfficialSupplierDefinition? supplier = null)
    {
        internal readonly string Owner = owner, Request = request, Actor = actor, Run = run;
        internal readonly int Slot = slot;
        internal readonly long Generation = generation;
        internal readonly OfficialDialogueScript Script = script;
        internal readonly OfficialSupplierDefinition? Supplier = supplier;
        internal StoreClient? Client;
        internal bool ArrivalRequested, Retired, Observed, DismissRequested;
        internal AutomaticOffer? Automatic;
        internal readonly OfficialStoryConversationSelection Selection = new();
        internal readonly List<GameItem> Items = [];
        internal readonly List<Dialogue> Dialogues = [];
        internal readonly HashSet<IntPtr> DisabledChoices = [];
        internal readonly List<Action> Callbacks = [];
    }

    internal static void Install(Action<string> log)
    {
        if (_installed) return;
        _log = log;
        var harmony = new HarmonyLib.Harmony(HookId);
        try
        {
            harmony.Patch(OfficialStoryHooks.Require(typeof(DialogUIManager), nameof(DialogUIManager.DisplayChoice), Type.EmptyTypes),
                postfix: new HarmonyLib.HarmonyMethod(typeof(OfficialStoryPresentation), nameof(AfterChoicesDisplayed)));
            harmony.Patch(OfficialStoryHooks.Require(typeof(DialogUIManager), nameof(DialogUIManager.SelectChoice), [typeof(int)]),
                prefix: new HarmonyLib.HarmonyMethod(typeof(OfficialStoryPresentation), nameof(BeforeAutomaticChoiceInput)));
            ModHook.OnGameLoadedInit += BeforeReset;
            ModHook.OnGameLoadedNormal += AfterLoaded;
            ModHook.OnNewGameStarted += NewRun;
            _loading = false;
            _installed = true;
            OfficialPhoneApi.Install(log);
        }
        catch
        {
            harmony.UnpatchAll(harmony.Id);
            ModHook.OnGameLoadedInit -= BeforeReset;
            ModHook.OnGameLoadedNormal -= AfterLoaded;
            ModHook.OnNewGameStarted -= NewRun;
            _installed = false;
            throw;
        }
    }
    private static void AfterLoaded() => _loading = false;
    private static void NewRun(string _) { BeforeReset(); _loading = false; }

    private static void BeforeReset()
    {
        // Invalidate callbacks before touching UI or invoking any native code.
        _generation++; _loading = true;
        try
        {
            var dialog = DialogUIManager.Instance;
            if (dialog != null && Visits.Any(v => References(dialog, v))) dialog.InterruptDialog();
            OfficialStoryUi.CloseAll();
        }
        catch (Exception ex) { Log(ex); }
        // Load/menu teardown owns native object destruction; never touch old wrappers afterwards.
        foreach (var visit in Visits.ToArray()) Release(visit);
        Visits.Clear(); Requests.Clear();
        _storePointer = IntPtr.Zero; _run = null;
    }

    private static PlayerStore? Store()
    {
        if (!_installed || _loading) return null;
        var store = OfficialStoryGame.Store; // never use the constructing Instance getter
        if (store == null || NativeObject.Handle(store) == IntPtr.Zero || string.IsNullOrWhiteSpace(store.runID) || store.saveSlotId < 0) return null;
        if (_storePointer != IntPtr.Zero && (_storePointer != NativeObject.Handle(store) || _run != store.runID || _slot != store.saveSlotId))
        {
            BeforeReset(); _loading = false;
        }
        _storePointer = NativeObject.Handle(store); _run = store.runID; _slot = store.saveSlotId;
        return store;
    }

    internal static OfficialPresentationResult Status(string owner, string request)
    {
        if (!OfficialStoryConversationContract.ValidOwner(owner) || !OfficialStoryConversationContract.Request(request)) return new(OfficialPresentationStatus.Invalid);
        try
        {
            if (Store() == null) return new(OfficialPresentationStatus.Unavailable);
            RefreshVisits(false);
            return new(Requests.Find(owner, request)?.Status ?? OfficialPresentationStatus.Unavailable);
        }
        catch (Exception ex) { Log(ex); return new(OfficialPresentationStatus.Indeterminate, ex.Message); }
    }

    internal static OfficialPresentationResult Queue(string owner, string request, string run, int slot,
        string actor, string sprite, OfficialDialogueScript script, OfficialSupplierDefinition? supplier = null)
    {
        if (!OfficialStoryConversationContract.Owned(owner, actor) || !OfficialStoryConversationContract.Request(request) ||
            script == null || script.OwnerId != owner || string.IsNullOrWhiteSpace(sprite)) return new(OfficialPresentationStatus.Invalid);
        Visit? visit = null;
        bool attempted = false;
        try
        {
            var store = Store();
            if (store == null || store.storeState != PlayerStore.StoreState.OPEN) return new(OfficialPresentationStatus.Unavailable);
            if (store.runID != run || store.saveSlotId != slot) return new(OfficialPresentationStatus.Conflict);
            RefreshVisits(false);
            var previous = Requests.Find(owner, request);
            var status = Requests.Reserve(owner, request, actor, script.Definition.Id);
            if (previous != null || status != OfficialPresentationStatus.Queued) return new(status);
            var manager = store.storeClientManager;
            if (manager?.clientStack == null)
            { Requests.Set(owner, request, OfficialPresentationStatus.Unavailable); return new(OfficialPresentationStatus.Unavailable); }
            if (manager.clientStack.ToArray().Any(x => x != null && x.identifier == actor) ||
                store.currentClientInstance?.GetClientBlueprint()?.identifier == actor)
            { Requests.Set(owner, request, OfficialPresentationStatus.Conflict); return new(OfficialPresentationStatus.Conflict); }
            visit = new(owner, request, actor, script, _generation, run, slot, supplier);
            var definition = script.Definition;
            visit.Client = new StoreClient
            {
                identifier = actor, displayName = definition.Speaker, spriteName = sprite,
                clientFaction = StoreClient.FACTION_LOWER, clientIntent = StoreClient.ClientIntent.DIALOGUE,
                dismissable = true, arrestable = false, nonShootable = true,
                isRegularSellDisable = true, isBargainAvailable = false, isMultiBuyDisabled = true,
                isNoContributeToEvidence = true
            };
            ModHelper.SetClientSource(visit.Client, owner);
            BuildDialogue(visit);
            Visits.Add(visit); // retain callbacks before a native arrival can run
            attempted = true;
            manager.AddNextClient(visit.Client);
            if (!manager.clientStack.ToArray().Any(x => x != null && NativeObject.Handle(x) == NativeObject.Handle(visit.Client)))
                throw new InvalidOperationException("Queue insertion not observed");
            visit.Observed = true;
            return new(OfficialPresentationStatus.Queued);
        }
        catch (Exception ex)
        {
            Log(ex);
            var status = attempted ? OfficialPresentationStatus.Indeterminate : OfficialPresentationStatus.Unavailable;
            Requests.Set(owner, request, status);
            if (!attempted && visit != null) Release(visit);
            return new(status, ex.Message);
        }
    }

    private static Dialogue Line(Visit v, string text)
    {
        var dialogue = new Dialogue().SetText(v.Script.Definition.Speaker, text);
        v.Dialogues.Add(dialogue); return dialogue;
    }
    private static void BuildDialogue(Visit v)
    {
        if (v.Supplier != null)
        {
            var menu = Line(v, v.Supplier.FailureText);
            menu.isMainDialog = true;
            v.Client!.mainDialogue = menu;
            FillSupplierMenu(v, menu);
            return;
        }
        var d = v.Script.Definition;
        var root = Line(v, d.Body);
        root.isMainDialog = true;
        v.Client!.mainDialogue = root;
        foreach (var option in d.Choices)
        {
            var response = Line(v, d.FailureText);
            void Commit()
            {
                if (!v.Selection.TryEnter(v.Generation, _generation, IsCurrent(v))) return;
                string result;
                try { result = option.Confirm(new(v.Generation, v.Run, v.Slot, v.Owner, v.Request, d.Id, option.Id)); }
                catch (Exception ex) { Log(ex); result = d.FailureText; }
                if (IsCurrent(v)) response.SetText(d.Speaker, result ?? d.FailureText);
            }
            Choice(v, response, d.LeaveLabel, null, () => Leave(v));
            if (d.RequireConfirmation)
            {
                var preview = Line(v, option.Preview);
                Choice(v, preview, d.ConfirmLabel, response, Commit);
                Choice(v, preview, d.BackLabel, root, null);
                Choice(v, root, option.Label, preview, null);
            }
            else Choice(v, root, option.Label, response, Commit);
        }
        if (d.AllowLater) Choice(v, root, d.LaterLabel, null, () => Leave(v));
        Paginate(v, root, d.NextPageLabel, d.PreviousPageLabel);
    }

    private static OfficialDialogueContext SupplierContext(Visit v, string choice) =>
        new(v.Generation, v.Run, v.Slot, v.Owner, v.Request, v.Supplier!.Id, choice);

    private static bool SupplierCanContinue(Visit v)
    {
        try { return v.Supplier!.CanContinue?.Invoke(SupplierContext(v, "continue")) ?? true; }
        catch (Exception ex) { Log(ex); return false; }
    }

    private static void FillSupplierMenu(Visit v, Dialogue menu)
    {
        var d = v.Supplier!;
        OfficialSupplierPage page;
        try
        {
            page = OfficialStorySupplierContract.FreezePage(d.Capture(SupplierContext(v, "capture")));
            if (page.AutomaticOfferId != null && d.RequireConfirmation)
                throw new ArgumentException("Automatic supplier offers require direct selection");
        }
        catch (Exception ex) { Log(ex); page = new(d.FailureText, []); }
        menu.SetText(d.Speaker, page.Body);
        // One menu represents one quote opportunity. Continue builds a new menu;
        // it never clears an old gate or gives an old callback another chance.
        var selection = new OfficialStoryConversationSelection();
        Dialogue? automaticChoice = null;
        v.Automatic = null;
        // The optional automatic character decision must be on the first visible page.
        // Keep every other offer in its quoted order and paginate without dropping choices.
        foreach (var offer in page.Offers.OrderBy(offer => offer.Id == page.AutomaticOfferId ? 0 : 1).ToArray())
        {
            var response = Line(v, d.FailureText);
            void Commit()
            {
                if (!offer.Enabled) return;
                if (!selection.TryEnter(v.Generation, _generation, IsCurrent(v))) return;
                v.Automatic = null;
                string text;
                try { text = offer.Purchase(SupplierContext(v, offer.Id)); }
                catch (Exception ex) { Log(ex); text = d.FailureText; }
                if (IsCurrent(v))
                {
                    response.SetText(d.Speaker, text ?? d.FailureText);
                    if (!SupplierCanContinue(v))
                    {
                        // Keep the prebuilt leave choice; no new native allocation after settlement.
                        response.choices.RemoveAt(0);
                    }
                }
            }
            var resume = Line(v, d.FailureText);
            var refresh = new OfficialStoryConversationSelection();
            Choice(v, response, d.ContinueLabel, resume, () =>
            {
                if (!refresh.TryEnter(v.Generation, _generation, IsCurrent(v))) return;
                if (!SupplierCanContinue(v)) { Leave(v); return; }
                FillSupplierMenu(v, resume);
            });
            Choice(v, response, d.LeaveLabel, null, () => Leave(v));
            if (d.RequireConfirmation)
            {
                var preview = Line(v, offer.Preview);
                Choice(v, preview, offer.ConfirmLabel ?? d.ConfirmLabel, response, Commit);
                Choice(v, preview, d.BackLabel, menu, null);
                Choice(v, menu, offer.Label, preview, null, offer.Enabled);
            }
            else
            {
                var choice = Choice(v, menu, offer.Label, response, Commit, offer.Enabled);
                if (offer.Id == page.AutomaticOfferId) automaticChoice = choice;
            }
        }
        if (d.AllowMenuRefresh && SupplierCanContinue(v))
        {
            var resume = Line(v, d.FailureText);
            Choice(v, menu, d.ContinueLabel, resume, () =>
            {
                if (!selection.TryEnter(v.Generation, _generation, IsCurrent(v))) return;
                if (!SupplierCanContinue(v)) { Leave(v); return; }
                FillSupplierMenu(v, resume);
            });
        }
        Choice(v, menu, d.LeaveLabel, null, () => Leave(v));
        Paginate(v, menu, d.NextPageLabel, d.PreviousPageLabel);
        if (automaticChoice != null)
        {
            // Pagination may have moved the offer. Bind by native node identity, never label text.
            var pageWithChoice = v.Dialogues.First(x => x.choices.ToArray().Any(c => NativeObject.Handle(c.Item2) == NativeObject.Handle(automaticChoice)));
            v.Automatic = new(pageWithChoice, automaticChoice, page.AutomaticDelaySeconds);
        }
    }

    private static void Paginate(Visit v, Dialogue menu, string nextLabel, string previousLabel)
    {
        // DisplayChoice indexes this fixed native list for every offered choice.
        // Only reshape Official-owned menus; preserve their nodes and callbacks.
        var dialog = DialogUIManager.Instance;
        int capacity = dialog?.dialogChoiceBubbles?.Count ?? 0;
        if (capacity == 0) throw new InvalidOperationException("Native dialogue choice buttons are unavailable");
        OfficialStoryNativeChoice.Paginate(menu, Math.Min(4, capacity), nextLabel, previousLabel,
            body => Line(v, body), (page, label, next) => Choice(v, page, label, next, null));
    }

    private static Dialogue Choice(Visit v, Dialogue parent, string text, Dialogue? next, Action? action, bool enabled = true)
    {
        var item = DirectoryMaster.Create<GameItem>("handnote") ?? throw new InvalidOperationException("Choice template unavailable");
        v.Items.Add(item);
        Action? callback = null;
        if (action != null)
        {
            callback = () =>
            {
                try { if (IsCurrent(v)) action(); }
                catch (Exception ex) { Log(ex); }
            };
            v.Callbacks.Add(callback);
        }
        var choice = OfficialStoryNativeChoice.Add(parent, item, text, next, callback, enabled);
        v.Dialogues.Add(choice);
        if (!enabled) v.DisabledChoices.Add(NativeObject.Handle(choice));
        return choice;
    }

    private static bool IsCurrent(Visit v)
    {
        if (v.Retired || v.Generation != _generation) return false;
        var store = Store();
        return v.Generation == _generation && store != null && store.runID == v.Run && store.saveSlotId == v.Slot &&
            NativeObject.Handle(store.currentClientInstance?.GetClientBlueprint()) == NativeObject.Handle(v.Client);
    }
    private static void Leave(Visit v)
    {
        if (!IsCurrent(v) || v.DismissRequested) return;
        v.DismissRequested = true;
        v.Supplier?.Dismiss(SupplierContext(v, "dismiss"));
        v.Client!.CompleteBusiness();
        var dialog = DialogUIManager.Instance;
        if (dialog != null) dialog.CurrentClientLeave();
    }
    private static bool References(DialogUIManager dialog, Visit v) => v.Dialogues.Any(x =>
        NativeObject.Handle(x) == NativeObject.Handle(dialog.currentDialog) || NativeObject.Handle(x) == NativeObject.Handle(DisplayingDialogue(dialog)));

    internal static void Update()
    {
        OfficialStoryUi.Update();
        OfficialPhoneApi.Update();
        if (!_installed || Visits.Count == 0 || DateTime.UtcNow < _nextUpdate) return;
        _nextUpdate = DateTime.UtcNow.AddMilliseconds(250);
        try
        {
            if (Store() == null) return;
            RefreshVisits(true);
            UpdateAutomaticOffers();
        }
        catch (Exception ex) { Log(ex); }
    }

    private static void RefreshVisits(bool callNext)
    {
        if (Visits.Count == 0) return;
        var store = Store();
        var queue = store?.storeClientManager?.clientStack;
        if (store == null || queue == null) return;
        var current = store.currentClientInstance?.GetClientBlueprint();
        var queued = queue.ToArray();
        var dialog = DialogUIManager.Instance;
        foreach (var v in Visits.ToArray())
        {
            if (v.Generation != _generation) continue;
            if (current != null && NativeObject.Handle(current) == NativeObject.Handle(v.Client))
            {
                v.Observed = true;
                v.ArrivalRequested = true;
                Requests.Set(v.Owner, v.Request, OfficialPresentationStatus.Arrived);
            }
            else if (queued.Any(x => x != null && NativeObject.Handle(x) == NativeObject.Handle(v.Client)))
            {
                v.Observed = true;
                Requests.Set(v.Owner, v.Request, OfficialPresentationStatus.Queued);
            }
            else if (v.Observed)
            {
                v.Retired = true;
                Requests.Set(v.Owner, v.Request, OfficialPresentationStatus.Dismissed);
                if (dialog == null || !References(dialog, v)) { Release(v); Visits.Remove(v); }
            }
        }
        if (!callNext || current != null || store.isClientArrived || store.storeState != PlayerStore.StoreState.OPEN ||
            OfficialWorkshopApi.IsVisible || OfficialStoryUi.AnyVisible || dialog == null || dialog.isConversationActiveFlag != 0) return;
        var calendar = AdvCalendarUIManager.Instance;
        var reader = HandnoteUIManager.Instance;
        if (calendar != null && calendar.UIPanel != null && calendar.UIPanel.activeInHierarchy ||
            reader != null && reader.panel != null && reader.panel.activeInHierarchy) return;
        var waiting = Visits.FirstOrDefault(v => !v.Retired && !v.ArrivalRequested &&
            queued.Any(x => x != null && NativeObject.Handle(x) == NativeObject.Handle(v.Client)));
        if (waiting == null || queued.Length == 0 || !ReferenceEquals(queued[0], waiting.Client) || !store.CanNextClient()) return;
        store.TryCallNextClient();
        waiting.ArrivalRequested = ReferenceEquals(store.currentClientInstance?.GetClientBlueprint(), waiting.Client);
    }

    private static void Release(Visit v)
    {
        v.Retired = true;
        foreach (var item in v.Items)
            try { if (item != null && item.parentInventory == null) item.Destroy(); }
            catch (Exception ex) { Log(ex); }
        v.Items.Clear(); v.Callbacks.Clear(); v.Dialogues.Clear();
    }

    internal static void Remove(string owner)
    {
        Requests.Remove(owner);
        var store = PlayerStore.HasInstance ? PlayerStore.Instance : null;
        foreach (var visit in Visits.Where(v => v.Owner == owner).ToArray())
        {
            if (store?.currentClientInstance?.GetClientBlueprint() == visit.Client) DialogUIManager.Instance?.InterruptDialog();
            store?.storeClientManager?.clientStack?.Remove(visit.Client!);
            Release(visit); Visits.Remove(visit);
        }
    }
    internal static void Uninstall()
    {
        ModHook.OnGameLoadedInit -= BeforeReset;
        ModHook.OnGameLoadedNormal -= AfterLoaded;
        ModHook.OnNewGameStarted -= NewRun;
        BeforeReset();
        OfficialPhoneApi.Uninstall();
        _installed = false;
        new HarmonyLib.Harmony(HookId).UnpatchAll(HookId);
    }
    private static void Log(Exception ex) { try { _log?.Invoke("[WARN] [Official/Conversation] " + ex.Message); } catch { } }
}
