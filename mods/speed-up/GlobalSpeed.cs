using HarmonyLib;
using UnityEngine;

namespace SpeedUp
{
    internal static class GlobalSpeed
    {
        static readonly GlobalSpeedClock Clock = new GlobalSpeedClock();
        internal static bool Active => Clock.Active;
        internal static string Status => Text.Get("global_status",
            Time.timeScale, SpeedUpSettings.ToggleGlobalHotkey, SpeedUpSettings.ResetSpeedHotkey);

        internal static void Update()
        {
            float current = Time.timeScale;
            float next = Clock.Update(current, SpeedUpSettings.GlobalEnabled, SpeedUpRuntime.InGame, SpeedUpSettings.GlobalMultiplier);
            if (current != next) Time.timeScale = next;
        }

        internal static void Release()
        {
            float current = Time.timeScale;
            float next = Clock.Update(current, false, false, 1f);
            if (current != next) Time.timeScale = next;
        }
    }

    // Rechecked on Build 26317870: Time.deltaTime is added once at the end of this private
    // method, including after a click resets the timer. Replace that increment only.
    [HarmonyPatch(typeof(CustomEventHandler), "HandleDoubleClickEvent")]
    internal static class GlobalSpeedDoubleClickClockPatch
    {
        static void Postfix(ref float ___timeDeltaSeconds, bool __runOriginal)
        {
            if (__runOriginal && GlobalSpeed.Active)
                ___timeDeltaSeconds = GlobalSpeedRules.RealTimeElapsed(___timeDeltaSeconds, Time.deltaTime, Time.unscaledDeltaTime);
        }
    }
}
