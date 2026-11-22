namespace AugMechanicalAscension.Domain;

internal static class StoryItemActionRules
{
    internal static string[] Actions(StoryState state, string key, int day)
    {
        if (state.ContentVersion != 4) return [];
        var actions = new List<string>();
        if (key == StoryJournal.Key)
        {
            if (state.EndingId == null)
            {
                actions.Add("request_visit");
                if (StoryRules.DecisionDue(state, day))
                    actions.AddRange(StoryCatalog.Events.First(x => x.Id == state.ActiveEventId).Options.Select(x => "deadline:" + x.Id));
            }
            else if (state.EndingId == "beyond_the_world") actions.Add("letter");
        }
        else if (key == "recording" && state.ContentVersion == 4 && state.EndingId == null)
        {
            if (state.ActiveEventId == "E07" && !state.RecordingRepaired) actions.AddRange(["repair_parts", "repair_cash"]);
            if (state.Choice("E07") != "" && state.Choice("E08") == "" && state.RecordingRepaired && !state.Items.Any(x => x.Key == "copy"))
                actions.AddRange(["copy_parts", "copy_cash"]);
        }
        else if (key == "copy" && state.Choice("E07") != "" && state.Choice("E08") == "") actions.Add("deliver_copy");
        else if (key == "broken" && state.ContentVersion == 4 && state.EndingId == null)
        {
            if (state.Choice("E04") != "" && !state.Items.Any(x => x.Key == "broken" && x.Removed)) actions.Add("salvage");
        }
        else if (key == "repair" && StoryInvestigation.Allowed(state, day, "x1")) actions.Add("x1");
        else if (key == "communicator" && StoryInvestigation.Open(state) && state.DiagnosticRetained &&
            state.Investigation == 1 && state.Choice("E07") != "")
        {
            if (state.ExperimentDay < 0) actions.Add("x2_start");
            else if (day > state.ExperimentDay) actions.Add("x2_read");
        }
        else if (key == "credential" && StoryInvestigation.Allowed(state, day, "x3")) actions.Add("x3");
        else if (key == "core" && state.CoreDisposition == "held")
        {
            actions.Add("read_core");
            if (state.EndingId == null)
            {
                if (state.Choice("E08") == "share" && state.CopyDelivered && !state.CoreConsented) actions.Add(StoryRules.RequestCoreAid);
                actions.Add("core_visit");
            }
            else if (state.EndingId != "lost_and_found") actions.Add(state.CoreCustodyConsented ? StoryRules.ArchiveCore : StoryRules.RequestCoreCustody);
        }
        return actions.ToArray();
    }
}
