namespace AugMechanicalAscension.Domain;

internal static class StoryInvestigation
{
    internal static readonly string[] ClosedReasons = ["none", "no_prosthetic", "prosthetic_salvaged", "diagnostics_lost"];
    internal static bool Open(StoryState s) => s.ContentVersion == 4 && s.EndingId == null && s.Pending == null && s.HiddenClosedReason == "none";
    internal static bool Complete(StoryState s) => s.ContentVersion == 4 && s.ProstheticInspected && s.Investigation == 3 && s.DiagnosticRetained && s.OfferReserved && s.HiddenClosedReason == "none";
    internal static StoryState Close(StoryState s, string reason) => s.HiddenClosedReason == "none" ? s with { HiddenClosedReason = reason } : s;
    internal static bool Allowed(StoryState s, int day, string action)
    {
        // Looking at a retained object remains possible after the optional route is lost.
        // This records its visible details without reopening the optional route.
        if (s.ContentVersion != 4 || s.Pending != null || s.EndingId != null) return false;
        if (action == "inspect_prosthetic")
            return !s.ProstheticInspected && (s.Choice("E01") is "refund" or "investigate");
        if (action == "inspect_photo") return !s.PhotoInspected && s.Choice("E03") != "";
        return Open(s) && action switch
        {
            "x1" => s.ProstheticInspected && s.Investigation == 0 && s.Choice("E05") != "",
            "x2_start" => s.Investigation == 1 && s.DiagnosticRetained && s.ExperimentDay < 0 && s.Choice("E07") != "",
            "x2_read" => s.Investigation == 1 && s.DiagnosticRetained && s.ExperimentDay >= 0 && day > s.ExperimentDay,
            "x3" => s.ProstheticInspected && s.DiagnosticRetained && s.Investigation == 2 && s.Choice("E09") != "",
            _ => false
        };
    }
    internal static StoryState Commit(StoryState s, int day, string action)
    {
        if (!Allowed(s, day, action)) throw new InvalidOperationException("Investigation unavailable");
        var next = action switch
        {
            "inspect_prosthetic" => s with { ProstheticInspected = true },
            "inspect_photo" => s with { PhotoInspected = true },
            "x1" => s with { Investigation = 1, InvestigationDay = day },
            "x2_start" => s with { ExperimentDay = day, InvestigationDay = day },
            "x2_read" => s with { Investigation = 2 },
            "x3" => s with { Investigation = 3, InvestigationDay = day },
            _ => throw new ArgumentException("Unknown investigation")
        };
        return next with { Revision = s.Revision + 1 };
    }
    internal static StoryState Repair(StoryState s, bool personally)
    {
        var next = s with { RecordingRepaired = true, DiagnosticRetained = personally, Revision = s.Revision + 1 };
        return personally ? next : Close(next, "diagnostics_lost");
    }
    internal static StoryState ReserveOffer(StoryState s)
    {
        if (s.OfferReserved) return s;
        return s with { OfferReserved = true, Revision = s.Revision + 1 };
    }
    internal static int OwnedCybernetics(IEnumerable<WorkshopOwnedItem> items) => items
        .Where(x => x.Owned && x.Units > 0 && x.UniqueId > 0)
        .GroupBy(x => x.UniqueId).Where(x => x.Select(i => i.ItemId).Distinct(StringComparer.Ordinal).Count() == 1)
        .Select(x => x.First().ItemId).Where(WorkshopAchievements.Relevant).Distinct(StringComparer.Ordinal).Count();
}
