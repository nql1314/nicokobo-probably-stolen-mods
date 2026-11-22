using DefinitelyStolen.Domain;
using DefinitelyStolen.Persistence;

internal static class TimingChecks
{
    internal static void Run(Catalog catalog, Func<int, ulong, RunState> create, Action<bool, string> check, Action<Action, string> reject)
    {
        const long now = 639000000000000000;
        var practiceRun = create(0, 12345); TheftRules.SelectMode(practiceRun, TheftMode.Timing);
        var practiceNpc = practiceRun.Night!.Npcs[0];
        string beforePractice = StateCodec.Encode(practiceRun);
        var practice = TimingRules.Practice(practiceRun, practiceNpc, catalog.Role(practiceNpc.RoleId), now);
        var practiceZones = practice.Zones;
        check(practiceZones == TimingRules.Preview(practiceRun, practiceNpc, catalog.Role(practiceNpc.RoleId)), "Practice uses actual manual zones");
        check(practiceZones.At(.5) == AttemptOutcome.Success && practiceZones.At(0) == AttemptOutcome.Caught
            && practiceZones.At((practiceZones.Exposure / 2 + practiceZones.SuccessStart) / 2) == AttemptOutcome.HiddenFailure,
            "Practice supports all three landing results");
        check(StateCodec.Encode(practiceRun) == beforePractice, "Practice does not change actions, NPCs, inventory, XP, wind, RNG or receipts");
        practiceRun.ManualPracticeUsed = true;
        var savedPractice = StateCodec.Decode(StateCodec.Encode(practiceRun), practiceRun.RunId, practiceRun.SlotId, catalog);
        check(savedPractice.ManualPracticeUsed, "One-time practice marker survives reload");
        reject(() => TimingRules.Practice(savedPractice, savedPractice.Night!.Npcs[0], catalog.Role(practiceNpc.RoleId), now), "Cannot repeat consumed practice");
        practiceRun.ManualPracticeUsed = false; TheftRules.SelectMode(practiceRun, TheftMode.Probability);
        reject(() => TimingRules.Practice(practiceRun, practiceNpc, catalog.Role(practiceNpc.RoleId), now), "Automatic mode never enters manual practice");

        var isolated = create(0, 12345); var isolatedNpc = isolated.Night!.Npcs[1]; var isolatedRole = catalog.Role(isolatedNpc.RoleId);
        var isolatedBefore = TheftRules.Preview(isolated, isolatedNpc, isolatedRole);
        isolated.Night.Attempts = 7; isolated.Night.Npcs[0].Alert = true; isolated.Night.Npcs[0].SuccessfulThefts = 2;
        check(TheftRules.Preview(isolated, isolatedNpc, isolatedRole) == isolatedBefore, "Other NPC attempts, alert and stolen items cannot change this NPC odds");

        var unified = create(0, 12345); var unifiedNpc = unified.Night!.Npcs[0];
        var unifiedRole = catalog.Role(unifiedNpc.RoleId);
        var probabilities = TheftRules.Preview(unified, unifiedNpc, unifiedRole);
        var unifiedZones = TimingRules.Preview(unified, unifiedNpc, unifiedRole);
        check(Math.Abs(unifiedZones.Success - probabilities.Success) < 1e-10 && Math.Abs(unifiedZones.Exposure - probabilities.Caught) < 1e-10,
            "Manual zones match automatic outcome probabilities");
        unifiedNpc.Alert = true;
        var alertZones = TimingRules.Preview(unified, unifiedNpc, unifiedRole);
        check(Math.Abs(alertZones.Success - unifiedZones.Success / 2) < 1e-10 && alertZones.Exposure > unifiedZones.Exposure && alertZones.Failure > unifiedZones.Failure,
            "Alert halves success and redistributes both other zones");
        check(unified.Night.Npcs.All(x => x.Items.Count == 5), "Every NPC starts with five items");

        RunState Manual(int level = 0, ulong seed = 12345)
        { var state = create(level, seed); TheftRules.SelectMode(state, TheftMode.Timing); return state; }
        TimingAttempt Begin(RunState state, bool infinite = false)
        { var npc = state.Night!.Npcs[0]; TimingRules.Begin(state, npc, catalog.Role(npc.RoleId), infinite, now); return state.Night.Timing!; }
        ActionReceipt Resolve(RunState state)
        { var npc = state.Night!.Npcs.Single(x => x.Key == state.Night.Timing!.Target); return TheftRules.Attempt(state, npc, catalog.Role(npc.RoleId), state.Night.Timing!.Infinite); }

        foreach (var (coefficient, expectedWidth) in new[] { (-1d, 0d), (0d, 0d), (.5, .075), (1d, .15), (2d, .30),
            (3d, .30), (double.NaN, .15), (double.PositiveInfinity, .15), (double.NegativeInfinity, .15) })
        {
            var state = Manual(); var npc = state.Night!.Npcs[0]; npc.Faction = "FACTION_LOWER_LEVEL";
            var role = catalog.Role(npc.RoleId); string before = StateCodec.Encode(state);
            var zones = TimingRules.Preview(state, npc, role, manualDifficultyCoefficient: coefficient);
            check(Math.Abs(zones.Success - expectedWidth) < 1e-10, "Manual coefficient scales final width and sanitizes config: " + coefficient);
            check(zones.Failure >= 0 && zones.Exposure >= 0 && Math.Abs(zones.Success + zones.Failure + zones.Exposure - 1) < 1e-10,
                "Scaled timing zones remain a valid partition: " + coefficient);
            check(Math.Abs(zones.Exposure / (1 - zones.Success) - TheftRules.Preview(state, npc, role).Exposure) < 1e-10,
                "Scaling retains the exposure share of non-success: " + coefficient);
            check(zones.At(.5) == (expectedWidth == 0 ? AttemptOutcome.HiddenFailure : AttemptOutcome.Success),
                "Zero coefficient removes even an exact center success: " + coefficient);
            var rehearsal = TimingRules.Practice(state, npc, role, now, manualDifficultyCoefficient: coefficient);
            check(rehearsal.Zones == zones && StateCodec.Encode(state) == before,
                "Practice matches scaled preview without gameplay effects: " + coefficient);
            TimingRules.Begin(state, npc, role, false, now, manualDifficultyCoefficient: coefficient);
            check(state.Night.Timing!.Zones == zones,
                "Real attempt freezes the same scaled zones: " + coefficient);
        }
        var minimum = Manual(); var minimumNpc = minimum.Night!.Npcs[0]; minimumNpc.Faction = "FACTION_UPPER_LEVEL"; minimumNpc.Alert = true;
        check(TimingRules.Preview(minimum, minimumNpc, catalog.Role(minimumNpc.RoleId), manualDifficultyCoefficient: .5).Success == .025,
            "Coefficient applies after the alert-adjusted 5% minimum");
        var maximum = Manual(10); var maximumNpc = maximum.Night!.Npcs[0]; maximumNpc.Faction = "FACTION_LOWER_LEVEL";
        check(TimingRules.Preview(maximum, maximumNpc, catalog.Role(maximumNpc.RoleId), manualDifficultyCoefficient: 2).Success == .60,
            "Coefficient 2 doubles the capped 30% baseline to 60%");

        var frozenRun = Manual(); var frozenNpc = frozenRun.Night!.Npcs.First(x => x.Faction == "FACTION_LOWER_LEVEL");
        frozenRun.Night.Npcs.Remove(frozenNpc); frozenRun.Night.Npcs.Insert(0, frozenNpc);
        TimingRules.Begin(frozenRun, frozenNpc, catalog.Role(frozenNpc.RoleId), false, now, manualDifficultyCoefficient: .5);
        var reloaded = StateCodec.Decode(StateCodec.Encode(frozenRun), frozenRun.RunId, frozenRun.SlotId, catalog);
        var reloadedNpc = reloaded.Night!.Npcs[0]; var reloadedRole = catalog.Role(reloadedNpc.RoleId);
        check(reloaded.Night.Timing!.SuccessWidth == .075 && TimingRules.Preview(reloaded, reloadedNpc, reloadedRole, manualDifficultyCoefficient: 2).At(.55) == AttemptOutcome.Success,
            "New config preview cannot alter an in-flight attempt after save/reload");
        TimingRules.Stop(reloaded, .55, now);
        check(Resolve(reloaded).Outcome == AttemptOutcome.HiddenFailure && reloaded.Night.Loot.Count == 0,
            "Settlement uses saved narrow zones even when the new coefficient would succeed");
        TimingRules.Begin(reloaded, reloadedNpc, reloadedRole, false, now, manualDifficultyCoefficient: 2);
        check(reloaded.Night.Timing!.SuccessWidth == .15, "Next attempt uses the new coefficient with the resulting alert penalty");

        var automaticZero = create(0, 12345); TheftRules.SelectMode(automaticZero, TheftMode.Probability);
        var automaticDouble = StateCodec.Clone(automaticZero);
        foreach (var (state, coefficient) in new[] { (automaticZero, 0d), (automaticDouble, 2d) })
        {
            var npc = state.Night!.Npcs[0];
            TimingRules.Begin(state, npc, catalog.Role(npc.RoleId), false, now, manualDifficultyCoefficient: coefficient);
        }
        check(StateCodec.Encode(automaticZero) == StateCodec.Encode(automaticDouble),
            "Manual coefficient leaves automatic zones, outcomes, RNG and gameplay state unchanged");

        foreach (double position in new[] { 0d, .1, .5, .9, 1d })
        foreach (double coefficient in new[] { 0d, .5, 1d, 2d })
        {
            var state = Manual(); state.Night!.Npcs[0].ManualSuccessPosition = position;
            var npc = state.Night!.Npcs[0]; var role = catalog.Role(npc.RoleId);
            var zones = TimingRules.Preview(state, npc, role, manualDifficultyCoefficient: coefficient);
            check(zones.SuccessStart >= zones.Exposure / 2 - 1e-10 && zones.SuccessEnd <= 1 - zones.Exposure / 2 + 1e-10,
                "Moved success zone stays between both exposure edges");
            int successes = 0, exposures = 0;
            for (int i = 0; i < 1000; i++)
            {
                var outcome = zones.At((i + .5) / 1000);
                if (outcome == AttemptOutcome.Success) successes++;
                if (outcome == AttemptOutcome.Caught) exposures++;
            }
            check(Math.Abs(successes / 1000d - zones.Success) <= .002 && Math.Abs(exposures / 1000d - zones.Exposure) <= .002,
                "Moving success does not change the measured success or exposure widths");
            if (zones.Success > 0)
                check(zones.At(zones.SuccessStart) == AttemptOutcome.Success && zones.At(zones.SuccessEnd) == AttemptOutcome.Success,
                    "Both shifted success boundaries favor the player");
        }
        var moving = Manual(10); var movingAttempt = Begin(moving);
        check(movingAttempt.SuccessPosition == .5 && movingAttempt.RandomizeSuccessZoneAfterSuccess,
            "Default setting starts centered and enables relocation after success");
        TimingRules.Stop(moving, .5, now);
        var relocationReplay = StateCodec.Clone(moving); Resolve(moving); Resolve(relocationReplay);
        check(Math.Abs(moving.Night!.Npcs[0].ManualSuccessPosition - .5) >= .25 && moving.Night.Npcs[0].ManualSuccessPosition is >= 0 and <= 1,
            "Successful manual theft moves the next success zone to a different position");
        check(StateCodec.Encode(moving) == StateCodec.Encode(relocationReplay), "Replaying the same saved success produces the same new position and loot");
        var savedMoving = StateCodec.Decode(StateCodec.Encode(moving), moving.RunId, moving.SlotId, catalog);
        string beforeMovingPreview = StateCodec.Encode(savedMoving);
        var movingNpc = savedMoving.Night!.Npcs[0]; var movingRole = catalog.Role(movingNpc.RoleId);
        var movedZones = TimingRules.Preview(savedMoving, movingNpc, movingRole);
        var movedPractice = TimingRules.Practice(savedMoving, movingNpc, movingRole, now);
        check(movingNpc.ManualSuccessPosition == moving.Night.Npcs[0].ManualSuccessPosition && movedPractice.Zones == movedZones,
            "Reload and practice retain the moved success zone");
        check(TimingRules.Preview(savedMoving, movingNpc, movingRole) == movedZones && StateCodec.Encode(savedMoving) == beforeMovingPreview,
            "Reopening preview and practice cannot reroll or consume the saved position");
        check(savedMoving.Night.Npcs.Skip(1).All(npc => npc.ManualSuccessPosition == .5 &&
            TimingRules.Preview(savedMoving, npc, catalog.Role(npc.RoleId)).SuccessPosition == .5),
            "All untouched NPCs still start centered after another target's successful theft");
        double firstTargetPosition = movingNpc.ManualSuccessPosition;
        var otherTarget = savedMoving.Night.Npcs[1];
        TimingRules.Begin(savedMoving, otherTarget, catalog.Role(otherTarget.RoleId), false, now);
        check(savedMoving.Night.Timing!.SuccessPosition == .5, "Switching to a new NPC begins with a centered success zone");
        TimingRules.Stop(savedMoving, .5, now); Resolve(savedMoving);
        double otherTargetPosition = otherTarget.ManualSuccessPosition;
        check(movingNpc.ManualSuccessPosition == firstTargetPosition && otherTargetPosition != .5 && savedMoving.Night.Npcs.Skip(2).All(npc => npc.ManualSuccessPosition == .5),
            "A second NPC's success changes only that target's position");
        savedMoving = StateCodec.Decode(StateCodec.Encode(savedMoving), savedMoving.RunId, savedMoving.SlotId, catalog);
        movingNpc = savedMoving.Night!.Npcs[0]; otherTarget = savedMoving.Night.Npcs[1];
        check(movingNpc.ManualSuccessPosition == firstTargetPosition && otherTarget.ManualSuccessPosition == otherTargetPosition,
            "Reload retains distinct positions for each NPC");
        TimingRules.Begin(savedMoving, movingNpc, catalog.Role(movingNpc.RoleId), false, now);
        var returnedZones = savedMoving.Night.Timing!.Zones;
        check(returnedZones.SuccessPosition == firstTargetPosition, "Returning to a previously stolen NPC resumes that NPC's own position");
        TimingRules.Stop(savedMoving, (returnedZones.SuccessStart + returnedZones.SuccessEnd) / 2, now); Resolve(savedMoving);
        check(movingNpc.ManualSuccessPosition != firstTargetPosition && otherTarget.ManualSuccessPosition == otherTargetPosition,
            "Repeated success against the first NPC leaves the second NPC's position unchanged");
        var freshTargets = Manual(10, 739);
        check(freshTargets.Night!.Npcs.All(npc => TimingRules.Preview(freshTargets, npc, catalog.Role(npc.RoleId)).SuccessPosition == .5),
            "Every newly generated NPC in every region starts centered");

        var disabled = Manual(10); disabled.Night!.Npcs[0].ManualSuccessPosition = .1;
        var disabledNpc = disabled.Night!.Npcs[0];
        TimingRules.Begin(disabled, disabledNpc, catalog.Role(disabledNpc.RoleId), false, now, randomizeSuccessZoneAfterSuccess: false);
        check(disabled.Night.Timing!.SuccessPosition == .5 && !disabled.Night.Timing.RandomizeSuccessZoneAfterSuccess,
            "Disabled option centers even a previously moved zone");
        TimingRules.Stop(disabled, .5, now); Resolve(disabled);
        check(disabledNpc.ManualSuccessPosition == .1 && TimingRules.Preview(disabled, disabledNpc, catalog.Role(disabledNpc.RoleId), randomizeSuccessZoneAfterSuccess: false).SuccessPosition == .5,
            "Success with relocation disabled keeps the centered behavior");

        var shifted = Manual(); shifted.Night!.Npcs[0].ManualSuccessPosition = .05; var shiftedAttempt = Begin(shifted);
        double shiftedStop = (shiftedAttempt.Zones.SuccessStart + shiftedAttempt.Zones.SuccessEnd) / 2;
        check(new TimingZones(shiftedAttempt.SuccessWidth, shiftedAttempt.ExposureWidth).At(shiftedStop) != AttemptOutcome.Success,
            "Shifted fixture hits green outside the former centered zone");
        shifted = StateCodec.Decode(StateCodec.Encode(shifted), shifted.RunId, shifted.SlotId, catalog);
        shifted.Night!.Npcs[0].ManualSuccessPosition = .95;
        var shiftedNpc = shifted.Night!.Npcs[0];
        check(TimingRules.Preview(shifted, shiftedNpc, catalog.Role(shiftedNpc.RoleId), randomizeSuccessZoneAfterSuccess: false).At(shiftedStop) != AttemptOutcome.Success,
            "New configuration and next-position changes cannot replace frozen geometry");
        TimingRules.Stop(shifted, shiftedStop, now);
        check(Resolve(shifted).Outcome == AttemptOutcome.Success && shifted.Night.Loot.Count == 1,
            "Saved shifted green awards the item using its frozen position");
        foreach (var outcome in new[] { AttemptOutcome.HiddenFailure, AttemptOutcome.Caught })
        {
            var state = Manual(); state.Night!.Npcs[0].ManualSuccessPosition = .2; var attempt = Begin(state);
            double stop = outcome == AttemptOutcome.Caught ? 0 : (attempt.Zones.Exposure / 2 + attempt.Zones.SuccessStart) / 2;
            TimingRules.Stop(state, stop, now);
            check(Resolve(state).Outcome == outcome && state.Night.Npcs[0].ManualSuccessPosition == .2, "Non-success never relocates the zone: " + outcome);
        }
        var autoOn = create(0, 12345); TheftRules.SelectMode(autoOn, TheftMode.Probability); autoOn.Night!.Npcs[0].ManualSuccessPosition = .1;
        var autoOff = StateCodec.Clone(autoOn);
        foreach (var (state, enabled) in new[] { (autoOn, true), (autoOff, false) })
        {
            var npc = state.Night!.Npcs[0];
            TimingRules.Begin(state, npc, catalog.Role(npc.RoleId), false, now, new DebugTuning { Success = 1, Exposure = 0 }, randomizeSuccessZoneAfterSuccess: enabled);
            TimingRules.Stop(state, 0, now + TimeSpan.TicksPerSecond); Resolve(state);
        }
        check(autoOn.Night!.Npcs[0].ManualSuccessPosition == .1 && StateCodec.Encode(autoOn) == StateCodec.Encode(autoOff),
            "Automatic success ignores relocation setting and preserves manual position and RNG");

        var legacyPosition = Manual(); Begin(legacyPosition);
        var legacyJson = System.Text.Json.Nodes.JsonNode.Parse(StateCodec.Encode(legacyPosition))!;
        legacyJson["ManualSuccessPosition"] = .1;
        foreach (var legacyNpc in legacyJson["Night"]!["Npcs"]!.AsArray())
            legacyNpc!.AsObject().Remove(nameof(NpcState.ManualSuccessPosition));
        var legacyTiming = legacyJson["Night"]!["Timing"]!.AsObject();
        legacyTiming.Remove(nameof(TimingAttempt.SuccessPosition)); legacyTiming.Remove(nameof(TimingAttempt.RandomizeSuccessZoneAfterSuccess));
        var restoredPosition = StateCodec.Decode(legacyJson.ToJsonString(), legacyPosition.RunId, legacyPosition.SlotId, catalog);
        check(restoredPosition.Night!.Npcs.All(npc => npc.ManualSuccessPosition == .5) && restoredPosition.Night.Timing!.SuccessPosition == .5 && !restoredPosition.Night.Timing.RandomizeSuccessZoneAfterSuccess,
            "Existing shared position is not inherited by NPCs; absent target and attempt fields remain centered");
        foreach (double invalidPosition in new[] { -.01, 1.01 })
        {
            var state = Manual(); state.Night!.Npcs[1].ManualSuccessPosition = invalidPosition;
            reject(() => StateCodec.Validate(state, catalog), "Reject invalid saved manual position");
            state.Night.Npcs[1].ManualSuccessPosition = .5; Begin(state); state.Night.Timing!.SuccessPosition = invalidPosition;
            reject(() => StateCodec.Validate(state, catalog), "Reject invalid frozen success position");
        }

        foreach (var sample in new[] { ("FACTION_LOWER_LEVEL", .75, .85, .95), ("FACTION_MIDDLE", .65, .775, .90), ("FACTION_UPPER_LEVEL", .55, .70, .85) })
        foreach (var point in new[] { (0, sample.Item2), (5, sample.Item3), (10, sample.Item4) })
        {
            var curve = create(point.Item1, 12345); TheftRules.SelectMode(curve, TheftMode.Probability);
            var target = curve.Night!.Npcs[0]; target.Faction = sample.Item1;
            check(Math.Abs(TheftRules.Preview(curve, target, catalog.Role(target.RoleId)).Success - point.Item2) < 1e-10,
                "Approved faction success at level " + point.Item1 + ": " + sample.Item1);
        }
        foreach (int level in Enumerable.Range(0, 11))
        {
            var legacy = create(level, 12345); legacy.Schema = 1;
            foreach (var npc in legacy.Night!.Npcs) npc.Items.RemoveRange(3, 2);
            legacy.Experience = 10 * level * level + 40 * level + (level < 10 ? 5 : 0);
            var migrated = StateCodec.Decode(StateCodec.Encode(legacy), legacy.RunId, legacy.SlotId, catalog);
            check(migrated.Schema == 5 && TheftRules.Level(migrated.Experience) == level, "Legacy migration preserves level " + level);
            check(StateCodec.Encode(StateCodec.Decode(StateCodec.Encode(migrated), migrated.RunId, migrated.SlotId, catalog)) == StateCodec.Encode(migrated), "Experience migration only runs once");
            foreach (int progress in new[] { 0, 3, 8 })
            foreach (var outcome in Enum.GetValues<AttemptOutcome>())
            {
                var state = Manual(level);
                state.Experience = Math.Min(TheftRules.Threshold(10), state.Experience + progress);
                int before = state.Experience;
                var pendingXp = Begin(state); pendingXp.MissExposure = 0;
                var zonesXp = pendingXp.Zones;
                double stopXp = outcome == AttemptOutcome.Success ? .5 : outcome == AttemptOutcome.Caught ? 0 : (zonesXp.Exposure / 2 + zonesXp.SuccessStart) / 2;
                TimingRules.Stop(state, stopXp, now);
                var receiptXp = Resolve(state);
                int expected = Math.Clamp(before + (outcome == AttemptOutcome.Success ? (state.Night!.Loot.Last().Rare ? 10 : 5) : outcome == AttemptOutcome.HiddenFailure ? -2 : -5), TheftRules.Threshold(level), 1600);
                check(state.Experience == expected && receiptXp.ExperienceAward == expected - before && TheftRules.Level(state.Experience) >= level,
                    "Actual XP delta, level floor and max cap: " + level + "/" + progress + "/" + outcome);
                StateCodec.Validate(state, catalog);
            }
        }
        var tuning = new DebugTuning { Success = 1, Exposure = 0 };
        var tuned = create(0, 456); var tunedNpc = tuned.Night!.Npcs[0]; var tunedRole = catalog.Role(tunedNpc.RoleId);
        var effective = TimingRules.Preview(tuned, tunedNpc, tunedRole, tuning);
        check(effective.Success == 1 && effective.Failure == 0 && effective.Exposure == 0, "Debug uses the same probability and zone formula");
        TimingRules.Begin(tuned, tunedNpc, tunedRole, false, now, tuning);
        tuning.Success = 0;
        check(tuned.Night.Timing!.AutomaticOutcome == AttemptOutcome.Success && tuned.Night.Timing.AutomaticSuccess == 1, "Automatic attempt freezes overridden probability");
        StateCodec.Validate(tuned, catalog);
        tuning.Success = .5; tuning.Exposure = .5;
        var tunedManual = Manual(); var tunedManualNpc = tunedManual.Night!.Npcs[0];
        TimingRules.Begin(tunedManual, tunedManualNpc, catalog.Role(tunedManualNpc.RoleId), false, now, tuning);
        var frozen = tunedManual.Night.Timing!; var frozenZones = frozen.Zones;
        TimingRules.Stop(tunedManual, (frozenZones.Exposure / 2 + frozenZones.SuccessStart) / 2, now);
        check(Resolve(tunedManual).Outcome == AttemptOutcome.HiddenFailure, "Manual miss is deterministic with unified zones");
        foreach (var mode in Enum.GetValues<TheftMode>())
        foreach (bool infinite in new[] { false, true })
        {
            var continued = create(5, 789); TheftRules.SelectMode(continued, mode);
            var target = continued.Night!.Npcs[0]; var role = catalog.Role(target.RoleId);
            var previousLoot = continued.Night.Npcs[1].Items[0];
            continued.Night.Npcs[1].Items.Remove(previousLoot); continued.Night.Loot.Add(previousLoot);
            continued.Experience += 5;
            TimingRules.Begin(continued, target, role, infinite, now, new DebugTuning { Success = 0, Exposure = 1 });
            TimingRules.Stop(continued, 0, now + 10 * TimeSpan.TicksPerSecond);
            continued = StateCodec.Decode(StateCodec.Encode(continued), continued.RunId, continued.SlotId, catalog);
            var receipt = Resolve(continued);
            check(receipt.Outcome == AttemptOutcome.Caught && !continued.Night!.Ended && !continued.Night.Caught && continued.Night.Timing == null,
                "Debug exposure keeps the outing open after stopped-state readback: " + mode + "/" + infinite);
            check(continued.Night!.Attempts == 1 && continued.Night.SpentActions == (infinite ? 0 : 1) && receipt.ExperienceAward == (infinite ? 0 : -5),
                "Continuing debug exposure preserves the existing action and XP rules");
            check(continued.Night.Loot.Count == 0 && continued.Night.Abandoned.Single().UniqueId == previousLoot.UniqueId && receipt.TargetReputationPenalty == 1 && continued.Night.PendingWind == 10,
                "Continuing debug exposure still settles loot loss, reputation penalty and rumour");
            StateCodec.Validate(continued, catalog);
            target = continued.Night.Npcs[0];
            TheftRules.SelectMode(continued, TheftMode.Timing);
            TimingRules.Begin(continued, target, role, false, now + 20 * TimeSpan.TicksPerSecond);
            TimingRules.Stop(continued, 0, now + 30 * TimeSpan.TicksPerSecond);
            check(Resolve(continued).Outcome == AttemptOutcome.Caught && continued.Night.Ended && continued.Night.Caught,
                "A following ordinary exposure still ends the outing");
            StateCodec.Validate(continued, catalog);
        }
        var ordinaryPreview = TheftRules.Preview(create(0, 456), tunedNpc, tunedRole);
        check(ordinaryPreview.Success == TheftRules.FactionSuccess(tunedNpc.Faction).Automatic, "No override retains normal probability");
        var old = create(2, 12345);
        var oldJson = StateCodec.Encode(old).Replace("\"Mode\":0,", "").Replace("\"PreferredMode\":0,", "");
        check(StateCodec.Decode(oldJson, old.RunId, old.SlotId, catalog).Night!.Mode == TheftMode.Timing, "Missing preference defaults to manual");
        foreach (int level in new[] { 0, 2, 5, 7, 10 })
        {
            var state = Manual(level); var npc = state.Night!.Npcs[0];
            npc.Faction = "FACTION_LOWER_LEVEL";
            var neutral = new RoleDefinition { Success = .70, Tier = 1 };
            var zones = TimingRules.Preview(state, npc, neutral);
            check(Math.Abs(zones.Success - (.15 + .015 * level)) < 1e-10, "Level expands timing success width");
            check(zones.At(.5) == AttemptOutcome.Success && zones.At(0) == AttemptOutcome.Caught && zones.At(1) == AttemptOutcome.Caught, "Center succeeds, both extremes expose");
            check(zones.At(zones.SuccessStart) == AttemptOutcome.Success && zones.At(zones.SuccessEnd) == AttemptOutcome.Success, "Exact success boundaries favor player");
            check(zones.At(zones.SuccessStart - 1e-8) == AttemptOutcome.HiddenFailure && zones.At(zones.SuccessEnd + 1e-8) == AttemptOutcome.HiddenFailure, "Near miss on either side");
            check(zones.At(zones.Exposure / 2 + 1e-8) == AttemptOutcome.HiddenFailure && zones.At(zones.Exposure / 2 - 1e-8) == AttemptOutcome.Caught, "Exposure border uses deterministic miss");
            state.Habitual = true; var habitual = TimingRules.Preview(state, npc, neutral);
            check(Math.Abs(habitual.Exposure - zones.Exposure / 2) < 1e-10, "Habitual halves the actual exposure probability and red width");
            npc.Alert = true; var alert = TimingRules.Preview(state, npc, neutral);
            check(Math.Abs(alert.Success - Math.Max(.05, zones.Success * .5)) < 1e-10 && alert.Exposure > habitual.Exposure, "Alert shrinks success and expands exposure");
        }
        foreach (var role in catalog.Enabled)
        {
            var state = Manual(0); var npc = state.Night!.Npcs[0]; state.Night.WindPenalty = .20; state.Night.Attempts = 30; npc.Attempts = 30; npc.Alert = true;
            var low = TimingRules.Preview(state, npc, role); state.Experience = TheftRules.Threshold(10); var high = TimingRules.Preview(state, npc, role);
            check(low.Success > 0 && low.Failure >= 0 && low.Exposure > 0 && Math.Abs(low.Success + low.Failure + low.Exposure - 1) < 1e-10, "Valid zones under worst wind, alert and attempts: " + role.Id);
            check(high.Success >= low.Success && high.Exposure <= low.Exposure, "Higher level retains advantage: " + role.Id);
        }
        foreach (var outcome in Enum.GetValues<AttemptOutcome>())
        {
            var state = Manual(); var begin = Begin(state); var zones = begin.Zones;
            begin.MissExposure = 0; // Isolate deterministic zone outcomes; risk rolls are exercised below.
            int xp = state.Experience; ulong rng = state.Night!.RandomState;
            check(state.Night.Attempts == 1 && state.Night.SpentActions == 1 && state.Night.Started && state.Night.LockedRegion == 0 && state.Experience == xp, "Begin commits action and region without premature XP");
            reject(() => TheftRules.SelectMode(state, TheftMode.Probability), "No mid-attempt mode switch");
            reject(() => Begin(state), "No duplicate begin");
            reject(() => Resolve(state), "No resolution before stop");
            reject(() => TheftRules.Settle(state, 1), "Cannot settle pending attempt");
            var reload = StateCodec.Decode(StateCodec.Encode(state), state.RunId, state.SlotId, catalog);
            check(reload.Night!.Timing!.StartedUtcTicks == now && reload.Night.Timing.Target == begin.Target, "Reload retains clock and target");
            double position = outcome switch { AttemptOutcome.Success => .5, AttemptOutcome.Caught => 0, _ => (zones.Exposure / 2 + zones.SuccessStart) / 2 };
            TimingRules.Stop(reload, position, now + TimeSpan.TicksPerSecond);
            reject(() => TimingRules.Stop(reload, .5, now), "Cannot replace a latched stop");
            var stopped = StateCodec.Decode(StateCodec.Encode(reload), reload.RunId, reload.SlotId, catalog);
            var receipt = Resolve(stopped);
            check(receipt.Outcome == outcome && receipt.Mode == TheftMode.Timing && receipt.StopPosition == position, "Stop and reload preserve exact outcome " + outcome);
            check(stopped.Night!.SpentActions == 1 && stopped.Night.Attempts == 1 && stopped.Night.Timing == null, "Resolve never consumes twice");
            check(stopped.Experience == xp + (outcome == AttemptOutcome.Success ? (stopped.Night!.Loot.Last().Rare ? 10 : 5) : 0), "Outcome XP is awarded once");
            if (outcome == AttemptOutcome.Caught) check(stopped.Night.RandomState == rng, "Red zone consumes no random draw");
            if (outcome == AttemptOutcome.Success) check(stopped.Night.Loot.Count == 1 && stopped.Night.Npcs[0].Items.Count == 4, "Success transfers one existing item");
            if (outcome == AttemptOutcome.HiddenFailure) check(stopped.Night.Npcs[0].Alert && !stopped.Night.Ended, "Miss marks target alert");
            if (outcome == AttemptOutcome.Caught) check(stopped.Night.Caught && stopped.Night.Ended && receipt.TargetReputationPenalty == 1, "Exposure ends outing");
            reject(() => Resolve(stopped), "Repeated resolution rejected");
            if (stopped.Night!.Ended) reject(() => TheftRules.SelectMode(stopped, TheftMode.Probability), "Ended outing cannot switch");
            else check(TheftRules.SelectMode(stopped, TheftMode.Probability) && stopped.Night.SpentActions == 1 && stopped.Night.LockedRegion == 0, "Between-attempt switch preserves action and region");
            StateCodec.Validate(stopped, catalog);
        }
        var timeout = Manual(); Begin(timeout); TimingRules.Stop(timeout, .5, now + (long)(TimingRules.Duration * TimeSpan.TicksPerSecond));
        check(Resolve(timeout).Outcome == AttemptOutcome.Caught, "Deadline overrides a late center click");
        var noRedTimeout = Manual(); var noRedNpc = noRedTimeout.Night!.Npcs[0];
        TimingRules.Begin(noRedTimeout, noRedNpc, catalog.Role(noRedNpc.RoleId), false, now, new DebugTuning { Success = 1, Exposure = 0 });
        TimingRules.Stop(noRedTimeout, .5, now + 7 * TimeSpan.TicksPerSecond);
        noRedTimeout = StateCodec.Decode(StateCodec.Encode(noRedTimeout), noRedTimeout.RunId, noRedTimeout.SlotId, catalog);
        check(Resolve(noRedTimeout).Outcome == AttemptOutcome.Caught, "Timeout still exposes when debug removes the red band, including reload");
        var debug = Manual(5); Begin(debug, true); TimingRules.Stop(debug, .5, now); var debugReceipt = Resolve(debug);
        check(debugReceipt.ExperienceAward == 0 && debug.Night!.SpentActions == 0 && debug.Night.Attempts == 1, "Infinite timing still tracks attempt, no XP or normal consumption");
        var upgrade = Manual(2); upgrade.Experience = TheftRules.Threshold(3) - 5; upgrade.Night!.SpentActions = 2; upgrade.Night.Attempts = 2;
        var pending = Begin(upgrade); pending.MissExposure = 0; var band = pending.Zones;
        TimingRules.Stop(upgrade, .5, now); Resolve(upgrade);
        check(TheftRules.Level(upgrade.Experience) == 3 && TheftRules.Remaining(upgrade) == 1, "Last-action timing upgrade adds only newly earned allowance");
        var switchable = Manual(); TheftRules.SelectMode(switchable, TheftMode.Probability); TheftRules.SelectMode(switchable, TheftMode.Timing);
        check(switchable.Night!.Attempts == 0 && switchable.PreferredMode == TheftMode.Timing, "Pre-action switching does not consume attempts");
        var nextNight = GenerationRules.Night(switchable, 2, catalog, (id, tier, random) => new ItemState { Id = id }, (role, random) => "portrait");
        check(nextNight.Mode == TheftMode.Timing, "Next night inherits preference");
        var seenAutomatic = new HashSet<AttemptOutcome>();
        var seenMiss = new HashSet<AttemptOutcome>();
        for (ulong seed = 1; seed <= 200; seed++)
        {
            var automatic = create(0, seed); var auto = Begin(automatic);
            int spent = automatic.Night!.SpentActions;
            reject(() => TimingRules.Stop(automatic, .5, now), "Automatic cannot be stopped early");
            reject(() => TheftRules.SelectMode(automatic, TheftMode.Timing), "Cannot switch a pending automatic attempt");
            var restored = StateCodec.Decode(StateCodec.Encode(automatic), automatic.RunId, automatic.SlotId, catalog);
            var replay = StateCodec.Clone(restored);
            // Even well after the manual timeout, auto retains its committed result.
            TimingRules.Stop(restored, 0, now + 10 * TimeSpan.TicksPerSecond);
            TimingRules.Stop(replay, 1, now + 20 * TimeSpan.TicksPerSecond);
            var first = Resolve(restored); var second = Resolve(replay);
            seenAutomatic.Add(first.Outcome);
            check(first.Outcome == auto.AutomaticOutcome && first.Outcome == second.Outcome && first.ItemUniqueId == second.ItemUniqueId && first.StopPosition == auto.AutomaticPosition,
                "Automatic reload preserves result and ignores supplied stop position");
            check(restored.Night!.SpentActions == spent && restored.Night.Attempts == 1 && restored.Night.Timing == null, "Automatic spends exactly one action");
            StateCodec.Validate(restored, catalog);
            var manual = Manual(0, seed); var attempt = Begin(manual);
            var zones = attempt.Zones;
            TimingRules.Stop(manual, (zones.Exposure / 2 + zones.SuccessStart) / 2, now);
            var saved = StateCodec.Decode(StateCodec.Encode(manual), manual.RunId, manual.SlotId, catalog);
            var miss = Resolve(manual); var repeated = Resolve(saved);
            seenMiss.Add(miss.Outcome);
            check(miss.Outcome == repeated.Outcome && manual.Night!.RandomState == saved.Night!.RandomState, "Miss exposure roll survives save and retry deterministically");
        }
        check(seenAutomatic.Count == 3, "Automatic animation supports all three outcomes");
        check(seenMiss.SetEquals(new[] { AttemptOutcome.HiddenFailure }), "Amber zone always means hidden failure");
        check(new RunState().PreferredMode == TheftMode.Timing && new NightState().Mode == TheftMode.Timing, "New runs and nights default to unchecked auto");
        var independent = Manual(5); var source = independent.Night!.Npcs[0]; var other = independent.Night.Npcs[1];
        var otherRole = catalog.Role(other.RoleId); var oddsBefore = TheftRules.Preview(independent, other, otherRole);
        Begin(independent); TimingRules.Stop(independent, .5, now); Resolve(independent);
        check(TheftRules.Preview(independent, other, otherRole) == oddsBefore && source.Attempts == 1 && other.Attempts == 0,
            "Actual successful theft leaves other NPC risk untouched");
        var persistedIndependent = StateCodec.Decode(StateCodec.Encode(independent), independent.RunId, independent.SlotId, catalog);
        check(persistedIndependent.Night!.Npcs[0].Attempts == 1 && persistedIndependent.Night.Npcs[1].Attempts == 0, "Per-target attempts survive reload");
        foreach (var mode in Enum.GetValues<TheftMode>())
        {
            var repeated = create(5, 12345); TheftRules.SelectMode(repeated, mode);
            var target = repeated.Night!.Npcs[0]; var role = catalog.Role(target.RoleId);
            var original = TheftRules.Preview(repeated, target, role).Success;
            target.SuccessfulThefts = 1;
            check(Math.Abs(TheftRules.Preview(repeated, target, role).Success - original * .85) < 1e-10, "First stolen item reduces own success by 15%: " + mode);
            target.SuccessfulThefts = 2;
            check(Math.Abs(TheftRules.Preview(repeated, target, role).Success - original * .85 * .85) < 1e-10, "Second stolen item applies the same relative reduction: " + mode);
        }
        foreach (int level in Enumerable.Range(0, 11))
        foreach (int region in Enumerable.Range(0, 3))
        foreach (var role in catalog.Enabled)
        foreach (bool alert in new[] { false, true })
        foreach (int stolen in new[] { 0, 1, 2 })
        {
            var difficulty = Manual(level); var npc = difficulty.Night!.Npcs[0]; npc.Region = region; npc.Alert = alert; npc.SuccessfulThefts = stolen;
            var zones = TimingRules.Preview(difficulty, npc, role);
            check(zones.Success >= .05 && zones.Success <= .30 && zones.Failure >= 0 && zones.Exposure >= 0, "Manual success stays within 5%-30% across difficulties");
        }
        var benchmarks = Manual(); var benchmarkNpc = benchmarks.Night!.Npcs[0]; benchmarkNpc.Faction = "FACTION_LOWER_LEVEL";
        var ordinaryRole = new RoleDefinition { Tier = 1, Success = .70 };
        check(TimingRules.Preview(benchmarks, benchmarkNpc, ordinaryRole).Success == .15, "L0 lower ordinary NPC is 15%");
        benchmarkNpc.Region = 2; benchmarkNpc.Faction = "FACTION_UPPER_LEVEL";
        check(Math.Abs(TimingRules.Preview(benchmarks, benchmarkNpc, new RoleDefinition { Tier = 3, Success = .70 }).Success - .05) < 1e-10, "L0 upper high-tier NPC is 5%");
        benchmarks.Experience = TheftRules.Threshold(10); benchmarkNpc.Region = 0; benchmarkNpc.Faction = "FACTION_LOWER_LEVEL";
        check(TimingRules.Preview(benchmarks, benchmarkNpc, ordinaryRole).Success == .30, "L10 lower ordinary NPC reaches 30%");
        var capped = Manual(10); var cappedNpc = capped.Night!.Npcs[0];
        for (int i = 0; i < 3; i++)
        {
            var attempt = Begin(capped); TimingRules.Stop(capped, (attempt.Zones.SuccessStart + attempt.Zones.SuccessEnd) / 2, now); Resolve(capped);
            capped = StateCodec.Decode(StateCodec.Encode(capped), capped.RunId, capped.SlotId, catalog);
        }
        cappedNpc = capped.Night!.Npcs[0];
        check(cappedNpc.Items.Count == 2 && cappedNpc.SuccessfulThefts == 3 && TheftRules.Remaining(capped) == 6, "Target cap is independent of remaining items and nightly actions");
        reject(() => Begin(capped), "Fourth successful theft blocked after reload");
        TheftRules.SelectMode(capped, TheftMode.Probability);
        reject(() => TheftRules.Attempt(capped, cappedNpc, catalog.Role(cappedNpc.RoleId), false), "Direct automatic path also enforces target limit");
        var missed = Manual(); var beforeMiss = Begin(missed);
        var missZones = beforeMiss.Zones;
        TimingRules.Stop(missed, (missZones.Exposure / 2 + missZones.SuccessStart) / 2, now); Resolve(missed);
        check(missed.Night!.Npcs[0].SuccessfulThefts == 0, "Miss does not consume successful-theft quota");
        var afterMiss = Begin(missed);
        check(afterMiss.SuccessWidth == Math.Max(.05, beforeMiss.SuccessWidth / 2) && afterMiss.ExposureWidth > beforeMiss.ExposureWidth, "Next attempt snapshots alert-adjusted zones");
        var overrideAlert = Manual(); var overrideNpc = overrideAlert.Night!.Npcs[0]; overrideNpc.Alert = true;
        check(TimingRules.Preview(overrideAlert, overrideNpc, catalog.Role(overrideNpc.RoleId), new DebugTuning { Success = .3 }).Success == .15, "Debug cannot mask alert multiplier");
        var regionRun = Manual(); var regionNpc = regionRun.Night!.Npcs[0]; var regionRole = catalog.Role(regionNpc.RoleId);
        double previousSuccess = TimingRules.Preview(regionRun, regionNpc, regionRole).Success, previousExposure = -1;
        for (int region = 0; region < 3; region++)
        {
            regionNpc.Region = region;
            var r = TimingRules.Preview(regionRun, regionNpc, regionRole);
            check(r.Success == previousSuccess && r.Exposure > previousExposure, "Area changes exposure but never success " + region);
            previousSuccess = r.Success; previousExposure = r.Exposure;
        }
        var legacyInventory = create(0, 123); legacyInventory.Schema = 2;
        foreach (var npc in legacyInventory.Night!.Npcs) npc.Items.RemoveRange(3, 2);
        legacyInventory.Night.Npcs[0].Items.RemoveAt(0);
        var upgradedInventory = StateCodec.Decode(StateCodec.Encode(legacyInventory), legacyInventory.RunId, legacyInventory.SlotId, catalog);
        check(upgradedInventory.Night!.Npcs[0].Items.Count == 2 && upgradedInventory.Night.Npcs[0].SuccessfulThefts == 1, "Legacy inventory stays fixed and consumed quota migrates");
        foreach (int level in Enumerable.Range(0, 11))
            check(TheftRules.Actions(level) == new[] { 3,3,3,4,4,5,5,6,6,7,9 }[level], "Current action budget: " + level);
        var nine = Manual(10);
        for (int i = 0; i < 9; i++)
        {
            var npc = nine.Night!.Npcs[i % 3];
            TimingRules.Begin(nine, npc, catalog.Role(npc.RoleId), false, now);
            var zones = nine.Night.Timing!.Zones;
            TimingRules.Stop(nine, (zones.SuccessStart + zones.SuccessEnd) / 2, now); Resolve(nine);
            if (i == 7)
            {
                nine = StateCodec.Decode(StateCodec.Encode(nine), nine.RunId, nine.SlotId, catalog);
                check(TheftRules.Remaining(nine) == 1, "Reload after eight attempts retains the ninth action");
            }
        }
        check(nine.Night!.Loot.Count == 9 && nine.Night.Npcs.Take(3).All(x => x.SuccessfulThefts == 3) && TheftRules.Remaining(nine) == 0,
            "Max level can successfully steal three times from each of three targets");
        nine.Night.Npcs[0].SuccessfulThefts = 0;
        reject(() => Begin(nine), "Tenth normal action is rejected independently of target quota");
        var levelUp = Manual(9); levelUp.Experience = TheftRules.Threshold(10) - 5;
        levelUp.Night!.SpentActions = levelUp.Night.Attempts = 6;
        Begin(levelUp); TimingRules.Stop(levelUp, .5, now); Resolve(levelUp);
        check(TheftRules.Level(levelUp.Experience) == 10 && TheftRules.Remaining(levelUp) == 2, "Reaching max level on seventh action grants two additional actions");
        foreach (double target in new[] { .001, .25, .5, .75, .999 })
        {
            var animation = new TimingAttempt { AutomaticPosition = target };
            check(TimingRules.Position(animation, 0) == 0 && TimingRules.Position(animation, 1) == target && TimingRules.Position(animation, 5) == target,
                "Automatic pointer starts at zero and reaches its exact saved target at one second");
            double firstEdgeTime = 1 / (2 + target);
            check(Math.Abs(TimingRules.Position(animation, firstEdgeTime) - 1) < 1e-10, "Accelerated pointer reaches the far edge before its round trip");
        }
        var quickAuto = create(0, 654); Begin(quickAuto);
        reject(() => TimingRules.Stop(quickAuto, 0, now + TimeSpan.TicksPerSecond - 1), "Auto cannot resolve before one second");
        quickAuto = StateCodec.Decode(StateCodec.Encode(quickAuto), quickAuto.RunId, quickAuto.SlotId, catalog);
        var savedOutcome = quickAuto.Night!.Timing!.AutomaticOutcome;
        TimingRules.Stop(quickAuto, 0, now + TimeSpan.TicksPerSecond);
        check(Resolve(quickAuto).Outcome == savedOutcome, "Auto resolves at exactly one second with its saved outcome after reload");
        foreach (var mode in Enum.GetValues<TheftMode>())
        foreach (var faction in new[] { "FACTION_LOWER_LEVEL", "FACTION_MIDDLE", "FACTION_TOURIST", "FACTION_UPPER_LEVEL" })
        {
            var factionRun = create(5, 12345); TheftRules.SelectMode(factionRun, mode);
            var npc = factionRun.Night!.Npcs[0]; npc.Faction = faction;
            double expected = TheftRules.Preview(factionRun, npc, catalog.Role(npc.RoleId)).Success;
            foreach (int region in Enumerable.Range(0, 3))
            foreach (var role in catalog.Enabled.Where(x => x.Faction == faction))
            {
                npc.Region = region;
                check(TheftRules.Preview(factionRun, npc, role).Success == expected, "Faction success ignores area and identity tier: " + mode + "/" + faction);
            }
            npc.Alert = true; npc.SuccessfulThefts = 2;
            double penalized = expected * .5 * .85 * .85;
            if (mode == TheftMode.Timing) penalized = Math.Clamp(penalized, .05, .30);
            check(Math.Abs(TheftRules.Preview(factionRun, npc, catalog.Role(npc.RoleId)).Success - penalized) < 1e-10, "Faction base retains alert and repeated-success penalties");
        }
        var corrupt = Manual(); Begin(corrupt); corrupt.Night!.Timing!.StopPosition = double.NaN;
        reject(() => StateCodec.Validate(corrupt, catalog), "Reject non-finite stop");
        reject(() => new TimingZones(.3, .2).At(-.1), "Reject out-of-range stop");
        check(TimingRules.Position(0) == 0 && TimingRules.Position(TimingRules.TraverseSeconds) == 1 && TimingRules.Position(2 * TimingRules.TraverseSeconds) == 0, "Pointer travels at fixed speed and reverses");
    }
}
