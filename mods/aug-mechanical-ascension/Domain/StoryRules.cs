namespace AugMechanicalAscension.Domain;

// All transitions are immutable; native money/inventory must be verified before Commit.
internal static class StoryRules
{
    internal const string SellCore = "sell_core", ArchiveCore = "archive_core",
        RequestCoreAid = "request_core_aid", RequestCoreCustody = "request_core_custody";
    internal const int DecisionGraceDays = 7;
    internal static int? VisitWeekday(string id) => id switch
    { "E02" => 2, "E03" or "E06" or "E08" or "E10" => 1, _ => null };

    // Choose once before the first event; saves/reloads must not turn a new run
    // into a catch-up run or replay choices already made.
    internal static StoryState InitializeOpening(StoryState s, bool existingRun) =>
        s.CatchUpOpening != null || s.Pending != null ? s : s with
        {
            CatchUpOpening = existingRun && s.Choices.Length == 0 && s.ActiveEventId == null,
            Revision = s.Revision + 1
        };

    // Presentation dates are separate from transaction eligibility: an encounter
    // that actually started remains answerable throughout its decision window.
    internal static int VisitDay(StoryState s, int day)
    {
        if (day < 0) throw new ArgumentOutOfRangeException(nameof(day));
        var next = StoryCatalog.Events.ElementAtOrDefault(s.Choices.Length);
        if (next == null || s.CatchUpOpening == true && (next.Id is "E02" or "E03") ||
            VisitWeekday(next.Id) is not { } weekday ||
            s.ActiveEventId == next.Id && (s.EncounterDay >= 0 || s.DecisionStartedDay >= 0)) return day;
        int previous = s.Choices.LastOrDefault()?.Day ?? day;
        long first = (long)previous + 7 - ((long)previous + 6) % 7 + weekday - 1;
        long due = first >= day ? first : first + ((day - first + 6) / 7) * 7;
        return checked((int)due);
    }

    internal static string? ArrivalDenial(StoryState s, StoryContext c) =>
        VisitDenial(s, c) ?? (c.IgnoreVisitDays || VisitDay(s, c.Day) == c.Day ? null : "visit_date");

    internal static StoryState Arrive(StoryState s, int day) =>
        s.ActiveEventId == null || s.Pending != null || s.EncounterDay >= 0 || day < s.LastStartedDay ? s :
        StartDecision(s with { EncounterDay = day, Revision = s.Revision + 1 }, day);
    internal static bool TimedDecision(string? id) => id is "E01" or "E06" or "E08";
    internal static long? DecisionDeadline(StoryState s) => TimedDecision(s.ActiveEventId) && s.DecisionStartedDay >= 0
        ? (long)s.DecisionStartedDay + DecisionGraceDays : null;
    internal static bool DecisionDue(StoryState s, int day) => s.Pending == null && s.EndingId == null &&
        DecisionDeadline(s) is { } deadline && day >= deadline;
    internal static StoryState StartDecision(StoryState s, int day) =>
        !TimedDecision(s.ActiveEventId) || s.DecisionStartedDay >= 0 || s.Pending != null || day < s.LastStartedDay
            ? s : s with { DecisionStartedDay = day, Revision = s.Revision + 1 };

    internal static StoryEvent? Available(StoryState state, StoryContext context)
    {
        return VisitDenial(state, context) == null ? StoryCatalog.Events[state.Choices.Length] : null;
    }

    internal static string? VisitDenial(StoryState state, StoryContext context)
    {
        if (state.ContentVersion != 4) return "version";
        if (state.Pending != null) return "pending";
        if (state.EndingId != null || state.Choices.Length >= StoryCatalog.Events.Length) return "ending";
        if (!context.IgnoreVisitDays && context.Day <= context.StartDay) return "first_night";
        var next = StoryCatalog.Events[state.Choices.Length];
        if (next.Id == "E10" && !context.MechanicalAscensionCompleted) return "achievement";
        if (context.OwnedCybernetics < next.RequiredCybernetics) return "ownership";
        return context.IgnoreVisitDays || state.ActiveEventId == next.Id || next.Id is not ("E02" or "E03") ||
            context.Day > state.LastStartedDay ? null : "day_quota";
    }

    internal static StoryState Begin(StoryState state, StoryContext context)
    {
        var next = Available(state, context) ?? throw new InvalidOperationException("Event unavailable");
        return state.ActiveEventId == next.Id ? state : state with
        { ActiveEventId = next.Id, EncounterDay = -1, LastStartedDay = context.Day, Revision = state.Revision + 1 };
    }

    internal static bool CanAid(StoryState s, StoryContext c) =>
        s.Choice("E06") == "buy" && s.CoreDisposition == "held" && c.OwnsCore &&
        s.Choice("E08") == "share" && s.CoreConsented && s.Choice("E07") != "";

    internal static string? Denial(StoryState s, StoryContext c, string action)
    {
        if (s.ContentVersion != 4) return "version";
        if (s.Pending != null) return "pending";
        if (action is RequestCoreAid or RequestCoreCustody)
        {
            if (s.CoreDisposition != "held" || !c.OwnsCore) return "core";
            return action == RequestCoreAid
                ? s.EndingId != null || s.Choice("E08") != "share" || !s.CopyDelivered || s.CoreConsented ? "aid" : null
                : s.EndingId == null || s.EndingId == "lost_and_found" || s.CoreCustodyConsented ? "ending" : null;
        }
        if (action == SellCore || action == ArchiveCore)
        {
            if (s.CoreDisposition != "held" || !c.OwnsCore) return "core";
            if (action == SellCore && s.EndingId != null ||
                action == ArchiveCore && (s.EndingId == null || s.EndingId == "lost_and_found")) return "ending";
            if (action == SellCore && c.Cash > int.MaxValue - 300) return "cash";
            if (action == ArchiveCore && !s.CoreCustodyConsented) return "consent";
            return null;
        }
        var ev = Available(s, c);
        if (ev == null || s.ActiveEventId != ev.Id) return "event";
        var option = ev.Options.FirstOrDefault(x => x.Id == action);
        if (option == null) return "option";
        if ((long)c.Cash + option.CashDelta < 0 || (long)c.Cash + option.CashDelta > int.MaxValue) return "cash";
        if (ev.Id == "E07" && !s.ConfessionHeard) return "confession";
        if (ev.Id == "E10" && action == "beyond_the_world" && !StoryInvestigation.Complete(s)) return "investigation";
        if (ev.Id == "E10" && action == "lost_and_found" && !CanAid(s, c)) return "aid";
        return null;
    }

    internal static int CashDelta(StoryState s, string action) => action == SellCore ? 300 :
        action == ArchiveCore ? 0 : StoryCatalog.Events.First(x => x.Id == s.ActiveEventId)
            .Options.First(x => x.Id == action).CashDelta;
    internal static bool RemovesCore(StoryState s, string action) => action is SellCore or ArchiveCore ||
        s.ActiveEventId == "E10" && action == "lost_and_found";

    internal static StoryState Consent(StoryState s, StoryContext c, string action)
    {
        if (Denial(s, c, action) is { } denial) throw new InvalidOperationException(denial);
        return action switch
        {
            RequestCoreAid => s with { CoreConsented = true, Revision = s.Revision + 1 },
            RequestCoreCustody => s with { CoreCustodyConsented = true, Revision = s.Revision + 1 },
            _ => throw new ArgumentException("Unknown core request")
        };
    }

    internal static StoryState Commit(StoryState s, StoryContext c, string action, int deliveredUid = 0)
    {
        var source = s with { Pending = null };
        if (Denial(source, c, action) is { } denial) throw new InvalidOperationException(denial);
        var next = source with { Revision = s.Revision + 1 };
        if (action == SellCore) return next with { CoreDisposition = "sold" };
        if (action == ArchiveCore) return next with { CoreDisposition = "archived" };
        var id = s.ActiveEventId!;
        next = next with
        {
            ActiveEventId = null,
            DecisionStartedDay = -1,
            EncounterDay = -1,
            LastStartedDay = c.Day,
            Choices = source.Choices.Concat(new[] { new StoryChoice(id, action, c.Day) }).ToArray()
        };
        if (id == "E06" && action == "buy")
        {
            if (deliveredUid <= 0) throw new InvalidOperationException("Core delivery is missing");
            next = next with { CoreUid = deliveredUid, CoreDisposition = "held" };
        }
        if (id == "E01" && action == "decline") next = StoryInvestigation.Close(next, "no_prosthetic");
        if (id == "E09") next = StoryInvestigation.ReserveOffer(next);
        if (id == "E10") next = next with
        { EndingId = action, EndingDay = c.Day, CoreDisposition = action == "lost_and_found" ? "delivered" : next.CoreDisposition };
        return next;
    }

    internal static StoryState ReadEpilogue(StoryState s, int day) =>
        s.Pending == null && s.EndingId != null && day > s.EndingDay && !s.EpilogueSeen
            ? s with { EpilogueSeen = true, Revision = s.Revision + 1 } : s;
}
