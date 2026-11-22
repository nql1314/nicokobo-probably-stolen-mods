using Nicokobo.AutomatedLogistics.Integration;
using Nicokobo.ContentInterop;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Nicokobo.AutomatedLogistics;

public sealed class Plugin : IMod
{
    private GameObject? _host;
    private IDisposable? _nativeMachines;
    private ConfigEntry<KeyCode>? _hotkey;
    private ConfigEntry<bool>? _ctrl, _alt, _shift, _feedDispenserRestocking;
    private AutomatedLogisticsRuntime? _runtime;
    private TerminalWindow? _window;
    public void Init(ModManifest manifest)
    {
        var config = ModHelper.GetConfig(manifest.ID);
        const string terminalSection = "logistics.config.terminal";
        _hotkey = config.DefineEnum("Hotkey", KeyCode.H, terminalSection, () => PsModI18n.I18n.Get("hotkey"));
        _ctrl = config.Define("RequireCtrlModifier", false, terminalSection, () => PsModI18n.I18n.Get("require_key", "Ctrl"));
        _alt = config.Define("RequireAltModifier", false, terminalSection, () => PsModI18n.I18n.Get("require_key", "Alt"));
        _shift = config.Define("RequireShiftModifier", false, terminalSection, () => PsModI18n.I18n.Get("require_key", "Shift"));
        _feedDispenserRestocking = config.Define("FeedDispenserRestockingEnabled", true, "logistics.config.automation",
            () => PsModI18n.I18n.Get("feed_restock"));
    }
    public void OnEnable() => Nicokobo.ContentInterop.ForgeClient.Enable(Content.Owner, EnableContent, DisableContent);
    public void OnDisable() => Nicokobo.ContentInterop.ForgeClient.Disable(DisableContent);

    private void EnableContent()
    {
        try
        {
            ContentRuntime.Attach(Content.Owner);
            ModHook.OnModItemDirectoryInit += Register;
            ModHook.OnCreateTooltipEarly += Content.Tooltip;
            ModHook.OnGameLoadedLate += Ready;
            ModHook.OnNewGameStarted += NewGame;
            _runtime = new(Warn, info: message => Debug.Log(message))
            { FeedDispenserRestockingEnabled = () => _feedDispenserRestocking!.Value };
            _runtime.Install();
            _nativeMachines = NativeMachineAccess.Subscribe(Content.Owner, machine =>
            {
                bool furnace = machine.identifier == "furnace";
                long before = -1; int generation = -1;
                var input = furnace ? MachineHelper.GetBodyStorage(machine, 2) as GameInventory : null;
                if (furnace && input != null ? !_runtime.BeforeFurnaceBatch(machine, input, out before) : !_runtime.BeforeNativeMachineBatch(machine, out generation)) return null;
                return success =>
                {
                    if (!success) _runtime.CancelNativeMachineBatch(machine, generation);
                    else if (furnace && input != null) _runtime.AfterFurnaceBatch(machine, input, before);
                    else _runtime.AfterNativeMachineBatch(machine, generation, true);
                };
            });
            _window = new(_runtime, Warn);
            PsModInput.TextInputFocus.Register(Content.Owner, () => _window?.TextEditing == true);
            NativeHooks.Open = _window.Open; NativeHooks.OpenMachine = _window.OpenMachine;
            NativeHooks.Rename = _window.OpenRename; NativeHooks.Close = _window.Close;
            NativeHooks.Visible = () => _window?.Visible == true; NativeHooks.PointerInside = () => _window?.PointerInside == true;
            NativeHooks.TextEditing = () => _window?.TextEditing == true; NativeHooks.DismissPopup = _window.DismissPopup;
            NativeHooks.Runtime = _runtime; NativeHooks.Log = Warn; NativeHooks.Install();
            _host = new GameObject("Nicokobo.AutomatedLogistics"); UnityEngine.Object.DontDestroyOnLoad(_host);
            _host.AddComponent<LogisticsBehaviour>().Owner = this;
            SceneManager.activeSceneChanged += SceneChanged;
        }
        catch { OnDisable(); throw; }
    }
    private static void Register(ModItemDirectory directory) => Content.Register(directory);
    private void Ready() { Content.RegisterModuleRecipes(); _runtime?.EnsureLoaded(); }
    private void NewGame(string scenario) => Ready();
    private void SceneChanged(Scene previous, Scene next) { NativeRenameMenu.Clear(); _window?.Clear(); }
    internal void Tick()
    {
        try
        {
            if (_window?.TextEditing == true) PsModInput.TextInputFocus.BlockCurrentFrame();
            _runtime?.Update(); Hotkey(); _window?.Update();
        }
        catch (Exception error) { Warn("Automation stopped this frame: " + error); }
    }
    private void Hotkey()
    {
        if (_runtime == null || _window == null || _hotkey == null || _hotkey.Value == KeyCode.None ||
            PsModInput.TextInputFocus.ShouldSuppressHotkeys() || !Input.GetKeyDown(_hotkey.Value)) return;
        bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
        bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        if (ctrl != _ctrl!.Value || alt != _alt!.Value || shift != _shift!.Value) return;
        if (_window.Visible) { _window.Close(); return; }
        var store = StoreUIManager.Instance;
        if (!PlayerStore.HasInstance || EmporiumEntry.Instance?.invElement == null || store == null || store.IsTyping() || _window.TextEditing) return;
        _runtime.EnsureLoaded(); if (!_runtime.Ready) return;
        var mouse = Input.mousePosition;
        var hovered = store.hoveringStoreUI ? null : RenderHandler.RaycastElement<GameItemElement>(new Vector2(mouse.x, mouse.y));
        var terminal = hovered?.identifier == Content.Terminal && NativeInventoryApi.IsPlayerOwned(hovered) ? hovered :
            _runtime.Items().Where(item => item.identifier == Content.Terminal).OrderBy(item => item.GetUniqueID()).FirstOrDefault();
        if (terminal == null) { Warn(PsModI18n.I18n.Get("own_a_network_terminal_before_using_its")); return; }
        _window.Open(terminal);
    }
    private void DisableContent()
    {
        _nativeMachines?.Dispose(); _nativeMachines = null;
        SceneManager.activeSceneChanged -= SceneChanged;
        ModHook.OnModItemDirectoryInit -= Register; ModHook.OnGameLoadedLate -= Ready; ModHook.OnNewGameStarted -= NewGame;
        ModHook.OnCreateTooltipEarly -= Content.Tooltip;
        if (_host != null) { _host.SetActive(false); UnityEngine.Object.Destroy(_host); _host = null; }
        PsModInput.TextInputFocus.Unregister(Content.Owner);
        NativeHooks.Uninstall(); _window?.Clear(); _window = null; _runtime?.Dispose(); _runtime = null;
        NativeHooks.Open = null; NativeHooks.Close = null; NativeHooks.Visible = NativeHooks.PointerInside = null;
        NativeHooks.OpenMachine = null; NativeHooks.TextEditing = null; NativeHooks.DismissPopup = null;
        NativeHooks.Rename = null; NativeHooks.Log = null; NativeHooks.Runtime = null;
        ContentRuntime.Detach(Content.Owner);
        ItemArtwork.Clear();
    }
    private static void Warn(string message) => Debug.LogWarning("[AutomatedLogistics] " + message);
}

[DefaultExecutionOrder(10000)]
public sealed class LogisticsBehaviour : MonoBehaviour
{
    internal Plugin? Owner;
    private void LateUpdate() => Owner?.Tick();
}
