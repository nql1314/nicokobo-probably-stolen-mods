using HarmonyLib;

namespace Nicokobo.ContentInterop;

// The built-in victory reward owns this multiplier; ordinary sales keep using
// the native balance, affordability check and ModBudget deduction.
internal static class NativeVictoryBudget
{
    private const string HookId = "nicokobo.forge.native_workshop.victory_budget";
    private static readonly HashSet<StoreClient> ScaledClients = [];
    private static bool _active;
    private static readonly HarmonyLib.Harmony Hooks = new(HookId);
    internal static bool Installed { get; private set; }

    internal static void Install()
    {
        if (Installed) return;
        try
        {
            NativeWorkshop.InstallHooks(Hooks,
            [
                new(AccessTools.DeclaredMethod(typeof(StoreClient), nameof(StoreClient.SetBudget), [typeof(int)]), Postfix: nameof(InitializedPostfix)),
                new(AccessTools.DeclaredMethod(typeof(StoreClient), nameof(StoreClient.SetBudget), [typeof(int), typeof(int)]), Postfix: nameof(InitializedPostfix)),
                new(AccessTools.DeclaredMethod(typeof(StoreClient), nameof(StoreClient.SetClientBudget), [typeof(int)]), Postfix: nameof(InitializedPostfix)),
                new(AccessTools.DeclaredMethod(typeof(StoreClient), nameof(StoreClient.SetClientBudget), [typeof(int), typeof(int)]), Postfix: nameof(InitializedPostfix)),
                new(AccessTools.DeclaredMethod(typeof(StoreClientInstance), nameof(StoreClientInstance.CreateClientInstance), [typeof(StoreClient)]), Prefix: nameof(ArrivalPrefix))
            ], name => new HarmonyMethod(typeof(NativeVictoryBudget), name));
            Installed = true;
        }
        catch { Uninstall(); throw; }
    }

    internal static void Uninstall() { Hooks.UnpatchAll(Hooks.Id); Installed = false; Reset(); }

    internal static void Reset()
    {
        _active = false;
        ScaledClients.Clear();
    }

    internal static void SetActive(bool claimed, StoreClient? currentClient)
    {
        _active = Installed && claimed;
        Apply(currentClient);
    }

    [HarmonyAfter("nicokobo.mechcore.synthesis.aug_trading")]
    private static void InitializedPostfix(StoreClient __instance)
    {
        if (__instance == null) return;
        // These setters assign a fresh native budget. A replacement budget
        // receives the reward again, without scaling its previous balance.
        ScaledClients.Remove(__instance);
        Apply(__instance);
    }

    private static void ArrivalPrefix(StoreClient __0) => Apply(__0);

    private static void Apply(StoreClient? client)
    {
        if (!_active || client == null ||
            !client.useClientBudget || ScaledClients.Contains(client)) return;
        try
        {
            int budget = client.GetBudget();
            if (budget > 0)
                client.OverrideBudget((int)Math.Min(int.MaxValue,
                    (long)budget * 2));
            // Arrival and repeated save confirmations may see the same client
            // after a sale. They must never multiply or refill that balance.
            ScaledClients.Add(client);
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogWarning("[WARN] [NicokoboForge/Workshop] victory budget unavailable: " + ex.Message);
        }
    }
}
