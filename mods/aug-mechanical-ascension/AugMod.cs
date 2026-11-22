using Nicokobo.ModUtilities;
using HarmonyLib;
using Nicokobo.ContentInterop;
using AugMechanicalAscension.Domain;
using AugMechanicalAscension.Integration;
using UnityEngine;

namespace AugMechanicalAscension;

public sealed class AugMod
{
    internal const string Id="nicokobo.aug";
    internal static bool Active { get; private set; }
    internal static void Log(string message)=>Debug.Log("[Aug] "+message);
    private ConfigEntry<bool>? _storyEnabled;
    private ConfigSnapshot _snapshot=new();
    private AugBehaviour? _behaviour;
    internal NativeSessionBridge? Bridge;
    internal OpeningRuntimeFlow? Opening;
    internal DayReconciliationFlow? Days;
    internal ExposureVisitRuntimeFlow? Visits;
    internal AugInteractionRuntimeFlow? Interactions;
    internal SameDayClientRecovery? Recovery;
    internal UI.StatusOverlay? Overlay;
    internal UI.StatusPageVitals? Vitals;
    internal StoryRuntimeFlow? Story;
    public void Init(ModManifest manifest)
    {
        var config=ModHelper.GetConfig(manifest.ID);
        _storyEnabled=config.Define("StoryEnabled",MechcorePackage.PreviousBoolean(Id,"StoryEnabled",true),"aug.config.story",
            () => AugText.Get("aug.story_enabled"));
        try
        {
            var loadedConfig=Persistence.ConfigSnapshotLoader.ParseBytes(ModHelper.ModDataFileExists(Id,"config.json")
                ?ModHelper.ReadModDataFileBytes(Id,"config.json"):null);
            _snapshot=loadedConfig.Config;
            if(loadedConfig.Status==Persistence.ConfigLoadStatus.Invalid)
                Log("Invalid config.json; using defaults: "+loadedConfig.Error);
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException)
        { Log("Unable to read scoped config.json: "+ex.Message); }
    }

    internal void EnableContent()
    {
        if(Active)return;
        FixedModuleAbilities.RequireVersion(1);
        Active=true;
        try
        {
            ContentRuntime.Attach(Id);
            ModHook.OnModItemDirectoryInit+=RegisterItems;
            ModHook.OnCreateTooltipEarly+=OfficialItemApi.LocalizeTooltip;
            Nicokobo.ContentInterop.NightShop.Register(Id,OfficialItemApi.NightShop);
            ModHook.OnLootTablesLoaded+=BionicRepairGelManufacturing.RegisterLoot;
            var hooks=new Harmony("nicokobo.aug.runtime");
            OfficialRunForkApi.Install(true,Log);
            if(!NeuralModuleDirectory.Install(hooks,Log))throw new InvalidOperationException("Brain-computer interface installation failed");
            Bridge=NativeSessionBridge.Install(hooks,Log)??throw new InvalidOperationException("Session lifecycle installation failed");
            Recovery=SameDayClientRecovery.Install(hooks,Bridge,Log)??throw new InvalidOperationException("Loaded customer recovery unavailable");
            RunForkIntegration.Install(Bridge);
            NeuralModuleDirectory.BindSession(Bridge);
            if(!NicoWorkshop.Install(hooks,Bridge,Log))throw new InvalidOperationException("Workshop installation failed");
            if(!CyberneticManufacturing.Install(Log))throw new InvalidOperationException("Cybernetic factories unavailable");
            if(!CyberneticRuntimeEffects.Install(hooks,Bridge,Log,_snapshot.CyberneticPrices))throw new InvalidOperationException("Owned cybernetic effects unavailable");
            if(!ProtectedCyberneticItems.Install(hooks,Log))throw new InvalidOperationException("Protected item guards unavailable");
            if(!JacksonOpeningNote.Install(hooks,Log))throw new InvalidOperationException("Opening introduction unavailable");
            Story=StoryRuntimeFlow.Install(Bridge,Log,()=>_storyEnabled?.Value!=false)??throw new InvalidOperationException("Story runtime unavailable");
            if(!ConsumableRuntimeFlow.Install(hooks,Bridge,Log))throw new InvalidOperationException("Medical and electronic recovery unavailable");
            if(!SameKindBargainPerk.Install(hooks,Log))throw new InvalidOperationException("Character passive unavailable");
            if(!BargainRuntimeFlow.Install(hooks,Bridge,Log))throw new InvalidOperationException("Aug trading features unavailable");
            if(!AugIdentityLabel.Install(hooks,Bridge,Log))throw new InvalidOperationException("Aug identity presentation unavailable");
            Days=DayReconciliationFlow.Install(hooks,Bridge,Log);
            Visits=ExposureVisitRuntimeFlow.Install(hooks,Bridge,Log)??throw new InvalidOperationException("Exposure visits unavailable");
            Interactions=new(Bridge,Log);
            var start=PlayableStartFlow.Install(hooks,Bridge,_snapshot,Log);
            Opening=new(Bridge,Log);start.OnNewRunSaved(Opening.QueueNewRun);
            Overlay=new(Bridge,Log,Story);Vitals=new(Bridge,Log);
            PsModInput.TextInputFocus.Register(Id,()=>!OfficialWorkshopApi.IsVisible&&Overlay?.TextEditing==true);
            _behaviour=new GameObject("NicoKobo Aug Runtime").AddComponent<AugBehaviour>();
            _behaviour.Owner=this;UnityEngine.Object.DontDestroyOnLoad(_behaviour.gameObject);
        }
        catch { DisableContent();throw; }
    }
    private static void RegisterItems(ModItemDirectory directory)
    {
        OfficialItemApi.Apply(directory);
        CyberneticManufacturing.RegisterRecipes();
    }
    internal static void AssetsLoaded(LoadedMod mod)
    {
        if(mod.Manifest.ID!=MechcorePackage.Id)return;
        ItemArtwork.Validate(mod);
        UI.StartingIconAssets.Load(mod);
    }
    internal void DisableContent()
    {
        if(!Active)return;Active=false;
        ModHook.OnModItemDirectoryInit-=RegisterItems;
        ModHook.OnCreateTooltipEarly-=OfficialItemApi.LocalizeTooltip;
        ModHook.OnLootTablesLoaded-=BionicRepairGelManufacturing.RegisterLoot;
        RunForkIntegration.Uninstall();StoryRuntimeFlow.Uninstall(Log);JacksonOpeningNote.Uninstall();
        NativeSessionBridge.Uninstall();NicoWorkshop.Uninstall();NeuralModuleDirectory.Uninstall();
        OfficialRunForkApi.Uninstall();OfficialLifecycleApi.Uninstall();
        ModHelper.RemoveStartingScenarios(MechcorePackage.Id);
        PsModInput.TextInputFocus.Unregister(Id);
        ContentRuntime.Detach(Id);
        foreach(string owner in new[]{"nicokobo.aug.runtime","nicokobo.aug.session","nicokobo.aug.neural","nicokobo.aug.owned_scavenging","nicokobo.aug.workshop.lifecycle","nicokobo.aug.workshop.observation","nicokobo.aug.bargain","nicokobo.aug.interactions","nicokobo.aug.protected-items"})new Harmony(owner).UnpatchSelf();
        if(_behaviour!=null)UnityEngine.Object.Destroy(_behaviour.gameObject);_behaviour=null;
    }
}
internal sealed class AugBehaviour : MonoBehaviour
{
    internal AugMod? Owner;
    private long _nextSlowUpdateLog;

    private void Update()
    {
        if (!AugMod.Active || Owner is not { } mod) return;
        var timing = new AugUpdateTiming();
        mod.Opening?.Update();
        SameKindBargainPerk.Update();
        JacksonOpeningNote.Update();
        timing.Step("opening/perks");
        mod.Days?.Update();
        mod.Visits?.Update();
        mod.Interactions?.Update();
        timing.Step("day/exposure/interactions");
        NeuralModuleDirectory.RefreshOwnedBases();
        timing.Step(nameof(NeuralModuleDirectory.RefreshOwnedBases));
        NicoWorkshop.Update();
        StoryIconAtlas.TryInstall();
        timing.Step("workshop/story icons");
        mod.Story?.Update();
        timing.Step(nameof(StoryRuntimeFlow));
        mod.Vitals?.Update();
        timing.Step(nameof(UI.StatusPageVitals));
        if (!OfficialWorkshopApi.IsVisible) mod.Overlay?.Update();
        timing.Step(nameof(UI.StatusOverlay));
        timing.LogIfSlow(nameof(AugBehaviour), ref _nextSlowUpdateLog);
    }
    private void LateUpdate(){if(!AugMod.Active)return;StoryJournalWindow.Update();JacksonOpeningNote.LateUpdate();Owner?.Recovery?.Update();}
    private void OnGUI(){if(!AugMod.Active)return;if(!OfficialWorkshopApi.IsVisible)Owner?.Overlay?.Draw();}
}

internal struct AugUpdateTiming
{
    private readonly long _start = System.Diagnostics.Stopwatch.GetTimestamp();
    private long _previous;
    private long _slowest;
    private string? _stage;

    public AugUpdateTiming() => _previous = _start;

    internal void Step(string stage)
    {
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        long elapsed = now - _previous;
        _previous = now;
        if (elapsed <= _slowest) return;
        _slowest = elapsed;
        _stage = stage;
    }

    internal void LogIfSlow(string scope, ref long nextLog)
    {
        const double SlowUpdateMilliseconds = 40;
        const int LogIntervalSeconds = 5;
        long frequency = System.Diagnostics.Stopwatch.Frequency;
        double totalMs = (_previous - _start) * 1000.0 / frequency;
        if (totalMs < SlowUpdateMilliseconds || _previous < nextLog) return;
        nextLog = _previous + LogIntervalSeconds * frequency;
        double stageMs = _slowest * 1000.0 / frequency;
        AugMod.Log($"[perf] {scope} total={totalMs:F1}ms; slowest={_stage} {stageMs:F1}ms");
    }
}
