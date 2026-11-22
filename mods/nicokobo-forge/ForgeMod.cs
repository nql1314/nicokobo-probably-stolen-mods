using HarmonyLib;
using Nicokobo.ContentInterop;
using UnityEngine;

namespace Nicokobo.Forge;

public sealed class ForgeMod : IMod, IServiceProvider
{
    public const string Id = "nicokobo.forge";
    private readonly MechanicLocalizedPatches _modulePatches = new(Id, "module-effects", InstallLocalizedPatches);
    private bool _enabled;
    public void Init(ModManifest manifest)
    {
        OfficialWorkshopApi.DefineConfig();
        ForgeDossier.DefineConfig();
    }
    public void OnEnable()
    {
        try
        {
            ContentRuntime.Attach(Id);
            _enabled = true;
            ForgeRuntime.Attach();
            ModHook.OnModAssetsLoaded += AssetsReady;
        }
        catch
        {
            OnDisable();
            throw;
        }
    }
    public void OnDisable()
    { try { _modulePatches.Stop(); ModHook.OnModAssetsLoaded -= AssetsReady; ContentRuntime.Shutdown(); ForgeRuntime.Shutdown(); } finally { FixedModuleAbilities.Clear(); _enabled = false; } }
    public object? GetService(Type serviceType)
    {
        if (!_enabled || serviceType != typeof(Func<string, object[], object>)) return null;
        EnsureServices();
        return (Func<string, object[], object?>)Dispatch;
    }

    private void AssetsReady(LoadedMod mod)
    {
        if (!_enabled) return;
        EnsureServices();
        if (mod.Manifest.ID != Id) return;

        // The loader registers Forge after OnEnable. The coroutine needs that registration.
        try
        {
            _modulePatches.Start();
        }
        catch
        {
            OnDisable();
            throw;
        }
    }

    private static void InstallLocalizedPatches(Harmony harmony)
    {
        FixedModuleAbilities.Patch(harmony);
        if (!NativeLiquidValueRuntime.Install(Debug.LogWarning))
            throw new InvalidOperationException("Liquid value hooks unavailable");
    }

    private void EnsureServices()
    {
        try { ForgeRuntime.Ready(); }
        catch { OnDisable(); throw; }
    }

    private object? Dispatch(string operation, object[] args)
    {
        if (operation == "runtime.detach")
        { FixedModuleAbilities.Remove((string)args[0]); ContentRuntime.Detach((string)args[0]); return null; }
        if (operation == "module.abilities.remove") { FixedModuleAbilities.Remove((string)args[0]); return null; }
        if (!_enabled) throw new InvalidOperationException("NicoKobo Forge is disabled; restart with the prerequisite enabled.");
        switch (operation)
        {
            case "protocol": return 1;
            case "module.abilities.version": return FixedModuleAbilities.ApiVersion;
            case "module.abilities.register":
                FixedModuleAbilities.Register((string)args[0], (string)args[1], (Func<GameItem, bool>)args[2],
                    (Func<string>)args[3], (Func<string>)args[4], (Action<GameItem, GameInventory?>?)args[5]); return null;
            case "module.abilities.contains": return FixedModuleAbilities.Contains((string)args[0]);
            case "module.abilities.add": FixedModuleAbilities.Add((GameItem)args[0], (string)args[1], (int)args[2]); return null;
            case "runtime.attach": ContentRuntime.Attach((string)args[0]); return null;
            case "runtime.attached": return ContentRuntime.IsAttached((string)args[0]);
            case "recipe.open": RecipeQuery.OpenItem((string)args[0]); return null;
            case "extra.register": ExtraRecipes.Register((string)args[0], (ExtraRecipe)ForgeWire.Recipe((object[])args[1])); return null;
            case "extra.for": return ExtraRecipes.ForMachine((string)args[0]).Select(ForgeWire.Recipe).ToArray();
            case "extra.machines": return ExtraRecipes.MachineIds().ToArray();
            case "machine.register":
                var ports = (Func<GameItem, object[]>)args[2]; var recipes = (Func<object[][]>)args[3];
                MachineCatalog.Register((string)args[0], (string)args[1], item => ForgeWire.Ports(ports(item)), () => recipes().Select(ForgeWire.Recipe).ToArray(), (int)args[4]); return null;
            case "machine.native":
                var native = (Func<object[][]>)args[2];
                MachineCatalog.AddNativeRecipes((string)args[0], (string)args[1], () => native().Select(ForgeWire.Recipe).ToArray()); return null;
            case "machine.contains": return MachineCatalog.Contains((string)args[0]);
            case "machine.ids": return MachineCatalog.MachineIds().ToArray();
            case "machine.ports": var result = MachineCatalog.Ports((GameItem)args[0]); return result == null ? null : ForgeWire.Ports(result);
            case "machine.recipes": return MachineCatalog.Recipes((string)args[0]).Select(ForgeWire.Recipe).ToArray();
            case "production.flush": ProductionHooks.RegisterNightFlush((string)args[0], (Action)args[1]); return null;
            case "production.subscribe":
                var before = (Action<object[]>?)args[2]; var after = (Action<object[]>?)args[3];
                ProductionHooks.Subscribe((string)args[0], (string)args[1], before == null ? null : context => before(ForgeWire.Context(context)),
                    after == null ? null : work => after(ForgeWire.Result(work))); return null;
            case "production.begin":
                var attempt = ProductionHooks.Begin(ForgeWire.Context((object[])args[0]));
                return new object[] { (Action)attempt.Commit, (Func<bool>)(() => attempt.HasInputConsumption), (Action)attempt.Dispose };
            case "power.register": DevicePower.Register((string)args[0], (Func<GameItem, int?>)args[1], (Func<GameItem, int, bool?>)args[2]); return null;
            case "power.energy": bool handled = DevicePower.TryEnergy((GameItem)args[0], out int energy); return new object[] { handled, energy };
            case "power.debit": bool paid = DevicePower.TryDebit((GameItem)args[0], (int)args[1], out bool success); return new object[] { paid, success };
            case "supply.register": SupplyCatalog.Register((string)args[0], ((object[][])args[1]).Select(ForgeWire.Offer)); return null;
            case "supply.available": return SupplyCatalog.Available((StockCategory)(int)args[0], (StockSupplier)(int)args[1], (bool)args[2]).Select(ForgeWire.Offer).ToArray();
            default: return ForgeServices.Dispatch(operation, args);
        }
    }
}
