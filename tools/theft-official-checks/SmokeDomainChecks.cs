using DefinitelyStolen.Domain;
using DefinitelyStolen.Persistence;
internal static class SmokeDomainChecks
{
    internal static void Run(Catalog catalog, Func<int, ulong, RunState> create, Action<bool, string> check, Action<Action, string> reject)
    {
        check(EscapeSmokeRules.IsEscapeSmoke("smoke_grenade"), "Native smoke grenades can trigger automatic theft escape");
        check(EscapeSmokeRules.IsEscapeSmoke("nicokobo.definitely_stolen.escape_smoke"), "Existing escape smoke grenades retain their automatic escape effect");
        check(!EscapeSmokeRules.IsEscapeSmoke("flashbang_grenade") && !EscapeSmokeRules.IsEscapeSmoke("c4"), "Other native explosives cannot trigger theft escape");
        const long now = 639000000000000000;
        RunState AttemptFixture(TheftMode mode, AttemptOutcome outcome)
        {
            var run = create(2, 12345); run.Experience += 10;
            TheftRules.SelectMode(run, mode);
            var npc = run.Night!.Npcs[0];
            var loot = npc.Items[0]; npc.Items.RemoveAt(0); npc.SuccessfulThefts++;
            run.Night.Loot.Add(loot);
            TimingRules.Begin(run, npc, catalog.Role(npc.RoleId), false, now);
            var timing = run.Night.Timing!;
            double stop = outcome == AttemptOutcome.Success ? (timing.Zones.SuccessStart + timing.Zones.SuccessEnd) / 2
                : outcome == AttemptOutcome.HiddenFailure ? (timing.Zones.Exposure / 2 + timing.Zones.SuccessStart) / 2 : 0;
            if (mode == TheftMode.Probability) { timing.AutomaticOutcome = outcome; timing.AutomaticPosition = stop; }
            TimingRules.Stop(run, stop, now + TimeSpan.TicksPerSecond);
            return run;
        }
        foreach (var mode in new[] { TheftMode.Timing, TheftMode.Probability })
        foreach (var outcome in new[] { AttemptOutcome.Success, AttemptOutcome.HiddenFailure, AttemptOutcome.Caught })
        {
            var run = AttemptFixture(mode, outcome); var night = run.Night!; var npc = night.Npcs[0];
            int lootId = night.Loot[0].UniqueId, wind = night.PendingWind;
            var ordinary = StateCodec.Clone(run);
            var receipt = TheftRules.Attempt(run, npc, catalog.Role(npc.RoleId), false, escapeItemUniqueId: 90001);
            var unprotected = TheftRules.Attempt(ordinary, ordinary.Night!.Npcs[0], catalog.Role(npc.RoleId), false);
            check(receipt.Outcome == outcome && receipt.ExperienceAward == unprotected.ExperienceAward && receipt.TargetReputationPenalty == unprotected.TargetReputationPenalty &&
                night.PendingWind == ordinary.Night.PendingWind && night.RandomState == ordinary.Night.RandomState,
                "Smoke retains the outcome, penalties and random sequence for " + mode + "/" + outcome);
            int windGain = outcome switch { AttemptOutcome.Success => 2, AttemptOutcome.HiddenFailure => 1, _ => 10 };
            check(night.PendingWind == wind + windGain, "Theft adds the reduced rumour amount for " + mode + "/" + outcome);
            if (outcome != AttemptOutcome.Caught)
            {
                check(receipt.EscapeItemUniqueId == 0 && !night.Escaped, "Success and hidden failure never consume smoke");
                continue;
            }
            check(receipt.EscapeItemUniqueId == 90001 && night.Escaped && !night.Ended && !night.Caught &&
                night.Loot.Single().UniqueId == lootId && night.Abandoned.Count == 0,
                "Caught smoke escape retains every loot item and leaves carry available for sorting");
            check(ordinary.Night.Ended && ordinary.Night.Loot.Count == 0 && ordinary.Night.Abandoned.Single().UniqueId == lootId,
                "Caught theft without smoke retains its existing loss and forced-return behavior");
            check(TheftRules.Remaining(run) == 0, "Smoke ends all remaining theft attempts");
            reject(() => TimingRules.Begin(run, npc, catalog.Role(npc.RoleId), true, now), "Infinite actions cannot bypass a smoke retreat");
            reject(() => TheftRules.Attempt(run, npc, catalog.Role(npc.RoleId), true), "Direct attempts cannot bypass a smoke retreat");
            reject(() => TheftRules.SelectMode(run, mode), "Changing theft mode cannot resume a smoke retreat");
            var restored = StateCodec.Decode(StateCodec.Encode(run), run.RunId, run.SlotId, catalog);
            check(restored.Night!.Escaped && TheftRules.Remaining(restored) == 0 && restored.Night.Loot.Single().UniqueId == lootId,
                "Reload retains the stopped theft and all protected loot");
            restored.Night.Mode = TheftMode.Timing;
            reject(() => TimingRules.Practice(restored, restored.Night.Npcs[0], catalog.Role(npc.RoleId), now), "Practice cannot restart theft after smoke");
            CustodyRules.FinishReturn(restored.Night, new HashSet<int> { lootId });
            StateCodec.Validate(restored, catalog);
            check(restored.Night.Ended && restored.Night.Loot.Single().UniqueId == lootId, "Protected loot completes the normal return flow");
        }

    }
}
