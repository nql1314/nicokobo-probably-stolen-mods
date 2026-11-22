using DefinitelyStolen.Domain;
using DefinitelyStolen.Integration;
using DefinitelyStolen.Runtime;
using DefinitelyStolen.UI;
using Nicokobo.ContentInterop;
using UnityEngine;

namespace DefinitelyStolen;

public sealed class Plugin : IMod
{
    internal static Plugin? Active { get; private set; }
    internal RunController Run { get; private set; } = null!;
    internal TheftPanel Panel { get; private set; } = null!;
    private ModLog _log = null!;
    private GameObject? _driver;
    private ConfigEntry<float> _manual = null!;
    private ConfigEntry<bool> _move = null!;
    private ConfigEntry<bool> _allowTheftWithoutPerk = null!;
    private Catalog _catalog = null!;
    private bool _newRunPending;
    private string? _pendingNoteLayout;
    internal bool Enabled { get; private set; }
    public void Init(ModManifest manifest)
    {
        _log = new(manifest); Active = this;
        var config = ModHelper.GetConfig(Identity.Owner);
        const string theftSection = "theft.config.theft";
        _allowTheftWithoutPerk = config.Define("AllowTheftWithoutPerk", false, theftSection, () => Native.Localize("allow_without_perk"),
            () => Native.Localize("enable_pickpocketing_in_existing_saves_and_other"));
        _manual = config.Define("ManualDifficultyCoefficient", 1f, theftSection, () => Native.Localize("manual_multiplier"), 0f, 2f,
            () => Native.Localize("multiply_the_final_manual_success_zone_0"));
        _move = config.Define("RandomizeSuccessZoneAfterSuccess", true, theftSection, () => Native.Localize("move_zone"),
            () => Native.Localize("each_target_starts_centered_and_moves_independently"));
        _catalog = Catalog.Load(); Run = new(_catalog, message => _log.Log(message), message => _log.Error(message), () => _manual.Value, () => _move.Value);
        Panel = new(Run, _catalog);
    }
    public void OnEnable() => Nicokobo.ContentInterop.ForgeClient.Enable(Identity.Owner, EnableContent, DisableContent, Assets);
    public void OnDisable() => Nicokobo.ContentInterop.ForgeClient.Disable(DisableContent);

    private void EnableContent()
    {
        if (Enabled) return;
        Enabled = true;
        try
        {
            ContentRuntime.Attach(Identity.Owner);
            ModHook.OnModItemDirectoryInit += Items;
            ModHook.OnCreateTooltipEarly += Tooltip;
            ModHook.OnNewGameStarted += NewRun;
            ModHook.OnGameLoadedInit += Loading;
            ModHook.OnGameLoadedLate += Loaded;
            ModHook.OnAfterhourLeaveEarly += NativeHooks.Leaving;
            Perks.Register(() => _allowTheftWithoutPerk.Value); Locations.Register(Run, Panel); NightEventBridge.Register(Run, _catalog);
            ModHelper.AddStartingScenario(Identity.Owner, Identity.StartId,
                () => Native.Localize("start_name"),
                () => Native.Localize("keep_the_shop_by_day_and_work"),
                new Func<string>[] { () => Native.Localize("base_starting_resources_plus_a_black_lamp"),
                    () => Native.Localize("random_stolen_goods_and_a_credit_adjustment"),
                    () => Native.Localize("built_in_streetwise_proficiency_2_or_5"),
                    () => Native.Localize("lower_level_reputation_15") },
                Perks.EnsureNewCareer, Identity.Owner + ":pickpocket", skipIntro: true);
            _driver = new GameObject(Identity.Owner + ".runtime"); UnityEngine.Object.DontDestroyOnLoad(_driver); _driver.AddComponent<TheftDriver>();
            _log.Log("Initialized: 31 occupations, eight named characters, three districts and six night events; API baseline: Steam Build 26317870.");
        }
        catch { OnDisable(); throw; }
    }
    private void DisableContent()
    {
        if (!Enabled) return;
        Enabled = false;
        ModHook.OnModItemDirectoryInit -= Items;
        ModHook.OnNewGameStarted -= NewRun; ModHook.OnGameLoadedInit -= Loading; ModHook.OnGameLoadedLate -= Loaded;
        ModHook.OnAfterhourLeaveEarly -= NativeHooks.Leaving; NativeHooks.Reset();
        ModHook.OnCreateTooltipEarly -= Tooltip;
        Panel?.Close(); Run?.Reset(); Art.Clear(); ContentRuntime.Detach(Identity.Owner);
        ModHelper.RemoveStartingScenarios(Identity.Owner); ModHelper.RemoveStartingPerks(Identity.Owner);
        ModHelper.RemoveAfterhourEvents(Identity.Owner); ModHelper.RemoveAfterhourLocations(Identity.Owner);
        if (_driver != null) UnityEngine.Object.Destroy(_driver); _driver = null;
        if (ReferenceEquals(Active, this)) Active = null;
    }
    private static void Tooltip(RichTextBuilder builder, GameItem item)
    {
        if (item.identifier == EscapeSmokeRules.NativeItemId)
            item.shortDescription = Native.Localize("pale_smoke_stings_the_eyes_even_a");
    }
    private void Items(ModItemDirectory directory)
    {
        directory.Add(EscapeSmokeRules.ItemId, EscapeSmoke.Create);
    }
    private void Assets(LoadedMod loaded)
    {
        if (loaded.Manifest.ID != Identity.Owner) return;
        Art.Load(loaded); Perks.InstallIcons();
    }
    private void NewRun(string scenario)
    {
        // The game's final perk grants follow this event in Build 26274380.
        // InitialSave's prefix is the first boundary after all opening grants.
        _newRunPending = true;
    }
    internal void BeginNativeInitial(PlayerStore store)
    {
        if (!_newRunPending) return;
        _newRunPending = false;
        try { Run.BeginInitial(store); }
        catch (Exception ex) { Run.ReportError("Could not prepare theft opening", ex); }
    }
    private void Loading() { Panel.Close(); Run.Reset(); NativeHooks.Reset(); _newRunPending = false; _pendingNoteLayout = null; }
    private void Loaded()
    {
        Run.Load(PlayerStore.Instance);
        if (Run.Qualified && Native.IsCareer(PlayerStore.Instance) && (int)PlayerStore.Instance.startType == Identity.NativeStartType)
        { PlayerStore.Instance.startType = NewGameData.StartType.MODDED; PlayerStore.Instance.startScenarioId = Identity.StartId; }
    }
    internal void Tick()
    {
        if (!Enabled) return;
        try
        {
            Run.FlushErrorNotification(); Panel.Tick();
            if (Run.State != null && !Run.Busy && !Run.Browsing && EmporiumEntry.Instance != null)
            {
                var pending = Run.State.Notes.LastOrDefault(x => x.Day == Native.Day && !x.Delivered && !x.Expired && !x.Invalidated);
                if (pending == null) _pendingNoteLayout = null;
                else
                {
                    string layout = Run.State.RunId + ":" + pending.Day + ":" + InventoryLayout.Key(Native.MainGrid);
                    if (layout != _pendingNoteLayout) { _pendingNoteLayout = layout; Run.DeliverPendingNote(); }
                }
            }
        }
        catch (Exception ex) { Run.ReportError("Theft frame failed", ex); }
    }
}

internal sealed class TheftDriver : MonoBehaviour
{
    private void Update() => Plugin.Active?.Tick();
}
