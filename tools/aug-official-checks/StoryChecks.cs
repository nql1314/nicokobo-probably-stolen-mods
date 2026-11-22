using AugMechanicalAscension.Domain;
using AugMechanicalAscension.Persistence;

internal static class StoryChecks
{
    // Domain fixtures represent observations already verified by the runtime checks.
    // They do not infer the core's separate answer from Lin's receipt of a copy.
    private static StoryState CommitObservedChoice(StoryState s, StoryContext context, string action, int uid = 0)
    {
        if (s.ActiveEventId == "E07")
        {
            if (!s.Items.Any(x => x.Key == "credential")) s = s with { Items = [.. s.Items, new("credential", 9001)] };
            if (!s.RecordingRepaired) s = StoryInvestigation.Repair(s, true);
            s = s with { ConfessionHeard = true };
        }
        if (s.ActiveEventId == "E08" && action == "share") s = s with { CopyDelivered = true };
        return StoryRules.Commit(s, context, action, uid);
    }

    private static void RunItemActions(Action<bool, string> check)
    {
        var fresh = new StoryState { ContentVersion = 4, GameRunId = "item-actions", SaveSlotId = 1 };
        check(StoryEvidence.Size(StoryJournal.Key) == (2, 3), "the opening journal occupies two by three cells");
        foreach (bool english in new[] { false, true })
        {
            string body = StoryJournal.Body(fresh, english);
            check(!body.Contains("逃逸") && !body.Contains("Escape") && !body.Contains("邵衡") && !body.Contains("Shao") &&
                !body.Contains("诊断触点") && !body.Contains("diagnostic contact"), "a fresh journal reveals no future actor or hidden route");
            var firstPage = StoryJournal.Pages(fresh, english);
            check(firstPage.Length == 1 && !firstPage[0].Body.Contains("邵衡") && !firstPage[0].Body.Contains("Shao"),
                "the journal cannot page ahead into undiscovered acts");
            var recorded = fresh with { Choices = [new("E01", "decline", 1), new("E04", "compare", 4)], ActiveEventId = "E07" };
            var pages = StoryJournal.Pages(recorded, english);
            string firstTitle = english ? StoryCatalog.Events[0].EnglishName : StoryCatalog.Events[0].Name;
            string secondTitle = english ? StoryCatalog.Events[3].EnglishName : StoryCatalog.Events[3].Name;
            check(pages.Length == 3 && pages[0].Body.Contains(firstTitle) && !pages[0].Body.Contains(secondTitle) &&
                pages[1].Body.Contains(secondTitle) && !pages[1].Body.Contains(firstTitle),
                "each act contains its own recorded events, without revealing other acts");
            check(!pages[2].Body.Contains(english ? "released your remote control" : "解除了你的远程控制"),
                "opening act three does not disclose the unrepaired recording");
            check(StoryJournal.Pages(recorded with { ActiveEventId = "E10" }, english).Length == 4,
                "the final act has its own fourth page");
        }
        foreach (string key in new[] { "broken", "photo", "recording", "credential", "communicator", "copy" })
            check(StoryItemActionRules.Actions(fresh, key, 1).Length == 0, "undiscovered items do not advertise later actions: " + key);
        var state = fresh;
        string[] baseline = ["investigate", "accept", "honest", "compare", "keep", "buy", "disclose"];
        for (int index = 0; index < baseline.Length; index++)
        {
            var context = new StoryContext(index + 1, 0, 12, 1000, true, true, true);
            state = CommitObservedChoice(StoryRules.Begin(state, context), context, baseline[index], index == 5 ? 72 : 0);
        }
        state = state with { RecordingRepaired = true, Items = [new(StoryJournal.Key, 10), new("recording", 11), new("credential", 9001)] };
        state = StoryStateCodec.Read(StoryStateCodec.Write(state), fresh.GameRunId, fresh.SaveSlotId);
        check(StoryItemActionRules.Actions(state, "recording", 8).SequenceEqual(new[] { "copy_parts", "copy_cash" }) &&
            !StoryItemActionRules.Actions(state, StoryJournal.Key, 8).Any(x => x.StartsWith("copy") || x.StartsWith("x")),
            "reloaded E07 progress puts copying on the real carrier rather than the journal");
        check(StoryJournal.Body(state, false).Contains("右键记录器") && StoryJournal.Body(state, true).Contains("right-click the recorder"),
            "E08 preparation explains the actual source item in both languages");
        var copied = state with { Items = [.. state.Items, new("copy", 12)] };
        check(StoryItemActionRules.Actions(copied, "recording", 8).Length == 0 &&
            StoryItemActionRules.Actions(copied, "copy", 8).SequenceEqual(new[] { "deliver_copy" }), "one saved copy removes copying and gains its own delivery action");
        var unrepaired = state with { Choices = state.Choices[..6], ActiveEventId = "E07", RecordingRepaired = false };
        check(StoryItemActionRules.Actions(unrepaired, "recording", 8).SequenceEqual(new[] { "repair_parts", "repair_cash" }),
            "the damaged carrier offers repairs before it can offer copying");
        var compared = state with { ProstheticInspected = true, DiagnosticRetained = true, Investigation = 1, HiddenClosedReason = "none" };
        check(StoryItemActionRules.Actions(compared, "communicator", 8).Contains("x2_start") &&
            !StoryItemActionRules.Actions(state, "communicator", 8).Contains("x2_start"), "observation appears only after the corresponding discovery and retained diagnosis");
        compared = compared with { ExperimentDay = 8 };
        check(StoryItemActionRules.Actions(compared, "communicator", 8).Length == 0 &&
            StoryItemActionRules.Actions(compared, "communicator", 9).SequenceEqual(new[] { "x2_read" }), "observations cannot be read before a normal night has passed");
        check(StoryItemActionRules.Actions(compared with { OfferReserved = true }, "communicator", 9).Contains("x2_read"),
            "an offer reservation does not remove a completed observation from the communicator");
        var due = copied with { ActiveEventId = "E08", EncounterDay = 8, DecisionStartedDay = 8, LastStartedDay = 8 };
        check(StoryItemActionRules.Actions(due, StoryJournal.Key, 15).Contains("deadline:hide") &&
            !StoryItemActionRules.Actions(due, StoryJournal.Key, 14).Any(x => x.StartsWith("deadline:")), "due replies remain reachable from the journal on the exact deadline");
    }

    private static void RunLiveDebug(Action<bool, string> check)
    {
        string[] choices = ["investigate", "accept", "honest", "compare", "keep", "buy", "disclose", "share", "read", "legal_property"];
        foreach (int day in new[] { 0, 1 })
        {
            var state = new StoryState { GameRunId = "live-debug", SaveSlotId = 1, ContentVersion = 4 };
            var context = new StoryContext(day, 0, 12, 1000, true, true, IgnoreVisitDays: true);
            for (int index = 0; index < choices.Length; index++)
            {
                check(StoryRules.ArrivalDenial(state, context) == null,
                    "live debug can call the next visitor without advancing the game day");
                if (index == 0)
                    check(StoryRules.ArrivalDenial(state, context with { IgnoreVisitDays = false }) == (day == 0 ? "first_night" : null),
                        "ordinary first-night rules are retained");
                else
                    check(StoryRules.ArrivalDenial(state, context with { IgnoreVisitDays = false }) ==
                        (day == 0 ? "first_night" : index < 3 ? "day_quota" : index is 5 or 7 or 9 ? "visit_date" : null),
                        "ordinary opening nights and scheduled dates remain, while ownership events allow the current day");
                var active = StoryRules.Begin(state, context);
                active = StoryStateCodec.Read(StoryStateCodec.Write(active), state.GameRunId, state.SaveSlotId);
                check(active.ActiveEventId == StoryCatalog.Events[index].Id && active.LastStartedDay == day,
                    "same-day active debug events survive saved-state validation with the real date");
                if (index == 0)
                    check(StoryRules.Denial(active, context with { Cash = 0 }, "investigate") == "cash",
                        "live debug still refuses payment without sufficient cash");
                if (StoryCatalog.Events[index].RequiredCybernetics > 0)
                    check(StoryRules.ArrivalDenial(state, context with { OwnedCybernetics = 0 }) == "ownership",
                        "live debug retains cybernetic ownership requirements");
                if (index == 9)
                    check(StoryRules.ArrivalDenial(state, context with { MechanicalAscensionCompleted = false }) == "achievement",
                        "live debug retains the Mechanical Ascension achievement requirement");
                state = CommitObservedChoice(active, context, choices[index], choices[index] == "buy" ? 73 : 0);
                state = StoryStateCodec.Read(StoryStateCodec.Write(state), state.GameRunId, state.SaveSlotId);
                check(state.Choices.Length == index + 1 && state.Choices.All(x => x.Day == day),
                    "multiple live events retain their real same-day choices across saved-state validation");
            }
            check(StoryRules.ArrivalDenial(state, context) == "ending", "live debug cannot replay a completed ending");
            var pending = state with { Pending = new("blocked", "physical:investigate", day, 1000, -60, 0) };
            check(StoryRules.ArrivalDenial(pending, context) == "pending", "live debug cannot bypass an unconfirmed transaction");
        }
        var reversed = new StoryState { GameRunId = "reverse-debug", SaveSlotId = 1, ContentVersion = 4,
            LastStartedDay = 2, Choices = [new("E01", "investigate", 2), new("E02", "accept", 1)] };
        bool Reject(StoryState state)
        { try { StoryStateCodec.Read(StoryStateCodec.Write(state), state.GameRunId, state.SaveSlotId); return false; } catch (Exception ex) when(ex is InvalidDataException or AugMechanicalAscension.Serialization.JsonException) { return true; } }
        check(Reject(reversed), "same-day support still rejects backwards story dates");
        check(Reject(reversed with { Choices = [new("E01", "investigate", -1)] }), "same-day support still rejects negative story dates");
    }

    internal static void RunSchedule(Action<bool, string> check)
    {
        RunOpeningCatchUp(check);
        string[] baseline = ["investigate", "accept", "honest", "compare", "keep", "buy", "disclose", "share", "read"];
        foreach (var (id, weekday) in new[] { ("E02", 2), ("E03", 1), ("E06", 1), ("E08", 1), ("E10", 1) })
        for (int anchor = 14; anchor < 21; anchor++)
        {
            int index = Array.FindIndex(StoryCatalog.Events, x => x.Id == id);
            var s = new StoryState { GameRunId = "schedule", SaveSlotId = 1, ContentVersion = 4 };
            for (int i = 0; i < index; i++)
            {
                var c = new StoryContext(anchor - index + i + 1, 0, 12, 1000, true, true);
                s = CommitObservedChoice(StoryRules.Begin(s, c), c, baseline[i], baseline[i] == "buy" ? 73 : 0);
            }
            int monday = anchor - (anchor + 6) % 7;
            int due = monday + 7 + weekday - 1;
            check(StoryRules.VisitDay(s, anchor) == due && due % 7 == weekday, id + " starts in the following natural week");
            if (id is "E06" or "E08" or "E10")
                check(StoryRules.VisitDay(s with { CatchUpOpening = true }, anchor) == due,
                    id + " keeps its natural-week schedule after an old-save opening");
            check(StoryRules.ArrivalDenial(s, new(due - 1, 0, 12, 1000, true, true)) != null &&
                StoryRules.ArrivalDenial(s, new(due, 0, 12, 1000, true, true)) == null, id + " only arrives on its scheduled day");
            check(StoryRules.VisitDay(s, due + 1) == due + 7 && StoryRules.VisitDay(s, due + 15) == due + 21,
                id + " rolls skipped days forward without losing the event");
            var queued = StoryRules.Begin(s, new(due, 0, 12, 1000, true, true));
            check(queued.DecisionStartedDay == -1 && queued.EncounterDay == -1 &&
                StoryRules.VisitDay(queued, due + 1) == due + 7, id + " queueing alone does not count as an encounter");
            queued = StoryStateCodec.Read(StoryStateCodec.Write(queued), "schedule", 1);
            check(StoryRules.VisitDay(queued, due + 1) == due + 7, id + " reload retains the missed visit schedule");
            var arrived = StoryRules.Arrive(queued, due + 7);
            check(arrived.EncounterDay == due + 7 && StoryRules.VisitDay(arrived, due + 8) == due + 8 &&
                StoryRules.Arrive(arrived, due + 8) == arrived, id + " an actual encounter can be reopened without a new first-arrival date");
            arrived = StoryStateCodec.Read(StoryStateCodec.Write(arrived), "schedule", 1);
            check(arrived.EncounterDay == due + 7 && (!StoryRules.TimedDecision(id) ||
                StoryRules.DecisionDeadline(arrived) == due + 14), id + " first encounter and deadline survive reload");
            check(StoryRules.ArrivalDenial(s, new(due, 0, 0, 1000, true, true)) ==
                (StoryCatalog.Events[index].RequiredCybernetics > 0 ? "ownership" : null), id + " scheduling preserves ownership gates");
        }
        check(new[] { "E01", "E04", "E05", "E07", "E09" }.All(x => StoryRules.VisitWeekday(x) == null),
            "other events retain their ordinary next-day availability");
        var pending = new StoryState { ActiveEventId = "E01", LastStartedDay = 2,
            Pending = new("pending", "investigate", 2, 1000, -60, 0) };
        check(StoryRules.Arrive(pending, 2) == pending, "arrival never mutates an unconfirmed transaction");
    }

    private static void RunOpeningCatchUp(Action<bool, string> check)
    {
        StoryState Reload(StoryState s) => StoryStateCodec.Read(StoryStateCodec.Write(s), "old-opening", 2);
        var fresh = new StoryState { GameRunId = "old-opening", SaveSlotId = 2 };
        var newRun = StoryRules.InitializeOpening(fresh, false);
        var reloadedNew = Reload(newRun);
        check(reloadedNew.CatchUpOpening == false &&
            StoryRules.InitializeOpening(reloadedNew, true) == reloadedNew,
            "saving a new run and loading it later never changes its opening schedule");
        string legacyJson = StoryStateCodec.Write(fresh).Replace("\"catchUpOpening\":null,", "");
        var old = StoryRules.InitializeOpening(StoryStateCodec.Read(legacyJson, "old-opening", 2), true);
        var reloadedOld = Reload(old);
        check(reloadedOld.CatchUpOpening == true && StoryRules.InitializeOpening(reloadedOld, false) == reloadedOld,
            "an unstarted old story record chooses catch-up once and retains it on reload");
        check(StoryRules.ArrivalDenial(old, new(0, 0, 0, 1000, false)) == "first_night",
            "an old save still needs its first overnight before E01");
        foreach (int firstDay in Enumerable.Range(14, 7))
        {
            var state = old with { Items = [new(StoryJournal.Key, 42)] };
            string[] options = ["investigate", "accept", "honest"];
            for (int i = 0; i < options.Length; i++)
            {
                int day = firstDay + i;
                var context = new StoryContext(day, 0, 0, 1000, false);
                check(StoryRules.Available(state, context)?.Id == $"E0{i + 1}" &&
                    StoryRules.ArrivalDenial(state, context) == null,
                    "an old save opens the next introductory event each overnight, on every weekday");
                var active = StoryRules.Arrive(StoryRules.Begin(state, context), day);
                state = Reload(StoryRules.Commit(active, context, options[i]));
                check(StoryRules.ArrivalDenial(state, context) == (i < 2 ? "day_quota" : "ownership"),
                    "catch-up cannot start another event on its completion day or bypass E04 ownership");
                check(state.Items.SequenceEqual(new[] { new StoryItemReceipt(StoryJournal.Key, 42) }) &&
                    state.Choices.Length == i + 1 && state.CatchUpOpening == true,
                    "catch-up reload preserves receipts, choices and its schedule without redelivery");
                check(StoryRules.Denial(state, context, options[i]) == "event",
                    "a completed catch-up choice cannot be applied again");
            }
        }
        var c = new StoryContext(20, 0, 0, 1000, false);
        var first = StoryRules.Begin(old, c);
        var completed = StoryRules.Commit(first, c, "investigate");
        check(StoryRules.ArrivalDenial(completed, c with { Day = 23 }) == null &&
            StoryRules.VisitDay(completed, 23) == 23,
            "skipping a catch-up day never adds a natural-week wait");
        check(StoryRules.InitializeOpening(first, true).CatchUpOpening == true &&
            StoryRules.InitializeOpening(completed with { CatchUpOpening = null }, true).CatchUpOpening == false,
            "an existing catch-up flag persists while already-progressed legacy records keep their schedule");
        var pending = fresh with { ActiveEventId = "E01", LastStartedDay = 20,
            Pending = new("pending-opening", "investigate", 20, 1000, -60, 0) };
        check(StoryRules.InitializeOpening(pending, true) == pending,
            "choosing an opening schedule never changes an unconfirmed transaction");
    }

    internal static void Run(Action<bool, string> check)
    {
        RunSchedule(check);
        RunLiveDebug(check);
        RunItemActions(check);
        const string run = "story-test-run";
        StoryState Fresh() => new() { GameRunId = run, SaveSlotId = 2, ContentVersion = 4 };
        StoryContext At(int day, bool core = false, int owned = 12, int cash = 1000, bool ascended = true) =>
            new(day, 0, owned, cash, core, ascended);
        StoryState Choose(StoryState s, int day, string option, bool core = false)
        {
            var c = At(day, core);
            var begun = StoryRules.Begin(s, c);
            var next = CommitObservedChoice(begun, c, option, option == "buy" ? 73 : 0);
            return StoryStateCodec.Read(StoryStateCodec.Write(next), run, 2);
        }
        bool Rejects(string json, string id = run, int slot = 2)
        {
            try { StoryStateCodec.Read(json, id, slot); return false; }
            catch (Exception ex) when (ex is AugMechanicalAscension.Serialization.JsonException or System.Text.Json.JsonException or InvalidDataException or InvalidOperationException) { return true; }
        }
        check(StoryRules.Available(Fresh(), At(0)) == null, "story waits for first overnight");
        check(StoryRules.VisitDenial(Fresh(), At(0)) == "first_night", "visit explains first-night gate");
        var first = StoryRules.Begin(Fresh(), At(1));
        var timed = StoryRules.StartDecision(first, 1);
        check(StoryRules.DecisionDeadline(timed) == 8 && !StoryRules.DecisionDue(timed, 7) && StoryRules.DecisionDue(timed, 8),
            "seven-game-day grace period expires on the exact boundary");
        check(StoryRules.StartDecision(timed, 6) == timed && StoryRules.DecisionDeadline(StoryRules.Begin(timed, At(6))) == 8,
            "reopening or repeated deferral cannot extend a deadline");
        check(StoryRules.DecisionDue(StoryStateCodec.Read(StoryStateCodec.Write(timed), run, 2), 8),
            "deadline persists across save codec roundtrip");
        check(StoryRules.Denial(timed, At(8, cash: 0), "investigate") == "cash" &&
            StoryRules.Denial(timed, At(8, cash: 0), "decline") == null,
            "deadline does not create debt or remove the free alternative");
        var timedDone = CommitObservedChoice(timed, At(8, cash: 0), "decline");
        check(timedDone.DecisionStartedDay == -1 && !StoryRules.DecisionDue(timedDone, 100),
            "a valid choice releases the day-advance guard");
        check(!StoryRules.TimedDecision("E07") && !StoryRules.TimedDecision("E10") &&
            StoryRules.TimedDecision("E06") && StoryRules.TimedDecision("E08"),
            "only the agreed three decisions have deadlines");
        check(StoryRules.DecisionDeadline(first) == null && StoryRules.StartDecision(first, 100).DecisionStartedDay == 100,
            "an active event starts its week on its next live encounter, not retroactively");
        check(Rejects(StoryStateCodec.Write(Fresh() with { DecisionStartedDay = 1 })),
            "deadline without an active critical event is rejected");
        check(Rejects(StoryStateCodec.Write(first with { DecisionStartedDay = 0 })),
            "deadline cannot predate event activation");
        check(StoryRules.Begin(first, At(1)) == first, "reopening a story event does not consume another quota");
        check(StoryRules.Denial(first, At(1), "refund") == "option", "retired compensation cannot be selected");
        var historical = CommitObservedChoice(first, At(1), "investigate") with { Choices = [new("E01", "refund", 1)] };
        check(StoryStateCodec.Read(StoryStateCodec.Write(historical), run, 2).Choice("E01") == "refund" &&
            StoryJournal.Pages(historical, false)[0].Body.Contains("赔偿100信用点"), "existing compensation history still loads and renders");
        check(StoryRules.Denial(first, At(1, cash: 50), "investigate") == "cash" &&
            StoryRules.Denial(first, At(1, cash: 0), "decline") == null,
            "insufficient cash preserves a free story choice");
        var late = CommitObservedChoice(first, At(4), "decline");
        check(StoryRules.Available(late, At(4)) == null && StoryRules.Available(late, At(5))?.Id == "E02",
            "resuming after several days cannot chain another event on the completion day");
        check(StoryRules.VisitDenial(late, At(4)) == "day_quota" && StoryRules.VisitDenial(first, At(1)) == null,
            "visit reason distinguishes daily quota from resuming an active event");
        check(StoryStateCodec.Read(StoryStateCodec.Write(late), run, 2).Choice("E01") == "decline",
            "delayed completion survives serialization");
        var pending = first with { Pending = new("request-1", "investigate", 1, 1000, -60, 0) };
        check(StoryRules.Available(pending, At(1)) == null && StoryRules.Denial(pending, At(1), "investigate") == "pending",
            "unconfirmed external changes block all replay of the story operation");
        check(StoryRules.VisitDenial(pending, At(0)) == "pending", "pending transaction takes precedence over ordinary visit gates");
        check(StoryStateCodec.Read(StoryStateCodec.Write(pending), run, 2).Pending?.RequestId == "request-1",
            "unconfirmed transaction identity is retained on reload");
        string clean = StoryStateCodec.Write(Fresh());
        check(Rejects(clean, "another-run") && Rejects(clean, run, 3), "story saves reject cross-run and cross-slot identities");
        check(Rejects(clean.Replace("\"schemaVersion\":1", "\"schemaVersion\":99")), "future story schema is not reset");
        check(Rejects(clean.Replace("\"schemaVersion\":1,", "")), "missing story schema is not defaulted silently");
        check(Rejects(clean.Replace("\"revision\":0", "\"revision\":0,\"revision\":1")), "duplicate story fields are rejected");
        check(Rejects("{not-json"), "corrupt story data is rejected");
        check(Rejects(StoryStateCodec.Write(Fresh() with { CoreUid = 73, CoreDisposition = "held" })),
            "a core receipt without its acquisition is rejected");

        foreach (bool buy in new[] { false, true })
        foreach (bool share in new[] { false, true })
        foreach (bool pretend in new[] { false, true })
        {
            var s = Fresh();
            string[] route = ["decline", "accept", pretend ? "pretend" : "honest", "compare", "keep",
                buy ? "buy" : "refuse", "disclose", share ? "share" : "hide", "read"];
            for (int i = 0; i < route.Length; i++)
            {
                s = Choose(s, i + 1, route[i], buy && i >= 6);
                check(i < 2 ? StoryRules.Available(s, At(i + 1, buy)) == null :
                    StoryRules.Available(s, At(i + 1, buy))?.Id == StoryCatalog.Events[i + 1].Id,
                    "opening events wait a night; later eligibility depends on current prerequisites");
            }
            check(StoryRules.Available(s, At(10, buy))?.Id == "E10",
                "completed Mechanical Ascension unlocks an ending");
            check(StoryRules.VisitDenial(s, At(10, buy, ascended: false)) == "achievement" &&
                StoryRules.Available(s, At(10, buy, owned: 0))?.Id == "E10",
                "owning twelve kinds cannot substitute for the achievement; completed achievement survives loss of items");
            if (buy && share)
            {
                check(!s.CoreConsented && !StoryRules.CanAid(s, At(10, true)), "sharing alone leaves the core's batch consent unset");
                s = StoryRules.Consent(s, At(10, true), StoryRules.RequestCoreAid);
            }
            var ready = StoryRules.Begin(s, At(10, buy));
            check(StoryRules.CanAid(ready, At(10, buy)) == (buy && share),
                "aid requires consent and retained core, while correcting an impersonation remains possible");
            check(!StoryRules.CanAid(ready, At(10, false)), "lost physical ownership prevents aid even with a saved receipt");
            foreach (var ending in new[] { "legal_property", "settled_account", "lost_and_found" })
            {
                if (ending == "lost_and_found" && !(buy && share))
                { check(StoryRules.Denial(ready, At(10, buy), ending) == "aid", "forfeited aid is explicitly denied"); continue; }
                var ended = CommitObservedChoice(ready, At(10, buy), ending);
                ended = StoryStateCodec.Read(StoryStateCodec.Write(ended), run, 2);
                check(ended.EndingId == ending && StoryRules.Available(ended, At(11, buy)) == null &&
                    StoryRules.Denial(ended, At(10, buy), ending) != null, "a committed ending cannot be applied twice");
                check(!StoryRules.ReadEpilogue(ended, 10).EpilogueSeen && StoryRules.ReadEpilogue(ended, 11).EpilogueSeen,
                    "epilogue unlocks the following day");
                if (buy && ending != "lost_and_found")
                {
                    check(StoryRules.Denial(ended, At(10, true), StoryRules.SellCore) != null,
                        "core profit is unavailable after an ending");
                    check(StoryRules.Denial(ended, At(10, true), StoryRules.ArchiveCore) == "consent", "batch consent does not permit individual placement");
                    var archived = CommitObservedChoice(StoryRules.Consent(ended, At(10, true), StoryRules.RequestCoreCustody), At(10, true), StoryRules.ArchiveCore);
                    check(archived.EndingId == ending && archived.CoreDisposition == "archived" &&
                        StoryRules.Denial(archived, At(10, false), StoryRules.ArchiveCore) != null,
                        "archiving is single-use and preserves the ending");
                }
            }
            if (buy)
            {
                var sold = CommitObservedChoice(ready, At(10, true), StoryRules.SellCore);
                check(!StoryRules.CanAid(sold, At(10, true)) &&
                    StoryRules.Denial(sold, At(10, true), StoryRules.SellCore) != null,
                    "handover closes aid permanently and cannot pay twice");
            }
        }
        var progress = Fresh();
        foreach (var option in new[] { "decline", "accept", "honest" }) progress = Choose(progress, progress.Choices.Length + 1, option);
        check(StoryRules.Available(progress, At(4, owned: 1)) == null &&
            StoryRules.Available(progress, At(4, owned: 2))?.Id == "E04", "two owned cybernetic kinds gate the first investigation");
        var begunRepair = StoryRules.Begin(progress, At(4, owned: 2));
        check(StoryRules.Denial(begunRepair, At(4, owned: 1), "compare") != null,
            "a started event rechecks ownership on submission without completing the event");
        progress = Choose(progress, 4, "compare");
        check(StoryRules.Available(progress, At(5, owned: 3)) == null, "four owned kinds gate the contract");
        progress = Choose(progress, 5, "keep");
        var buying = StoryRules.Begin(progress, At(6));
        check(StoryRules.Denial(buying, At(6, cash: 199), "buy") == "cash", "core purchase needs the full price");
        progress = Choose(progress, 6, "buy");
        check(StoryRules.Available(progress, At(7, true, 7)) == null, "eight owned kinds gate the recording");
        progress = Choose(progress, 7, "disclose", true);
        var sharing = StoryRules.Begin(progress, At(8));
        check(StoryRules.Denial(sharing, At(8, false), "share") == null, "sharing with Lin does not require custody of the core or create its consent");
        progress = Choose(progress, 8, "share", true);
        check(StoryRules.Available(progress, At(9, true, 9)) == null, "ten owned kinds gate the offer");

        foreach (var ev in StoryCatalog.Events)
        foreach (var option in ev.Options)
            check(!string.IsNullOrWhiteSpace(StoryReplies.Get(ev.Id, option.Id, false)) &&
                !string.IsNullOrWhiteSpace(StoryReplies.Get(ev.Id, option.Id, true)),
                "every NPC choice has an authored bilingual reply: " + ev.Id + "/" + option.Id);
        check(StoryEvidence.Grants("E02", "accept").Contains("@nicokobo.mechcore.synthesis.titanium_alloy") &&
            !StoryEvidence.Grants("E02", "accept").Contains("@nicokobo.mechcore.synthesis.precision_parts") &&
            StoryEvidence.Grants("E02", "accept", false).All(x => !x.StartsWith('@')),
            "the index includes titanium alloy but no precision part");
        check(StoryEvidence.Grants("E07", "disclose").Length == 0 &&
            StoryEvidence.Grants("E07", "withhold_name").SequenceEqual(new[] { "@nicokobo.mechcore.synthesis.control_chip" }) &&
            StoryEvidence.Grants("E07", "withhold_name", false).Length == 0,
            "only masking Grayline's name receives the control chip when rewards are enabled");
        check(StoryEvidence.Grants("E09", "read").Contains("@nicokobo.mechcore.synthesis.power_regulator"),
            "the unsigned offer delivers a power regulator without contract obligations");
        check(StoryEvidence.Grants("E01", "decline").SequenceEqual(new[] { "receipt" }),
            "refusing the return preserves evidence without delivering the optional prosthetic");
        foreach (var key in StoryEvidence.Keys)
            check(StoryEvidence.Body(key, null, false).Length > 0 && StoryEvidence.Body(key, null, true).Length > 0,
                "physical document factory has bilingual content: " + key);
        var photoState = Fresh();
        foreach (var option in new[] { "investigate", "accept", "honest" }) photoState = Choose(photoState, photoState.Choices.Length + 1, option);
        var inspectedPhoto = StoryInvestigation.Commit(photoState, 3, "inspect_photo");
        var restoredPhoto = StoryStateCodec.Read(StoryStateCodec.Write(inspectedPhoto), run, 2);
        check(restoredPhoto.PhotoInspected && !restoredPhoto.ProstheticInspected && restoredPhoto.Investigation == 0 &&
            !StoryInvestigation.Allowed(restoredPhoto, 3, "inspect_photo"), "photo inspection persists without granting hidden investigation progress or replay");
        check(StoryEvidence.InspectionNote("photo", photoState, false) == "" &&
            StoryEvidence.Body("photo", restoredPhoto, false).Contains(StoryEvidence.InspectionNote("photo", restoredPhoto, false)) &&
            StoryEvidence.Description("photo", restoredPhoto, true) == StoryEvidence.Body("photo", null, true),
            "photo description retains its content; inspection notes are shown by magnification and reading");
        check(Rejects(StoryStateCodec.Write(Fresh() with { PhotoInspected = true })), "a photograph cannot be inspected before acquisition");
        var earlyMarks = StoryInvestigation.Commit(Choose(Fresh(), 1, "investigate"), 1, "inspect_prosthetic");
        check(StoryStateCodec.Read(StoryStateCodec.Write(earlyMarks), run, 2).ProstheticInspected &&
            !StoryInvestigation.Allowed(earlyMarks, 2, "x1"), "the magnifier can reveal the original's marks without bypassing missing investigation documents");
        var closedMarks = StoryInvestigation.Commit(StoryInvestigation.Close(photoState, "diagnostics_lost"), 20, "inspect_prosthetic");
        check(closedMarks.HiddenClosedReason == "diagnostics_lost" && !StoryInvestigation.Allowed(closedMarks, 21, "x1"),
            "retained-object inspection cannot restore discarded diagnostic data");
        var targets = WorkshopAchievements.Definitions.Where(x => x.ProofItemId != null).Select((x, i) => new WorkshopOwnedItem(x.ProofItemId!, i + 1, 1, true)).ToArray();
        check(StoryInvestigation.OwnedCybernetics(targets) == 12 &&
            StoryInvestigation.OwnedCybernetics(targets.Concat([targets[0], targets[0] with { UniqueId = 200 }])) == 12,
            "actual target ownership counts all twelve kinds and never counts duplicates twice");
        check(StoryInvestigation.OwnedCybernetics(targets.Concat([new WorkshopOwnedItem("foreign", 1, 1, true)])) == 11 &&
            StoryInvestigation.OwnedCybernetics(targets.Select(x => x with { Owned = false })) == 0 &&
            StoryInvestigation.OwnedCybernetics(targets.Select(x => x with { Units = 0 })) == 0,
            "foreign ownership, empty stacks and colliding identities cannot satisfy cybernetic gates");
        progress = Fresh();
        foreach (var option in new[] { "investigate", "accept", "honest", "compare" }) progress = Choose(progress, progress.Choices.Length + 1, option);
        progress = StoryInvestigation.Commit(progress, 4, "inspect_prosthetic");
        check(!StoryInvestigation.Allowed(progress, 4, "x1"), "marks alone cannot bypass the missing contract");
        progress = Choose(progress, 5, "keep");
        check(StoryInvestigation.Allowed(progress, 5, "x1") &&
            StoryItemActionRules.Actions(progress, "repair", 5).Contains("x1"),
            "H2 is available immediately after receiving the contract, including on a main-event day");
        var sameDayComparison = StoryInvestigation.Commit(progress, 5, "x1");
        check(StoryStateCodec.Read(StoryStateCodec.Write(sameDayComparison), run, 2).Investigation == 1 &&
            !StoryItemActionRules.Actions(sameDayComparison, "repair", 5).Contains("x1"),
            "same-day comparison survives reload and removes its completed menu action");
        var missedRecords = Choose(progress with { ProstheticInspected = false }, 6, "refuse");
        check(missedRecords.HiddenClosedReason == "none" && !StoryInvestigation.Allowed(missedRecords, 20, "x1") &&
            StoryRules.Available(missedRecords, At(7))?.Id == "E07" && StoryInvestigation.Allowed(missedRecords with { ProstheticInspected = true }, 20, "x1"),
            "E06 does not close comparison; a real inspection remains necessary");
        progress = StoryInvestigation.Commit(progress, 6, "x1");
        check(StoryRules.VisitDenial(progress, At(6)) == null && StoryRules.ArrivalDenial(progress, At(6)) == "visit_date",
            "investigation does not delay an eligible main event, while its scheduled visit date remains");
        progress = Choose(progress, 7, "buy");
        var damaged = StoryRules.Begin(progress, At(8, true));
        var paidRepair = StoryInvestigation.Repair(damaged, false);
        check(paidRepair.RecordingRepaired && !paidRepair.DiagnosticRetained && paidRepair.HiddenClosedReason == "diagnostics_lost" &&
            CommitObservedChoice(paidRepair, At(8, true), "disclose").Choice("E07") == "disclose",
            "paid repair loses the optional tail while preserving the complete main recording and decision");
        progress = CommitObservedChoice(StoryInvestigation.Repair(damaged, true), At(8, true), "disclose");
        check(StoryInvestigation.Allowed(progress, 8, "x2_start") &&
            StoryItemActionRules.Actions(progress, "communicator", 8).Contains("x2_start"),
            "H4 can start immediately on the E07 completion day when H2 and H3 are met");
        var sameDayExperiment = StoryInvestigation.Commit(progress, 8, "x2_start");
        sameDayExperiment = StoryStateCodec.Read(StoryStateCodec.Write(sameDayExperiment), run, 2);
        check(!StoryInvestigation.Allowed(sameDayExperiment, 8, "x2_read") &&
            !StoryItemActionRules.Actions(sameDayExperiment, "communicator", 8).Contains("x2_read") &&
            StoryInvestigation.Allowed(sameDayExperiment, 9, "x2_read") &&
            StoryItemActionRules.Actions(sameDayExperiment, "communicator", 9).Contains("x2_read"),
            "same-day H4 start survives reload but its result still waits exactly one night");
        progress = StoryInvestigation.Commit(progress, 9, "x2_start");
        check(!StoryInvestigation.Allowed(progress, 9, "x2_read") &&
            !StoryInvestigation.Allowed(progress, 10, "x2_start"), "experiment cannot complete immediately or consume materials twice");
        progress = Choose(progress, 10, "share", true);
        var missedObservation = StoryInvestigation.ReserveOffer(progress);
        check(missedObservation.HiddenClosedReason == "none" && StoryInvestigation.Allowed(missedObservation, 10, "x2_read") &&
            StoryInvestigation.ReserveOffer(missedObservation) == missedObservation, "the first offer reservation preserves an unread overnight observation");
        progress = StoryInvestigation.Commit(progress, 10, "x2_read");
        progress = Choose(progress, 11, "read", true);
        check(StoryInvestigation.Allowed(progress, 11, "x3") && StoryItemActionRules.Actions(progress, "credential", 11).Contains("x3"),
            "H5 is immediately available on the E09 completion day");
        progress = StoryInvestigation.Commit(progress, 11, "x3");
        progress = StoryStateCodec.Read(StoryStateCodec.Write(progress), run, 2);
        check(StoryInvestigation.Complete(progress) && progress.ExperimentDay == 9,
            "all five investigation facts survive save codec roundtrip");
        var final = StoryRules.Begin(progress, At(13, true));
        var outside = CommitObservedChoice(final, At(13, true), "beyond_the_world");
        check(StoryStateCodec.Read(StoryStateCodec.Write(outside), run, 2).EndingId == "beyond_the_world" &&
            !StoryInvestigation.Allowed(outside, 14, "x1"), "fourth ending is persisted and closes investigation");
        var independent = final with { CoreUid = 0, CoreDisposition = "none", CoreConsented = false, CopyDelivered = false,
            Choices = final.Choices.Select(x => x.EventId switch {
                "E03" => x with { OptionId = "pretend" }, "E06" => x with { OptionId = "refuse" },
                "E07" => x with { OptionId = "withhold_name" }, "E08" => x with { OptionId = "hide" }, _ => x }).ToArray() };
        check(StoryStateCodec.Read(StoryStateCodec.Write(CommitObservedChoice(independent, At(13), "beyond_the_world")), run, 2).EndingId == "beyond_the_world",
            "identity, supplier anonymity, core rescue and sharing choices do not morally gate boundary verification");
        check(StoryRules.Denial(final with { Investigation = 2 }, At(13, true), "beyond_the_world") == "investigation",
            "fourth ending cannot bypass final channel verification");
        check(!StoryInvestigation.Complete(final with { ProstheticInspected = false }) &&
            !StoryInvestigation.Complete(final with { DiagnosticRetained = false }) &&
            !StoryInvestigation.Complete(final with { OfferReserved = false }), "one investigation level cannot substitute for all five independent facts");
        check(StoryEvidence.Body("recording", paidRepair, false).Contains("外围诊断尾段在修复时清掉了") &&
            StoryEvidence.Body("recording", progress, false).Contains("外围诊断尾段保住了") &&
            !StoryEvidence.Body("repair", Fresh(), false).Contains("调度"), "ordinary documents do not expose undiscovered boundary diagnostics");
        check(!StoryCatalog.Ending("beyond_the_world", outside, false).Contains("UserData") &&
            !StoryCatalog.Ending("beyond_the_world", outside, true).Contains("UserData"), "ending text leaves the external receipt location unstated");
        check(StoryLetter.ChooseName(" Windows\nUser\t ") == "WindowsUser" &&
            StoryLetter.ChooseName("\r\n") == "" &&
            StoryLetter.Text("玩家", false).Contains("\n玩家，谢谢你。\n") && StoryLetter.Text("Reader", true).Contains("\nReader, thank you.\n"), "the letter uses the local system name with control characters removed");
        var physical = Fresh() with { ContentVersion = 4, Items = [new("receipt", 101)] };
        check(StoryStateCodec.Read(StoryStateCodec.Write(physical), run, 2).Items[0].Uid == 101,
            "physical evidence receipts survive codec roundtrip");
        check(Rejects(StoryStateCodec.Write(physical with { Items = [new("receipt", 101), new("photo", 101)] })),
            "duplicate physical UID is rejected");
        check(Rejects(StoryStateCodec.Write(physical with { Items = [new("receipt", 101), new("receipt", 102)] })),
            "duplicate evidence delivery key is rejected");
        check(Rejects(StoryStateCodec.Write(physical with { Items = [new("unknown", 101)] })),
            "unknown evidence identities are not silently accepted");
        check(Rejects(StoryStateCodec.Write(physical with { Investigation = 3 })),
            "fabricated investigation without prerequisites is rejected");
        foreach (bool en in new[] { false, true })
        {
            string beforeContract = StoryEvidence.Body("receipt", photoState, en);
            string afterContract = StoryEvidence.Body("receipt", damaged, en);
            check(!beforeContract.Contains(en ? "Handwriting check" : "对照笔迹") && afterContract.Contains(en ? "Handwriting check" : "对照笔迹"),
                "the receipt adds its signature comparison only after actual contract progress");
            string repaired = StoryEvidence.Body("recording", paidRepair, en);
            string heard = StoryEvidence.Body("recording", progress, en);
            check(repaired.Contains(en ? "irreversibly damaged" : "不可逆损毁") &&
                !repaired.Contains(en ? "Grayline's account" : "灰线的说法") && heard.Contains(en ? "Grayline's account" : "灰线的说法"),
                "repair exposes the main diagnosis without pre-recording oral testimony");
            string log = StoryEvidence.MainRecord(en);
            int clearing = log.IndexOf(en ? "Personality erasure began" : "开始分段清除人格", StringComparison.Ordinal);
            int release = log.IndexOf(en ? "cut remote control of this unit's AI" : "切断本机 AI 远程控制", StringComparison.Ordinal);
            int diagnosis = log.IndexOf(en ? "irreversibly damaged" : "不可逆损毁", StringComparison.Ordinal);
            check(clearing >= 0 && clearing < release && release < diagnosis,
                "operation, lucid release and final diagnosis keep their causal order");
            var masked = progress with { Choices = progress.Choices.Select(x => x.EventId == "E07" ? x with { OptionId = "withhold_name" } : x).ToArray() };
            string maskedCopy = StoryEvidence.Body("copy", masked, en);
            check(!maskedCopy.Contains(en ? "Grayline" : "灰线") && maskedCopy.Contains(en ? "payment records" : "收款记录") &&
                StoryEvidence.Body("recording", masked, en).Contains(en ? "Grayline" : "灰线"),
                "masking the public name preserves transactions and the complete original");
            check(maskedCopy == StoryEvidence.Body("copy", masked with { CoreConsented = true, Investigation = 0, EndingId = "legal_property", EpilogueSeen = true }, en),
                "later research, core answers and endings cannot alter the fixed main-line copy");
            var hidden = progress with { Choices = progress.Choices.Select(x => x.EventId == "E08" ? x with { OptionId = "hide" } : x).ToArray(), CopyDelivered = false };
            check(!StoryCatalog.Ending("legal_property", hidden, en).Contains(en ? "Lin:" : "林岑留言") &&
                StoryCatalog.Ending("legal_property", progress, en).Contains(en ? "Lin:" : "林岑留言"),
                "A only gives Lin a knowledgeable message after successful sharing");
            check(StoryCatalog.Ending("settled_account", hidden, en).Contains(en ? "No one has replied yet" : "还没有人回信") &&
                StoryCatalog.Ending("settled_account", progress, en).Contains(en ? "Lin says she got" : "林岑说材料收到了"),
                "B distinguishes verified family contact from unconfirmed merchant delivery");
            foreach (string ending in new[] { "legal_property", "settled_account", "lost_and_found", "beyond_the_world" })
            {
                var endedFacts = progress with { EndingId = ending };
                string history = StoryEvidence.Body("investigation", endedFacts, en);
                check(!history.Contains(en ? "No final disposition was performed" : "凭据尚未执行最终处置"),
                    "the retained H5 history does not claim an unspent card after " + ending);
            }
            var impersonated = progress with { Choices = progress.Choices.Select(x => x.EventId == "E03" ? x with { OptionId = "pretend" } : x).ToArray() };
            check(StoryReplies.Get("E08", "share", en, impersonated).Contains(en ? "amnesia was a lie" : "上次说失忆是骗她的") &&
                !StoryReplies.Get("E08", "share", en, progress).Contains(en ? "amnesia was a lie" : "上次说失忆是骗她的"),
                "successful sharing corrects a prior impersonation without inventing memories");
        }
        check(Rejects(StoryStateCodec.Write(Fresh() with { ContentVersion = 3 })) &&
            StoryRules.VisitDenial(Fresh() with { ContentVersion = 3 }, At(1)) == "version",
            "unsupported story content is rejected without pretending it is a new V4 run");
        check(Rejects(StoryStateCodec.Write(damaged with { ConfessionHeard = true })) &&
            StoryRules.Denial(damaged, At(8, true), "disclose") == "confession",
            "unrepaired or unheard testimony cannot unlock the E07 reward");
        var h3First = StoryInvestigation.Repair(damaged with { Investigation = 0, InvestigationDay = -1,
            Items = [new("credential", 9876)] }, true);
        check(StoryStateCodec.Read(StoryStateCodec.Write(h3First), run, 2).DiagnosticRetained && h3First.Investigation == 0,
            "save validation permits real diagnostic retention before comparison");
        check(!StoryInvestigation.Allowed(outside, 14, "inspect_photo") &&
            !StoryInvestigation.Allowed(outside, 14, "inspect_prosthetic"), "final disposition stops new investigation facts");
        check(Rejects(StoryStateCodec.Write(first with { Pending = new("id", "physical:repair_parts", 1, 10, 0, 0)
            { OutputUids = [9], RemovedUids = [9] } })), "pending journal rejects the same UID as output and removed input");
        check(Rejects(StoryStateCodec.Write(first with { Pending = new("id", "physical:repair_parts", 1, 10, 0, 0)
            { MaterialUid = 9, MaterialUnitsBefore = 0 } })), "pending journal requires actual material quantities");
    }
}

