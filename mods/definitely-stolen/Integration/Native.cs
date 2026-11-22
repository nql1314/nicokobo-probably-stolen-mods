using Nicokobo.ModUtilities;
using System.Reflection;
using HarmonyLib;
using DefinitelyStolen.Domain;

namespace DefinitelyStolen.Integration;

internal static class Native
{
    internal static PlayerStore? Store => PlayerStore.HasInstance ? PlayerStore.Instance : null;
    internal static int Day => StoreStation.GetDayCounter();
    internal static bool Night => Store != null && Store.storeState == PlayerStore.StoreState.CLOSED;
    internal static bool Alive(GameItem? item) => item != null && (item is not GameItemElement element || !element.IsDestroyed());
    internal static GameGridInventory MainGrid => EmporiumEntry.Instance?.invElement ?? throw new InvalidOperationException("Store inventory unavailable");
    internal static GameGridInventory CarryGrid => EmporiumEntry.Instance?.afterhourInventory ?? throw new InvalidOperationException("Outside inventory unavailable");
    internal static string Text(string chinese, string english) =>
        ModText.IsChinese ? chinese : english;
    internal static string Localize(string key, params object[] args) =>
        ModText.Get(Identity.Owner, "theft." + key, args);
    internal static bool IsCareer(PlayerStore store) => store.startScenarioId == Identity.StartId || (int)store.startType == Identity.NativeStartType;
    private static readonly MethodInfo GoingOutside = AccessTools.Method(typeof(MapUIManager), "OnGoingOutside", new[] { typeof(AfterhourLocation) });
    private static readonly MethodInfo SetCurrent = AccessTools.PropertySetter(typeof(MapUIManager), nameof(MapUIManager.Current));
    private static readonly MethodInfo ShowStage = AccessTools.Method(typeof(MapUIManager), "ShowStage", new[] { typeof(AfterhourLocation) });
    private static readonly MethodInfo OpenPanel = AccessTools.Method(typeof(MapUIManager), "OpenLocationPanel", new[] { typeof(AfterhourLocation) });
    // The public travel method animates a new visit. Saved outings need an
    // immediate resume without charging or incrementing visit counters again.
    internal static void RestoreOutside(int region)
    {
        var map = MapUIManager.Instance ?? throw new InvalidOperationException("Outside manager unavailable");
        var location = ModHelper.GetAfterhourLocation(Locations.Id(region)) ?? throw new InvalidOperationException("Saved district unavailable");
        object[] arguments = { location };
        try
        {
            GoingOutside.Invoke(map, arguments); SetCurrent.Invoke(map, arguments);
            ShowStage.Invoke(map, arguments); location.Arrive(); OpenPanel.Invoke(map, arguments);
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
    }
}
