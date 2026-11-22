using System;
using System.Globalization;

namespace SpeedUp
{
    internal static class SpeedUpSettings
    {
        const float MinDelaySeconds = .001f;
        const float Step = .5f;
        const string DialogueSection = "speed.config.dialogue", AnimationSection = "speed.config.animation",
            GlobalSection = "speed.config.global", DisplaySection = "speed.config.display", HotkeysSection = "speed.config.hotkeys";
        static ConfigEntry<bool> enabled, instant, clientAnimEnabled, showSpeedStatus, globalEnabled;
        static ConfigEntry<float> multiplier, queueMultiplier, arriveSpeed, leaveSpeed, globalMultiplier;
        static ConfigEntry<string> speedUp, speedDown, animUp, animDown, reset, cycle, toggleGlobal, globalUp, globalDown;

        internal static bool Enabled => enabled?.Value ?? true;
        internal static bool Instant => instant?.Value ?? false;
        internal static bool DialogueAccelerated => Enabled && (Instant || Multiplier > 1f || QueueMultiplier > 1f);
        internal static float Multiplier => Clamp(multiplier?.Value ?? 4f, 1f, 32f, 4f);
        internal static float QueueMultiplier => Clamp(queueMultiplier?.Value ?? 1f, 1f, 32f, 1f);
        internal static bool ClientAnimEnabled => clientAnimEnabled?.Value ?? true;
        internal static float ClientArriveSpeed => Clamp(arriveSpeed?.Value ?? 3f, 1f, 8f, 3f);
        internal static float ClientLeaveSpeed => Clamp(leaveSpeed?.Value ?? 3f, 1f, 8f, 3f);
        internal static bool ShowSpeedStatus => showSpeedStatus?.Value ?? false;
        internal static bool GlobalEnabled => globalEnabled?.Value ?? false;
        internal static float GlobalMultiplier => GlobalSpeedRules.Clamp(globalMultiplier?.Value ?? 1f);
        internal static KeyCombination SpeedUpHotkey => Parse(speedUp);
        internal static KeyCombination SpeedDownHotkey => Parse(speedDown);
        internal static KeyCombination AnimSpeedUpHotkey => Parse(animUp);
        internal static KeyCombination AnimSpeedDownHotkey => Parse(animDown);
        internal static KeyCombination ResetSpeedHotkey => Parse(reset);
        internal static KeyCombination CyclePresetHotkey => Parse(cycle);
        internal static KeyCombination ToggleGlobalHotkey => Parse(toggleGlobal);
        internal static KeyCombination GlobalUpHotkey => Parse(globalUp);
        internal static KeyCombination GlobalDownHotkey => Parse(globalDown);

        internal static void Initialize(string modId)
        {
            ModConfig config = ModHelper.GetConfig(modId);
            enabled = config.Define("Enabled", true, DialogueSection, () => Text.Get("dialogue_enabled"));
            multiplier = config.Define("SpeedMultiplier", 4f, DialogueSection, () => Text.Get("typewriter_multiplier"), 1f, 32f);
            queueMultiplier = config.Define("QueueDelayMultiplier", 1f, DialogueSection, () => Text.Get("between_lines"), 1f, 32f);
            instant = config.Define("InstantText", false, DialogueSection, () => Text.Get("instant_text"));

            clientAnimEnabled = config.Define("ClientAnimEnabled", true, AnimationSection, () => Text.Get("animation_enabled"));
            arriveSpeed = config.Define("ClientArriveSpeed", 3f, AnimationSection, () => Text.Get("arrival_multiplier"), 1f, 8f);
            leaveSpeed = config.Define("ClientLeaveSpeed", 3f, AnimationSection, () => Text.Get("departure_multiplier"), 1f, 8f);

            globalEnabled = config.Define("GlobalSpeedEnabled", false, GlobalSection, () => Text.Get("global_enabled"),
                () => Text.Get("global_help"));
            globalMultiplier = config.Define("GlobalSpeedMultiplier", 1f, GlobalSection, () => Text.Get("global_multiplier"),
                GlobalSpeedRules.MinMultiplier, GlobalSpeedRules.MaxMultiplier);
            showSpeedStatus = config.Define("ShowSpeedStatus", false, DisplaySection, () => Text.Get("show_status"));

            speedUp = Hotkey(config, "SpeedUpHotkey", "Alt+Equals", "dialogue_faster");
            speedDown = Hotkey(config, "SpeedDownHotkey", "Alt+Minus", "dialogue_slower");
            animUp = Hotkey(config, "AnimSpeedUpHotkey", "Equals", "animation_faster");
            animDown = Hotkey(config, "AnimSpeedDownHotkey", "Minus", "animation_slower");
            reset = Hotkey(config, "ResetSpeedHotkey", "Ctrl+Home", "reset");
            cycle = Hotkey(config, "CyclePresetHotkey", "Ctrl+F8", "cycle_preset");
            toggleGlobal = Hotkey(config, "ToggleGlobalSpeedHotkey", "Ctrl+F9", "toggle_global");
            globalUp = Hotkey(config, "GlobalSpeedUpHotkey", "Ctrl+PageUp", "global_faster");
            globalDown = Hotkey(config, "GlobalSpeedDownHotkey", "Ctrl+PageDown", "global_slower");
        }

        static ConfigEntry<string> Hotkey(ModConfig config, string key, string value, string label) =>
            config.Define(key, value, HotkeysSection, () => Text.Get(label), tooltip: () => Text.Get("hotkey_help"));

        static KeyCombination Parse(ConfigEntry<string> entry) =>
            KeyCombination.TryParse(entry?.Value, out var combo) ? combo : default;

        static float Clamp(float value, float min, float max, float fallback) =>
            float.IsNaN(value) || float.IsInfinity(value) ? fallback : Math.Clamp(value, min, max);

        internal static float ApplyTypewriterDelay(float delay)
        {
            if (!Enabled || delay <= 0f || !Instant && Multiplier == 1f) return delay;
            return Math.Min(delay, Instant ? MinDelaySeconds : Math.Max(MinDelaySeconds, delay / Multiplier));
        }

        internal static float ApplyQueueDelay(float delay)
        {
            if (!Enabled || delay <= 0f || !Instant && QueueMultiplier == 1f) return delay;
            return Math.Min(delay, Instant ? MinDelaySeconds : Math.Max(MinDelaySeconds, delay / QueueMultiplier));
        }

        internal static void IncreaseSpeed()
        {
            if (!Instant)
            {
                float next = Round(Multiplier + Step);
                multiplier.Set(Math.Min(32f, next));
                if (next >= 32f) instant.Set(true);
            }
            NotifyDialogue();
        }

        internal static void DecreaseSpeed()
        {
            if (Instant) { instant.Set(false); multiplier.Set(32f); }
            else multiplier.Set(Math.Max(1f, Round(Multiplier - Step)));
            NotifyDialogue();
        }

        static void NotifyDialogue() => Notify(Instant ? Text.Get("instant_text")
            : Text.Get("dialogue_speed", Multiplier));

        internal static void IncreaseClientAnimSpeed() => StepAnimation(Step);
        internal static void DecreaseClientAnimSpeed() => StepAnimation(-Step);

        static void StepAnimation(float step)
        {
            float next = Math.Clamp(Round(Math.Max(ClientArriveSpeed, ClientLeaveSpeed) + step), 1f, 8f);
            arriveSpeed.Set(next);
            leaveSpeed.Set(next);
            Notify(Text.Get("animation_speed", next));
        }

        internal static void RestoreNormalSpeed()
        {
            SetGlobalSpeed(1f, false, false);
            ApplyPreset(0);
        }

        internal static void CyclePreset()
        {
            bool normal = !Instant && (!Enabled || Multiplier == 1f && QueueMultiplier == 1f)
                && (!ClientAnimEnabled || ClientArriveSpeed == 1f && ClientLeaveSpeed == 1f);
            bool balanced = Enabled && ClientAnimEnabled && !Instant && Multiplier == 4f && QueueMultiplier == 1f
                && ClientArriveSpeed == 3f && ClientLeaveSpeed == 3f;
            ApplyPreset(normal ? 1 : balanced ? 2 : 0);
        }

        static void ApplyPreset(int preset)
        {
            enabled.Set(true);
            clientAnimEnabled.Set(true);
            instant.Set(false);
            multiplier.Set(preset == 0 ? 1f : preset == 1 ? 4f : 8f);
            queueMultiplier.Set(preset == 2 ? 2f : 1f);
            arriveSpeed.Set(preset == 0 ? 1f : preset == 1 ? 3f : 5f);
            leaveSpeed.Set(arriveSpeed.Value);
            string name = Text.Get(preset == 0 ? "normal" : preset == 1 ? "balanced" : "fast");
            Notify(Text.Get("preset", name, SpeedSummary()));
        }

        internal static string SpeedSummary() => Text.Get("summary",
            Enabled && Instant ? Text.Get("instant") : "×" + (Enabled ? Multiplier : 1f).ToString("0.#", CultureInfo.InvariantCulture),
            Enabled ? QueueMultiplier : 1f, ClientAnimEnabled ? ClientArriveSpeed : 1f, ClientAnimEnabled ? ClientLeaveSpeed : 1f);

        internal static void ToggleGlobalSpeed() => SetGlobalSpeed(GlobalMultiplier, !GlobalEnabled);
        internal static void StepGlobalSpeed(float step) => SetGlobalSpeed(GlobalMultiplier + step, true);

        static void SetGlobalSpeed(float value, bool active, bool notify = true)
        {
            globalMultiplier.Set(GlobalSpeedRules.Clamp(value));
            globalEnabled.Set(active);
            GlobalSpeed.Update();
            if (notify) Notify(active ? Text.Get("global_speed", GlobalMultiplier) : Text.Get("global_disabled"));
        }

        static float Round(float value) => (float)Math.Round(value / Step) * Step;

        static void Notify(string message)
        {
            SpeedUpMod.Log?.Log(message);
            SpeedNotice.Show(message);
        }
    }
}
