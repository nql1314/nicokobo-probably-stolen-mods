using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.SceneManagement;

namespace Nicokobo.ContentNativeProbe;

public sealed partial class ProbeRunner
{
    private sealed class LocaleOperationState
    {
        public bool valid, done;
        public string status = "Invalid";
        public string? error, locale;
        internal bool Succeeded => valid && done && status == "Succeeded";
    }

    private static LocaleOperationState ReadLocaleOperation<T>(AsyncOperationHandle<T> handle)
    {
        // Read real public handles without blocking or replacing an operation.
        var state = new LocaleOperationState { valid = handle.IsValid() };
        if (!state.valid) return state;
        state.done = handle.IsDone;
        state.status = handle.Status.ToString();
        state.error = handle.OperationException?.ToString();
        if (state.Succeeded && handle.Result is Locale locale) state.locale = locale.Identifier.Code;
        return state;
    }

    internal IEnumerable<object?> PrepareManagerLocale(ProbeOptions o, ProbeResult r)
    {
        if (o.Family is not ("manager" or "manageren"))
        {
            foreach (object? wait in PrepareCurrentLocale(r)) yield return wait;
            yield break;
        }
        string target = o.Family == "manageren" ? "en" : "zh";
        int targetIndex = target == "en" ? 0 : 1;
        string targetLabel = target == "en" ? "English" : "Simplified Chinese";
        r.phase = "manager-main-menu-locale:" + target;
        MainMenuUIController? menu;
        TMP_Dropdown? dropdown;
        LocaleOperationState initialization, selected;
        bool ready;
        float deadline = Time.realtimeSinceStartup + 60f;
        do
        {
            menu = MainMenuUIController.Instance;
            dropdown = menu == null ? null : menu.localeDropdown;
            initialization = ReadLocaleOperation(LocalizationSettings.InitializationOperation);
            selected = ReadLocaleOperation(LocalizationSettings.SelectedLocaleAsync);
            ready = menu != null && menu.gameObject.scene == SceneManager.GetActiveScene() &&
                dropdown != null && dropdown.isActiveAndEnabled && dropdown.gameObject.activeInHierarchy && dropdown.IsInteractable() &&
                dropdown.options.Count > targetIndex && dropdown.options[targetIndex].text == targetLabel &&
                initialization.Succeeded && selected.Succeeded;
            if (ready || Time.realtimeSinceStartup >= deadline || initialization.done && initialization.status == "Failed" || selected.done && selected.status == "Failed") break;
            yield return null;
        } while (true);
        r.Check("manager.mainmenu-locale-ready." + target,
            "Active/interactable native main-menu language dropdown with completed successful initialization and selected-locale operations",
            new { scene = SceneManager.GetActiveScene().name, menuScene = menu == null ? null : menu.gameObject.scene.name,
                active = dropdown != null && dropdown.isActiveAndEnabled && dropdown.gameObject.activeInHierarchy,
                interactable = dropdown != null && dropdown.IsInteractable(), targetIndex, targetLabel,
                actualOption = dropdown != null && dropdown.options.Count > targetIndex ? dropdown.options[targetIndex].text : null,
                initialization, selected }, ready);
        if (!ready) throw new InvalidOperationException("Native main-menu locale UI/initialization is not ready; InventoryScene must not start");

        string? before = selected.locale;
        int beforeIndex = dropdown!.value;
        bool eventRequired = before != target;
        var events = new List<string>();
        Action<Locale> changed = locale => events.Add(locale.Identifier.Code);
        LocalizationSettings.SelectedLocaleChanged += changed;
        try
        {
            // Invoke the existing control's real listener even if the index is
            // already selected. MainMenuUIController owns saving the locale key.
            dropdown.SetValueWithoutNotify(targetIndex);
            dropdown.onValueChanged.Invoke(targetIndex);
            deadline = Time.realtimeSinceStartup + 60f;
            bool completed;
            do
            {
                initialization = ReadLocaleOperation(LocalizationSettings.InitializationOperation);
                selected = ReadLocaleOperation(LocalizationSettings.SelectedLocaleAsync);
                completed = initialization.Succeeded && selected.Succeeded && selected.locale == target &&
                    (!eventRequired || events.Contains(target));
                if (completed || Time.realtimeSinceStartup >= deadline || initialization.done && initialization.status == "Failed" || selected.done && selected.status == "Failed") break;
                yield return null;
            } while (true);
            string persisted = ES3.LoadString("locale", "");
            bool applied = completed && dropdown.value == targetIndex && persisted == target;
            r.Check("manager.mainmenu-locale-applied." + target,
                new { locale = target, savedPreference = target, dropdownIndex = targetIndex, eventRequired, operationsSucceeded = true },
                new { before, beforeIndex, locale = selected.locale, savedPreference = persisted, dropdownIndex = dropdown.value,
                    selectedLocaleEvents = events.ToArray(), initialization, selected }, applied,
                "Actual TMP_Dropdown.onValueChanged -> MainMenuUIController.OnLocaleChanged -> native SetLocale and native locale preference save. The probe only reads ES3; it never writes trust, enabled state, locale or operation internals.");
            r.environment["managerLocalePreparation"] = new { family = o.Family, target, before, after = selected.locale,
                beforeIndex, afterIndex = dropdown.value, eventRequired, selectedLocaleEvents = events.ToArray(), initialization, selected,
                savedPreference = persisted, applied,
                retention = "Keep the selected locale for this entire save/reload process. A later manager (zh) process restores the isolated preference through the same main-menu control; no InventoryScene hot switch." };
            if (!applied) throw new InvalidOperationException("Native main-menu locale transition did not complete; InventoryScene must not start");
        }
        finally { LocalizationSettings.SelectedLocaleChanged -= changed; }
    }

    private static IEnumerable<object?> PrepareCurrentLocale(ProbeResult r)
    {
        r.phase = "main-menu-current-locale-readiness";
        float deadline = Time.realtimeSinceStartup + 60f;
        LocaleOperationState initialization, selected;
        bool ready; int stableFrames = 0; string? stableLocale = null;
        do
        {
            initialization = ReadLocaleOperation(LocalizationSettings.InitializationOperation);
            selected = ReadLocaleOperation(LocalizationSettings.SelectedLocaleAsync);
            var menu = MainMenuUIController.Instance;
            bool available = menu != null && menu.gameObject.scene == SceneManager.GetActiveScene() &&
                menu.localeDropdown != null && menu.localeDropdown.isActiveAndEnabled && menu.localeDropdown.IsInteractable() &&
                initialization.Succeeded && selected.Succeeded && !string.IsNullOrEmpty(selected.locale);
            if (available && selected.locale == stableLocale) stableFrames++; else stableFrames = 0;
            stableLocale = available ? selected.locale : null;
            ready = available && stableFrames >= 2;
            if (ready || Time.realtimeSinceStartup >= deadline || initialization.done && initialization.status == "Failed" || selected.done && selected.status == "Failed") break;
            yield return null;
        } while (true);
        r.Check("localization.current-menu-ready", "Native menu and successful initialization/selected-locale operations stable across complete frames", new
            { initialization, selected, stableFrames, stableLocale }, ready,
            "Read public async handles and await native menu initialization. No locale, operation, cache or business state is assigned.");
        r.environment["nativeLocalePreparation"] = new { initialization, selected, stableFrames, stableLocale, ready };
        if (!ready) throw new InvalidOperationException("Native current locale initialization has not settled; InventoryScene must not start");
    }
}
