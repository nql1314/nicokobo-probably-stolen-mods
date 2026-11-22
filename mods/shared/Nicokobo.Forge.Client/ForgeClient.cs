using HarmonyLib;
using UnityEngine;

namespace Nicokobo.ContentInterop;

public static class ForgeClient
{
    private static Func<string, object[], object?>? _service;
    private static Action? _start, _stop;
    private static Action<LoadedMod>? _assets;
    private static string _owner = "";
    internal static bool Connected => _service != null;

    public static void Enable(string owner, Action start, Action stop, Action<LoadedMod>? assets = null)
    {
        if (_start != null) return;
        _owner = owner; _start = start; _stop = stop; _assets = assets;
        // The loader finishes local and Workshop discovery before loading any
        // assets. Deferring activation avoids dependence on folder/load order.
        ModHook.OnModAssetsLoaded += Ready;
    }
    private static void Ready(LoadedMod loaded)
    {
        string owner = _owner;
        var stop = _stop!;
        try
        {
            if (_service == null)
            {
                var host = ModLoader.Mods.SingleOrDefault(mod => mod.Manifest.ID == "nicokobo.forge" && mod.IsEnabled);
                if (host?.Instance is not IServiceProvider provider ||
                    provider.GetService(typeof(Func<string, object[], object>)) is not Func<string, object[], object?> service)
                    throw new InvalidOperationException("Enable NicoKobo Forge (nicokobo.forge) and restart before using " + owner);
                if (service("protocol", Array.Empty<object>()) is not int protocol || protocol != 1)
                    throw new InvalidOperationException("Incompatible NicoKobo Forge protocol; this content requires protocol 1.");
                _service = service;
                _start!();
            }
            // Keep ownership of the resource callback until this package is
            // loaded, so failure cleanup also covers other packages loading first.
            if (loaded.Manifest.ID == owner)
            {
                _assets?.Invoke(loaded);
                ModHook.OnModAssetsLoaded -= Ready;
            }
        }
        catch (Exception error)
        {
            ModHook.OnModAssetsLoaded -= Ready;
            try { stop(); }
            catch (Exception cleanup) { Debug.LogError("[" + owner + "] Cleanup failed: " + cleanup); }
            _service = null; _start = _stop = null; _assets = null;
            new Harmony("mod." + owner).UnpatchAll("mod." + owner);
            Debug.LogError("[" + owner + "] Content initialization stopped: " + error);
        }
    }
    public static void Disable(Action stop)
    {
        ModHook.OnModAssetsLoaded -= Ready;
        try { stop(); }
        finally { _service = null; _start = _stop = null; _assets = null; _owner = ""; }
    }
    internal static object? Call(string operation, params object[] args) =>
        (_service ?? throw new InvalidOperationException("NicoKobo Forge client is not connected."))(operation, args);
    internal static T Call<T>(string operation, params object[] args) => (T)Call(operation, args)!;
}
