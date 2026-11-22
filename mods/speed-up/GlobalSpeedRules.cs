using System;

namespace SpeedUp
{
    internal static class GlobalSpeedRules
    {
        internal const float MinMultiplier = .5f;
        internal const float MaxMultiplier = 10f;

        internal static float Clamp(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 1f : Math.Clamp(value, MinMultiplier, MaxMultiplier);

        internal static float RealTimeElapsed(float after, float scaled, float unscaled) => after - scaled + unscaled;
    }

    internal sealed class GlobalSpeedClock
    {
        bool owned, yielded, paused;
        float baseline = 1f, lastApplied = 1f;
        internal bool Active => owned;

        internal float Update(float current, bool enabled, bool inGame, float multiplier)
        {
            multiplier = GlobalSpeedRules.Clamp(multiplier);
            if (!enabled || !inGame || multiplier == 1f)
            {
                float restored = owned && Math.Abs(current - lastApplied) < .0001f ? baseline : current;
                owned = false;
                yielded = paused = false;
                return restored;
            }
            // A game pause belongs to the game; never resume it to apply a preference.
            if (current == 0f) { paused = true; return current; }
            if (yielded) return current;
            if (owned && Math.Abs(current - lastApplied) >= .0001f
                && !(paused && Math.Abs(current - baseline) < .0001f))
            {
                // Do not repeatedly undo a slow-motion effect or another Mod's clock.
                owned = false;
                yielded = true;
                paused = false;
                return current;
            }
            paused = false;
            if (!owned) { baseline = current; owned = true; }
            lastApplied = multiplier;
            return lastApplied;
        }
    }
}
