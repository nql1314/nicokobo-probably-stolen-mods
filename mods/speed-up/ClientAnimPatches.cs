using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DG.Tweening;
using HarmonyLib;

namespace SpeedUp
{
    internal static class ClientAnimSpeed
    {
        static readonly Dictionary<Tween, (float Baseline, float Applied)> Speeds = new();

        internal static void ApplyArrive(DOTweenPath path, DOTweenPath rightPath = null)
        {
            float speed = SpeedUpSettings.ClientAnimEnabled ? SpeedUpSettings.ClientArriveSpeed : 1f;
            Apply(path, speed);
            Apply(rightPath, speed);
        }

        internal static void ApplyLeave(DOTweenPath path, DOTweenPath rightPath = null)
        {
            float speed = SpeedUpSettings.ClientAnimEnabled ? SpeedUpSettings.ClientLeaveSpeed : 1f;
            Apply(path, speed);
            Apply(rightPath, speed);
        }

        static void Apply(DOTweenPath path, float speed)
        {
            var tween = path != null ? path.tween : null;
            if (tween == null) return;
            float current = tween.timeScale;
            bool tracked = Speeds.TryGetValue(tween, out var state);
            bool unchanged = tracked && current == state.Applied;
            if (speed == 1f)
            {
                if (unchanged) tween.timeScale = state.Baseline;
                Speeds.Remove(tween);
                return;
            }
            float baseline = unchanged ? state.Baseline : current;
            float next = baseline * speed;
            Speeds[tween] = (baseline, next);
            tween.timeScale = next;
        }

        internal static void Release()
        {
            foreach (var pair in Speeds)
                if (pair.Key.timeScale == pair.Value.Applied)
                    pair.Key.timeScale = pair.Value.Baseline;
            Speeds.Clear();
        }
    }

    [HarmonyPatch(typeof(StoreClientMono), nameof(StoreClientMono.StartApprochingStore))]
    internal static class StoreClientArrivePatch
    {
        static void Postfix(DOTweenPath ___dOTweenPath, bool __runOriginal)
        {
            if (__runOriginal) ClientAnimSpeed.ApplyArrive(___dOTweenPath);
        }
    }

    [HarmonyPatch(typeof(StoreClientMono), nameof(StoreClientMono.StartLeavingStore))]
    internal static class StoreClientLeavePatch
    {
        static void Postfix(DOTweenPath ___dOTweenPath, bool __runOriginal)
        {
            if (__runOriginal) ClientAnimSpeed.ApplyLeave(___dOTweenPath);
        }
    }

    [HarmonyPatch(typeof(StoreClientMono), nameof(StoreClientMono.StartLeavingStoreRight))]
    internal static class StoreClientLeaveRightPatch
    {
        static void Postfix(StoreClientMono __instance, bool __runOriginal)
        {
            if (__runOriginal) ClientAnimSpeed.ApplyLeave(__instance.pathRight);
        }
    }

    [HarmonyPatch(typeof(GenericClientMono), nameof(GenericClientMono.StartArrive))]
    internal static class GenericClientArrivePatch
    {
        static void Postfix(DOTweenPath ___dOTweenPath, bool __runOriginal)
        {
            if (__runOriginal) ClientAnimSpeed.ApplyArrive(___dOTweenPath);
        }
    }

    [HarmonyPatch(typeof(GenericClientMono), nameof(GenericClientMono.StartLeave))]
    internal static class GenericClientLeavePatch
    {
        static void Postfix(DOTweenPath ___dOTweenPath, bool __runOriginal)
        {
            if (__runOriginal) ClientAnimSpeed.ApplyLeave(___dOTweenPath);
        }
    }

    internal static class ShutterAnimSpeed
    {
        const float MinDuration = 0.1f;
        sealed class Timing
        {
            internal float Baseline, Applied;
        }
        static readonly ConditionalWeakTable<StoreShutterButton, Timing> Durations = new();

        internal static void Apply(StoreShutterButton button, ref float duration, bool isClosed)
        {
            if (button == null)
                return;
            // ToggleShutterDoor has already changed isClosed; the caller reads duration
            // afterwards for both animation interpolation and its flow coroutines.
            float speed = !SpeedUpSettings.ClientAnimEnabled ? 1f :
                isClosed ? SpeedUpSettings.ClientLeaveSpeed : SpeedUpSettings.ClientArriveSpeed;
            bool tracked = Durations.TryGetValue(button, out var state);
            bool unchanged = tracked && duration == state.Applied;
            if (speed == 1f)
            {
                if (unchanged) duration = state.Baseline;
                Durations.Remove(button);
                return;
            }
            float baseline = unchanged ? state.Baseline : duration;
            if (baseline <= 0f) return;
            if (!tracked)
            {
                state = new Timing();
                Durations.Add(button, state);
            }
            state.Baseline = baseline;
            state.Applied = System.Math.Min(baseline, System.Math.Max(MinDuration, baseline / speed));
            duration = state.Applied;
        }
    }

    [HarmonyPatch(typeof(StoreShutterButton), nameof(StoreShutterButton.ToggleShutterDoor))]
    internal static class ShutterTogglePatch
    {
        static void Postfix(StoreShutterButton __instance, ref float ___duration, bool ___isClosed, bool __runOriginal)
        {
            if (__runOriginal) ShutterAnimSpeed.Apply(__instance, ref ___duration, ___isClosed);
        }
    }
}
