using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Diagnostics;
using System.Text.RegularExpressions;
using DG.Tweening;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;
using Nicokobo.NativeTest;

public sealed class NativeSpeedStockProbe : IMod
{
    public const string Id = "nicokobo.native-speedstock";
    internal const string MarkerKey = "NicoKoboNativeSpeedStockRunId";
    internal static string RunId, Output, Persistent, Product;
    internal static bool Armed, InjectSupplyFailure, DeveloperMode;
    internal static int ProcessId, SaveBlocks, LoadBlocks, InitialBlocks, InjectedFailures;
    GameObject host;
    Harmony patches;
    public void Init(ModManifest manifest) { }
    public void OnEnable()
    {
        var args = Environment.GetCommandLineArgs();
        if (!args.Contains("--nicokobo-native-speedstock-test")) return;
        string Need(string key)
        {
            string prefix = "--native-test-" + key + "=";
            string[] matches = args.Where(x => x.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
            if (matches.Length != 1 || matches[0].Length == prefix.Length)
                throw new ArgumentException("Missing or duplicate option: " + key);
            return matches[0].Substring(prefix.Length);
        }
        RunId = Need("run"); Output = Need("output"); Persistent = Need("persistent"); Product = Need("product");
        string[] developerArgs = args.Where(arg => arg.StartsWith("--native-test-developer-mode=", StringComparison.Ordinal)).ToArray();
        if (developerArgs.Length > 1 || developerArgs.Any(arg => arg != "--native-test-developer-mode=true" && arg != "--native-test-developer-mode=false"))
            throw new ArgumentException("Developer mode expectation must be a single true/false option.");
        DeveloperMode = developerArgs.Contains("--native-test-developer-mode=true");
        if (!Guid.TryParseExact(RunId, "D", out _) || Need("mode") != "speed-stock")
            throw new ArgumentException("Expected a new GUID and speed-stock mode.");
        if (!Path.IsPathRooted(Output) || File.Exists(Output) || !System.IO.Directory.Exists(Path.GetDirectoryName(Output)))
            throw new ArgumentException("Output must be a new absolute file in the current run directory.");
        if (!Regex.IsMatch(Product, "^NicoT_[0-9a-f]{9}$") || Application.productName != Product ||
            Application.companyName != Need("company") ||
            !string.Equals(Path.GetFullPath(Application.persistentDataPath).TrimEnd('\\', '/'), Path.GetFullPath(Persistent).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(new DirectoryInfo(Persistent).Name, Product, StringComparison.Ordinal))
            throw new InvalidOperationException("Independent persistent directory and product identity are required before running fixtures.");
        if (SteamManager.Initialized) throw new InvalidOperationException("Expected the prepared offline clone.");
        ProcessId = NativeProbeLaunch.ReadProcessId(Output, RunId, "speed-stock", Product);
        PlayerPrefs.SetString(MarkerKey, RunId); PlayerPrefs.Save();
        Application.runInBackground = true;
        Armed = true;
        patches = new Harmony("nicokobo.native-speedstock.session");
        Patch(nameof(PlayerStore.SaveGame), nameof(BlockSave));
        Patch(nameof(PlayerStore.LoadGame), nameof(BlockLoad));
        Patch(nameof(PlayerStore.InitialSave), nameof(BlockInitial));
        patches.Patch(AccessTools.Method(typeof(StoreClientList), nameof(StoreClientList.AddScavGeneralStock)),
            prefix: new HarmonyMethod(typeof(NativeSpeedStockProbe), nameof(FailStock)));
        host = new GameObject("NativeSpeedStockProbe");
        UnityEngine.Object.DontDestroyOnLoad(host);
        host.AddComponent<NativeSpeedStockRunner>();
    }
    void Patch(string method, string prefix) => patches.Patch(AccessTools.Method(typeof(PlayerStore), method), prefix: new HarmonyMethod(typeof(NativeSpeedStockProbe), prefix));
    static bool BlockSave() { SaveBlocks++; return false; }
    static bool BlockLoad() { LoadBlocks++; return false; }
    static bool BlockInitial(PlayerStore __instance)
    {
        InitialBlocks++; __instance.runID = "native-test-" + RunId; __instance.saveSlotId = -19729;
        __instance.savedItemFeatureList = new List<ItemFeature>(); return false;
    }
    static void FailStock()
    {
        if (!InjectSupplyFailure) return;
        InjectedFailures++;
        PlayerStore.Instance.AddDirectSellingItemToTable("scrap_metal");
        throw new InvalidOperationException("NATIVE_TEST_EXPECTED_PARTIAL_SUPPLY_FAILURE");
    }
    public void OnDisable()
    {
        if (host != null) UnityEngine.Object.Destroy(host);
        patches?.UnpatchAll("nicokobo.native-speedstock.session"); Armed = false;
    }
}

[DefaultExecutionOrder(20000)]
public sealed class NativeSpeedStockRunner : MonoBehaviour
{
    readonly List<object> checks = new List<object>();
    readonly List<object> keyReceipts = new List<object>();
    readonly List<string> errors = new List<string>();
    float started;
    int request, keyEvents, failed;
    bool finished;
    bool awaitingKey, expectedControl;
    KeyCode expectedKey;
    bool bindingCaptureArmed;
    int bindingCaptureEndFrames;
    readonly List<object> bindingCaptureFrames = new List<object>();
    List<GameItem> bindingCaptureStock;
    float bindingCaptureMultiplier;
    bool bindingCaptureEnabled, bindingCaptureInvariant;
    PlayerStore store;
    EmporiumEntry emp;
    ModConfig speed, stock;
    GameItem purchased;
    void Start() { started = Time.realtimeSinceStartup; StartCoroutine(Guarded(Suite())); }
    void Update()
    {
        bool control = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
        if (awaitingKey && Input.GetKeyDown(expectedKey) && control == expectedControl && !shift && !alt)
        {
            keyEvents++;
            keyReceipts.Add(new { request, key = expectedKey.ToString(), control, shift, alt, frame = Time.frameCount });
        }
        if (bindingCaptureArmed && KeyBindHelper.SuppressHotkeys)
        {
            bool unchanged = SameStock(bindingCaptureStock) &&
                Near(Value<float>(speed, "GlobalSpeedMultiplier"), bindingCaptureMultiplier) &&
                Value<bool>(speed, "GlobalSpeedEnabled") == bindingCaptureEnabled;
            bindingCaptureInvariant &= unchanged;
            bool completedThisFrame = !KeyBindHelper.IsCapturing;
            if (completedThisFrame) bindingCaptureEndFrames++;
            if (completedThisFrame || Input.GetKeyDown(expectedKey))
                bindingCaptureFrames.Add(new { request, frame = Time.frameCount,
                    capturing = KeyBindHelper.IsCapturing, suppressed = KeyBindHelper.SuppressHotkeys,
                    completedThisFrame, actualKeyDown = Input.GetKeyDown(expectedKey), unchanged });
        }
        if (!finished && Time.realtimeSinceStartup - started > 900f) Finish("Native suite timed out.");
    }
    IEnumerator Guarded(IEnumerator root)
    {
        var stack = new Stack<IEnumerator>(); stack.Push(root);
        while (stack.Count > 0 && !finished)
        {
            object next = null; bool more = false; Exception error = null;
            try { NativeProbeLaunch.AssertWindow(); more = stack.Peek().MoveNext(); if (more) next = stack.Peek().Current; }
            catch (Exception ex) { error = ex; }
            if (error != null) { Finish(error.ToString()); yield break; }
            if (!more) { stack.Pop(); continue; }
            if (next is IEnumerator nested) { stack.Push(nested); continue; }
            yield return next;
        }
        if (!finished) Finish(null);
    }
    void Check(string name, bool passed, object observed, object expected)
    {
        checks.Add(new { name, passed, observed, expected, required = true, status = passed ? "passed" : "failed" }); if (!passed) failed++;
        Debug.Log("[NativeSpeedStock] " + name + "=" + passed + " " + JsonConvert.SerializeObject(observed));
        State("running", "none", name);
    }
    void State(string phase, string key, string label)
    {
        File.WriteAllText(NativeSpeedStockProbe.Output + ".state.json", JsonConvert.SerializeObject(new {
            runId = NativeSpeedStockProbe.RunId, phase, request, key, label, checks = checks.Count, failed,
            elapsed = Time.realtimeSinceStartup - started
        }));
    }
    void Finish(string error)
    {
        if (finished) return; finished = true; Time.timeScale = 1f;
        using (var resultFile = new StreamWriter(new FileStream(NativeSpeedStockProbe.Output, FileMode.CreateNew, FileAccess.Write, FileShare.Read)))
        resultFile.Write(JsonConvert.SerializeObject(new {
            runId = NativeSpeedStockProbe.RunId, outputArgument = NativeSpeedStockProbe.Output,
            mode = "speed-stock", processId = NativeSpeedStockProbe.ProcessId,
            executed = checks.Count > 0, passed = error == null && failed == 0 && checks.Count > 0, error, gameVersion = Application.version,
            persistentDataPath = Application.persistentDataPath, checks, keyReceipts, bindingCaptureFrames, diagnostics = errors,
            product = Application.productName, company = Application.companyName,
            environment = new { persistentDataPath = Application.persistentDataPath, product = Application.productName,
                processIdentitySource = "External supervisor launch.json; supervisor owns and verifies the actual process handle",
                company = Application.companyName, steamInitialized = SteamManager.Initialized,
                trustExpectation = NativeSpeedStockProbe.DeveloperMode ? "developer" : "ordinary",
                playerPrefsMarker = new { key = NativeSpeedStockProbe.MarkerKey, value = PlayerPrefs.GetString(NativeSpeedStockProbe.MarkerKey, ""),
                    expectedRegistryIdentity = "HKEY_CURRENT_USER\\Software\\" + Application.companyName + "\\" + Application.productName } },
            loadedMods = ModLoader.Mods.Select(m => new { id = m.Manifest.ID, version = m.Manifest.ModVersion, enabled = m.IsEnabled,
                trust = m.TrustState.ToString(), developerMode = m.TrustRecord?.developerMode }).ToArray(),
            protection = new { savesBlocked = NativeSpeedStockProbe.SaveBlocks, loadsBlocked = NativeSpeedStockProbe.LoadBlocks, initialSavesBlocked = NativeSpeedStockProbe.InitialBlocks },
            unverified = new [] { "Full ES3 save/reload", "Complete EndNight", "All customer variants", "Actual mouse bubble gestures" }
        }, Formatting.Indented));
        State("finished", "none", error ?? "done");
        Application.Quit(error == null && failed == 0 ? 0 : 1);
    }
    void Set<T>(ModConfig config, string key, T value) => ((ConfigEntry<T>)config.Get(key)).Set(value);
    T Value<T>(ModConfig config, string key) => ((ConfigEntry<T>)config.Get(key)).Value;
    static bool Near(float a, float b, float tolerance = .001f) => Mathf.Abs(a - b) <= tolerance;
    IEnumerator WaitFor(Func<bool> ready, float seconds, string label)
    {
        float end = Time.realtimeSinceStartup + seconds;
        while (!ready()) { if (Time.realtimeSinceStartup > end) throw new TimeoutException(label); yield return null; }
    }
    IEnumerator Key(string label, string key)
    {
        switch (key)
        {
            case "F5": expectedKey = KeyCode.F5; expectedControl = false; break;
            case "Control_L+r": expectedKey = KeyCode.R; expectedControl = true; break;
            case "Control_L+Page_Up": expectedKey = KeyCode.PageUp; expectedControl = true; break;
            default: throw new ArgumentException("Unsupported native key request: " + key);
        }
        request++; int before = keyEvents; State("key", key, label);
        awaitingKey = true;
        yield return WaitFor(() => keyEvents > before, 120f, "No real keyboard event for " + label);
        awaitingKey = false;
        yield return null; yield return null;
        State("running", "none", label);
    }
    IEnumerator BindingCapture(string label, string key, KeyCode capturedKey)
    {
        Prepare();
        var options = GameOptionUIManager.Instance;
        KeyCode originalBinding = KeyBindHelper.Get("rotate_cw");
        options.OpenUI();
        yield return null;
        var window = (CustomUIWindow)AccessTools.Field(typeof(GameOptionUIManager), "window").GetValue(options);
        string optionId = GameOptionRegistry.KeyBindOptionId("rotate_cw");
        var button = window.Get(optionId).button;
        Check("binding-capture-real-button." + label, options.IsOpen && button.gameObject.activeInHierarchy && button.IsInteractable(),
            new { options.IsOpen, optionId, active = button.gameObject.activeInHierarchy, interactable = button.IsInteractable() }, "Actual native options key-binding button");
        button.onClick.Invoke();
        Check("binding-capture-started." + label, KeyBindHelper.IsCapturing && KeyBindHelper.SuppressHotkeys && KeyBindHelper.CapturingOptionId == optionId,
            new { KeyBindHelper.IsCapturing, KeyBindHelper.SuppressHotkeys, KeyBindHelper.CapturingOptionId }, optionId);
        bindingCaptureStock = Stock();
        bindingCaptureMultiplier = Value<float>(speed, "GlobalSpeedMultiplier");
        bindingCaptureEnabled = Value<bool>(speed, "GlobalSpeedEnabled");
        bindingCaptureInvariant = true;
        bindingCaptureEndFrames = 0;
        bindingCaptureArmed = true;
        try
        {
            yield return Key("native-binding-capture-" + label, key);
            var pending = (Dictionary<string, KeyCode>)AccessTools.Field(typeof(GameOptionUIManager), "pendingKeys").GetValue(options);
            Check("binding-capture-native-poll-and-end." + label,
                !KeyBindHelper.IsCapturing && bindingCaptureEndFrames == 1 && pending.TryGetValue(optionId, out var value) && value == capturedKey,
                new { KeyBindHelper.IsCapturing, bindingCaptureEndFrames, pending = pending.ContainsKey(optionId) ? pending[optionId].ToString() : "missing", expectedKey = capturedKey.ToString() },
                "Real native PollKeyCapture consumes the physical key and completes on one observed suppressed frame");
            Check("binding-capture-preserves-stock-and-speed." + label,
                bindingCaptureInvariant && SameStock(bindingCaptureStock) && Near(Value<float>(speed, "GlobalSpeedMultiplier"), bindingCaptureMultiplier) && Value<bool>(speed, "GlobalSpeedEnabled") == bindingCaptureEnabled,
                new { bindingCaptureInvariant, unchangedStock = SameStock(bindingCaptureStock), multiplier = Value<float>(speed, "GlobalSpeedMultiplier"), enabled = Value<bool>(speed, "GlobalSpeedEnabled") },
                new { multiplier = bindingCaptureMultiplier, enabled = bindingCaptureEnabled, stockUnchanged = true });
        }
        finally
        {
            bindingCaptureArmed = false;
            options.CloseUI();
        }
        yield return null;
        Check("binding-capture-unapplied-native-binding." + label, KeyBindHelper.Get("rotate_cw") == originalBinding,
            KeyBindHelper.Get("rotate_cw").ToString(), originalBinding.ToString());
    }
    List<GameItem> Stock() => emp.frontInvinvElement.childItems.Concat(emp.backInvinvElement.childItems).Where(x => x != null && !GeneralHelper.IsItemOwned(x)).ToList();
    object[] Items(IEnumerable<GameItem> items) => items.Select(x => (object)new { id = x.identifier, uid = x.uniqueId, units = x.unitCount, owned = GeneralHelper.IsItemOwned(x), placed = x.parentInventory != null }).ToArray();
    bool SameStock(List<GameItem> before) => before.Count == Stock().Count && before.All(x => Stock().Contains(x));
    bool Replaced(List<GameItem> before) => Stock().Count > 0 && !Stock().Any(before.Contains);
    void Clear()
    {
        DialogUIManager.Instance.RemoveCurrentDialog(); NegociationUIManager.Instance.CloseUI(); BargainUIManager.Instance.CloseUI();
        emp.CloseItemWindowUIWindow(); emp.ClearFrontInv(); emp.ClearBackInv();
        store.isClientBeingArrested = false; store.isHandlingShootout = false; StoreUIManager.Instance.isShot = false;
    }
    StoreClient Prepare(bool scav = false)
    {
        Clear(); StoreClient client = scav ? StoreClientList.CreateScavGeneral() : StoreClientList.CreateJunkerSellOnly();
        store.currentClientInstance = StoreClientInstance.CreateClientInstance(client); store.isClientArrived = true;
        store.playerCash = 100000;
        // Execute the actual native stock-producing dialogue action. Its native
        // execution flag is what the installed reroll Mod is allowed to replay.
        for (Dialogue d = client.mainDialogue; d != null; d = d.nextDialogue)
            if (d.endAction != null) d.ExecuteEndAction();
        NegociationUIManager.Instance.CloseUI(); BargainUIManager.Instance.CloseUI();
        return client;
    }
    IEnumerator Suite()
    {
        State("running", "none", "waiting for menu");
        yield return WaitFor(() => MainMenuUIController.Instance != null && ModConfig.Find("nicokobo.speed-up") != null && ModConfig.Find("nicokobo.stock-reroll") != null, 35f, "Expected enabled Mods and menu");
        speed = ModConfig.Find("nicokobo.speed-up"); stock = ModConfig.Find("nicokobo.stock-reroll");
        Set(stock, "BlockAfterTransaction", true);
        Set(speed, "GlobalSpeedUpHotkey", "Ctrl+PageUp");
        foreach (object wait in NativeProbeLaunch.PrepareWindow(evidence => Check("windowed-test-display", true, evidence, "1280x720 window on the selected display"))) yield return wait;
        var loaded = ModLoader.Mods.Where(mod => mod.IsEnabled).ToArray();
        bool trustMatches = loaded.All(mod => NativeSpeedStockProbe.DeveloperMode
            ? mod.TrustRecord?.developerMode == true && (mod.TrustState == ModTrustState.Match || mod.TrustState == ModTrustState.DeveloperMode)
            : mod.TrustState != ModTrustState.DeveloperMode && mod.TrustRecord?.developerMode != true);
        Check("native-loader", loaded.Any(x => x.Manifest.ID == "nicokobo.speed-up") && loaded.Any(x => x.Manifest.ID == "nicokobo.stock-reroll") && trustMatches,
            loaded.Select(mod => new { id = mod.Manifest.ID, trust = mod.TrustState.ToString(), developerMode = mod.TrustRecord?.developerMode }).ToArray(),
            NativeSpeedStockProbe.DeveloperMode ? "Native loader with user-enabled Developer mode; ordinary trust acceptance is not covered" : "Native loader with ordinary approval and Developer mode off");
        bool hadStore = PlayerStore.HasInstance;
        Set(stock, "Hotkey", "F5"); yield return Key("menu-f5", "F5");
        Check("menu-hotkey-does-not-create-run", PlayerStore.HasInstance == hadStore, new { before = hadStore, after = PlayerStore.HasInstance }, "No new PlayerStore");
        Set(speed, "GlobalSpeedMultiplier", 2.5f); Set(speed, "GlobalSpeedEnabled", true); yield return null;
        Check("global-speed-excludes-menu", Near(Time.timeScale, 1f), Time.timeScale, 1f);
        Set(speed, "GlobalSpeedEnabled", false);
        float menuMultiplier = Value<float>(speed, "GlobalSpeedMultiplier");
        yield return Key("menu-speed-hotkey-block", "Control_L+Page_Up");
        Check("menu-speed-hotkey-preserves-settings", Near(Value<float>(speed, "GlobalSpeedMultiplier"), menuMultiplier)
            && !Value<bool>(speed, "GlobalSpeedEnabled") && Near(Time.timeScale, 1f) && PlayerStore.HasInstance == hadStore,
            new { multiplier = Value<float>(speed, "GlobalSpeedMultiplier"), enabled = Value<bool>(speed, "GlobalSpeedEnabled"), scale = Time.timeScale, hasStore = PlayerStore.HasInstance },
            new { multiplier = menuMultiplier, enabled = false, scale = 1f, hasStore = hadStore });
        NewGameData.Instance.saveSlot = -1; NewGameData.Instance.saveFile = null; NewGameData.Instance.skipIntro = true;
        NewGameData.Instance.startType = NewGameData.StartType.APPRENTICE; NewGameData.Instance.endlessMode = true;
        NewGameData.Instance.startingPerks.Clear(); NewGameData.Instance.difficultyId = "normal";
        SceneManager.LoadScene("InventoryScene");
        yield return WaitFor(() => EmporiumEntry.Instance != null && EmporiumEntry.Instance.frontInvinvElement != null && StoreShutterButton.Instance != null && PlayerStore.HasInstance && PlayerStore.Instance.runID == "native-test-" + NativeSpeedStockProbe.RunId, 35f, "Disposable scene initialization");
        store = PlayerStore.Instance; emp = EmporiumEntry.Instance;
        EmporiumOption.Instance.autoCallNext = false; EmporiumOption.Instance.autoNextDialog = false;
        EmporiumOption.Instance.autoOpenNegotiationUI = false;
        yield return null; Clear();
        Check("disposable-run-protection", NativeSpeedStockProbe.InitialBlocks == 1 && store.saveSlotId == -19729,
            new { store.runID, store.saveSlotId, NativeSpeedStockProbe.InitialBlocks }, "InitialSave intercepted before index or ES3 writes");

        Set(speed, "Enabled", false); EmporiumOption.Instance.enableTypewriterEffect = false;
        float baseDelay = DialogUIManager.GetTypewriterDelay();
        Set(speed, "Enabled", true); Set(speed, "SpeedMultiplier", 4f); Set(speed, "InstantText", false);
        float fastDelay = DialogUIManager.GetTypewriterDelay();
        Check("native-dialogue-delay-4x", baseDelay > 0 && Near(fastDelay, baseDelay / 4), new { baseDelay, fastDelay }, "Native method result / 4");
        Set(speed, "InstantText", true); float instant = DialogUIManager.GetTypewriterDelay();
        Check("native-dialogue-instant", Near(instant, .001f), instant, .001f);
        Set(speed, "InstantText", false); EmporiumOption.Instance.enableTypewriterEffect = true;
        Set(speed, "QueueDelayMultiplier", 4f);
        // The first scene frame can advance the scaled clock across loading work.
        // Measure native WaitForSeconds after that frame, not against its wall clock.
        yield return new WaitForEndOfFrame();
        yield return null;
        yield return null;
        bool callback = false; float t = Time.realtimeSinceStartup;
        var queueStart = new { frame = Time.frameCount, gameTime = Time.time, realtime = t,
            scale = Time.timeScale, delta = Time.deltaTime, unscaledDelta = Time.unscaledDeltaTime };
        AccessTools.Method(typeof(DialogUIManager), "ScheduleFlow").Invoke(DialogUIManager.Instance, new object[] { .8f, (Action)(() => callback = true) });
        yield return WaitFor(() => callback, 2f, "Native dialogue queue callback");
        float elapsed = Time.realtimeSinceStartup - t;
        Check("native-shop-queue-4x", elapsed >= .17f && elapsed < .45f,
            new { elapsed, start = queueStart, end = new { frame = Time.frameCount, gameTime = Time.time,
                realtime = Time.realtimeSinceStartup, scale = Time.timeScale, delta = Time.deltaTime, unscaledDelta = Time.unscaledDeltaTime } },
            ".8 native delay / 4, allowing frame timing");
        if (DialoguePhoneUIManager.Instance != null && DialoguePhoneUIManager.Instance.gameObject.activeInHierarchy)
        {
            callback = false; t = Time.realtimeSinceStartup;
            AccessTools.Method(typeof(DialoguePhoneUIManager), "ScheduleFlow").Invoke(DialoguePhoneUIManager.Instance, new object[] { .8f, (Action)(() => callback = true) });
            yield return WaitFor(() => callback, 2f, "Native phone queue callback"); elapsed = Time.realtimeSinceStartup - t;
            Check("native-phone-queue-4x", elapsed >= .17f && elapsed < .45f, elapsed, ".2s plus frame timing");
        }
        Set(speed, "QueueDelayMultiplier", 1f);
        var client = Prepare(); var visitor = store.currentClientInstance;
        var dialogueUi = DialogUIManager.Instance;
        float normalTyping = 0f, acceleratedTyping = 0f;
        foreach (float typingMultiplier in new [] { 1f, 4f })
        {
            Set(speed, "SpeedMultiplier", typingMultiplier);
            var sample = new Dialogue().SetText(client.displayName, new string('m', 60));
            float typingStart = Time.realtimeSinceStartup;
            dialogueUi.StartDialogue(sample, true);
            yield return null;
            yield return WaitFor(() => !dialogueUi.clientBubble.typewriter.isShowingText, 6f, "Actual native typewriter completion");
            float typingElapsed = Time.realtimeSinceStartup - typingStart;
            if (typingMultiplier == 1f) normalTyping = typingElapsed; else acceleratedTyping = typingElapsed;
            dialogueUi.RemoveCurrentDialog();
        }
        Check("actual-typewriter-4x", normalTyping > .2f && acceleratedTyping > .005f && acceleratedTyping < normalTyping * .55f,
            new { normalTyping, acceleratedTyping, ratio = normalTyping / acceleratedTyping }, "Same 60-character line displayed through native typewriter, faster at 4x");
        int endCalls = 0; var line = new Dialogue().SetText("NATIVE TEST", "native timing test");
        line.endAction = () => endCalls++; line.nextDialogue = new Dialogue().SetText("NATIVE TEST", "next line");
        DialogUIManager.Instance.currentDialog = line; AccessTools.Field(typeof(DialogUIManager), "displayingUncompletedDialog").SetValue(DialogUIManager.Instance, null);
        DialogUIManager.Instance.ContinueConversation(); line.ExecuteEndAction();
        Check("accelerated-end-action-exactly-once", endCalls == 1 && line.isEndActionExecuted, new { endCalls, line.isEndActionExecuted }, "One end action despite advance before delayed action");
        DialogUIManager.Instance.RemoveCurrentDialog();

        Set(speed, "ClientAnimEnabled", true); Set(speed, "ClientArriveSpeed", 3f); Set(speed, "ClientLeaveSpeed", 4f);
        var shutter = StoreShutterButton.Instance; AccessTools.Field(typeof(StoreShutterButton), "isClosed").SetValue(shutter, true);
        float baseline = (float)AccessTools.Field(typeof(StoreShutterButton), "duration").GetValue(shutter);
        shutter.ToggleShutterDoor(); float open = (float)AccessTools.Field(typeof(StoreShutterButton), "duration").GetValue(shutter);
        shutter.ToggleShutterDoor(); float close = (float)AccessTools.Field(typeof(StoreShutterButton), "duration").GetValue(shutter);
        shutter.ToggleShutterDoor(); float repeat = (float)AccessTools.Field(typeof(StoreShutterButton), "duration").GetValue(shutter);
        Check("native-shutter-open-close-no-compounding", Near(open, baseline / 3) && Near(close, baseline / 4) && Near(repeat, open), new { baseline, open, close, repeat }, "Opening /3, closing /4, repeated opening stable");
        Set(speed, "ClientAnimEnabled", false); shutter.ToggleShutterDoor();
        float restored = (float)AccessTools.Field(typeof(StoreShutterButton), "duration").GetValue(shutter);
        Check("native-shutter-disable-restores", Near(restored, baseline), restored, baseline);
        Set(speed, "ClientAnimEnabled", true);
        var mono = StoreClientMono.Instance; mono.gameObject.SetActive(true); yield return null;
        var path = (DOTweenPath)AccessTools.Field(typeof(StoreClientMono), "dOTweenPath").GetValue(mono);
        mono.StartApprochingStore(); float arrive = path.tween.timeScale;
        mono.StartApprochingStore(); float arrive2 = path.tween.timeScale;
        mono.StartLeavingStore(); float leave = path.tween.timeScale;
        mono.StartLeavingStoreRight(); float right = mono.pathRight.tween.timeScale;
        path.DOPause(); mono.pathRight.DOPause();
        Check("native-customer-tweens", Near(arrive, 3f) && Near(arrive2, 3f) && Near(leave, 4f) && Near(right, 4f), new { arrive, arrive2, leave, right }, "3/3 arrival, 4 main/right departure");

        Time.timeScale = 1f; Set(speed, "GlobalSpeedMultiplier", 2.5f); Set(speed, "GlobalSpeedEnabled", true);
        yield return null; yield return null; float global = Time.timeScale;
        Time.timeScale = 0f; yield return null; yield return null; float paused = Time.timeScale;
        Time.timeScale = 1f; yield return null; yield return null; float resumed = Time.timeScale;
        Time.timeScale = .75f; yield return null; yield return null; float yielded = Time.timeScale;
        Set(speed, "GlobalSpeedEnabled", false); yield return null; float releaseOther = Time.timeScale;
        Time.timeScale = 1f; Set(speed, "GlobalSpeedEnabled", true); yield return null; yield return null;
        Set(speed, "GlobalSpeedEnabled", false); yield return null; float release = Time.timeScale;
        Check("native-global-clock-and-pause", Near(global, 2.5f) && paused == 0f && Near(resumed, 2.5f) && Near(yielded, .75f) && Near(releaseOther, .75f) && Near(release, 1f),
            new { global, paused, resumed, yielded, releaseOther, release }, "2.5x; keeps pause; resumes; yields to external .75; restores own baseline");
        Time.timeScale = 1f; Set(speed, "GlobalSpeedMultiplier", 1f);
        yield return Key("speed-real-hotkey", "Control_L+Page_Up");
        Check("native-global-keyboard-shortcut", Near(Value<float>(speed, "GlobalSpeedMultiplier"), 1.5f) && Value<bool>(speed, "GlobalSpeedEnabled") && Near(Time.timeScale, 1.5f),
            new { multiplier = Value<float>(speed, "GlobalSpeedMultiplier"), enabled = Value<bool>(speed, "GlobalSpeedEnabled"), scale = Time.timeScale }, "Real Ctrl+PageUp enables 1.5x");
        Set(speed, "GlobalSpeedEnabled", false); yield return null; Time.timeScale = 1f;

        client = Prepare(); var inputStock = Stock();
        var inputRoot = new GameObject("Native speed-stock text input fixture", typeof(Canvas), typeof(GraphicRaycaster));
        inputRoot.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        var inputObject = DefaultControls.CreateInputField(new DefaultControls.Resources());
        inputObject.transform.SetParent(inputRoot.transform, false);
        var input = inputObject.GetComponent<InputField>();
        input.Select(); input.ActivateInputField();
        yield return WaitFor(() => input.isFocused, 5f, "Real Unity input field focus");
        yield return Key("text-input-reroll-block", "F5");
        Check("real-input-field-blocks-reroll", input.isFocused && SameStock(inputStock),
            new { focused = input.isFocused, unchangedStock = SameStock(inputStock) }, "Focused native Unity input preserves stock after real F5");
        float inputMultiplier = Value<float>(speed, "GlobalSpeedMultiplier");
        bool inputSpeedEnabled = Value<bool>(speed, "GlobalSpeedEnabled");
        yield return Key("text-input-speed-block", "Control_L+Page_Up");
        Check("real-input-field-blocks-speed", input.isFocused && Near(Value<float>(speed, "GlobalSpeedMultiplier"), inputMultiplier) && Value<bool>(speed, "GlobalSpeedEnabled") == inputSpeedEnabled,
            new { focused = input.isFocused, multiplier = Value<float>(speed, "GlobalSpeedMultiplier"), enabled = Value<bool>(speed, "GlobalSpeedEnabled") },
            new { multiplier = inputMultiplier, enabled = inputSpeedEnabled, fixture = "Unity InputField; actual Mod search/terminal UI is separate" });
        input.DeactivateInputField(); EventSystem.current.SetSelectedGameObject(null);
        UnityEngine.Object.Destroy(inputRoot); yield return null; yield return null;

        var manager = ModLoader.Mods.Single(mod => mod.Manifest.ID == "nicokobo.item-manager").Instance;
        var panel = AccessTools.Property(manager.GetType(), "Panel").GetValue(manager);
        AccessTools.Method(panel.GetType(), "Toggle").Invoke(panel, null);
        var managerWindow = (CustomUIWindow)AccessTools.Field(panel.GetType(), "window").GetValue(panel);
        var managerSearch = managerWindow.Get("search").input;
        managerSearch.Select(); managerSearch.ActivateInputField();
        yield return WaitFor(() => managerSearch.isFocused, 5f, "Actual Item Manager search focus");
        var managerStock = Stock();
        yield return Key("manager-search-reroll-block", "F5");
        Check("manager-search-blocks-reroll", managerSearch.isFocused && SameStock(managerStock),
            new { focused = managerSearch.isFocused, unchangedStock = SameStock(managerStock) }, "Actual Item Manager TMP search preserves stock after real F5");
        yield return Key("manager-search-speed-block", "Control_L+Page_Up");
        Check("manager-search-blocks-speed", managerSearch.isFocused && Near(Value<float>(speed, "GlobalSpeedMultiplier"), inputMultiplier)
            && Value<bool>(speed, "GlobalSpeedEnabled") == inputSpeedEnabled,
            new { focused = managerSearch.isFocused, multiplier = Value<float>(speed, "GlobalSpeedMultiplier"), enabled = Value<bool>(speed, "GlobalSpeedEnabled") },
            new { multiplier = inputMultiplier, enabled = inputSpeedEnabled });
        managerSearch.DeactivateInputField(); EventSystem.current.SetSelectedGameObject(null);
        AccessTools.Method(panel.GetType(), "Close").Invoke(panel, null); yield return null; yield return null;

        yield return BindingCapture("reroll", "F5", KeyCode.F5);
        yield return BindingCapture("speed", "Control_L+Page_Up", KeyCode.PageUp);

        client = Prepare(); visitor = store.currentClientInstance; var old = Stock();
        Check("customer-fixture-panels-closed", !NegociationUIManager.Instance.IsOpen && !BargainUIManager.Instance.IsOpen,
            new { negotiation = NegociationUIManager.Instance.IsOpen, bargaining = BargainUIManager.Instance.IsOpen }, "No trade panel before positive refresh tests");
        string state = JsonConvert.SerializeObject(store.storeClientData); int cash = store.playerCash, discount = client.wholesaleDiscount;
        store.AddDirectSellingItemToTable("energy_credit", isOwend: true);
        var owned = emp.frontInvinvElement.childItems.First(x => GeneralHelper.IsItemOwned(x));
        yield return Key("customer-reroll-success", "F5");
        Check("customer-reroll-replaces-only-unsold", Replaced(old) && owned.parentInventory != null && GeneralHelper.IsItemOwned(owned),
            new { before = Items(old), after = Items(Stock()), retainedOwnedUid = owned.uniqueId }, "Native replacement stock; original owned canary retained");
        Check("customer-reroll-preserves-visitor-and-terms", ReferenceEquals(visitor, store.currentClientInstance) && cash == store.playerCash && discount == client.wholesaleDiscount && state == JsonConvert.SerializeObject(store.storeClientData),
            new { sameVisitor = ReferenceEquals(visitor, store.currentClientInstance), cash = store.playerCash, discount = client.wholesaleDiscount, unchangedClientData = state == JsonConvert.SerializeObject(store.storeClientData) }, "Same instance, cash, discount and introduction/progression data");
        old = Stock(); yield return Key("customer-reroll-repeat", "F5");
        Check("customer-reroll-repeat", Replaced(old), Items(Stock()), "Second genuine replacement");
        old = Stock(); store.isHandlingShootout = true;
        yield return Key("unsafe-state-block", "F5"); store.isHandlingShootout = false;
        Check("shootout-block-keeps-stock", SameStock(old), Items(Stock()), "Original item references unchanged");
        old = Stock(); store.currentNegociatedItem = old[0]; NegociationUIManager.Instance.OpenUI();
        bool panelOpen = NegociationUIManager.Instance.IsOpen;
        yield return Key("negotiation-block", "F5"); NegociationUIManager.Instance.CloseUI();
        Check("negotiation-block-keeps-stock", panelOpen && SameStock(old), new { panelOpen, sameStock = SameStock(old) }, "Open native trade panel prevents reroll");
        purchased = Stock()[0]; int price = (int)purchased.GetNegociatedValue(); cash = store.playerCash;
        store.BuyItem(purchased); old = Stock();
        yield return Key("after-purchase-block", "F5");
        Check("purchase-transaction-block", GeneralHelper.IsItemOwned(purchased) && store.playerCash == cash - price && SameStock(old),
            new { owned = GeneralHelper.IsItemOwned(purchased), beforeCash = cash, afterCash = store.playerCash, price, unchangedStock = SameStock(old) }, "Native purchase charged once; subsequent F5 blocked");
        Set(stock, "BlockAfterTransaction", false); old = Stock();
        yield return Key("after-purchase-allowed", "F5");
        Check("optional-post-purchase-reroll-preserves-purchase", Replaced(old) && purchased.parentInventory != null && GeneralHelper.IsItemOwned(purchased),
            new { replaced = Replaced(old), boughtItem = Items(new [] { purchased }) }, "New unsold goods; purchased item retained");
        Set(stock, "BlockAfterTransaction", true);

        client = Prepare(); old = Stock();
        foreach (var item in old) GraphUtils.TryAcceptAll(emp.backInvinvElement, item);
        store.currentNegociatedItem = emp.backInvinvElement.childItems.First(); store.BuyAllItemFromTable();
        bool bulkOwned = old.All(GeneralHelper.IsItemOwned); var allAfterBulk = emp.backInvinvElement.childItems.ToList();
        yield return Key("bulk-purchase-block", "F5");
        Check("bulk-purchase-transaction-block", bulkOwned && emp.backInvinvElement.childItems.Count == allAfterBulk.Count && allAfterBulk.All(emp.backInvinvElement.childItems.Contains) && Stock().Count == 0,
            new { bulkOwned, originalCount = allAfterBulk.Count, remaining = emp.backInvinvElement.childItems.Count, newUnowned = Stock().Count }, "Bulk ownership transition is recognized; no replacement after purchase");

        client = Prepare(true); old = Stock();
        if (old.Count == 0) throw new InvalidOperationException("Native scav supply unexpectedly empty.");
        store.AddDirectSellingItemToTable("energy_credit", isOwend: true);
        owned = emp.frontInvinvElement.childItems.First(x => GeneralHelper.IsItemOwned(x));
        bool frontLock = emp.frontInvinvElement.overrideLockRemove, backLock = emp.backInvinvElement.overrideLockRemove;
        NativeSpeedStockProbe.InjectSupplyFailure = true;
        yield return Key("partial-supply-failure-rollback", "F5"); NativeSpeedStockProbe.InjectSupplyFailure = false;
        Check("native-partial-failure-rolls-back", NativeSpeedStockProbe.InjectedFailures == 1 && SameStock(old) && GeneralHelper.IsItemOwned(owned) && owned.parentInventory != null && frontLock == emp.frontInvinvElement.overrideLockRemove && backLock == emp.backInvinvElement.overrideLockRemove,
            new { failures = NativeSpeedStockProbe.InjectedFailures, originalStockRestored = SameStock(old), stock = Items(Stock()), owned = Items(new [] { owned }), frontLockRestored = frontLock == emp.frontInvinvElement.overrideLockRemove, backLockRestored = backLock == emp.backInvinvElement.overrideLockRemove }, "Partial generated item removed; original references/ownership/locks restored");
        yield return Key("reroll-after-rollback", "F5");
        Check("native-reroll-after-rollback", Replaced(old), Items(Stock()), "Subsequent genuine reroll works");

        client = Prepare(); old = Stock(); Set(stock, "Hotkey", "Ctrl+R");
        yield return Key("old-hotkey-disabled", "F5");
        Check("old-hotkey-disabled", SameStock(old), SameStock(old), true);
        yield return Key("custom-hotkey", "Control_L+r");
        Check("custom-combination-hotkey", Replaced(old), Replaced(old), true);
        old = Stock(); Set(stock, "Hotkey", "None");
        yield return Key("none-disables-hotkey", "F5");
        Check("none-disables-hotkey", SameStock(old), SameStock(old), true); Set(stock, "Hotkey", "F5");

        Clear(); store.currentClientInstance = null; store.isClientArrived = false;
        MapUIManager.Instance.Travel(new DrJacksonLocation());
        yield return WaitFor(() => MapUIManager.Instance.Current?.Id == AfterhourLocation.DR_JACKSON && Stock().Count > 0, 12f, "Native Dr Jackson travel");
        yield return new WaitForSecondsRealtime(.3f);
        old = Stock(); int visits = store.GetAfterhourState(AfterhourLocation.DR_JACKSON).visits; int browse = store.daySinceLastBrowseUpgrade; cash = store.playerCash;
        yield return Key("night-shop-reroll", "F5");
        Check("native-night-shop-reroll", Replaced(old) && store.GetAfterhourState(AfterhourLocation.DR_JACKSON).visits == visits && store.daySinceLastBrowseUpgrade == browse && store.playerCash == cash,
            new { replaced = Replaced(old), stock = Items(Stock()), visitsBefore = visits, visitsAfter = store.GetAfterhourState(AfterhourLocation.DR_JACKSON).visits, browseBefore = browse, browseAfter = store.daySinceLastBrowseUpgrade, cashBefore = cash, cashAfter = store.playerCash }, "New shop goods without repeated travel/visit/browse effects or payment");
        purchased = Stock()[0]; store.BuyItem(purchased); old = Stock();
        yield return Key("night-shop-after-purchase-block", "F5");
        Check("native-night-shop-transaction-block", SameStock(old) && GeneralHelper.IsItemOwned(purchased), new { unchanged = SameStock(old), bought = Items(new [] { purchased }) }, "Night visit transaction blocks subsequent refresh");
        store.SaveGame();
        Check("native-save-guard", NativeSpeedStockProbe.SaveBlocks >= 1, NativeSpeedStockProbe.SaveBlocks, "Player save entry intercepted");
    }
}
