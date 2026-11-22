namespace SpeedUp
{
    internal static class HotkeyHandler
    {
        internal static void Update()
        {
            if (TextInputFocus.ShouldSuppressHotkeys()) return;
            if (SpeedUpSettings.ResetSpeedHotkey.IsPressed())
            { SpeedUpSettings.RestoreNormalSpeed(); return; }
            if (SpeedUpSettings.CyclePresetHotkey.IsPressed())
            { SpeedUpSettings.CyclePreset(); return; }
            if (SpeedUpSettings.ToggleGlobalHotkey.IsPressed())
            { SpeedUpSettings.ToggleGlobalSpeed(); return; }
            if (SpeedUpSettings.GlobalUpHotkey.IsPressed())
            { SpeedUpSettings.StepGlobalSpeed(.5f); return; }
            if (SpeedUpSettings.GlobalDownHotkey.IsPressed())
            { SpeedUpSettings.StepGlobalSpeed(-.5f); return; }

            if (SpeedUpSettings.AnimSpeedUpHotkey.IsPressed()) SpeedUpSettings.IncreaseClientAnimSpeed();
            else if (SpeedUpSettings.AnimSpeedDownHotkey.IsPressed()) SpeedUpSettings.DecreaseClientAnimSpeed();
            if (SpeedUpSettings.SpeedUpHotkey.IsPressed()) SpeedUpSettings.IncreaseSpeed();
            else if (SpeedUpSettings.SpeedDownHotkey.IsPressed()) SpeedUpSettings.DecreaseSpeed();
        }
    }
}
