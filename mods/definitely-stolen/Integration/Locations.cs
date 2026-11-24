using DefinitelyStolen.Domain;
using DefinitelyStolen.Runtime;
using DefinitelyStolen.UI;
using UnityEngine;

namespace DefinitelyStolen.Integration;

internal static class Locations
{
    private static readonly string[] Names = { "lower_night_market", "transit_hub", "upper_commercial" };
    internal static string Id(int region) => Identity.Owner + "." + Names[region];
    internal static bool IsOwn(string? id) => id != null && Enumerable.Range(0, Names.Length).Any(x => Id(x) == id);
    internal static string Title(int region) => Native.Localize(Names[region]);
    internal static int? NextOpenRegion(int currentRegion)
    {
        for (int offset = 1; offset < Names.Length; offset++)
        {
            int region = (currentRegion + offset) % Names.Length;
            if (ModHelper.GetAfterhourLocation(Id(region)) is TheftLocation location && !location.IsClosedByEvent()) return region;
        }
        return null;
    }

    internal static void Register(RunController run, TheftPanel panel)
    {
        for (int i = 0; i < Names.Length; i++)
            ModHelper.AddAfterhourLocation(new TheftLocation(run, panel, i));
    }

    // The native location owns stage/panel activation and the afterhour bag.
    // Supplying PanelObject avoids the generic draggable dialog and its footer.
    private sealed class TheftLocation(RunController run, TheftPanel panel, int region) : AfterhourLocation
    {
        public override string Id => Locations.Id(region);
        public override string Source => Identity.Owner;
        // These locations do not ship a separate ambience clip.
        public override string AmbienceId => "";
        public override string ImageKey => Identity.Owner + ":" + SceneLayout.SceneName(region, 0);
        public override string BackgroundKey => ImageKey;
        public override GameObject StageObject => panel.StageObject;
        public override GameObject PanelObject => panel.CreateOverlay(region);
        public override string GetTitle() => NightEventBridge.Title(run, region);
        public override bool IsDiscovered() => run.Qualified;
        public override bool IsAvailable() => run.Qualified && Native.Night && run.EnsureNight() &&
            run.State!.Night is { Ended: false } night && (!night.Started || night.LockedRegion == region) &&
            (night.Started || Native.Store!.visitLeftTonight > 0) && !IsClosedByEvent();
        public override void Arrive() { run.Open(); panel.ShowRegion(region); }
        public override void Leave() => panel.Close();
    }
}
