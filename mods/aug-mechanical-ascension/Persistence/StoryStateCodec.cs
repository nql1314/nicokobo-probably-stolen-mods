using AugMechanicalAscension.Serialization;
using AugMechanicalAscension.Domain;

namespace AugMechanicalAscension.Persistence;

internal static class StoryStateCodec
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    internal static string Write(StoryState state) => JsonSerializer.Serialize(state, Options);
    internal static StoryState Read(string json, string runId, int slotId)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
        RejectDuplicateKeys(document.RootElement);
        foreach (string field in new[] { "schemaVersion", "contentVersion", "gameRunId", "saveSlotId", "startId", "revision", "choices" })
            if (!document.RootElement.TryGetProperty(field, out _)) throw new InvalidDataException("Missing story field: " + field);
        var s = JsonSerializer.Deserialize<StoryState>(json, Options) ?? throw new InvalidDataException("Empty story");
        if (s.SchemaVersion != 1 || s.ContentVersion != 4 || s.GameRunId != runId ||
            s.SaveSlotId != slotId || s.StartId != RunState.AugMechanicalAscensionId ||
            string.IsNullOrWhiteSpace(runId) || slotId < 0 || s.Revision < 0 ||
            s.Choices == null || s.Choices.Length > 10 || s.LastStartedDay < -1)
            throw new InvalidDataException("Story version or identity mismatch");
        if (s.Items == null || s.Items.Any(x => x == null || x.Uid <= 0 || x.CounterNoticeDay < -1 || !StoryEvidence.Keys.Contains(x.Key)) ||
            s.Items.Select(x => x.Key).Distinct().Count() != s.Items.Length ||
            s.Items.Select(x => x.Uid).Distinct().Count() != s.Items.Length ||
            s.Investigation is < 0 or > 3 || s.ExperimentDay < -1 || s.InvestigationDay < -1 ||
            s.Investigation > 0 && s.Choice("E04") == "" ||
            s.Investigation >= 2 && (s.Choice("E07") == "" || s.ExperimentDay < 0) ||
            s.Investigation == 3 && s.Choice("E09") == "" ||
            s.CopyDelivered && !s.RecordingRepaired || s.EndingId == "beyond_the_world" && !StoryInvestigation.Complete(s))
            throw new InvalidDataException("Invalid physical evidence or investigation state");
        if (s.HiddenClosedReason == null || !StoryInvestigation.ClosedReasons.Contains(s.HiddenClosedReason) ||
                s.ProstheticInspected && s.Choice("E01") is not ("refund" or "investigate") ||
                s.PhotoInspected && s.Choice("E03") == "" ||
                s.Investigation > 0 && (!s.ProstheticInspected || s.Choice("E05") == "") ||
                s.DiagnosticRetained && (!s.RecordingRepaired || s.Choice("E06") == "") ||
                s.ExperimentDay >= 0 && (!s.DiagnosticRetained || s.Choice("E07") == "") ||
                s.OfferReserved && s.Choice("E08") == "")
            throw new InvalidDataException("Invalid hidden investigation facts");
        if (s.ConfessionHeard && (!s.RecordingRepaired || s.Choice("E07") == "" && s.ActiveEventId != "E07") ||
            s.Choice("E07") != "" && !s.ConfessionHeard ||
            s.CopyDelivered != (s.Choice("E08") == "share") ||
            s.ProstheticMarksTransferred && (!s.ProstheticInspected || s.Choice("E04") == "" ||
                !s.Items.Any(x => x.Key == "broken" && x.Removed)) ||
            s.RecordingRepaired && !s.Items.Any(x => x.Key == "credential"))
            throw new InvalidDataException("Invalid story knowledge or recovered credential");
        int lastDay = -1;
        if (s.EncounterDay < -1 || s.EncounterDay >= 0 &&
            (s.ActiveEventId == null || s.EncounterDay < s.LastStartedDay || s.EndingId != null))
            throw new InvalidDataException("Invalid story encounter day");
        if (s.DecisionStartedDay < -1 || s.DecisionStartedDay >= 0 &&
            (!StoryRules.TimedDecision(s.ActiveEventId) || s.DecisionStartedDay < s.LastStartedDay || s.EndingId != null))
            throw new InvalidDataException("Invalid story decision deadline");
        for (int i = 0; i < s.Choices.Length; i++)
        {
            var choice = s.Choices[i]; var ev = StoryCatalog.Events[i];
            // Live debugging can finish consecutive events on the same real day.
            // Event order stays exact; dates must be nonnegative and cannot go back.
            if (choice == null || choice.EventId != ev.Id || choice.Day < 0 || choice.Day < lastDay ||
                StoryCatalog.RecordedOption(ev, choice.OptionId) == null) throw new InvalidDataException("Invalid story sequence");
            lastDay = choice.Day;
        }
        if (s.LastStartedDay < lastDay || s.ActiveEventId != null &&
            (s.Choices.Length == 10 || s.ActiveEventId != StoryCatalog.Events[s.Choices.Length].Id ||
             s.LastStartedDay < 0 || s.LastStartedDay < lastDay)) throw new InvalidDataException("Invalid active event");
        if (s.CoreDisposition is not ("none" or "held" or "sold" or "delivered" or "archived") ||
            (s.Choice("E06") == "buy" ? s.CoreUid <= 0 || s.CoreDisposition == "none" : s.CoreUid != 0 || s.CoreDisposition != "none"))
            throw new InvalidDataException("Invalid core receipt");
        if (s.Choices.Length == 10 ? s.EndingId != s.Choice("E10") || s.EndingDay != lastDay :
            s.EndingId != null || s.EndingDay != -1 || s.EpilogueSeen)
            throw new InvalidDataException("Invalid ending");
        if (s.CoreConsented && (s.Choice("E08") != "share" || s.Choice("E06") != "buy") ||
            s.CoreCustodyConsented && (s.Choice("E06") != "buy" || s.EndingId == null || s.EndingId == "lost_and_found" ||
                s.CoreDisposition is not ("held" or "archived")) ||
            s.EndingId == "lost_and_found" && (s.CoreDisposition != "delivered" || !s.CoreConsented || s.Choice("E08") != "share") ||
            s.CoreDisposition == "delivered" && s.EndingId != "lost_and_found" ||
            s.CoreDisposition == "archived" && (s.EndingId == null || s.EndingId == "lost_and_found" || !s.CoreCustodyConsented))
            throw new InvalidDataException("Invalid ending core disposition");
        if (s.Pending is { } p && (string.IsNullOrWhiteSpace(p.RequestId) || string.IsNullOrWhiteSpace(p.ActionId) || p.Day < 0 || p.CashBefore < 0 ||
            p.CoreUid < 0 || p.OutputUids == null || p.RemovedUids == null ||
            p.OutputUids.Any(x => x <= 0) || p.RemovedUids.Any(x => x <= 0) ||
            p.OutputUids.Distinct().Count() != p.OutputUids.Length || p.RemovedUids.Distinct().Count() != p.RemovedUids.Length ||
            p.OutputUids.Intersect(p.RemovedUids).Any() || p.MaterialUid < 0 || p.MaterialUnitsBefore < 0 ||
            (p.MaterialUid == 0) != (p.MaterialUnitsBefore == 0) || (long)p.CashBefore + p.CashDelta is < 0 or > int.MaxValue ||
            !(p.ActionId.StartsWith("physical:", StringComparison.Ordinal) || p.ActionId is StoryRules.SellCore or StoryRules.ArchiveCore || s.ActiveEventId != null &&
              StoryCatalog.Events.First(x => x.Id == s.ActiveEventId).Options.Any(x => x.Id == p.ActionId))))
            throw new InvalidDataException("Invalid pending transaction");
        return s;
    }

    private static void RejectDuplicateKeys(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!seen.Add(property.Name)) throw new InvalidDataException("Duplicate story field");
                RejectDuplicateKeys(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray()) RejectDuplicateKeys(child);
    }
}
