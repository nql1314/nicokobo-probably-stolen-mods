using HarmonyLib;
using Nicokobo.ModUtilities;
using AugMechanicalAscension.Domain;
using AugMechanicalAscension.Persistence;
namespace AugMechanicalAscension.Integration;
internal sealed class PlayableStartFlow
{
    private sealed class InitialAttempt(PlayerStore store,PendingStart reservation)
    { internal readonly IntPtr StorePointer=NativeObject.Handle(store); internal readonly int ThreadId=Environment.CurrentManagedThreadId; internal readonly PendingStart Reservation=reservation; internal InitialStateStagedAttempt? Staged; }
    private static PlayableStartFlow? _active;
    [ThreadStatic] private static InitialAttempt? _initial;
    private readonly StartIntentCoordinator _intents=new();
    private readonly NativeSessionBridge _bridge;
    private readonly ConfigSnapshot _config;
    private readonly Action<string> _log;
    private PendingStart? _reservation;
    private Action<PlayerStore>? _onNewRunSaved;
    private PlayableStartFlow(NativeSessionBridge bridge,ConfigSnapshot config,Action<string> log) { _bridge=bridge;_config=config;_log=log; }
    internal void OnNewRunSaved(Action<PlayerStore> callback)=>_onNewRunSaved=callback;
    internal static PlayableStartFlow Install(Harmony harmony,NativeSessionBridge bridge,ConfigSnapshot config,Action<string> log)
    {
        var flow=new PlayableStartFlow(bridge,config,log); _active=flow;
        ModHelper.AddStartingScenario(MechcorePackage.Id, StartDefinition.Id,
            () => StartDefinition.Name.Display, () => StartDefinition.Description.Display, new Func<string>[] { StartingEquipment },
            flow.StartScenario, iconKey: MechcorePackage.Id + ":aug-start", skipIntro: true);
        harmony.Patch(AccessTools.DeclaredMethod(typeof(PlayerStore),nameof(PlayerStore.InitialSave)),prefix:new HarmonyMethod(typeof(PlayableStartFlow),nameof(InitialSavePrefix)),postfix:new HarmonyMethod(typeof(PlayableStartFlow),nameof(InitialSavePostfix)),finalizer:new HarmonyMethod(typeof(PlayableStartFlow),nameof(InitialSaveFinalizer)));
        harmony.Patch(AccessTools.DeclaredMethod(typeof(PlayerStore),nameof(PlayerStore.SaveGame)),prefix:new HarmonyMethod(typeof(PlayableStartFlow),nameof(SaveGamePrefix)));
        return flow;
    }

    private static string StartingEquipment() => AugText.Get("aug.start.equipment",
        AugText.Get(NeuralModuleRules.ItemId + ".name"), AugText.Get(OpeningJournalRules.MaterialRefinerId + ".name"),
        NativeItemText.Name("energy_credit", "energy_credit"), NativeItemText.Name("common_electronic", "common_electronic"),
        NativeItemText.Name("topical_bandage_item", "topical_bandage_item"), NativeItemText.Name("scrap_metal", "scrap_metal"),
        AugNumbers.OpeningItems.ScrapMetalCount, AugNumbers.OpeningItems.RefinerScrapMetalCount,
        NativeItemText.Name("sign_material", "sign_material"), AugNumbers.OpeningItems.MaterialSignCount,
        AugNumbers.OpeningAndSurvival.InitialSecurityReputation);
    private void StartScenario()
    {
        if(!NeuralModuleDirectory.ReadyForInitialSave) throw new InvalidOperationException("Brain-computer interface is unavailable");
        _intents.Invalidate();var epoch=_intents.OpenMenu();
        if(!_intents.SelectAug(epoch)||(_reservation=_intents.Confirm(epoch))==null)throw new InvalidOperationException("Opening reservation failed");
        _=RunStateCodec.Write(RunStateCodec.CreateNew(BuildProfile.ProfileId,Guid.NewGuid().ToString(),0,0,_config));
        OfficialWorkshopApi.GrantOpeningCard();
    }
    private void CancelSelection(){_intents.Invalidate();_reservation=null;}
    private static void InitialSavePrefix(PlayerStore __instance)
    {
        if (OfficialRunForkApi.IsForking(__instance)) return;
        var flow = _active;
        if (flow == null ||
            !StartDefinition.MatchesNativeStartType((int)__instance.startType))
            return;
        try
        {
            if (!NeuralModuleDirectory.ReadyForInitialSave)
                throw new InvalidOperationException(
                    "Aug initial-save rejected: neural node and overclock must both be Applied");
            if (flow._reservation is not { } reservation)
                throw new InvalidOperationException(
                    "Native InitialSave for startType 55 has no Aug intent");
            if (_initial != null || __instance.modData == null ||
                __instance.modData.ContainsKey(RunState.SaveKey) ||
                StoreStation.GetDayCounter() < 0)
                throw new InvalidOperationException("Aug initial-save preflight rejected");
            _initial = new InitialAttempt(__instance, reservation);
            flow._log("[P1] Aug InitialSave entered; native IDs pending");
        }
        catch
        {
            // Rejections before _initial exists never reach the finalizer's
            // staged-attempt cleanup. Retire the confirmed intent here too.
            _initial = null;
            flow.CancelSelection();
            throw;
        }
    }

    private static void SaveGamePrefix(PlayerStore __instance)
    {
        var flow = _active;
        var initial = _initial;
        if (flow == null || initial == null ||
            NativeObject.Handle(__instance) != initial.StorePointer ||
            Environment.CurrentManagedThreadId != initial.ThreadId)
            return;
        if (initial.Staged != null) return;
        try
        {
            var result = InitialStateNativeSaveAdapter.Stage(__instance,
                flow._intents, initial.Reservation, flow._config);
            if (result.Status != InitialStateStageStatus.Staged || result.Attempt == null)
            {
                flow._log($"[ERROR] [P1] Aug initial staging failed; status={result.Status}; "
                    + $"plan={result.PlanStatus}; write={result.WriteStatus}; error={result.Error}");
                return;
            }
            initial.Staged = result.Attempt;
            flow._log($"[P1] Aug initial state staged; slot={__instance.saveSlotId}; "
                + $"runID={__instance.runID}; startType={(int)__instance.startType}");
        }
        catch (Exception ex)
        {
            flow._log("[ERROR] [P1] Aug initial staging threw: " + ex);
        }
    }

    private static void InitialSavePostfix(PlayerStore __instance)
    {
        var flow = _active;
        var initial = _initial;
        _initial = null;
        if (flow == null || initial == null) return;
        flow._reservation = null;
        var result = InitialStateNativeSaveAdapter.Complete(__instance,
            initial.Staged, flow._bridge.Sessions);
        if (result.Status == InitialStateCompleteStatus.Adopted)
        {
            flow._bridge.MarkNewStore(__instance);
            flow._onNewRunSaved?.Invoke(__instance);
        }
        flow._log($"{(result.Status == InitialStateCompleteStatus.Adopted ? "" : "[ERROR] ")}[P1] Aug initial save result={result.Status}; "
            + $"readback={result.ReadbackStatus}; slot={__instance.saveSlotId}; "
            + $"error={result.Error ?? "none"}");
    }

    private static Exception? InitialSaveFinalizer(Exception? __exception)
    {
        if (__exception != null && _initial != null)
        {
            _active?._log($"[ERROR] [P1] Aug InitialSave threw; "
                + $"{__exception.GetType().Name}: {__exception.Message}");
            _initial = null;
            _active?._intents.Invalidate();
            if (_active != null) _active._reservation = null;
        }
        return __exception;
    }
}
