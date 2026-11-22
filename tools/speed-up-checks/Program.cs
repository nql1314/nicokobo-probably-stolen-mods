using System.Reflection;
using HarmonyLib;
using SpeedUp;
using UnityEngine;

int assertions = 0;
var config = ModHelper.GetConfig("nicokobo.speed-up");
void Set<T>(string key, T value) => ((ConfigEntry<T>)config.Get(key)).Set(value);
void Check(bool condition, string message)
{
    assertions++;
    if (!condition) throw new InvalidOperationException(message);
}

void Advance(DialogUIManager ui)
{
    foreach (Type type in typeof(SpeedUpSettings).Assembly.GetTypes())
    {
        var patch = type.GetCustomAttribute<HarmonyPatch>();
        if (patch?.Type == ui.GetType() && patch.Method == nameof(DialogUIManager.ContinueConversation))
        {
            object[] args = { ui, ui.displayingUncompletedDialog };
            type.GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args);
            ui.displayingUncompletedDialog = (Dialogue)args[1];
        }
    }

    // Relevant native ContinueConversation order: validate the visitor, settle the
    // pending displayed line, then move to nextDialogue (Mono Build 26263409,
    // DialogUIManager / DialoguePhoneUIManager.ContinueConversation).
    if (ui.currentDialog == null || (ui is not DialoguePhoneUIManager && !ui.HasClient)) return;
    if (ReferenceEquals(ui.currentDialog, ui.displayingUncompletedDialog))
    {
        ui.displayingUncompletedDialog = null;
        ui.currentDialog.ExecuteEndAction();
    }
    ui.currentDialog = ui.currentDialog.nextDialogue;
}

config.Saved["SpeedMultiplier"] = 6.5f;
config.Saved["ClientArriveSpeed"] = 2.5f;
config.Saved["ResetSpeedHotkey"] = "None";
SpeedUpSettings.Initialize("nicokobo.speed-up");
Check(LocHelper.LocaleReads == 0, "Registering configuration must not touch the game's locale before the settings window opens.");
Check(config.Writes == 0 && SpeedUpSettings.Multiplier == 6.5f && SpeedUpSettings.ClientArriveSpeed == 2.5f
    && SpeedUpSettings.ResetSpeedHotkey.Key == KeyCode.None, "Startup must preserve existing settings, including unbound shortcuts.");
Check(!SpeedUpSettings.GlobalEnabled && !SpeedUpSettings.ShowSpeedStatus,
    "Global speed and the persistent speed display remain off by default.");
var hotkeys = config.Entries.Where(entry => entry.Section == "speed.config.hotkeys").ToArray();
Check(hotkeys.Length == 9, "All hotkey settings must share the stable section key.");
foreach (var entry in hotkeys)
    Check(KeyCombination.TryParse(((ConfigEntry<string>)entry).Value, out _), "A default hotkey must be parseable.");
Set("SpeedMultiplier", 4f);
Check(SpeedUpSettings.ApplyTypewriterDelay(.8f) == .2f, "Dialogue uses its configured multiplier.");
Set("QueueDelayMultiplier", 2f);
Check(SpeedUpSettings.ApplyQueueDelay(.8f) == .4f && SpeedUpSettings.ApplyQueueDelay(0f) == 0f,
    "Queue speed must preserve immediate native actions.");
Set("InstantText", true);
Check(SpeedUpSettings.ApplyTypewriterDelay(.8f) == .001f && SpeedUpSettings.ApplyQueueDelay(.8f) == .001f,
    "Instant text keeps a nonzero coroutine delay.");
Set("Enabled", false);
Check(SpeedUpSettings.ApplyTypewriterDelay(.8f) == .8f && SpeedUpSettings.ApplyQueueDelay(.8f) == .8f,
    "Disabling restores native dialogue timing even when instant text is configured.");
Set("Enabled", true);
Set("InstantText", false);

foreach (bool phone in new[] { false, true })
{
    DialogUIManager Ui() => phone ? new DialoguePhoneUIManager() : new DialogUIManager();

    // The blackmailer adds the payment choice from the PREVIOUS line's delayed
    // end action. Fast flow can reach the choices before the native 1.1s callback.
    // 0.99s + 0.85s pauses at maximum native dialogue speed take only 0.92s
    // with typewriter waits skipped, even before QueueDelayMultiplier > 1.
    {
        var ui = Ui(); var choices = new Dialogue(); int setups = 0;
        var previous = new Dialogue { nextDialogue = choices };
        previous.endAction = () => { setups++; choices.choices.Add("Pay"); };
        ui.currentDialog = previous;
        Check(ui.displayingUncompletedDialog == null, "Fixture must represent a fully displayed line.");
        Check((0.99f + 0.85f) * 0.5f < 1.1f, "Fixture no longer reaches choices before the native callback.");
        Advance(ui);
        Check(ReferenceEquals(ui.currentDialog, choices), "Fast flow did not advance to the choice line.");
        Check(choices.choices.Contains("Pay"), $"{(phone ? "Phone" : "Store")}: payment option missing after accelerated advance.");

        // The already queued native coroutine must remain safe after early flow.
        previous.ExecuteEndAction();
        Check(setups == 1 && choices.choices.Count == 1, "The delayed callback registered duplicate payment options.");
        Check(ui.displayingUncompletedDialog == null, "Native advance did not clear the pending line.");
    }

    // Completed callbacks, the existing manual-skip path and the master switch.
    foreach (bool enabled in new[] { false, true })
    foreach (bool executed in new[] { false, true })
    foreach (bool stillTyping in new[] { false, true })
    {
        Set("Enabled", enabled);
        var ui = Ui(); int calls = 0;
        var line = new Dialogue { endAction = () => calls++, isEndActionExecuted = executed, nextDialogue = new() };
        ui.currentDialog = line;
        if (stillTyping) ui.displayingUncompletedDialog = line;
        Advance(ui);
        Check(calls == (!executed && (enabled || stillTyping) ? 1 : 0), "Callback/switch/manual-skip behavior changed.");
        line.ExecuteEndAction();
        Check(calls == (executed ? 0 : 1), "Native late callback executed more than once.");
    }

    Set("Enabled", true);
    if (!phone)
    {
        var ui = Ui(); int calls = 0;
        ui.currentDialog = new() { endAction = () => calls++ };
        ui.HasClient = false;
        Advance(ui);
        Check(calls == 0, "A missing visitor bypassed the native guard and ran story effects.");
    }
    {
        var ui = Ui(); Advance(ui);
        Check(ui.currentDialog == null, "An empty conversation was changed.");
        ui.currentDialog = new() { nextDialogue = new() };
        Advance(ui);
        Check(ui.displayingUncompletedDialog == null, "An action-free line was marked pending.");
    }
}

var client = new StoreClientMono();
var generic = new GenericClientMono();
Set("ClientArriveSpeed", 3f);
Set("ClientLeaveSpeed", 1f);
ClientAnimSpeed.ApplyArrive(client.dOTweenPath, client.pathRight);
ClientAnimSpeed.ApplyArrive(generic.dOTweenPath);
Check(client.dOTweenPath.tween.timeScale == 3f && generic.dOTweenPath.tween.timeScale == 3f,
    "Arrival uses the configured multiplier.");
ClientAnimSpeed.ApplyLeave(client.dOTweenPath, client.pathRight);
ClientAnimSpeed.ApplyLeave(generic.dOTweenPath);
Check(client.dOTweenPath.tween.timeScale == 1f && client.pathRight.tween.timeScale == 1f
    && generic.dOTweenPath.tween.timeScale == 1f, "A 1x departure must reset the cached arrival multiplier.");
ClientAnimSpeed.ApplyArrive(client.dOTweenPath, client.pathRight);
ClientAnimSpeed.ApplyArrive(generic.dOTweenPath);
Set("ClientAnimEnabled", false);
ClientAnimSpeed.ApplyLeave(client.dOTweenPath, client.pathRight);
ClientAnimSpeed.ApplyLeave(generic.dOTweenPath);
Check(client.dOTweenPath.tween.timeScale == 1f && client.pathRight.tween.timeScale == 1f
    && generic.dOTweenPath.tween.timeScale == 1f, "Disabling animation acceleration restores native speed on the next animation.");
client.dOTweenPath = null;
generic.dOTweenPath.tween = null;
ClientAnimSpeed.ApplyArrive(client.dOTweenPath, client.pathRight);
ClientAnimSpeed.ApplyArrive(generic.dOTweenPath);
Check(client.pathRight.tween.timeScale == 1f, "Missing paths and tweens remain harmless.");

var shutter = new StoreShutterButton();
float duration = 3f;
Set("ClientAnimEnabled", true);
ShutterAnimSpeed.Apply(shutter, ref duration, false);
Check(duration == 1f, "Opening uses the arrival rate and the button's original duration.");
ShutterAnimSpeed.Apply(shutter, ref duration, true);
Check(duration == 3f, "Closing at 1x restores the original duration without compounding.");
ShutterAnimSpeed.Apply(shutter, ref duration, false);
Set("ClientAnimEnabled", false);
ShutterAnimSpeed.Apply(shutter, ref duration, false);
Check(duration == 3f, "Disabling shutter acceleration restores its own original duration.");
Set("ClientAnimEnabled", true);
var nextShutter = new StoreShutterButton();
duration = 4.5f;
ShutterAnimSpeed.Apply(nextShutter, ref duration, false);
Check(duration == 1.5f, "A new shutter instance captures its own baseline.");

var clock = new GlobalSpeedClock();
Check(clock.Update(1f, false, true, 4f) == 1f && !clock.Active, "Global acceleration is opt-in.");
Check(clock.Update(1f, true, false, 4f) == 1f && !clock.Active, "The main menu does not acquire a global speed override.");
Check(clock.Update(1f, true, true, 10f) == 10f && clock.Active, "An enabled gameplay session can run at 10x global speed.");
Check(clock.Update(10f, true, true, 3.5f) == 3.5f, "Runtime preference changes affect global speed.");
Check(clock.Update(0f, true, true, 3.5f) == 0f, "A game pause must never be forcibly resumed by global speed.");
Check(clock.Update(1f, true, true, 3.5f) == 3.5f, "Global speed resumes only after the game resumes itself.");
Check(clock.Update(3.5f, false, true, 3.5f) == 1f && !clock.Active, "Disabling restores the pre-override time scale.");
Check(clock.Update(.75f, true, true, 2f) == 2f && clock.Update(2f, true, false, 2f) == .75f,
    "Leaving gameplay restores the time scale owned by the previous game or another Mod.");
clock.Update(1f, true, true, 3f);
Check(clock.Update(.25f, false, true, 3f) == .25f && !clock.Active,
    "Releasing an override must preserve a newer time scale written by the game or another Mod.");
clock.Update(1f, true, true, 3f);
Check(clock.Update(0f, false, true, 3f) == 0f && !clock.Active,
    "Disabling the Mod during a game pause must leave the pause intact.");
Check(GlobalSpeedRules.Clamp(float.NaN) == 1 && GlobalSpeedRules.Clamp(float.PositiveInfinity) == 1
    && GlobalSpeedRules.Clamp(-1) == .5f && GlobalSpeedRules.Clamp(99) == 10f,
    "Invalid or out-of-range global settings cannot stop or overdrive the game clock.");
Check(Math.Abs(GlobalSpeedRules.RealTimeElapsed(.28f, .08f, .016f) - .216f) < .0001f,
    "An observed 5x scaled double-click clock still uses a real-time 16ms interval.");
Check(GlobalSpeedRules.RealTimeElapsed(.08f, .08f, .016f) == .016f,
    "The first increment after a native click reset also uses real time.");
Check(GlobalSpeedRules.RealTimeElapsed(.2f, 0f, .016f) == .216f,
    "Paused game clocks still allow real-time UI double-clicks.");

SpeedUpSettings.StepGlobalSpeed(2f);
Set("InstantText", true);
SpeedUpSettings.RestoreNormalSpeed();
Check(!SpeedUpSettings.GlobalEnabled && SpeedUpSettings.GlobalMultiplier == 1f && !SpeedUpSettings.Instant
    && SpeedUpSettings.Multiplier == 1f && SpeedUpSettings.QueueMultiplier == 1f
    && SpeedUpSettings.ClientArriveSpeed == 1f && SpeedUpSettings.ClientLeaveSpeed == 1f,
    "Restore-normal must undo global and all local acceleration together.");
SpeedUpSettings.CyclePreset();
Check(SpeedUpSettings.Multiplier == 4f && SpeedUpSettings.QueueMultiplier == 1f && SpeedUpSettings.ClientArriveSpeed == 3f,
    "Normal cycles to Balanced.");
SpeedUpSettings.CyclePreset();
Check(SpeedUpSettings.Multiplier == 8f && SpeedUpSettings.QueueMultiplier == 2f && SpeedUpSettings.ClientArriveSpeed == 5f,
    "Balanced cycles to Fast.");
SpeedUpSettings.CyclePreset();
Check(SpeedUpSettings.Multiplier == 1f && SpeedUpSettings.ClientLeaveSpeed == 1f, "Fast cycles to Normal.");
Set("SpeedMultiplier", 6.5f);
SpeedUpSettings.CyclePreset();
Check(SpeedUpSettings.Multiplier == 1f, "Custom settings cycle to Normal first.");
SpeedUpSettings.StepGlobalSpeed(100f);
SpeedUpSettings.CyclePreset();
Check(SpeedUpSettings.GlobalEnabled && SpeedUpSettings.GlobalMultiplier == 10f,
    "Local presets leave the global setting intact, and the global multiplier stays capped.");
Set("SpeedMultiplier", float.NaN);
Set("ClientArriveSpeed", float.PositiveInfinity);
Check(SpeedUpSettings.Multiplier == 4f && SpeedUpSettings.ClientArriveSpeed == 3f,
    "Non-finite native config values must not poison text waits or tweens.");

Check(KeyCombination.TryParse("Alt+Equals", out var dialogueKey) && KeyCombination.TryParse("Equals", out _),
    "Documented shortcut strings parse.");
KeyCombination.TryParse("Equals", out var animationKey);
Input.Down.Add(KeyCode.Equals);
Input.Held.Add(KeyCode.LeftAlt);
Check(dialogueKey.IsPressed() && !animationKey.IsPressed(), "Alt+= must not also trigger the bare = animation binding.");
Input.Held.Add(KeyCode.RightControl);
Check(!dialogueKey.IsPressed() && !animationKey.IsPressed(), "Unlisted modifiers suppress the shortcut.");
Input.Held.Clear();
Input.Down.Clear();
Input.Down.Add(KeyCode.KeypadPlus);
Check(animationKey.IsPressed(), "The default animation-up shortcut retains keypad-plus support.");
foreach (string disabled in new[] { "None", "none", "" })
    Check(KeyCombination.TryParse(disabled, out var key) && !key.IsPressed(), "Unbound shortcuts do not fire.");
foreach (string invalid in new[] { "43", "Ctrl+Ctrl+Equals", "Mouse0", "Escape", "Alt+UnknownKey" })
    Check(!KeyCombination.TryParse(invalid, out _), "Invalid or non-keyboard bindings are rejected.");
// Shared values can already belong to the game or another Mod.
Set("Enabled", true);
Set("InstantText", false);
Set("SpeedMultiplier", 1f);
Set("QueueDelayMultiplier", 1f);
Check(SpeedUpSettings.ApplyTypewriterDelay(0f) == 0f
    && SpeedUpSettings.ApplyQueueDelay(.0005f) == .0005f, "Normal speed must leave native timing untouched.");
Set("ClientAnimEnabled", false);
var externalPath = new DG.Tweening.DOTweenPath();
externalPath.tween.timeScale = .5f;
ClientAnimSpeed.ApplyArrive(externalPath);
Check(externalPath.tween.timeScale == .5f, "Disabled animation speed must not overwrite another Mod.");
Set("ClientAnimEnabled", true);
Set("ClientArriveSpeed", 3f);
ClientAnimSpeed.ApplyArrive(externalPath);
Check(externalPath.tween.timeScale == 1.5f, "Arrival should scale the existing tween speed.");
ClientAnimSpeed.ApplyArrive(externalPath);
Check(externalPath.tween.timeScale == 1.5f, "Reusing a path must not compound the multiplier.");
externalPath.tween.timeScale = .25f;
Set("ClientAnimEnabled", false);
ClientAnimSpeed.ApplyLeave(externalPath);
Check(externalPath.tween.timeScale == .25f, "Releasing animation speed must preserve a later writer.");
Set("ClientAnimEnabled", true);
var externalShutter = new StoreShutterButton();
duration = 3f;
ShutterAnimSpeed.Apply(externalShutter, ref duration, false);
duration = 6f;
ShutterAnimSpeed.Apply(externalShutter, ref duration, false);
Check(duration == 2f, "Shutter speed must use a newer duration supplied by another Mod.");
duration = 4f;
Set("ClientAnimEnabled", false);
ShutterAnimSpeed.Apply(externalShutter, ref duration, false);
Check(duration == 4f, "Disabling shutter speed must preserve a later duration writer.");
var cooperativeClock = new GlobalSpeedClock();
Check(cooperativeClock.Update(.5f, true, true, 1f) == .5f && !cooperativeClock.Active,
    "A neutral global multiplier must not acquire the game clock.");
cooperativeClock.Update(1f, true, true, 3f);
Check(cooperativeClock.Update(.25f, true, true, 3f) == .25f && !cooperativeClock.Active,
    "Global speed must yield when another system changes a running clock.");
Check(cooperativeClock.Update(1f, true, true, 3f) == 1f,
    "A yielded clock must not fight other writers on the following frames.");
cooperativeClock.Update(1f, false, true, 3f);
Check(cooperativeClock.Update(1f, true, true, 3f) == 3f,
    "Explicitly toggling global speed back on can acquire the clock again.");
var untouchedLine = new Dialogue { endAction = () => throw new Exception("Normal-speed flow changed"), nextDialogue = new() };
Advance(new DialogUIManager { currentDialog = untouchedLine });
Check(!untouchedLine.isEndActionExecuted, "Normal-speed flow must retain native end-action scheduling.");
Set("SpeedMultiplier", 4f);
var otherPendingLine = new Dialogue();
var externallyManagedUi = new DialogUIManager { currentDialog = new() { endAction = () => { }, nextDialogue = new() },
    displayingUncompletedDialog = otherPendingLine };
Advance(externallyManagedUi);
Check(ReferenceEquals(externallyManagedUi.displayingUncompletedDialog, otherPendingLine),
    "An unrelated pending dialogue marker must remain owned by its original writer.");
DialogUIManager.Instance = new() { clientBubble = new(), playerBubble = new(), secBubble = new() };
DialoguePhoneUIManager.Instance = new() { clientBubble = new(), playerBubble = new() };
Check(DialogueTypewriters.Contains(DialogUIManager.Instance.secBubble.typewriter)
    && DialogueTypewriters.Contains(DialoguePhoneUIManager.Instance.playerBubble.typewriter)
    && !DialogueTypewriters.Contains(new Febucci.UI.TypewriterByCharacter())
    && !DialogueTypewriters.Contains(null), "Typewriter acceleration must stay inside the native dialogue bubbles.");
var textPostfix = typeof(GetWaitAppearanceTimeOfPatch).GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic)!;
object[] skippedText = { DialogUIManager.Instance.clientBubble.typewriter, .8f, false };
textPostfix.Invoke(null, skippedText);
Check((float)skippedText[1] == .8f, "A replacement typewriter method must not be modified after its original was skipped.");
object[] unrelatedText = { new Febucci.UI.TypewriterByCharacter(), .8f, true };
textPostfix.Invoke(null, unrelatedText);
Check((float)unrelatedText[1] == .8f, "Other Mods' typewriters must not inherit Speed Up's dialogue multiplier.");
var ownedPath = new DG.Tweening.DOTweenPath();
ownedPath.tween.timeScale = .5f;
Set("ClientAnimEnabled", true);
ClientAnimSpeed.ApplyArrive(ownedPath);
ClientAnimSpeed.Release();
Check(ownedPath.tween.timeScale == .5f && externalPath.tween.timeScale == .25f,
    "Runtime cleanup restores only owned tween overrides.");
// The production HotkeyHandler and TextInputFocus are linked above. Observe
// actual setting writes rather than reimplementing their suppression logic.
{
    Set("ClientArriveSpeed", 3f); Set("ClientLeaveSpeed", 3f);
    Set("AnimSpeedUpHotkey", "Equals");
    Input.Held.Clear(); Input.Down.Clear(); Input.Down.Add(KeyCode.Equals);
    KeyBindHelper.SuppressHotkeys = true;
    HotkeyHandler.Update();
    Check(SpeedUpSettings.ClientArriveSpeed == 3f && SpeedUpSettings.ClientLeaveSpeed == 3f,
        "Native binding capture changed animation speed.");
    // The real native API remains true on the frame capture finishes.
    Time.frameCount++; HotkeyHandler.Update();
    Check(SpeedUpSettings.ClientArriveSpeed == 3f, "Native capture-end frame changed animation speed.");
    KeyBindHelper.SuppressHotkeys = false; Time.frameCount++; HotkeyHandler.Update();
    Check(SpeedUpSettings.ClientArriveSpeed == 3.5f && SpeedUpSettings.ClientLeaveSpeed == 3.5f,
        "The frame after native capture did not restore the animation hotkey.");

    var selected = new GameObject();
    var tmp = selected.AddComponent<TMPro.TMP_InputField>(); tmp.isFocused = true;
    UnityEngine.EventSystems.EventSystem.current = new() { currentSelectedGameObject = selected };
    Time.frameCount++; HotkeyHandler.Update();
    Check(SpeedUpSettings.ClientArriveSpeed == 3.5f, "TMP editing changed animation speed.");
    tmp.isFocused = false; HotkeyHandler.Update();
    Check(SpeedUpSettings.ClientArriveSpeed == 3.5f, "TMP submit frame changed animation speed.");
    Time.frameCount++; HotkeyHandler.Update();
    Check(SpeedUpSettings.ClientArriveSpeed == 4f, "Normal animation key stayed blocked after text submit.");
    var legacy = selected.AddComponent<UnityEngine.UI.InputField>(); legacy.isFocused = true;
    Time.frameCount++; HotkeyHandler.Update();
    Check(SpeedUpSettings.ClientArriveSpeed == 4f, "Legacy text focus changed animation speed.");
    legacy.isFocused = false; HotkeyHandler.Update();
    Check(SpeedUpSettings.ClientArriveSpeed == 4f, "Legacy text submit frame changed animation speed.");
    Time.frameCount++; Input.compositionString = "拼"; HotkeyHandler.Update();
    Check(SpeedUpSettings.ClientArriveSpeed == 4f, "IME composition changed animation speed.");
    Input.compositionString = ""; HotkeyHandler.Update();
    Check(SpeedUpSettings.ClientArriveSpeed == 4f, "IME completion frame changed animation speed.");
    Time.frameCount++; StoreUIManager.Editing = true; HotkeyHandler.Update();
    Check(SpeedUpSettings.ClientArriveSpeed == 4f, "Native text focus changed animation speed.");
    StoreUIManager.Editing = false; HotkeyHandler.Update();
    Check(SpeedUpSettings.ClientArriveSpeed == 4f, "Native text submit frame changed animation speed.");
    Time.frameCount++; Application.isFocused = false; HotkeyHandler.Update();
    Check(SpeedUpSettings.ClientArriveSpeed == 4f, "Unfocused application changed animation speed.");
    Application.isFocused = true; Time.frameCount++; HotkeyHandler.Update();
    Check(SpeedUpSettings.ClientArriveSpeed == 4.5f, "Normal animation hotkey failed after the input guards.");
    UnityEngine.EventSystems.EventSystem.current = null; Input.Down.Clear();
}
Console.WriteLine($"Speed Up checks passed: {assertions} assertions.");
