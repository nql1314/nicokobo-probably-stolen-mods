namespace Nicokobo.ContentInterop;
public static class OfficialPhoneApi
{
    public static bool IsAvailable => OfficialStoryPhoneRuntime.IsAvailable;
    internal static void Install(Action<string> log) => OfficialStoryPhoneRuntime.Install(log);
    internal static void Update() => OfficialStoryPhoneRuntime.Update();
    internal static void Uninstall() => OfficialStoryPhoneRuntime.Uninstall();
    public static OfficialPresentationResult Register(string ownerId, OfficialPhoneContact contact) =>
        OfficialStoryPhoneRuntime.Register(ownerId, contact);
}

internal static class OfficialStoryPhoneRuntime
{
    private const string HookId = "nicokobo.forge.phone";
    private sealed record Contact(string Owner, OfficialPhoneContact Definition);
    private sealed class Call(Contact contact, StorePhoneClient client, PlayerStore store,
        long generation, OfficialDialogueScript script)
    {
        internal readonly Contact Contact = contact;
        internal readonly StorePhoneClient Client = client;
        internal readonly IntPtr Store = NativeObject.Handle(store);
        internal readonly string Run = store.runID;
        internal readonly int Slot = store.saveSlotId;
        internal readonly long Generation = generation;
        internal readonly OfficialDialogueScript Script = script;
        internal readonly OfficialStoryConversationSelection Selection = new();
        internal readonly List<GameItem> Items = [];
        internal readonly List<Dialogue> Dialogues = [];
        internal readonly List<Action> Callbacks = [];
    }
    private static readonly Dictionary<long, Contact> Contacts = new();
    private static readonly HashSet<long> Conflicts = [];
    private static Action<string>? _log;
    private static bool _installed, _loading;
    private static long _generation;
    private static IntPtr _store;
    private static Call? _call;
    private static DateTime _nextUpdate;
    internal static bool IsAvailable => _installed;

    internal static void Install(Action<string> log)
    {
        if (_installed) return;
        _log = log;
        var harmony = new HarmonyLib.Harmony(HookId);
        try
        {
            Patch(typeof(StorePhoneClient), nameof(StorePhoneClient.GetCallDialog), Type.EmptyTypes, nameof(GetCall));
            Patch(typeof(StorePhoneClient), nameof(StorePhoneClient.GetLocalizedDisplayName), Type.EmptyTypes, nameof(GetName));
            Patch(typeof(StorePhoneClient), nameof(StorePhoneClient.ShownInPhoneBook), Type.EmptyTypes, nameof(Shown));
            Patch(typeof(PhoneBookUIManager), nameof(PhoneBookUIManager.OpenUI), Type.EmptyTypes, nameof(SyncBeforeOpen));
            Patch(typeof(PhoneUIManager), nameof(PhoneUIManager.OpenUI), Type.EmptyTypes, nameof(SyncBeforeOpen));
            harmony.Patch(OfficialStoryHooks.Require(typeof(PhoneUIManager), nameof(PhoneUIManager.OnPhoneDialogEnd), [typeof(bool)]),
                postfix: new HarmonyLib.HarmonyMethod(typeof(OfficialStoryPhoneRuntime), nameof(EndCall)));
            ModHook.OnGameLoadedInit += BeforeReset;
            ModHook.OnGameLoadedNormal += AfterLoaded;
            ModHook.OnNewGameStarted += NewRun;
            _loading = false;
            _installed = true;
            void Patch(Type type, string name, Type[] args, string callback) => harmony.Patch(
                OfficialStoryHooks.Require(type, name, args), prefix: new HarmonyLib.HarmonyMethod(typeof(OfficialStoryPhoneRuntime), callback));
        }
        catch { harmony.UnpatchAll(harmony.Id); throw; }
    }
    private static void AfterLoaded() => _loading = false;
    private static void NewRun(string _) { BeforeReset(); _loading = false; }

    internal static OfficialPresentationResult Register(string owner, OfficialPhoneContact definition)
    {
        if (!_installed) return new(OfficialPresentationStatus.Unavailable);
        if (definition == null || !OfficialStoryConversationContract.Owned(owner, definition.Id) ||
            definition.Number is < 1 or > 9999999999 || definition.Visible == null ||
            definition.DisplayName == null || definition.Call == null)
            return new(OfficialPresentationStatus.Invalid);
        if (Contacts.TryGetValue(definition.Number, out var old))
            return old.Owner == owner && old.Definition.Id == definition.Id
                ? new(OfficialPresentationStatus.Conflict, "Contact is already registered")
                : new(OfficialPresentationStatus.Conflict, "Phone number belongs to another contact");
        if (Contacts.Values.Any(x => x.Definition.Id == definition.Id)) return new(OfficialPresentationStatus.Conflict);
        Contacts.Add(definition.Number, new(owner, definition));
        return new(OfficialPresentationStatus.Queued);
    }

    private static PlayerStore? Store()
    {
        if (!_installed || _loading) return null;
        var store = OfficialStoryGame.Store;
        if (store == null || NativeObject.Handle(store) == IntPtr.Zero || string.IsNullOrWhiteSpace(store.runID) || store.saveSlotId < 0) return null;
        if (_store != IntPtr.Zero && _store != NativeObject.Handle(store)) { _generation++; _call = null; Conflicts.Clear(); }
        _store = NativeObject.Handle(store);
        return store;
    }
    private static OfficialPhoneContext Context(PlayerStore store) => new(_generation, store.runID, store.saveSlotId);
    private static Contact? Owned(StorePhoneClient client)
    {
        var store = Store();
        if (store?.PhoneClientDict == null || client == null) return null;
        foreach (var pair in Contacts)
            if (!Conflicts.Contains(pair.Key) && pair.Value.Definition.Id == client.dialogFuncId &&
                store.PhoneClientDict.ContainsKey(pair.Key) && NativeObject.Handle(store.PhoneClientDict[pair.Key]) == NativeObject.Handle(client))
                return pair.Value;
        return null;
    }
    private static bool Visible(Contact contact, PlayerStore store)
    {
        try { return contact.Definition.Visible(Context(store)); }
        catch (Exception ex) { Log(ex); return false; }
    }
    private static void SyncBeforeOpen() => UpdateContacts();
    internal static void Update()
    {
        if (!_installed || DateTime.UtcNow < _nextUpdate) return;
        _nextUpdate = DateTime.UtcNow.AddSeconds(1);
        UpdateContacts();
    }
    private static void UpdateContacts()
    {
        try
        {
            var store = Store();
            var dictionary = store?.PhoneClientDict;
            if (store == null || dictionary == null) return;
            foreach (var pair in Contacts)
            {
                if (Conflicts.Contains(pair.Key)) continue;
                var existing = dictionary.ContainsKey(pair.Key) ? dictionary[pair.Key] : null;
                if (existing != null && existing.dialogFuncId != pair.Value.Definition.Id)
                { Conflicts.Add(pair.Key); Log(new InvalidOperationException("Phone-number conflict: " + pair.Key)); continue; }
                if (!Visible(pair.Value, store))
                {
                    if (existing != null) dictionary.Remove(pair.Key);
                    continue;
                }
                if (existing == null)
                {
                    existing = new StorePhoneClient
                    {
                        phoneClientType = StorePhoneClient.PhoneClientType.None,
                        phoneState = StorePhoneClient.PhoneState.Regular,
                        dialogFuncId = pair.Value.Definition.Id, locID = pair.Value.Definition.Id,
                        displayName = pair.Value.Definition.DisplayName(Context(store)),
                        dialedBefore = true, cooldownDuration = 0, currentCooldown = 0,
                        alias = new List<string>()
                    };
                    dictionary.Add(pair.Key, existing);
                }
            }
        }
        catch (Exception ex) { Log(ex); }
    }
    private static bool GetName(StorePhoneClient __instance, ref string __result)
    {
        var contact = Owned(__instance); var store = Store();
        if (contact == null || store == null) return true;
        try { __result = contact.Definition.DisplayName(Context(store)); }
        catch (Exception ex) { Log(ex); __result = __instance.displayName; }
        return false;
    }
    private static bool Shown(StorePhoneClient __instance, ref bool __result)
    {
        var contact = Owned(__instance); var store = Store();
        if (contact == null || store == null) return true;
        __result = Visible(contact, store); return false;
    }
    private static bool GetCall(StorePhoneClient __instance, ref Dialogue __result)
    {
        var contact = Owned(__instance); var store = Store();
        if (contact == null || store == null) return true;
        try
        {
            EndCall();
            if (!Visible(contact, store))
            { __result = new Dialogue().SetText(__instance.displayName, "…"); return false; }
            var script = contact.Definition.Call(Context(store));
            if (script == null || script.OwnerId != contact.Owner) throw new InvalidOperationException("Phone dialogue owner mismatch");
            var call = new Call(contact, __instance, store, _generation, script);
            _call = call;
            __result = Build(call);
        }
        catch (Exception ex) { Log(ex); __result = new Dialogue().SetText(__instance.displayName, "…"); }
        return false;
    }
    private static bool Current(Call call)
    {
        if (_call != call || call.Generation != _generation) return false;
        var store = Store(); var phone = PhoneUIManager.Instance;
        return _call == call && call.Generation == _generation && store != null && NativeObject.Handle(store) == call.Store &&
            store.runID == call.Run && store.saveSlotId == call.Slot && phone != null &&
            NativeObject.Handle(phone.currentPhoneClient) == NativeObject.Handle(call.Client) && Visible(call.Contact, store);
    }
    private static Dialogue Line(Call call, string text)
    {
        var line = new Dialogue().SetText(call.Script.Definition.Speaker, text);
        call.Dialogues.Add(line); return line;
    }
    private static Dialogue Build(Call call)
    {
        int capacity = DialoguePhoneUIManager.Instance?.dialogChoiceBubbles?.Count ?? 0;
        if (capacity == 0) throw new InvalidOperationException("Native phone choice buttons are unavailable");
        var d = call.Script.Definition;
        var root = Line(call, d.Body);
        foreach (var option in d.Choices)
        {
            var response = Line(call, d.FailureText);
            void Commit()
            {
                if (!call.Selection.TryEnter(call.Generation, _generation, Current(call))) return;
                string text;
                try { text = option.Confirm(new(call.Generation, call.Run, call.Slot, call.Contact.Owner,
                    "phone:" + call.Contact.Definition.Id, d.Id, option.Id)); }
                catch (Exception ex) { Log(ex); text = d.FailureText; }
                if (Current(call)) response.SetText(d.Speaker, text ?? d.FailureText);
            }
            Choice(call, response, d.LeaveLabel, null, () => Leave(call));
            if (d.RequireConfirmation)
            {
                var preview = Line(call, option.Preview);
                Choice(call, preview, d.ConfirmLabel, response, Commit);
                Choice(call, preview, d.BackLabel, root, null);
                Choice(call, root, option.Label, preview, null);
            }
            else Choice(call, root, option.Label, response, Commit);
        }
        if (d.AllowLater) Choice(call, root, d.LaterLabel, null, () => Leave(call));
        OfficialStoryNativeChoice.Paginate(root, capacity, d.NextPageLabel, d.PreviousPageLabel,
            body => Line(call, body), (page, label, next) => Choice(call, page, label, next, null));
        return root;
    }
    private static void Choice(Call call, Dialogue parent, string label, Dialogue? next, Action? action)
    {
        var item = DirectoryMaster.Create<GameItem>("handnote") ?? throw new InvalidOperationException("Phone choice template unavailable");
        call.Items.Add(item);
        Action? callback = null;
        if (action != null)
        {
            callback = () =>
            { try { if (Current(call)) action(); } catch (Exception ex) { Log(ex); } };
            call.Callbacks.Add(callback);
        }
        call.Dialogues.Add(OfficialStoryNativeChoice.Add(parent, item, label, next, callback));
    }
    private static void Leave(Call call)
    {
        if (!Current(call)) return;
        var dialog = DialoguePhoneUIManager.Instance;
        if (dialog != null) dialog.CurrentClientLeave();
    }
    private static void EndCall()
    {
        var call = _call; _call = null;
        if (call == null) return;
        foreach (var item in call.Items)
            try { if (item != null && item.parentInventory == null) item.Destroy(); }
            catch (Exception ex) { Log(ex); }
        call.Items.Clear(); call.Callbacks.Clear(); call.Dialogues.Clear();
    }
    private static void BeforeReset()
    {
        _generation++; _loading = true; EndCall(); _store = IntPtr.Zero; Conflicts.Clear();
    }
    internal static void Remove(string owner)
    {
        if (_call?.Contact.Owner == owner) EndCall();
        var store = PlayerStore.HasInstance ? PlayerStore.Instance : null;
        foreach (long number in Contacts.Where(pair => pair.Value.Owner == owner).Select(pair => pair.Key).ToArray())
        {
            if (store?.PhoneClientDict.TryGetValue(number, out var client) == true && Owned(client)?.Owner == owner) store.PhoneClientDict.Remove(number);
            Contacts.Remove(number); Conflicts.Remove(number);
        }
    }
    internal static void Uninstall()
    {
        ModHook.OnGameLoadedInit -= BeforeReset;
        ModHook.OnGameLoadedNormal -= AfterLoaded;
        ModHook.OnNewGameStarted -= NewRun;
        var dictionary = OfficialStoryGame.Store?.PhoneClientDict;
        if (dictionary != null)
            foreach (var contact in Contacts)
                if (dictionary.TryGetValue(contact.Key, out var client) && client.dialogFuncId == contact.Value.Definition.Id)
                    dictionary.Remove(contact.Key);
        EndCall(); BeforeReset(); _installed = false; Contacts.Clear();
        new HarmonyLib.Harmony(HookId).UnpatchAll(HookId);
    }
    private static void Log(Exception ex) { try { _log?.Invoke("[WARN] [OfficialPhone] " + ex); } catch { } }
}
