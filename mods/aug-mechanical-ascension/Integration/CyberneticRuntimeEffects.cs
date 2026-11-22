using HarmonyLib;
using AugMechanicalAscension.Domain;

namespace AugMechanicalAscension.Integration;

/// <summary>Native scavenging effects enabled by owned manufactured products.</summary>
internal static class CyberneticRuntimeEffects
{
    private static NativeSessionBridge? _bridge;
    private static Action<string>? _log;
    private static CyberneticConfig _fallback = new();
    private static bool _installed;

    internal static bool Install(HarmonyLib.Harmony harmony,
        NativeSessionBridge bridge, Action<string> log, CyberneticConfig fallback)
    {
        _bridge = bridge;
        _log = log;
        _fallback = fallback;
        _installed = false;
        harmony = new HarmonyLib.Harmony("nicokobo.aug.owned_scavenging");
        try
        {
            Patch(harmony, typeof(ScavHelper), nameof(ScavHelper.GetMaxScavAttempts),
                postfix: nameof(MaxScavPostfix));
            Patch(harmony, typeof(ScavHelper), nameof(ScavHelper.GetMinorWoundChance),
                postfix: nameof(WoundChancePostfix));
            Patch(harmony, typeof(ScavHelper), nameof(ScavHelper.GetMajorWoundChance),
                postfix: nameof(WoundChancePostfix));
            _installed = true;
            log("[P3] Owned ankle and infrared scavenging effects installed");
            return true;
        }
        catch (Exception ex)
        {
            harmony.UnpatchSelf();
            log($"[ERROR] [P3] Cybernetic runtime effects unavailable; {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static void Patch(HarmonyLib.Harmony harmony, Type type, string name,
        string? prefix = null, string? postfix = null, string? finalizer = null)
    {
        var target = NativeHookSet.Find(type, name, Type.EmptyTypes);
        HarmonyMethod? Hook(string? method) => method == null ? null :
            new HarmonyMethod(typeof(CyberneticRuntimeEffects), method);
        harmony.Patch(target, Hook(prefix), Hook(postfix), finalizer: Hook(finalizer));
    }

    private static void MaxScavPostfix(ref int __result)
    {
        if (!_installed) return;
        try
        {
            var store = (PlayerStore.HasInstance ? PlayerStore.Instance : null);
            if (!Valid(store)) return;
            __result = CyberneticItemOwnership.Capture(store)
                .ScavengingMaximum(__result, ConfigFor(store));
        }
        catch (Exception ex) { LogError("maximum", ex); }
    }

    private static void WoundChancePostfix(ref float __result)
    {
        if (!_installed) return;
        try
        {
            var store = (PlayerStore.HasInstance ? PlayerStore.Instance : null);
            if (!Valid(store)) return;
            __result = CyberneticItemOwnership.Capture(store)
                .ScavengingWoundChance(__result, ConfigFor(store));
        }
        catch (Exception ex) { LogError("wound chance", ex); }
    }

    private static CyberneticConfig ConfigFor(PlayerStore store)
    {
        var state = _bridge?.CurrentSession?.State;
        var config = state != null && NativeObject.Handle(store) == _bridge!.CurrentStorePointer &&
            store.runID == state.GameRunId && store.saveSlotId == state.SaveSlotId
            ? state.ConfigSnapshot.CyberneticPrices : _fallback;
        // This balance value follows the current build in existing runs too.
        return config.AnkleExtraScavAttempts == AugNumbers.Cybernetics.AnkleExtraScavAttempts
            ? config : config with { AnkleExtraScavAttempts = AugNumbers.Cybernetics.AnkleExtraScavAttempts };
    }

    private static bool Valid([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] PlayerStore? store) => store != null &&
        NativeObject.Handle(store) != IntPtr.Zero && !string.IsNullOrWhiteSpace(store.runID) && store.saveSlotId >= 0;

    private static void LogError(string action, Exception ex) =>
        _log?.Invoke($"[ERROR] [P3] Owned scavenging {action} unavailable; {ex.GetType().Name}: {ex.Message}");
}
