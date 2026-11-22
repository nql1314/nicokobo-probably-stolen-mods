using System.Collections;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Localization.Settings;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Nicokobo.ContentInterop;

internal sealed class MechanicLocalizedPatches(string modId, string patchGroup, Action<Harmony> install)
{
    private readonly Harmony _harmony = new(modId + "." + patchGroup);
    private Coroutine? _pending;
    private bool _installed;

    internal void Start() => _pending = ModHelper.StartCoroutine(modId, WaitAndInstall());

    internal void Stop()
    {
        if (_pending != null)
            ModHelper.StopCoroutine(modId, _pending);
        _pending = null;
        if (!_installed) return;
        _harmony.UnpatchAll(_harmony.Id);
        _installed = false;
    }

    private IEnumerator WaitAndInstall()
    {
        // Harmony detours can run native static initializers that synchronously
        // read Mechanic. Let Unity load that table asynchronously first.
        yield return null;
        var initialization = LocalizationSettings.InitializationOperation;
        yield return initialization;
        if (initialization.Status == AsyncOperationStatus.Succeeded)
        {
            var locale = LocalizationSettings.SelectedLocale;
            while (locale != null)
            {
                var table = LocalizationSettings.StringDatabase.GetTableAsync("Mechanic", locale);
                yield return table;
                var selected = LocalizationSettings.SelectedLocale;
                if (locale == selected)
                {
                    if (table.IsValid() && table.Status == AsyncOperationStatus.Succeeded && table.Result != null)
                    {
                        _pending = null;
                        Install();
                        yield break;
                    }
                    break;
                }
                locale = selected;
            }
        }
        _pending = null;
        Debug.LogError($"[{_harmony.Id}] Patches could not start: Mechanic localization is unavailable.");
    }

    private void Install()
    {
        try
        {
            install(_harmony);
            _installed = true;
            Debug.Log($"[{_harmony.Id}] Patches installed after Mechanic localization loaded.");
        }
        catch (Exception error)
        {
            _harmony.UnpatchAll(_harmony.Id);
            Debug.LogError($"[{_harmony.Id}] Patches failed: {error}");
        }
    }
}
