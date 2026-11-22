using HarmonyLib;
using AugMechanicalAscension.Domain;

namespace AugMechanicalAscension.Integration;

// A run-owned marker. It is never added to StartingPerkList, so it cannot be
// selected, priced, slotted, or traded against the ordinary start perk budget.
internal static class SameKindBargainPerk
{
    internal const string Id = "nicokobo.aug.neural_network";
    private static Action<string>? _log;
    private static HarmonyLib.Harmony? _harmony;
    private static bool _displayHooksInstalled;
    private static StartingPerk? _activePerk;
    private static DateTime _nextLocalizationRefreshUtc;

    internal static bool Install(HarmonyLib.Harmony harmony, Action<string> log)
    {
        _log = log;
        _harmony = harmony;
        try
        {
            Patch(harmony, nameof(PlayerStore.InitialSave), nameof(EnsurePrefix), true);
            Patch(harmony, nameof(PlayerStore.LoadGame), nameof(EnsurePostfix), false);
            var remove = AccessTools.Method(typeof(StartingPerk),
                nameof(StartingPerk.RemovePerk), [typeof(string)])
                ?? throw new MissingMethodException(nameof(StartingPerk),
                    nameof(StartingPerk.RemovePerk));
            harmony.Patch(remove, prefix: new HarmonyMethod(
                AccessTools.Method(typeof(SameKindBargainPerk), nameof(RemovePrefix))));
            log("[P3] Built-in Aug perk hooks installed");
            return true;
        }
        catch (Exception ex)
        {
            log($"[ERROR] [P3] Built-in perk hooks failed; {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static void Patch(HarmonyLib.Harmony harmony, string targetName,
        string patchName, bool prefix)
    {
        var target = AccessTools.Method(typeof(PlayerStore), targetName, Type.EmptyTypes)
            ?? throw new MissingMethodException(nameof(PlayerStore), targetName);
        var patch = new HarmonyMethod(AccessTools.Method(
            typeof(SameKindBargainPerk), patchName));
        harmony.Patch(target, prefix: prefix ? patch : null,
            postfix: prefix ? null : patch);
    }

    private static void EnsurePrefix(PlayerStore __instance) => Ensure(__instance);
    private static void EnsurePostfix(PlayerStore __instance) => Ensure(__instance);

    private static void Ensure(PlayerStore store)
    {
        if (store == null || NativeObject.Handle(store) == IntPtr.Zero ||
            !StartDefinition.MatchesNativeStartType((int)store.startType))
            return;
        EnsureDisplayHooks();
        store.startingPerks ??= new System.Collections.Generic.List<StartingPerk>();
        for (int index = 0; index < store.startingPerks.Count; index++)
        {
            if (store.startingPerks[index]?.id != Id) continue;
            ApplyDefinition(store.startingPerks[index]);
            _activePerk = store.startingPerks[index];
            return;
        }
        var perk = new StartingPerk();
        ApplyDefinition(perk);
        store.startingPerks.Add(perk);
        _activePerk = perk;
        _log?.Invoke("[P3] Built-in Aug perk attached; cost=0; slots=0");
    }

    internal static void Update()
    {
        if (DateTime.UtcNow < _nextLocalizationRefreshUtc) return;
        _nextLocalizationRefreshUtc = DateTime.UtcNow.AddSeconds(AugNumbers.Timing.BargainLocalizationRefreshSeconds);
        var perk = _activePerk;
        if (perk == null) return;
        try
        {
            if (NativeObject.Handle(perk) == IntPtr.Zero || perk.id != Id)
            {
                _activePerk = null;
                return;
            }
            var name = DisplayName();
            var description = Description();
            if (perk.rawName != name) perk.rawName = name;
            if (perk.rawDescription != description) perk.rawDescription = description;
        }
        catch { _activePerk = null; }
    }

    private static void ApplyDefinition(StartingPerk perk)
    {
        perk.id = Id;
        perk.cost = 0;
        perk.maxSlot = 0;
        perk.incompatiblePerks = new System.Collections.Generic.List<string>();
        perk.type = StartingPerk.StartingPerkType.POSITIVE;
        perk.rawName = DisplayName();
        perk.rawDescription = Description();
    }

    private static void EnsureDisplayHooks()
    {
        if (_displayHooksInstalled || _harmony == null) return;
        try
        {
            var name = AccessTools.Method(typeof(StartingPerk),
                nameof(StartingPerk.GetLocalizedDisplayName), Type.EmptyTypes)
                ?? throw new MissingMethodException(nameof(StartingPerk),
                    nameof(StartingPerk.GetLocalizedDisplayName));
            var description = AccessTools.Method(typeof(StartingPerk),
                nameof(StartingPerk.GetLocalizedDescription), Type.EmptyTypes)
                ?? throw new MissingMethodException(nameof(StartingPerk),
                    nameof(StartingPerk.GetLocalizedDescription));
            var tooltip = AccessTools.Method(typeof(StartingPerkElement),
                nameof(StartingPerkElement.SetTooltipContent), [typeof(StartingPerk)])
                ?? throw new MissingMethodException(nameof(StartingPerkElement),
                    nameof(StartingPerkElement.SetTooltipContent));
            _harmony.Patch(name, prefix: new HarmonyMethod(AccessTools.Method(
                typeof(SameKindBargainPerk), nameof(NamePrefix))));
            _harmony.Patch(description, prefix: new HarmonyMethod(AccessTools.Method(
                typeof(SameKindBargainPerk), nameof(DescriptionPrefix))));
            _harmony.Patch(tooltip, postfix: new HarmonyMethod(AccessTools.Method(
                typeof(SameKindBargainPerk), nameof(TooltipPostfix))));
            _displayHooksInstalled = true;
            _log?.Invoke("[P3] Aug perk localization hooks installed");
        }
        catch (Exception ex)
        {
            _log?.Invoke($"[ERROR] [P3] Aug perk localization hooks failed; {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static bool NamePrefix(StartingPerk __instance, ref string __result)
    {
        if (__instance?.id != Id) return true;
        __result = DisplayName();
        return false;
    }

    private static bool DescriptionPrefix(StartingPerk __instance, ref string __result)
    {
        if (__instance?.id != Id) return true;
        __result = Description();
        return false;
    }

    private static void TooltipPostfix(StartingPerk __0)
    {
        if (__0?.id != Id) return;
        var ui = PerkUIController.Instance;
        if (ui == null) return;
        if (ui.startingPerkTooltipTitle != null)
            ui.startingPerkTooltipTitle.text = DisplayName();
        if (ui.startingPerkTooltipDescription != null)
            ui.startingPerkTooltipDescription.text = Description();
        // This built-in perk costs no points; the native cost line is redundant.
        if (ui.startingPerkTooltipCost != null)
            ui.startingPerkTooltipCost.text = "";
    }

    private static string DisplayName() =>
        AugText.Get("aug.aug");

    private static string Description() => AugText.Get("aug.automatically_identifies_aug_customers_aug_customers_25", AugNumbers.OpeningAndSurvival.BargainThreshold1 - 1, AugNumbers.OpeningAndSurvival.BargainThreshold1, AugNumbers.OpeningAndSurvival.BargainThreshold2 - 1, AugNumbers.OpeningAndSurvival.BargainThreshold2, AugNumbers.OpeningAndSurvival.MaxExposure - 1);

    private static bool RemovePrefix(string perkId)
        => perkId != Id;
}
