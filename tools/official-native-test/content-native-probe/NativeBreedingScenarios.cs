using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Nicokobo.ContentNativeProbe;

internal sealed partial class ProbeContext
{
    private const string BreedingDataKey = "nicokobo.content_native_probe:breeding-progress";
    private const string BreedingGoal = "rat_800g";
    private const int BreedingCageCount = 10, BreedingParentCages = 8;
    private const int BreedingNightsPerProcess = 384, BreedingTotalNightLimit = 4096;
    private const float BreedingGrowOutThreshold = 1.6612f;
    private JObject _breeding = new();
    private readonly List<GameItem> _breedingCages = new(), _breedingBays = new();
    private GameItem? _breedingSupplies, _breedingMeatBag, _breedingBlade;
    private float _breedingDeadline;
    private bool _breedingCheckpointUnsafe;
    private string? _breedingLedgerPath;
    private int _breedingLedgerRows;
    private JObject BreedingLineage => (JObject)_breeding["lineage"]!;
    private JArray BreedingJournal => (JArray)_breeding["dailyLedger"]!;
    private JArray BreedingActions => (JArray)_breeding["actions"]!;
    private JArray BreedingFixtures => (JArray)_breeding["fixtures"]!;

    internal IEnumerable<object?> NativeBreeding()
    {
        BreedingInitialize();
        BreedingRequire("fresh-rancher-six-native-animals", "Actual D1 RANCHER, normal difficulty, no perks, no modded start and exactly the native six animals", new
        {
            day = StoreStation.GetDayCounter(), startType = Store.startType.ToString(), Store.startScenarioId,
            perks = NewGameData.Instance.startingPerks.Select(perk => perk.id).ToArray(),
            rats = BreedingRats().Select(BreedingRat).ToArray()
        }, !O.Reload && StoreStation.GetDayCounter() == 1 && Store.startType == NewGameData.StartType.RANCHER &&
            string.IsNullOrEmpty(Store.startScenarioId) && NewGameData.Instance.startingPerks.Count == 0 &&
            BreedingRats().Count() == 6 && new[] { 1, 3, 5 }.All(age => new[] { "MALE", "FEMALE" }.All(sex =>
                BreedingRats().Count(rat => BreedingAge(rat) == age && BreedingSex(rat) == sex && AnimalSpecies.Rat.geneticTraitIDs.All(gene =>
                    rat.GetTagReadonly(gene + "_ALLELE_A").GetFloat() == AnimalSpecies.Rat.geneticBaseValues[gene] &&
                    rat.GetTagReadonly(gene + "_ALLELE_B").GetFloat() == AnimalSpecies.Rat.geneticBaseValues[gene])) == 1)));
        foreach (var rat in BreedingRats())
            BreedingLineage[rat.uniqueId.ToString()] = new JObject
            {
                ["uid"] = rat.uniqueId, ["generation"] = 0, ["motherUid"] = 0, ["fatherUid"] = 0,
                ["source"] = "Actual NewGameData RANCHER initialization, PreBuiltItemHelper.CreateRat(defaultStats:true); explicit seed ages/weights, not an earned growth proof",
                ["firstObservedDay"] = 1, ["entry"] = BreedingRat(rat), ["genes"] = BreedingGenes(rat)
            };
        _breeding["seedUids"] = new JArray(BreedingRats().Select(rat => rat.uniqueId).OrderBy(uid => uid));
        foreach (object? wait in BreedingAttempt(() => BreedingPrepareAndRun())) yield return wait;
    }

    internal IEnumerable<object?> NativeBreedingReload(JObject baseline)
    {
        BreedingInitialize();
        var evidence = baseline["persistence"]?["nativeBreeding"] as JObject ?? throw new InvalidOperationException("Native breeding ES3 checkpoint evidence is missing");
        var expected = evidence["checkpoint"] as JObject ?? throw new InvalidOperationException("Native breeding checkpoint body is missing");
        string json = Store.ModData().TryGetValue(BreedingDataKey, out string persisted) ? persisted : "";
        BreedingRequire("reload-binding", "A different process, identical run/slot/goal/checkpoint actually loaded from ES3, and a resumable baseline", new
        {
            baselineProcess = (string?)evidence["sourceProcessToken"], thisProcess = R.processToken,
            run = Store.runID, slot = Store.saveSlotId, day = StoreStation.GetDayCounter(), persisted
        }, (int?)evidence["schema"] == 1 && (bool?)evidence["resumeAllowed"] == true && (string?)evidence["sourceRunId"] == (string?)baseline["runId"] &&
            (string?)evidence["sourceRunId"] != O.RunId && !string.IsNullOrEmpty((string?)evidence["sourceProcessToken"]) &&
            (string?)evidence["goal"] == BreedingGoal && (string?)evidence["gameRunId"] == Store.runID && (int?)evidence["slotId"] == Store.saveSlotId &&
            (string?)evidence["sourceProcessToken"] != R.processToken && (string?)baseline["processToken"] == (string?)evidence["sourceProcessToken"] &&
            (string?)expected["gameRunId"] == Store.runID && (int?)expected["slotId"] == Store.saveSlotId && (string?)expected["goal"] == BreedingGoal &&
            (int?)expected["day"] == StoreStation.GetDayCounter() && (int?)expected["cash"] == Store.playerCash && json.Length > 0 &&
            (string?)expected["lastCheckpointRunId"] == (string?)evidence["sourceRunId"] &&
            (string?)expected["lastCheckpointProcessToken"] == (string?)evidence["sourceProcessToken"] &&
            (string?)evidence["checkpointJson"] == json && JToken.DeepEquals(JObject.Parse(json), expected));
        _breeding = (JObject)expected.DeepClone();
        R.environment["nativeBreedingProgress"] = _breeding;
        BreedingBindFixtures();
        foreach (var ledger in ((JArray)_breeding["ledgerFiles"]!).OfType<JObject>())
        {
            string path = (string)ledger["path"]!;
            BreedingRequire("reload-native-daily-ledger." + (string?)ledger["processToken"], ledger["sha256"], File.Exists(path) ? Reflect.HashFile(path) : null,
                File.Exists(path) && Reflect.HashFile(path) == (string?)ledger["sha256"], "Original append-only evidence from the preceding process is read and hashed before continuation; it is never rewritten.");
        }
        var expectedItems = (JArray)evidence["items"]!;
        var actualItems = BreedingItems();
        BreedingRequire("reload-exact-owned-animals-and-fixtures", expectedItems, actualItems, JToken.DeepEquals(expectedItems, actualItems),
            "New process normal LoadGame restored native UID, ownership, parent UID, count, value, shape, every animal/serum/cage/feed tag and all alleles. No DecodeSaveItem or genetic method is invoked by this scenario.");
        BreedingRequire("reload-exact-forge-state", evidence["forgeState"], ForgeState(), JToken.DeepEquals(evidence["forgeState"], ForgeState()));
        var previousMatrix = evidence["forgeMatrix"] as JArray;
        if (previousMatrix != null)
            foreach (var row in previousMatrix.OfType<JObject>())
                if (_forgeGoals.ContainsKey((string?)row["id"] ?? "")) _forgeGoals[(string)row["id"]!] = (JObject)row.DeepClone();
        if (evidence["forgeClaims"] is JArray previousClaims) foreach (var receipt in previousClaims) _forgeClaims.Add(receipt.DeepClone());
        if (evidence["rewardUids"] is JArray previousRewards) foreach (int uid in previousRewards.Values<int>()) _forgeReceipts.Add(uid);
        R.environment["forgeAchievementMatrix"] = new JArray(ForgeGoalInventory.Select(goal => _forgeGoals[goal.Id]));
        if ((int?)_breeding["earnedRatUid"] is > 0) _forgeProofUids.Add((int)_breeding["earnedRatUid"]!);
        // Readback is finished before any continuation action or SaveGame.
        R.persistence["nativeBreedingIndependentReadback"] = new JObject
        {
            ["sourceRunId"] = (string?)baseline["runId"], ["sourceProcessToken"] = (string?)baseline["processToken"],
            ["thisRunId"] = O.RunId, ["thisProcessToken"] = R.processToken, ["day"] = StoreStation.GetDayCounter(),
            ["items"] = actualItems.DeepClone(), ["checkpoint"] = expected.DeepClone(), ["forgeState"] = ForgeState()
        };
        if ((int?)_breeding["earnedRatUid"] is > 0)
        {
            foreach (object? wait in BreedingVerifyClaimReload()) yield return wait;
            _breeding["status"] = "passed-save-and-reload";
            foreach (object? wait in BreedingCheckpoint("independent native LoadGame, qualification, rewards and duplicate guard verified")) yield return wait;
            yield break;
        }
        _breeding["lastResumeRunId"] = O.RunId;
        _breeding["lastResumeProcessToken"] = R.processToken;
        _breeding["status"] = "in-progress-not-tested";
        // This is an actual continuation of the loaded population. It does not
        // create another starting generation or merely assert a saved marker.
        foreach (object? wait in BreedingAttempt(BreedingRun)) yield return wait;
    }

    private void BreedingInitialize()
    {
        BreedingIsolation(); ForgeInitializeRows(); ForgeValidateDenominator();
        Reflect.Call(Reflect.Type("SpeedUp.SpeedUpSettings", "SpeedUp"), "RestoreNormalSpeed");
        BreedingRequire("normal-speed", 1f, Time.timeScale, Time.timeScale == 1f);
        _breedingDeadline = Time.realtimeSinceStartup + Math.Max(1, O.TimeoutSeconds - 300);
        _breeding = new JObject
        {
            ["schema"] = 1, ["goal"] = BreedingGoal, ["gameRunId"] = Store.runID, ["slotId"] = Store.saveSlotId,
            ["startType"] = "RANCHER", ["originRunId"] = O.RunId, ["originProcessToken"] = R.processToken,
            ["status"] = "in-progress-not-tested", ["setupComplete"] = false, ["totalNights"] = 0, ["maximumGeneration"] = 0, ["highestWeight"] = 0f,
            ["lineage"] = new JObject(), ["pendingFathers"] = new JObject(), ["dailyLedger"] = new JArray(),
            ["ledgerFiles"] = new JArray(),
            ["actions"] = new JArray(), ["fixtures"] = new JArray(), ["cages"] = new JArray(), ["bayUids"] = new JArray(), ["meatUids"] = new JArray(),
            ["rng"] = "Original RNG and mutation methods; no random seed, roll, inheritance, growth, age, pregnancy, weight, reputation, date, story or achievement-state replacement"
        };
        R.environment["nativeBreedingScope"] = "Only rat_800g. Actual six native RANCHER seed animals; their supplied ages/weights are disclosed and never count as an earned rat. Native factory cages/feed/serum/repair inputs/storage; original target dispatch, slots, mating, inheritance, birth, full EndNight and ES3. Select observed alleles without writing them. No economic acquisition claim for fixture supplies. Bounded continuation retains exact pedigree, care and night ledgers. Formal Steam account remains disabled.";
        R.environment["nativeBreedingProgress"] = _breeding;
        R.environment["nativeBreedingBounds"] = new { cages = BreedingCageCount, parentCages = BreedingParentCages, nightsPerProcess = BreedingNightsPerProcess, totalNightLimit = BreedingTotalNightLimit, growOutThreshold = BreedingGrowOutThreshold, secondsReservedForCheckpoint = 300 };
        R.environment["graylineActions"] = _graylineActions;
        R.environment["forgeAchievementClaims"] = _forgeClaims;
    }

    private void BreedingIsolation()
    {
        if (SteamManager.Initialized || !ProbeOptions.NormalizePath(Application.persistentDataPath).Equals(O.Persistent, StringComparison.OrdinalIgnoreCase) ||
            Application.companyName != O.Company || Application.productName != O.Product || Store.saveSlotId != O.Slot || Store.devMode ||
            Store.startType != NewGameData.StartType.RANCHER || !string.IsNullOrEmpty(Store.startScenarioId))
        { _breedingCheckpointUnsafe = true; throw new BreedingSafetyFailure("Breeding data-protection/profession guard failed; stop before any further game or ES3 write"); }
    }

    private IEnumerable<object?> BreedingAttempt(Func<IEnumerable<object?>> action)
    {
        Exception? failure = null; IEnumerator<object?>? iterator = null;
        try { iterator = action().GetEnumerator(); } catch (Exception error) { failure = error; }
        if (iterator != null)
        {
            try
            {
                while (failure == null)
                {
                    bool next = false; object? value = null;
                    try { next = iterator.MoveNext(); if (next) value = iterator.Current; } catch (Exception error) { failure = error; }
                    if (!next || failure != null) break;
                    yield return value;
                }
            }
            finally { iterator.Dispose(); }
        }
        if (failure != null && failure is not FixtureUnavailable)
        {
            _breedingCheckpointUnsafe = true;
            if (R.persistence.TryGetValue("nativeBreeding", out object? prior) && prior is JObject unsafePrior) unsafePrior["resumeAllowed"] = false;
            R.Check("native-breeding.execution", "Original native business succeeds with independent readback", null, false, error: failure.ToString());
            R.environment["nativeBreedingUnsafeStop"] = new { reason = failure.Message, lastPersistedCheckpoint = _breeding["checkpointOrdinal"], native = NightUiState() };
            // In particular, do not save after pollution, isolation, native
            // errors, observer failures, or an uncertain in-flight coroutine.
            yield break;
        }
        if (failure is FixtureUnavailable unavailable)
        {
            _breeding["stopReason"] = unavailable.Message;
            _breeding["status"] = "in-progress-not-tested";
        }
        // Native clientStack/current dialogue are not serialized. An OPEN or
        // CLOSED day cannot be advertised as an exact resumable visitor save.
        // Keep the last verified MORNING ES3 bytes and report the attempted
        // visitor separately. Never serialize/restore or clear a client queue.
        bool safeMorning = Store.storeState == PlayerStore.StoreState.MORNING && Store.currentClientInstance == null && DialogUIManager.Instance.currentDialog == null && !EndOfDayUIManager.Instance.IsOpen;
        if (!safeMorning)
        {
            string path = SaveFiles.AbsolutePath(SaveFiles.Slot(Store.saveSlotId));
            var prior = R.persistence.TryGetValue("nativeBreeding", out object? retained) ? retained as JObject : null;
            string? hash = prior == null ? null : (string?)prior["es3Sha256AtReadback"];
            bool unchanged = prior != null && hash?.Length == 64 && File.Exists(path) && Reflect.HashFile(path) == hash;
            R.environment["nativeBreedingUnpersistedAttempt"] = new { reason = failure?.Message ?? "Native state is not a quiescent morning", native = NightUiState(), preservedMorningCheckpoint = unchanged };
            if (prior != null)
            {
                prior["haltedBeforeUnsafeVisitorSave"] = true; prior["attemptStopReason"] = failure?.Message;
                prior["resumeAllowed"] = unchanged && (bool?)prior["resumeAllowed"] == true;
            }
            BreedingRequire("retain-last-morning-es3-after-visitor-stop", "Last verified MORNING ES3 retained byte for byte; no in-flight client state is claimed to persist", new { path, expected = hash, actual = File.Exists(path) ? Reflect.HashFile(path) : null }, unchanged);
            R.Skip("native-breeding.rat_800g-complete", (failure?.Message ?? "Native day could not finish") + "; no unsafe mid-visitor save. The retained checkpoint contains the exact preceding morning. Continue only after reviewing this precise visitor/economic route; no queue/choice/dismissability fields were replaced.");
            yield break;
        }
        bool claimed = (int?)_breeding["earnedRatUid"] is > 0;
        if (!claimed)
        {
            string reason = (string?)_breeding["stopReason"] ?? "Bounded process/night budget reached before a genuinely born animal reached 800g";
            R.Skip("native-breeding.rat_800g-complete", reason + "; observed nights=" + (int)_breeding["totalNights"]! + ", generation=" + (int)_breeding["maximumGeneration"]! + ", maximumWeight=" + (float)_breeding["highestWeight"]! + "g. Next: reload the identical validated ES3 checkpoint and continue its real population; resolve the exact recorded visitor/economic blocker if present.");
            _forgeGoals[BreedingGoal]["status"] = "not-tested";
            _forgeGoals[BreedingGoal]["reason"] = reason;
        }
        else R.Skip("native-breeding.independent-process-proof", "Actual rat qualification and UI claim are saved. A different process must verify these ES3 bytes, the earned animal, all ten serum reward UIDs and the duplicate guard before this complete scenario passes.");
        foreach (object? wait in BreedingCheckpoint(failure?.Message ?? "bounded native breeding stage finished")) yield return wait;
    }

    private IEnumerable<object?> BreedingPrepareAndRun()
    {
        _breedingSupplies = Spawn("backpack_large"); Retain(_breedingSupplies); _forgeBags.Add(_breedingSupplies);
        BreedingFixtures.Add(new JObject { ["purpose"] = "native supply/reward storage, no topology or shape changes", ["item"] = BreedingContent(_breedingSupplies) });
        _breedingMeatBag = Spawn("backpack_large"); Retain(_breedingMeatBag); _forgeBags.Add(_breedingMeatBag);
        BreedingFixtures.Add(new JObject { ["purpose"] = "native storage for actual butcher cuts, never used as animal feed", ["item"] = BreedingContent(_breedingMeatBag) });
        _breeding["suppliesUid"] = _breedingSupplies.uniqueId; _breeding["meatBagUid"] = _breedingMeatBag.uniqueId;
        _breedingBlade = BreedingOwned().SingleOrDefault(item => item.identifier == "kitchen_cleaver") ?? throw new FixtureUnavailable("Actual RANCHER cleaver is missing");
        _breeding["bladeUid"] = _breedingBlade.uniqueId;
        BreedingMove(BreedingGrid(_breedingSupplies), _breedingBlade, "store native starting cleaver");
        var starterCage = BreedingOwned().SingleOrDefault(item => item.identifier == "animal_cage") ?? throw new FixtureUnavailable("Actual RANCHER starter cage is missing");
        for (int i = 0; i < BreedingCageCount; i++)
        {
            var cage = i == 0 ? starterCage : Spawn("animal_cage");
            bool installed = false;
            foreach (var bay in _breedingBays)
                if (BreedingTryMove(BreedingGrid(bay), cage, "native cage into native machine bay")) { installed = true; break; }
            if (!installed)
            {
                if (_breedingBays.Count >= 4) throw new FixtureUnavailable("Four native machine bays have no valid slot for another cage; no resized/manufactured inventory is substituted");
                var bay = Spawn("machine_bay_ext"); Retain(bay); _breedingBays.Add(bay);
                ((JArray)_breeding["bayUids"]!).Add(bay.uniqueId);
                BreedingFixtures.Add(new JObject { ["purpose"] = "actual native machine bay, original factory geometry and acceptance", ["item"] = BreedingContent(bay) });
                BreedingMove(BreedingGrid(bay), cage, "native cage installation");
            }
            _breedingCages.Add(cage);
            BreedingFixtures.Add(new JObject { ["purpose"] = i == 0 ? "actual RANCHER initial cage" : "actual native cage fixture", ["item"] = BreedingContent(cage) });
            while (AnimalCage.CanUpgradeSize(cage))
            {
                int height = cage.GetTagReadonly("CAGE_SIZE_TAG").GetInt();
                var ingot = BreedingInput("metal_ingot", "one native cage-height upgrade ingredient"); int uid = ingot.uniqueId;
                BreedingTarget(ingot, cage, "real metal-ingot target upgrades cage");
                BreedingRequire("native-cage-upgrade." + cage.uniqueId + "." + height, new { height = height + 1, width = 10, consumedUid = uid }, BreedingCage(cage),
                    cage.GetTagReadonly("CAGE_SIZE_TAG").GetInt() == height + 1 && AnimalCage.GetCageInventory(cage).inventoryShape.width == 10 &&
                    AnimalCage.GetCageInventory(cage).inventoryShape.height == height + 1 && ForgeOwnedUnits(uid) == 0);
            }
            var dispenser = AnimalCage.GetDispenserSlot(cage).childItem;
            if (dispenser == null) { dispenser = Spawn("livestock_feed_dispenser"); BreedingMove(AnimalCage.GetDispenserSlot(cage), dispenser, "real cage dispenser slot"); }
            var vacuum = Spawn("vacuum_robot"); BreedingMove(AnimalCage.GetCageInventory(cage), vacuum, "real cage cleaning device");
            var electronic = BreedingInput("common_electronic", "actual vacuum incinerator upgrade input"); int electronicUid = electronic.uniqueId;
            BreedingTarget(electronic, vacuum, "real electronic target upgrades vacuum");
            BreedingRequire("native-cleaning-upgrade." + vacuum.uniqueId, new { incinerator = true, consumed = electronicUid }, BreedingContent(vacuum),
                vacuum.IsTag("VACUUM_INCINERATOR_UPGRADED_TAG") && HusbandryHelper.IsIncineratorOn(vacuum) && ForgeOwnedUnits(electronicUid) == 0);
            ((JArray)_breeding["cages"]!).Add(new JObject { ["uid"] = cage.uniqueId, ["dispenserUid"] = dispenser.uniqueId, ["vacuumUid"] = vacuum.uniqueId });
            yield return null;
        }
        _breeding["setupComplete"] = true;
        BreedingArrangeAndSelect(); BreedingCare();
        foreach (object? wait in BreedingCheckpoint("actual RANCHER seeds and lawful fixture space prepared before the first real night")) yield return wait;
        foreach (object? wait in CaptureEvidenceFrame("native-breeding-real-rancher-fixtures")) yield return wait;
        foreach (object? wait in BreedingRun()) yield return wait;
    }

    private IEnumerable<object?> BreedingRun()
    {
        BreedingIsolation(); BreedingBindFixtures();
        using var observation = new BreedingObservation(this);
        int stageNights = 0;
        while ((int)_breeding["totalNights"]! < BreedingTotalNightLimit && stageNights < BreedingNightsPerProcess && Time.realtimeSinceStartup < _breedingDeadline)
        {
            BreedingIsolation();
            if (GameOverUIManager.Instance?.IsShowing == true) throw new FixtureUnavailable("Actual native game-over is visible; no ending/health/rent bypass. Inspect saved progress and the exact game-over before attempting continuation");
            var earned = BreedingRats().FirstOrDefault(rat => !HusbandryHelper.IsAnimalDead(rat) && BreedingWeight(rat) >= 800f && BreedingGeneration(rat.uniqueId) > 0);
            if (earned != null)
            {
                _forgeProofUids.Add(earned.uniqueId);
                _forgeGoals[BreedingGoal]["nativePedigree"] = BreedingLineage[earned.uniqueId.ToString()]!.DeepClone();
                _forgeGoals[BreedingGoal]["nativeBreedingNights"] = (int)_breeding["totalNights"]!;
                foreach (object? wait in ForgeClaim(ForgeGoalInventory.Single(goal => goal.Id == BreedingGoal))) yield return wait;
                BreedingRequire("actually-claimed-earned-animal", "Native-born 800g animal and exactly the independent actual UI reward receipt", ForgeState(),
                    (string?)_forgeGoals[BreedingGoal]["claim"] == "passed" && _forgeClaims.OfType<JObject>().Count(row => (string?)row["goal"] == BreedingGoal) == 1);
                int proofUid = ((JArray)_forgeClaims.OfType<JObject>().Single(row => (string?)row["goal"] == BreedingGoal)["proofUids"]!).Values<int>().Single();
                BreedingRequire("claimed-proof-is-real-descendant", "A live genuinely born descendant, not a supplied starter animal", BreedingLineage[proofUid.ToString()], BreedingGeneration(proofUid) > 0);
                _breeding["earnedRatUid"] = proofUid; _breeding["status"] = "claimed-awaiting-independent-reload";
                foreach (object? wait in ForgeSaveReadback()) yield return wait;
                yield break;
            }
            BreedingArrangeAndSelect(); BreedingCare();
            var before = BreedingRats().Where(rat => !HusbandryHelper.IsAnimalDead(rat)).ToArray();
            if (!before.Any(rat => BreedingSex(rat) == "MALE") || !before.Any(rat => BreedingSex(rat) == "FEMALE"))
                throw new FixtureUnavailable("Native breeding population has lost a sex; no replacement/high-gene animal is injected. Preserve exact surviving lineage and arrange a genuinely acquired mate before continuation");
            BreedingRequire("no-untracked-or-prion-animals", "Every live animal belongs to observed seed/birth pedigree and no cannibalism/prion contamination", before.Select(BreedingRat).ToArray(),
                before.All(rat => BreedingLineage[rat.uniqueId.ToString()] != null && !rat.IsTag("ANIMAL_PRION_TAG")));
            int day = StoreStation.GetDayCounter(); observation.BeginDay(day);
            foreach (object? wait in BreedingCheckpoint("actual care and parent selection in a quiescent morning before opening day " + day)) yield return wait;
            var pre = new JArray(before.OrderBy(rat => rat.uniqueId).Select(BreedingRat));
            using (var night = new NightObservation(this, Array.Empty<RecipeFixture>(), day))
            {
                foreach (object? wait in BreedingNight(day, night)) yield return wait;
                BreedingRequire("native-errors-and-observation." + day, "No errors or failed observers; original speed 1", new { breedingObserverErrors = observation.Errors, nativeErrors = night.Errors, night.ObserverErrors, night.MinimumTimeScale, night.MaximumTimeScale },
                    observation.Errors.Count == 0 && night.Errors.Count == 0 && night.ObserverErrors.Count == 0 && night.MinimumTimeScale == 1f && night.MaximumTimeScale == 1f);
                var post = new JArray(BreedingRats().OrderBy(rat => rat.uniqueId).Select(BreedingRat));
                BreedingRequire("one-real-husbandry-tick." + day, "Each pre-night live UID entered original husbandry once; alive entry ages once and must grow once or have an observed native death; a native death before entry keeps age/weight and never grows; all alleles remain unchanged",
                    new { ticks = observation.DayTicks, nativeDeaths = observation.DayDeaths }, before.All(rat =>
                    {
                        var rows = observation.DayTicks.OfType<JObject>().Where(row => (int?)row["uid"] == rat.uniqueId).ToArray();
                        return rows.Length == 1 && BreedingValidTick((JObject)pre.Single(row => (int?)row["uid"] == rat.uniqueId), rows[0], observation.DayGrowth);
                    }));
                BreedingRequire("independent-weight-ledger." + day, "Each original GrowAnimal result equals independently computed stage/fed/serum growth; no direct growth call", observation.DayGrowth,
                    observation.DayGrowth.OfType<JObject>().All(row => (bool?)row["valid"] == true));
                BreedingRequire("native-cleaning-and-pedigree." + day, "Every born UID is traceable, owned once and stored; native incinerators removed feces before morning", new
                {
                    births = observation.DayBirths, cages = _breedingCages.Select(BreedingCage).ToArray(), animals = post
                }, BreedingRats().All(rat => BreedingLineage[rat.uniqueId.ToString()] != null) &&
                    _breedingCages.All(cage => !AnimalCage.GetCageInventory(cage).childItems.Any(item => item.IsTag("ANIMAL_FECES_TAG"))) &&
                    observation.DayBirths.OfType<JObject>().All(row => ForgeOwnedUnits((int)row["uid"]!) == 1));
                BreedingAppendNight(new JObject
                {
                    ["dayBefore"] = day, ["dayAfter"] = StoreStation.GetDayCounter(), ["before"] = pre, ["after"] = post,
                    ["mating"] = observation.DayMatings.DeepClone(), ["births"] = observation.DayBirths.DeepClone(), ["nativeDeaths"] = observation.DayDeaths.DeepClone(), ["husbandryTicks"] = observation.DayTicks.DeepClone(),
                    ["growth"] = observation.DayGrowth.DeepClone(), ["night"] = BreedingNightEvidence(night)
                });
            }
            _breeding["totalNights"] = (int)_breeding["totalNights"]! + 1; stageNights++;
            _breeding["maximumGeneration"] = BreedingLineage.Properties().Select(property => (int?)property.Value["generation"] ?? 0).DefaultIfEmpty(0).Max();
            float maximum = BreedingRats().Select(BreedingWeight).DefaultIfEmpty(0f).Max();
            _breeding["highestWeight"] = (double)Math.Max((float)_breeding["highestWeight"]!, maximum);
            _breeding["highestWeightDay"] = StoreStation.GetDayCounter();
            foreach (object? wait in BreedingCheckpoint("completed genuine full native night " + day)) yield return wait;
        }
        _breeding["stopReason"] = "Bound reached: stageNights=" + stageNights + "/" + BreedingNightsPerProcess + ", totalNights=" + (int)_breeding["totalNights"]! + "/" + BreedingTotalNightLimit + ", reserved-time checkpoint=" + (Time.realtimeSinceStartup >= _breedingDeadline);
    }

    private void BreedingArrangeAndSelect()
    {
        BreedingIsolation();
        var living = BreedingRats().Where(rat => !HusbandryHelper.IsAnimalDead(rat)).ToArray();
        IOrderedEnumerable<GameItem> Rank(IEnumerable<GameItem> rats) => rats.OrderByDescending(BreedingBestAllele).ThenByDescending(BreedingExpression)
            .ThenBy(rat => BreedingAge(rat) >= 11).ThenByDescending(rat => rat.GetTagReadonly("ANIMAL_CURRENT_FERTILITY_TAG").GetFloat()).ThenBy(rat => BreedingAge(rat));
        var males = Rank(living.Where(rat => BreedingSex(rat) == "MALE")).Take(8).ToArray();
        var females = Rank(living.Where(rat => BreedingSex(rat) == "FEMALE")).Take(16).ToArray();
        var keep = males.Concat(females).Concat(living.Where(rat => rat.IsTag("ANIMAL_PREGNANT_TAG")))
            .Concat(Rank(living.Where(rat => BreedingExpression(rat) >= BreedingGrowOutThreshold && BreedingAge(rat) <= 11)).Take(8))
            .Select(rat => rat.uniqueId).ToHashSet();
        if (keep.Count > 64) throw new FixtureUnavailable("More than64 actual retained breeders/pregnancies/grow-out candidates; native space requires a reviewed selection before another night");
        foreach (var animal in living.Where(rat => !keep.Contains(rat.uniqueId)).Concat(BreedingRats().Where(HusbandryHelper.IsAnimalDead)).Distinct().ToArray()) BreedingCull(animal);
        var matureMales = males.Where(rat => BreedingAge(rat) >= 3).ToArray();
        var matureFemales = females.Concat(living.Where(rat => rat.IsTag("ANIMAL_PREGNANT_TAG") && keep.Contains(rat.uniqueId))).Distinct().Where(rat => BreedingAge(rat) >= 3).ToArray();
        for (int i = 0; i < matureMales.Length; i++) BreedingMove(AnimalCage.GetCageInventory(_breedingCages[i]), matureMales[i], "select actual high-allele male parent");
        for (int i = 0; i < matureFemales.Length; i++)
            BreedingMove(AnimalCage.GetCageInventory(_breedingCages[i % Math.Max(1, matureMales.Length)]), matureFemales[i], "select actual high-allele female parent / preserve real pregnancy");
        var arranged = matureMales.Concat(matureFemales).Select(rat => rat.uniqueId).ToHashSet();
        foreach (var rat in BreedingRats().Where(rat => !HusbandryHelper.IsAnimalDead(rat) && !arranged.Contains(rat.uniqueId)))
            BreedingMove(AnimalCage.GetCageInventory(_breedingCages[BreedingSex(rat) == "MALE" ? 8 : 9]), rat, "actual nursery/grow-out cage, sex separated");
        foreach (var cage in _breedingCages)
        {
            int level = HusbandryHelper.GetAnimalOvercrowdingLevel(AnimalCage.GetCageInventory(cage));
            if (level >= 2) throw new FixtureUnavailable("Native cage " + cage.uniqueId + " has overcrowding level" + level + "; no forced slot/size change or risky night. Reallocate the recorded animals through legal slots");
        }
        BreedingActions.Add(new JObject { ["action"] = "actual observed-genotype parent selection, no gene writes", ["day"] = StoreStation.GetDayCounter(),
            ["maleUids"] = new JArray(males.Select(rat => rat.uniqueId)), ["femaleUids"] = new JArray(females.Select(rat => rat.uniqueId)), ["keepUids"] = new JArray(keep.OrderBy(uid => uid)) });
    }

    private void BreedingCull(GameItem animal)
    {
        if (!BreedingLineage.TryGetValue(animal.uniqueId.ToString(), out JToken? lineage)) throw new BreedingSafetyFailure("Untracked animal cannot be culled");
        if ((string?)lineage["removedBy"] == "actual-native-butcher") return;
        var beforeUids = BreedingOwned().Select(item => item.uniqueId).ToHashSet(); var snapshot = BreedingRat(animal); int uid = animal.uniqueId;
        if (!HusbandryHelper.IsAnimalDead(animal)) BreedingTarget(_breedingBlade!, animal, "native cleaver target kills rejected breeding animal");
        BreedingRequire("native-cull-death." + uid, true, HusbandryHelper.IsAnimalDead(animal), HusbandryHelper.IsAnimalDead(animal));
        int tally = Store.animalsButcheredTotal;
        BreedingTarget(_breedingBlade!, animal, "native cleaver target butchers actual animal");
        var cuts = BreedingOwned().Where(item => !beforeUids.Contains(item.uniqueId)).ToArray();
        BreedingRequire("native-cull-consumption." + uid, new { uidRemoved = true, tally = tally + 1, actualCuts = true }, new { remaining = ForgeOwnedUnits(uid), Store.animalsButcheredTotal, cuts = cuts.Select(BreedingContent).ToArray() },
            ForgeOwnedUnits(uid) == 0 && Store.animalsButcheredTotal == tally + 1 && cuts.Length > 0 && cuts.All(item => item.GetTagReadonly("MEAT_TYPE_TAG").GetString() == "ANIMAL_RAT"));
        foreach (var cut in cuts)
        {
            ((JArray)_breeding["meatUids"]!).Add(cut.uniqueId);
            BreedingMove(BreedingGrid(_breedingMeatBag!), cut, "native actual butcher-cut storage; no animal cannibalism feed");
        }
        lineage["removedBy"] = "actual-native-butcher"; lineage["removedDay"] = StoreStation.GetDayCounter(); lineage["lastAnimalState"] = snapshot;
        BreedingActions.Add(new JObject { ["action"] = "native cleaver kill/butcher", ["day"] = StoreStation.GetDayCounter(), ["animal"] = snapshot, ["cutUids"] = new JArray(cuts.Select(cut => cut.uniqueId)) });
    }

    private void BreedingCare()
    {
        foreach (var cage in _breedingCages)
        {
            int hp = cage.GetTagReadonly("CAGE_HEALTH_TAG").GetInt(), max = cage.GetTagReadonly("CAGE_MAX_HEALTH_TAG").GetInt();
            while (hp < max)
            {
                var scrap = BreedingInput("scrap_metal", "real cage maintenance input"); int uid = scrap.uniqueId;
                BreedingTarget(scrap, cage, "native scrap target repairs cage");
                int after = cage.GetTagReadonly("CAGE_HEALTH_TAG").GetInt();
                BreedingRequire("actual-cage-repair." + uid, new { hp = Math.Min(max, hp + max / 2), inputRemaining = 0 }, new { hp = after, inputRemaining = ForgeOwnedUnits(uid) }, after == Math.Min(max, hp + max / 2) && ForgeOwnedUnits(uid) == 0); hp = after;
            }
            var dispenser = AnimalCage.GetDispenserSlot(cage).childItem ?? throw new BreedingSafetyFailure("Native cage lost its dispenser");
            var rats = AnimalCage.GetCageInventory(cage).childItems.Where(item => item.identifier == "rat" && !HusbandryHelper.IsAnimalDead(item)).ToArray();
            int required = (int)Math.Ceiling(rats.Sum(rat => (double)HusbandryHelper.GetMaxHunger(rat))) + 1000;
            while (rats.Length > 0 && MachineFeedDispenser.GetAnimalKcal(dispenser) < required)
            {
                var food = BreedingInput("beis_icecream", "real native nonmeat feed input, original1500 kcal; no plant-genetic fixture metadata"); int uid = food.uniqueId, calories = food.GetTagReadonly("CALORIE_VALUE_TAG").GetInt();
                if (calories <= 0 || food.IsTag("CANNED_FOOD_TAG") || FoodItemHelper.IsMeatFood(food)) throw new BreedingSafetyFailure("Native feed factory returned unexpected calorie/meat/canned state");
                int before = MachineFeedDispenser.GetAnimalKcal(dispenser);
                BreedingMove(MachineFeedDispenser.GetStorageGrid(dispenser), food, "native feeder ingredient slot");
                MachineFeedDispenser.Blend(dispenser, MachineFeedDispenser.GetStorageGrid(dispenser));
                BreedingRequire("actual-nonmeat-feed-consumption." + uid, new { kcal = before + calories, consumed = uid }, BreedingContent(dispenser),
                    MachineFeedDispenser.GetAnimalKcal(dispenser) == before + calories && ForgeOwnedUnits(uid) == 0 && dispenser.GetTagReadonly("STORED_ANIMAL_RAT").GetInt() == 0);
                BreedingActions.Add(new JObject { ["action"] = "actual native feeder blend", ["day"] = StoreStation.GetDayCounter(), ["inputUid"] = uid,
                    ["dispenserUid"] = dispenser.uniqueId, ["calories"] = calories, ["kcalBefore"] = before, ["kcalAfter"] = MachineFeedDispenser.GetAnimalKcal(dispenser), ["inputUnitsAfter"] = ForgeOwnedUnits(uid) });
            }
        }
        foreach (var rat in BreedingRats().Where(rat => !HusbandryHelper.IsAnimalDead(rat)))
        {
            BreedingSerum(rat, "serum_green", "livestock_immunity");
            // Steroid is used from the first care opportunity after real birth
            // only for a genetically promising grow-out animal. Green/red's
            // actual immunity penalty and any resulting disease remain real.
            if (BreedingGeneration(rat.uniqueId) > 0 && BreedingExpression(rat) >= BreedingGrowOutThreshold && BreedingAge(rat) <= 11)
                BreedingSerum(rat, "serum_red", "livestock_steroid");
        }
    }

    private void BreedingSerum(GameItem rat, string id, string liquid)
    {
        string part = Liquid.GetCurrentPartFromId(liquid); int before = rat.GetTagReadonly(part).GetInt();
        if (before >= 10) return;
        var serum = BreedingOwned().FirstOrDefault(item => item.identifier == id && item.GetTagReadonly("CURRENT_USE_COUNT_INT").GetInt() > 0) ?? BreedingInput(id, "actual native " + liquid + " input, ten native uses per factory vial");
        int uses = serum.GetTagReadonly("CURRENT_USE_COUNT_INT").GetInt(); var genes = BreedingGenes(rat); float weight = BreedingWeight(rat); int age = BreedingAge(rat);
        BreedingTarget(serum, rat, "native serum target");
        BreedingRequire("actual-serum." + serum.uniqueId + "." + rat.uniqueId + "." + StoreStation.GetDayCounter(), new { uses = uses - 1, part = before + 20, sameGenes = true, weight, age }, new
        {
            uses = serum.GetTagReadonly("CURRENT_USE_COUNT_INT").GetInt(), part = rat.GetTagReadonly(part).GetInt(), genes = BreedingGenes(rat), weight = BreedingWeight(rat), age = BreedingAge(rat)
        }, serum.GetTagReadonly("CURRENT_USE_COUNT_INT").GetInt() == uses - 1 && rat.GetTagReadonly(part).GetInt() == before + 20 &&
            JToken.DeepEquals(genes, BreedingGenes(rat)) && BreedingWeight(rat) == weight && BreedingAge(rat) == age);
        BreedingActions.Add(new JObject { ["action"] = "actual native serum dose ledger", ["day"] = StoreStation.GetDayCounter(), ["serumUid"] = serum.uniqueId, ["ratUid"] = rat.uniqueId,
            ["liquid"] = liquid, ["usesBefore"] = uses, ["usesAfter"] = serum.GetTagReadonly("CURRENT_USE_COUNT_INT").GetInt(), ["partBefore"] = before, ["partAfter"] = rat.GetTagReadonly(part).GetInt(),
            ["genesUnchanged"] = JToken.DeepEquals(genes, BreedingGenes(rat)), ["age"] = age, ["weight"] = weight });
        if (uses == 1)
            BreedingMove(EmporiumEntry.Instance.trashcanInvElement, serum, "spent actual serum vial into native janitorial trash");
    }

    private IEnumerable<object?> BreedingNight(int day, NightObservation observation)
    {
        string prefix = "native-breeding.night." + day;
        foreach (object? wait in NightWait(prefix + ".morning-ready", () => (Store.storeState is PlayerStore.StoreState.MORNING or PlayerStore.StoreState.OPEN) &&
            !StoreUIManager.Instance.blackscreen.gameObject.activeSelf && Store.lockShutterButtonFlag == 0, observation)) yield return wait;
        if (AdvCalendarUIManager.Instance.UIPanel.activeSelf) AdvCalendarUIManager.Instance.CloseUI();
        if (Store.storeState == PlayerStore.StoreState.MORNING)
        {
            if (!Store.CanStartDay()) throw new FixtureUnavailable("Native CanStartDay rejected current saved state; no shutter/date/queue flag is changed");
            StoreShutterButton.Instance.OnClick();
            foreach (object? wait in NightWait(prefix + ".shutter-opened", () => Store.storeState == PlayerStore.StoreState.OPEN && Store.lockShutterButtonFlag == 0, observation)) yield return wait;
        }
        foreach (object? wait in BreedingVisitors(observation)) yield return wait;
        if (!Store.CanEndDay() || Store.isClientBeingArrested || Store.isHandlingShootout || Store.lockShutterButtonFlag != 0 || DialogUIManager.Instance.currentDialog != null)
            throw new FixtureUnavailable("Native CanEndDay/dialogue/arrest/shootout guard prevents closing; preserve exact visitor instead of ending it");
        StoreShutterButton.Instance.OnClick();
        if (StoreUIManager.Instance.closeShutterConfirmationPanel.activeSelf)
        { if (!Store.CanEndDay()) throw new BreedingSafetyFailure("Native close eligibility changed"); StoreShutterButton.Instance.EndAndClose(); }
        foreach (object? wait in NightWait(prefix + ".shutter-closed", () => Store.storeState == PlayerStore.StoreState.CLOSED && Store.currentClientInstance == null &&
            StoreShutterButton.Instance.shutterGameObject.transform.position == StoreShutterButton.Instance.startPosition &&
            StoreShutterButton.Instance.blockerGameObject.transform.localPosition == StoreShutterButton.Instance.blockerStartPosition, observation)) yield return wait;
        var beds = UnityEngine.Object.FindObjectsOfType<StoreBedroll>().Where(bed => bed.gameObject.activeInHierarchy).ToArray();
        BreedingRequire("actual-bedroll." + day, 1, beds.Length, beds.Length == 1); beds[0].OnClick();
        foreach (object? wait in NightWait(prefix + ".end-day-tablet", () => EndOfDayUIManager.Instance.IsOpen && Reflect.Get(EndOfDayUIManager.Instance, "endDayButton") is TabletButton, observation)) yield return wait;
        var end = (TabletButton)Reflect.Get(EndOfDayUIManager.Instance, "endDayButton")!;
        observation.Invoice = new JArray(Store.storeService.Where(service => service.unlocked && service.isUsing).Select(service => JObject.FromObject(new { service.id, service.cost, service.totalCooldown })));
        observation.InvoiceTotal = Store.storeService.Where(service => service.unlocked && service.isUsing).Sum(service => service.cost);
        observation.Evidence["invoice"] = observation.Invoice;
        if (!end.gameObject.activeInHierarchy || !end.Interactable || end.button == null || !end.button.IsInteractable() || observation.InvoiceTotal > Store.playerCash)
            throw new FixtureUnavailable("Actual End Day button/service invoice is unavailable or unaffordable; no funds, service flags or night are forced");
        observation.Armed = true; end.button.onClick.Invoke();
        foreach (object? wait in NightWait(prefix + ".next-morning", () => StoreStation.GetDayCounter() == day + 1 && Store.storeState == PlayerStore.StoreState.MORNING &&
            !StoreUIManager.Instance.blackscreen.gameObject.activeSelf && !StoreUIManager.Instance.savingTMP.gameObject.activeSelf && !EndOfDayUIManager.Instance.IsOpen &&
            AdvCalendarUIManager.Instance.UIPanel.activeSelf && AdvCalendarUIManager.Instance.nightlyReportSection.activeSelf && observation.Events.Count(row => (string?)row["name"] == "OnWakingUpLate") == 1, observation, 60f)) yield return wait;
        BreedingRequire("exact-native-stages." + day, "One EndNight, BeginDay, automatic SaveGame; all ordered original lifecycle hooks", BreedingNightEvidence(observation),
            observation.Entered.GetValueOrDefault("EndNight") == 1 && observation.Returned.GetValueOrDefault("EndNight") == 1 &&
            observation.Entered.GetValueOrDefault("BeginDay") == 1 && observation.Returned.GetValueOrDefault("BeginDay") == 1 &&
            observation.Entered.GetValueOrDefault("SaveGame") == 1 && observation.Returned.GetValueOrDefault("SaveGame") == 1 && observation.Events.Select(row => (string)row["name"]!).SequenceEqual(NightEvents) &&
            observation.Events.All(row => (int)row["day"]! == ((string)row["name"]! is "OnWakingUpEarly" or "OnWakingUpLate" ? day + 1 : day)) && (bool?)observation.AutoSave?["valid"] == true);
        var sleep = observation.Events.Single(row => (string?)row["name"] == "OnGoingSleepEarly"); var charged = observation.Events.Single(row => (string?)row["name"] == "OnHandlingNightlyServicesEarly");
        BreedingRequire("actual-service-charge." + day, observation.InvoiceTotal, (int)sleep["cash"]! - (int)charged["cash"]!, (int)sleep["cash"]! - (int)charged["cash"]! == observation.InvoiceTotal);
    }

    private IEnumerable<object?> BreedingVisitors(NightObservation observation)
    {
        float deadline = Math.Min(_breedingDeadline, Time.realtimeSinceStartup + 240f); int visited = 0;
        bool NeedIncome() => Store.playerCash < Store.GetRentValue() * 3 + 200 && BreedingSaleCuts().Any();
        while (!Store.CanEndDay() || DialogUIManager.Instance.currentDialog != null || (NeedIncome() && visited < 12))
        {
            BreedingIsolation(); observation.SampleClock();
            if (Time.realtimeSinceStartup > deadline) throw new FixtureUnavailable("Bounded natural visitor/economic path did not finish; native client and queue are retained");
            if (GameOverUIManager.Instance?.IsShowing == true) throw new FixtureUnavailable("Actual native game-over appeared during a visitor; no choice or ending is fabricated");
            var client = Store.currentClientInstance?.GetClientBlueprint();
            if (client == null)
            {
                if (Store.CanNextClient() && Store.lockNextBellFlag == 0 && DialogUIManager.Instance.currentDialog == null) GraylineRingBell();
                else if (Store.CanEndDay() && DialogUIManager.Instance.currentDialog == null) yield break;
                yield return null; continue;
            }
            if (!Store.isClientArrived) { yield return null; continue; }
            bool reviewed = client.identifier is "landlord" or "loan_shark2" or "wanted4";
            if (!reviewed && !client.dismissable)
            {
                _breeding["visitorBlocker"] = JObject.FromObject(new { client.identifier, intent = client.clientIntent.ToString(), client.dismissable,
                    dialogue = DialogUIManager.Instance.currentDialog?.plainText, choices = DialogUIManager.Instance.currentDialog?.choices.Select(choice => choice.Item2.plainText).ToArray() });
                throw new FixtureUnavailable("Unknown required native visitor " + client.identifier + ": stop immediately before advancing/selecting/dismissing it; review its actual dialogue route before reloading this checkpoint");
            }
            if (reviewed)
            {
                // Exact existing reviewed landlord pay-rent / loan_shark2
                // decline / wanted4 no-choice dialogue route, without AugReady.
                if (client.identifier == "landlord" && !Store.CanPayRent()) throw new FixtureUnavailable("Actual landlord rent is unaffordable: cash=" + Store.playerCash + ", rent=" + Store.GetRentValue() + ". Sell actual recorded butcher cuts to a naturally arrived eligible buyer; no cash/reputation assignment");
                foreach (object? wait in GraylineOrdinaryClient(client)) { observation.SampleClock(); yield return wait; }
                visited++; continue;
            }
            GraylineAdvanceDialogue();
            if (GraylineChoicesVisible()) throw new FixtureUnavailable("Unreviewed natural customer choice on " + client.identifier + "; no fallback/automatic choice is selected");
            if (DialogUIManager.Instance.currentDialog == null && !NegociationUIManager.IsTradeLocked() && (client.clientIntent is StoreClient.ClientIntent.BUY or StoreClient.ClientIntent.SELLNBUY))
            {
                var cut = BreedingSaleCuts().FirstOrDefault(item => client.IsClientBuyingThisItem(item) && !client.CanClientExposeAnyFeatureDeep(item) && (!client.useClientBudget || client.HasBudgetLeftToBuy(item)));
                if (cut != null)
                {
                    int uid = cut.uniqueId; BreedingMove(EmporiumEntry.Instance.backInvinvElement, cut, "native table placement of actual rat butcher cut");
                    int quote = (int)cut.GetNegociatedValue(), cash = Store.playerCash, budget = client.GetBudget();
                    if (ReferenceEquals(Store.currentNegociatedItem, cut) && Store.CanSellItemFromTable() && quote > 0 && (!client.useClientBudget || budget >= quote))
                    {
                        BreedingActions.Add(new JObject { ["action"] = "actual natural buyer sale of native butcher product", ["day"] = StoreStation.GetDayCounter(), ["client"] = client.identifier, ["uid"] = uid, ["quote"] = quote, ["cashBefore"] = cash, ["budgetBefore"] = budget });
                        NegociationUIManager.Instance.SellItem(); yield return null;
                        BreedingRequire("real-cull-meat-sale." + uid, new { cash = cash + quote, ownedUnits = 0, budget = client.useClientBudget ? budget - quote : budget },
                            new { cash = Store.playerCash, ownedUnits = ForgeOwnedUnits(uid), budget = client.GetBudget() }, Store.playerCash == cash + quote && ForgeOwnedUnits(uid) == 0 && (!client.useClientBudget || client.GetBudget() == budget - quote));
                        continue;
                    }
                    BreedingMove(BreedingGrid(_breedingMeatBag!), cut, "restore unsold native butcher cut after a genuine rejected sale precondition");
                }
            }
            // Only the game's own unlocked bell, after visible dialogue has
            // actually finished and the client is natively dismissable.
            if (Store.IsClientDimissable() && Store.lockNextBellFlag == 0 && DialogUIManager.Instance.currentDialog == null)
            { GraylineRingBell(); visited++; }
            yield return null;
        }
    }

    private IEnumerable<object?> BreedingVerifyClaimReload()
    {
        var goal = ForgeGoalInventory.Single(item => item.Id == BreedingGoal); int uid = (int)_breeding["earnedRatUid"]!;
        var rat = BreedingOwned().SingleOrDefault(item => item.uniqueId == uid);
        BreedingRequire("reloaded-earned-rat-qualification", "Identical real-born, alive, finite >=800g animal", rat == null ? null : BreedingRat(rat),
            rat != null && BreedingGeneration(uid) > 0 && !HusbandryHelper.IsAnimalDead(rat) && BreedingWeight(rat) >= 800f);
        var before = ForgeLedger(); bool opened = (bool)Reflect.Call(ForgeWorkshopUi, "OpenChain", ForgeAchievementChain)!; yield return null;
        ForgeNode(goal).onClick.Invoke(); yield return null;
        BreedingRequire("reloaded-actual-claim-page-disabled", "Visible native claimed node and disabled button", ForgeState(), opened && ForgeWindow()?.IsVisible == true &&
            ((JArray)ForgeState()["Claimed"]!).Values<string>().Contains(BreedingGoal) && !ForgeClaimButton(true).IsInteractable());
        Reflect.Call(ForgeWorkshop, "Claim", BreedingGoal);
        BreedingRequire("reloaded-no-duplicate-rewards", before, ForgeLedger(), JToken.DeepEquals(before, ForgeLedger()));
        var receipt = _forgeClaims.OfType<JObject>().Single(row => (string?)row["goal"] == BreedingGoal);
        var rewards = ((JArray)receipt["items"]!).OfType<JObject>().ToArray();
        BreedingRequire("reloaded-ten-actual-serum-rewards", "Five serum_green and five serum_red, every original new UID exactly once, unchanged native uses/tags", rewards,
            rewards.Length == 10 && new[] { "serum_green", "serum_red" }.All(id => rewards.Count(row => (string?)row["id"] == id) == 5) && rewards.All(row =>
                BreedingOwned().Count(item => item.uniqueId == (int)row["uid"]!) == 1 && JToken.DeepEquals(ForgeItem(BreedingOwned().Single(item => item.uniqueId == (int)row["uid"]!)), row)));
        _forgeGoals[BreedingGoal]["reload"] = "passed"; _forgeGoals[BreedingGoal]["status"] = "passed-save-and-reload";
        foreach (object? wait in CaptureEvidenceFrame("native-breeding-earned-800g-reloaded-claimed")) yield return wait;
        Reflect.Call(ForgeWorkshopUi, "CloseWindow");
    }

    private IEnumerable<object?> BreedingCheckpoint(string reason)
    {
        if (_breedingCheckpointUnsafe) throw new BreedingSafetyFailure("Unsafe state cannot be promoted to a resumable breeding checkpoint");
        BreedingIsolation();
        if (StoreUIManager.Instance.blackscreen.gameObject.activeSelf || StoreUIManager.Instance.savingTMP.gameObject.activeSelf ||
            Store.storeState != PlayerStore.StoreState.MORNING || Store.currentClientInstance != null || DialogUIManager.Instance.currentDialog != null || EndOfDayUIManager.Instance.IsOpen)
            throw new BreedingSafetyFailure("Only a quiescent native MORNING may become a resumable checkpoint; current client/queue/dialogue are not serializable");
        _breeding["checkpointOrdinal"] = ((int?)_breeding["checkpointOrdinal"] ?? 0) + 1;
        _breeding["day"] = StoreStation.GetDayCounter(); _breeding["cash"] = Store.playerCash;
        _breeding["lastCheckpointRunId"] = O.RunId; _breeding["lastCheckpointProcessToken"] = R.processToken;
        _breeding["lastCheckpointReason"] = reason;
        if (_breedingLedgerPath != null)
        {
            var row = ((JArray)_breeding["ledgerFiles"]!).OfType<JObject>().Single(ledger => (string?)ledger["processToken"] == R.processToken);
            row["sha256"] = Reflect.HashFile(_breedingLedgerPath); row["nightRows"] = _breedingLedgerRows;
        }
        string json = _breeding.ToString(Formatting.None);
        ModHelper.SetModData("nicokobo.content_native_probe", "breeding-progress", json);
        // Common Save owns the receipt and final selected-file hash. It is
        // called before the readback below, once per checkpoint, outside the
        // NightObservation lifetime; native automatic saving remains separate.
        Save();
        var items = BreedingItems();
        string path = SaveFiles.AbsolutePath(SaveFiles.Slot(Store.saveSlotId)); var settings = new ES3Settings(path, ES3.Location.File);
        var saved = (PlayerStore)Reflect.Call(typeof(SaveFiles), "ReadStoreForPreview", settings)!;
        bool stored = saved.ModData().TryGetValue(BreedingDataKey, out string storedJson);
        var station = ES3.Load<StoreStation>("storeStation", settings);
        BreedingRequire("checkpoint-real-es3." + (int)_breeding["checkpointOrdinal"]!, "Exact pedigree/progress/cash/run/slot/day in actual ES3 bytes", new { saved.runID, saved.saveSlotId, saved.playerCash, station.dayCounter, stored },
            stored && storedJson == json && saved.runID == Store.runID && saved.saveSlotId == Store.saveSlotId && saved.playerCash == Store.playerCash && station.dayCounter == StoreStation.GetDayCounter());
        var nodes = new[] { saved.mainInvJSON, saved.backInvJSON, saved.trashcanInvJSON }.Where(text => !string.IsNullOrEmpty(text))
            .SelectMany(text => JsonUtility.FromJson<SaveState>(text).saveItems).ToArray();
        var disk = new JArray(); var expectedContent = new JArray();
        foreach (var token in items.OfType<JObject>())
        {
            int uid = (int)token["uid"]!; var matches = nodes.Where(node => node.uniqueId == uid).ToArray();
            if (matches.Length != 1) throw new BreedingSafetyFailure("Actual ES3 checkpoint has missing/duplicate controlled item UID " + uid + "; no resumable baseline");
            var node = matches[0]; disk.Add(BreedingStored(node.identifier, node.uniqueId, node.unitCount, node.unitValue, node.itemState, node.itemModifiedShape));
            var content = (JObject)token.DeepClone(); content.Remove("parentUid"); content.Remove("parentInventory"); content.Remove("owned"); expectedContent.Add(content);
        }
        BreedingRequire("checkpoint-real-es3-item-tags." + (int)_breeding["checkpointOrdinal"]!, new { count = expectedContent.Count, exactNativeTags = true },
            new { count = disk.Count, exactNativeTags = JToken.DeepEquals(expectedContent, disk) }, JToken.DeepEquals(expectedContent, disk), "Full controlled-item snapshots are retained in the bound checkpoint result; each ES3 node is independently compared here.");
        bool claimed = (int?)_breeding["earnedRatUid"] is > 0;
        R.persistence["nativeBreeding"] = new JObject
        {
            ["schema"] = 1, ["goal"] = BreedingGoal, ["resumeAllowed"] = (bool?)_breeding["setupComplete"] == true, ["status"] = _breeding["status"]!.DeepClone(),
            ["sourceRunId"] = O.RunId, ["sourceProcessToken"] = R.processToken, ["gameRunId"] = Store.runID, ["slotId"] = Store.saveSlotId,
            ["persistentDataPath"] = O.Persistent, ["day"] = station.dayCounter, ["cash"] = saved.playerCash,
            ["totalNights"] = _breeding["totalNights"]!.DeepClone(), ["maximumGeneration"] = _breeding["maximumGeneration"]!.DeepClone(), ["highestWeight"] = _breeding["highestWeight"]!.DeepClone(),
            ["checkpoint"] = JObject.Parse(storedJson), ["checkpointJson"] = storedJson, ["items"] = items, ["es3PathAtReadback"] = path, ["es3Sha256AtReadback"] = Reflect.HashFile(path),
            ["forgeState"] = ForgeState(), ["forgeClaims"] = _forgeClaims.DeepClone(), ["forgeMatrix"] = new JArray(ForgeGoalInventory.Select(goal => _forgeGoals[goal.Id].DeepClone())),
            ["rewardUids"] = new JArray(_forgeReceipts.OrderBy(uid => uid)), ["earnedRatUid"] = claimed ? (int)_breeding["earnedRatUid"]! : 0
        };
        BreedingRequire("checkpoint-final-hash-binding", R.persistence["saveSha256"], Reflect.HashFile(path), (string?)R.persistence["saveSha256"] == Reflect.HashFile(path));
        _forgeGoals[BreedingGoal]["nativeBreedingStatus"] = _breeding["status"]!.DeepClone();
        yield return null;
    }

    private void BreedingBindFixtures()
    {
        GameItem Owned(int uid) => BreedingOwned().SingleOrDefault(item => item.uniqueId == uid) ?? throw new BreedingSafetyFailure("Missing or duplicated controlled fixture UID " + uid);
        _breedingCages.Clear(); foreach (var row in (JArray)_breeding["cages"]!) _breedingCages.Add(Owned((int)row["uid"]!));
        _breedingBays.Clear(); foreach (int uid in ((JArray)_breeding["bayUids"]!).Values<int>()) _breedingBays.Add(Owned(uid));
        _breedingSupplies = Owned((int)_breeding["suppliesUid"]!); _breedingMeatBag = Owned((int)_breeding["meatBagUid"]!); _breedingBlade = Owned((int)_breeding["bladeUid"]!);
        _forgeBags.Clear(); _forgeBags.Add(_breedingSupplies); _forgeBags.Add(_breedingMeatBag);
        BreedingRequire("native-fixture-space", new { cages = BreedingCageCount, geometry = "original native ingot upgrade to10x8, real feeder slot and robot" }, _breedingCages.Select(BreedingCage).ToArray(),
            _breedingCages.Count == BreedingCageCount && ((JArray)_breeding["cages"]!).All(row =>
            {
                var cage = Owned((int)row["uid"]!); var grid = AnimalCage.GetCageInventory(cage);
                return grid.inventoryShape.width == 10 && grid.inventoryShape.height == 8 && cage.GetTagReadonly("CAGE_SIZE_TAG").GetInt() == 8 &&
                    AnimalCage.GetDispenserSlot(cage).childItem?.uniqueId == (int)row["dispenserUid"]! && Owned((int)row["vacuumUid"]!).parentInventory == grid;
            }));
    }

    private GameItem BreedingInput(string id, string purpose)
    {
        BreedingIsolation(); var item = Spawn(id);
        BreedingMove(BreedingGrid(_breedingSupplies!), item, "real native fixture consumable " + id);
        BreedingFixtures.Add(new JObject { ["purpose"] = purpose, ["economicAcquisition"] = "not-tested; disclosed original registered factory input", ["day"] = StoreStation.GetDayCounter(), ["item"] = BreedingContent(item) });
        return item;
    }
    private void BreedingTarget(GameItem source, GameItem target, string action)
    {
        BreedingIsolation();
        if (!source.MayTarget(target) || !source.CanTarget(target)) throw new FixtureUnavailable("Native MayTarget/CanTarget rejected " + action + "; no target callback is forced");
        BreedingActions.Add(new JObject { ["action"] = action, ["day"] = StoreStation.GetDayCounter(), ["sourceUid"] = source.uniqueId, ["targetUid"] = target.uniqueId });
        source.Target(target);
    }
    private bool BreedingTryMove(GameInventory destination, GameItem item, string purpose)
    {
        if (ReferenceEquals(item.parentInventory, destination)) return true;
        var marker = destination.TryFindOneValidInventorySlot(item);
        if (marker == null || !marker.IsValid() || marker.inventory != destination) return false;
        int uid = item.uniqueId, count = item.unitCount; bool owned = GeneralHelper.IsItemOwned(item); int moved = marker.TryAcceptOnce();
        BreedingRequire("native-slot." + uid + "." + StoreStation.GetDayCounter(), new { sameUid = uid, count, owned, destination = destination.identifier },
            new { item.uniqueId, item.unitCount, owned = GeneralHelper.IsItemOwned(item), parent = item.parentInventory?.identifier, moved },
            moved == count && item.uniqueId == uid && item.unitCount == count && GeneralHelper.IsItemOwned(item) == owned && ReferenceEquals(item.parentInventory, destination), purpose);
        return true;
    }
    private void BreedingMove(GameInventory destination, GameItem item, string purpose)
    { if (!BreedingTryMove(destination, item, purpose)) throw new FixtureUnavailable("No real valid slot for " + purpose + ", uid=" + item.uniqueId + ", id=" + item.identifier + ", destination=" + destination.identifier); }
    private void BreedingRequire(string name, object? expected, object? observed, bool valid, string evidence = "")
    { R.Check("native-breeding." + name, expected, observed, valid, evidence); if (!valid) throw new BreedingSafetyFailure(name + " failed"); }
    private static bool BreedingValidTick(JObject previous, JObject tick, JArray growth)
    {
        int uid = (int)previous["uid"]!;
        var grows = growth.OfType<JObject>().Where(row => (int?)row["uid"] == uid).ToArray();
        if ((int?)tick["uid"] != uid || (int?)tick["ageBefore"] != (int?)previous["age"] ||
            (bool?)tick["sameGenes"] != true || !JToken.DeepEquals(previous["genes"], tick["genesBefore"])) return false;
        bool deadBefore = (bool?)tick["deadBefore"] == true, deadAfter = (bool?)tick["deadAfter"] == true;
        if (deadBefore)
            return deadAfter && (bool?)tick["nativeDeathBeforeEntry"] == true && (int?)tick["ageAfter"] == (int?)tick["ageBefore"] &&
                (float?)tick["weightAfter"] == (float?)tick["weightBefore"] && grows.Length == 0;
        if ((int?)tick["ageAfter"] != (int?)tick["ageBefore"] + 1 || (deadAfter && (bool?)tick["nativeDeathDuringTick"] != true)) return false;
        if (grows.Length == 1) return (bool?)grows[0]["valid"] == true && (bool?)grows[0]["dead"] == false;
        return grows.Length == 0 && deadAfter && (bool?)tick["nativeDeathDuringTick"] == true;
    }
    private sealed class BreedingSafetyFailure(string message) : Exception(message) { }
    private IEnumerable<GameItem> BreedingOwned() => Store.FindAllItem().Where(item => item.uniqueId > 0 && item.unitCount > 0 && GeneralHelper.IsItemOwned(item))
        .GroupBy(item => item.uniqueId).Select(group => group.Count() == 1 ? group.Single() : throw new BreedingSafetyFailure("Duplicate native owned UID " + group.Key));
    private IEnumerable<GameItem> BreedingRats() => BreedingOwned().Where(item => item.identifier == "rat");
    private IEnumerable<GameItem> BreedingSaleCuts() => BreedingOwned().Where(item => ((JArray)_breeding["meatUids"]!).Values<int>().Contains(item.uniqueId) &&
        item.GetTagReadonly("MEAT_TYPE_TAG").GetString() == "ANIMAL_RAT" && !item.IsTag("ANIMAL_PRION_TAG") && !GeneralHelper.IsHazardousWaste(item));
    private static GameInventory BreedingGrid(GameItem item) => item.contentWindow?.childElement as GameInventory ?? throw new BreedingSafetyFailure("Expected original native container grid: " + item.identifier);
    private static int BreedingAge(GameItem rat) => rat.GetTagReadonly("ANIMAL_AGE_TAG").GetInt();
    private static string BreedingSex(GameItem rat) => rat.GetTagReadonly("ANIMAL_SEXE_TAG").GetString();
    private static float BreedingWeight(GameItem rat)
    { float value = rat.GetTagReadonly("ANIMAL_WEIGHT_TAG").GetFloat(); if (float.IsNaN(value) || float.IsInfinity(value)) throw new BreedingSafetyFailure("Nonfinite real animal weight"); return value; }
    private static float BreedingExpression(GameItem rat) => (rat.GetTagReadonly("ANIMAL_GROWTH_RATE_TAG_ALLELE_A").GetFloat() + rat.GetTagReadonly("ANIMAL_GROWTH_RATE_TAG_ALLELE_B").GetFloat()) / 2f;
    private static float BreedingBestAllele(GameItem rat) => Math.Max(rat.GetTagReadonly("ANIMAL_GROWTH_RATE_TAG_ALLELE_A").GetFloat(), rat.GetTagReadonly("ANIMAL_GROWTH_RATE_TAG_ALLELE_B").GetFloat());
    private int BreedingGeneration(int uid) => (int?)BreedingLineage[uid.ToString()]?["generation"] ?? -1;
    private static JObject BreedingGenes(GameItem rat) => new(AnimalSpecies.Rat.geneticTraitIDs.OrderBy(gene => gene, StringComparer.Ordinal).Select(gene => new JProperty(gene,
        new JArray((double)rat.GetTagReadonly(gene + "_ALLELE_A").GetFloat(), (double)rat.GetTagReadonly(gene + "_ALLELE_B").GetFloat()))));
    private static JObject BreedingRat(GameItem rat) => new()
    {
        ["uid"] = rat.uniqueId, ["sex"] = BreedingSex(rat), ["age"] = BreedingAge(rat), ["weight"] = (double)BreedingWeight(rat),
        ["parentUid"] = rat.parentInventory?.GetParentItem()?.uniqueId ?? 0, ["owned"] = GeneralHelper.IsItemOwned(rat),
        ["genes"] = BreedingGenes(rat), ["growthExpression"] = (double)BreedingExpression(rat), ["growthBonus"] = (double)rat.GetTagReadonly("ANIMAL_GROWTH_RATE_TAG_BONUS").GetFloat(),
        ["growthBonusPercent"] = (double)rat.GetTagReadonly("ANIMAL_GROWTH_RATE_TAG_BONUS_PERCENT").GetFloat(),
        ["hunger"] = (double)rat.GetTagReadonly("ANIMAL_HUNGER_TAG").GetFloat(), ["health"] = (double)rat.GetTagReadonly("ANIMAL_HEALTH_TAG").GetFloat(),
        ["fertility"] = (double)rat.GetTagReadonly("ANIMAL_CURRENT_FERTILITY_TAG").GetFloat(), ["pregnant"] = rat.IsTag("ANIMAL_PREGNANT_TAG"),
        ["pregnancyProgress"] = (double)rat.GetTagReadonly("ANIMAL_PREGNANT_PROGRESS_TAG").GetFloat(), ["dead"] = HusbandryHelper.IsAnimalDead(rat),
        ["diseased"] = rat.IsTag("ANIMAL_DISEASED_TAG"), ["prion"] = rat.IsTag("ANIMAL_PRION_TAG"), ["starving"] = rat.IsTag("ANIMAL_IS_STARVING_TAG"),
        ["redPart"] = rat.GetTagReadonly(Liquid.GetCurrentPartFromId("livestock_steroid")).GetInt(), ["greenPart"] = rat.GetTagReadonly(Liquid.GetCurrentPartFromId("livestock_immunity")).GetInt()
    };
    private static JObject BreedingCage(GameItem cage) => new()
    {
        ["uid"] = cage.uniqueId, ["health"] = cage.GetTagReadonly("CAGE_HEALTH_TAG").GetInt(), ["maxHealth"] = cage.GetTagReadonly("CAGE_MAX_HEALTH_TAG").GetInt(),
        ["height"] = AnimalCage.GetCageInventory(cage).inventoryShape.height, ["width"] = AnimalCage.GetCageInventory(cage).inventoryShape.width,
        ["overcrowding"] = HusbandryHelper.GetAnimalOvercrowdingLevel(AnimalCage.GetCageInventory(cage)), ["items"] = new JArray(AnimalCage.GetCageInventory(cage).childItems.Select(item => item.uniqueId))
    };
    private JArray BreedingItems()
    {
        var controlled = new HashSet<int>(BreedingLineage.Properties().Select(property => (int)property.Value["uid"]!));
        foreach (var item in _breedingBays.Concat(_breedingCages).Concat(new[] { _breedingSupplies, _breedingMeatBag }).Where(item => item != null).Cast<GameItem>())
        { controlled.Add(item.uniqueId); foreach (var child in item.FindAllChildItems()) controlled.Add(child.uniqueId); }
        foreach (var fixture in BreedingFixtures.OfType<JObject>()) if ((int?)fixture["item"]?["uid"] is { } uid) controlled.Add(uid);
        foreach (int uid in ((JArray)_breeding["meatUids"]!).Values<int>()) controlled.Add(uid);
        foreach (int uid in _forgeReceipts) controlled.Add(uid);
        return new JArray(BreedingOwned().Where(item => controlled.Contains(item.uniqueId)).OrderBy(item => item.uniqueId).Select(item =>
        {
            var row = BreedingContent(item); row["parentUid"] = item.parentInventory?.GetParentItem()?.uniqueId ?? 0; row["parentInventory"] = item.parentInventory?.identifier ?? "";
            row["owned"] = GeneralHelper.IsItemOwned(item); return row;
        }));
    }
    private static JObject BreedingContent(GameItem item) => BreedingStored(item.identifier, item.uniqueId, item.unitCount, item.unitValue, item.state, item.modifiedShape);
    private static JObject BreedingStored(string id, int uid, int count, long value, TagSystem state, GridShape shape)
    {
        var tags = new JObject();
        foreach (var pair in state.dict.Where(pair => pair.Value.valueEnabled || pair.Value.valueInt != 0 || pair.Value.valueLong != 0 || pair.Value.valueFloat != 0 || pair.Value.valueDouble != 0 || pair.Value.valueBool || !string.IsNullOrEmpty(pair.Value.GetString())).OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var tag = pair.Value; tags[pair.Key] = JObject.FromObject(new { enabled = tag.valueEnabled, integer = tag.valueInt, longNumber = tag.valueLong, number = (double)tag.valueFloat, doubleNumber = tag.valueDouble, boolean = tag.valueBool, text = tag.GetString() ?? "" });
        }
        return new JObject { ["id"] = id, ["uid"] = uid, ["count"] = count, ["value"] = value, ["width"] = shape.width, ["height"] = shape.height,
            ["x"] = shape.minX, ["y"] = shape.minY, ["orientation"] = shape.orientation, ["flipped"] = shape.flipped,
            ["footprint"] = string.Join(",", Enumerable.Range(0, shape.height).SelectMany(y => Enumerable.Range(0, shape.width).Select(x => shape.GetLocal(x, y).ToString()))), ["tags"] = tags };
    }

    private void BreedingAppendNight(JObject row)
    {
        if (_breedingLedgerPath == null)
        {
            _breedingLedgerPath = Path.Combine(Path.GetDirectoryName(O.Output)!, "breeding-ledger." + R.processToken + ".jsonl");
            using (var stream = new FileStream(_breedingLedgerPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            using (var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false)))
                writer.WriteLine(new JObject { ["kind"] = "native-breeding-ledger", ["schema"] = 1, ["runId"] = O.RunId, ["processToken"] = R.processToken,
                    ["gameRunId"] = Store.runID, ["slotId"] = Store.saveSlotId, ["seedUids"] = _breeding["seedUids"]!.DeepClone(),
                    ["source"] = "Original game method observations and actual native care, no generated gameplay data" }.ToString(Formatting.None));
            ((JArray)_breeding["ledgerFiles"]!).Add(new JObject { ["path"] = _breedingLedgerPath, ["processToken"] = R.processToken, ["runId"] = O.RunId, ["nightRows"] = 0, ["sha256"] = Reflect.HashFile(_breedingLedgerPath) });
        }
        row["runId"] = O.RunId; row["processToken"] = R.processToken; row["gameRunId"] = Store.runID; row["slotId"] = Store.saveSlotId;
        File.AppendAllText(_breedingLedgerPath, row.ToString(Formatting.None) + Environment.NewLine, new System.Text.UTF8Encoding(false)); _breedingLedgerRows++;
        // The full append-only record remains in the bound artifact. ES3 keeps
        // the latest day and complete pedigree, not copies of its own history.
        BreedingJournal.Clear(); BreedingJournal.Add(row);
        R.environment["nativeBreedingCurrentLedger"] = new { path = _breedingLedgerPath, rows = _breedingLedgerRows };
    }

    private static JObject BreedingNightEvidence(NightObservation night)
    {
        var automatic = night.AutoSave;
        return new JObject
        {
            ["events"] = new JArray(night.Events.Select(row => row.DeepClone())), ["entered"] = JObject.FromObject(night.Entered), ["returned"] = JObject.FromObject(night.Returned),
            ["invoice"] = night.Invoice.DeepClone(), ["invoiceTotal"] = night.InvoiceTotal, ["errors"] = JArray.FromObject(night.Errors), ["observerErrors"] = new JArray(night.ObserverErrors),
            ["minimumTimeScale"] = night.MinimumTimeScale, ["maximumTimeScale"] = night.MaximumTimeScale,
            ["automaticSave"] = automatic == null ? null : new JObject
            {
                ["valid"] = automatic["valid"]!.DeepClone(), ["path"] = automatic["path"]!.DeepClone(), ["sha256"] = automatic["sha256"]!.DeepClone(),
                ["gameRunId"] = automatic["gameRunId"]!.DeepClone(), ["slotId"] = automatic["slotId"]!.DeepClone(), ["day"] = automatic["day"]!.DeepClone(),
                ["cash"] = automatic["cash"]!.DeepClone(), ["beforeWaking"] = automatic["beforeWaking"]!.DeepClone(),
                ["exactModData"] = JToken.DeepEquals(automatic["savedModData"], automatic["expectedModData"]),
                ["modDataKeys"] = new JArray(((JObject)automatic["savedModData"]!).Properties().Select(property => property.Name).OrderBy(key => key, StringComparer.Ordinal))
            }
        };
    }

    // Passive original-method observers. Prefixes never return bool, replace
    // an argument/result, change RNG state, or call a breeding/growth method.
    private sealed class BreedingObservation : IDisposable
    {
        private static BreedingObservation? Active;
        private readonly ProbeContext Context;
        private readonly Harmony Hooks;
        private readonly List<MethodInfo> Patched = new();
        private readonly Dictionary<int, JObject> BeforeTick = new(), BeforeGrowth = new(), BeforeDeathState = new();
        private readonly Dictionary<int, int> TickEntryOrdinal = new(), DeathEntryOrdinal = new();
        internal readonly JArray DayTicks = new(), DayMatings = new(), DayBirths = new(), DayGrowth = new(), DayDeaths = new(), Errors = new();
        private int Day, EventOrdinal;
        internal BreedingObservation(ProbeContext context)
        {
            if (Active != null) throw new BreedingSafetyFailure("An original breeding observation is already active");
            Context = context; Hooks = new Harmony("nicokobo.content_native_probe.breeding." + context.O.RunId);
            try
            {
                Patch(typeof(HusbandryHelper).GetMethod(nameof(HusbandryHelper.OnPregnant))!, null, nameof(Pregnant));
                Patch(typeof(GeneticHelper).GetMethod(nameof(GeneticHelper.ApplyInheritedGenetics))!, null, nameof(Inherited));
                Patch(typeof(HusbandryHelper).GetMethod(nameof(HusbandryHelper.OnAnimalItemDayEnd))!, nameof(BeforeAnimal), nameof(AfterAnimal));
                Patch(typeof(HusbandryHelper).GetMethod(nameof(HusbandryHelper.OnAnimalDead))!, nameof(BeforeDeath), nameof(AfterDeath));
                Patch(typeof(HusbandryHelper).GetMethod(nameof(HusbandryHelper.GrowAnimal))!, nameof(BeforeGrow), nameof(AfterGrow));
                Active = this;
            }
            catch { Dispose(); throw; }
        }
        internal void BeginDay(int day)
        { Day = day; EventOrdinal = 0; DayTicks.Clear(); DayMatings.Clear(); DayBirths.Clear(); DayGrowth.Clear(); DayDeaths.Clear(); BeforeTick.Clear(); BeforeGrowth.Clear(); BeforeDeathState.Clear(); TickEntryOrdinal.Clear(); DeathEntryOrdinal.Clear(); }
        private void Patch(MethodInfo method, string? prefix, string? postfix)
        {
            if (method == null) throw new BreedingSafetyFailure("Expected original breeding method is absent");
            Hooks.Patch(method, prefix: prefix == null ? null : new HarmonyMethod(typeof(BreedingObservation), prefix), postfix: postfix == null ? null : new HarmonyMethod(typeof(BreedingObservation), postfix)); Patched.Add(method);
        }
        private void Observe(Action action) { try { action(); } catch (Exception error) { Errors.Add(error.ToString()); Context._breedingCheckpointUnsafe = true; } }
        private static void Pregnant(GameItem __0, GameItem __1)
        {
            if (Active is not { } o) return;
            o.Observe(() =>
            {
                if (o.Context.BreedingGeneration(__0.uniqueId) < 0 || o.Context.BreedingGeneration(__1.uniqueId) < 0) throw new BreedingSafetyFailure("Untracked original mating parent");
                var callers = OriginalCallers();
                if (!callers.Any(caller => caller.Contains("TryMate") || caller.Contains("CheckMate")) || !callers.Any(caller => caller.Contains("EndNight"))) throw new BreedingSafetyFailure("Mating was not reached through the original full-night path");
                ((JObject)o.Context._breeding["pendingFathers"]!)[__0.uniqueId.ToString()] = __1.uniqueId;
                o.DayMatings.Add(new JObject { ["day"] = o.Day, ["motherUid"] = __0.uniqueId, ["fatherUid"] = __1.uniqueId, ["mother"] = BreedingRat(__0), ["father"] = BreedingRat(__1), ["source"] = "original HusbandryHelper.OnPregnant postfix", ["callers"] = new JArray(callers) });
            });
        }
        private static void Inherited(GameItem __0, GameItem __1)
        {
            if (Active is not { } o) return;
            o.Observe(() =>
            {
                int father = (int?)o.Context._breeding["pendingFathers"]?[__1.uniqueId.ToString()] ?? 0;
                int motherGeneration = o.Context.BreedingGeneration(__1.uniqueId), fatherGeneration = o.Context.BreedingGeneration(father);
                if (motherGeneration < 0 || fatherGeneration < 0 || __0.uniqueId <= 0 || o.Context.BreedingLineage[__0.uniqueId.ToString()] != null)
                    throw new BreedingSafetyFailure("Native inheritance has unbound parents/duplicate child identity");
                var callers = OriginalCallers();
                if (!callers.Any(caller => caller.Contains("GiveBirth")) || !callers.Any(caller => caller.Contains("OnAnimalItemDayEnd")) || !callers.Any(caller => caller.Contains("EndNight")))
                    throw new BreedingSafetyFailure("Inheritance was not reached through original GiveBirth and the original full-night path");
                var birth = new JObject
                {
                    ["uid"] = __0.uniqueId, ["motherUid"] = __1.uniqueId, ["fatherUid"] = father, ["generation"] = Math.Max(motherGeneration, fatherGeneration) + 1,
                    ["birthNight"] = o.Day, ["birthDay"] = StoreStation.GetDayCounter(), ["source"] = "original GiveBirth -> original ApplyInheritedGenetics postfix; original RNG unmodified",
                    ["genes"] = BreedingGenes(__0), ["motherGenes"] = BreedingGenes(__1), ["entry"] = BreedingRat(__0), ["callers"] = new JArray(callers)
                };
                o.Context.BreedingLineage[__0.uniqueId.ToString()] = birth; o.DayBirths.Add(birth.DeepClone());
            });
        }
        private static void BeforeAnimal(GameItem __0)
        {
            if (Active is not { } o) return;
            o.Observe(() => { o.BeforeTick[__0.uniqueId] = BreedingRat(__0); o.TickEntryOrdinal[__0.uniqueId] = ++o.EventOrdinal; });
        }
        private static void BeforeDeath(GameItem __0)
        {
            if (Active is not { } o) return;
            o.Observe(() => { o.BeforeDeathState[__0.uniqueId] = BreedingRat(__0); o.DeathEntryOrdinal[__0.uniqueId] = ++o.EventOrdinal; });
        }
        private static void AfterDeath(GameItem __0)
        {
            if (Active is not { } o) return;
            o.Observe(() => o.DayDeaths.Add(new JObject
            {
                ["uid"] = __0.uniqueId, ["day"] = o.Day, ["enterOrdinal"] = o.DeathEntryOrdinal[__0.uniqueId], ["returnOrdinal"] = ++o.EventOrdinal,
                ["deadBefore"] = o.BeforeDeathState[__0.uniqueId]["dead"]!.DeepClone(), ["deadAfter"] = HusbandryHelper.IsAnimalDead(__0),
                ["before"] = o.BeforeDeathState[__0.uniqueId].DeepClone(), ["after"] = BreedingRat(__0),
                ["source"] = "passive original HusbandryHelper.OnAnimalDead entry/return, no mortality substitution", ["callers"] = new JArray(OriginalCallers())
            }));
        }
        private static void AfterAnimal(GameItem __0)
        {
            if (Active is not { } o) return;
            o.Observe(() =>
            {
                var before = o.BeforeTick[__0.uniqueId];
                int entered = o.TickEntryOrdinal[__0.uniqueId], returned = ++o.EventOrdinal;
                var deaths = o.DayDeaths.OfType<JObject>().Where(row => (int?)row["uid"] == __0.uniqueId && (bool?)row["deadBefore"] == false && (bool?)row["deadAfter"] == true).ToArray();
                o.DayTicks.Add(new JObject { ["uid"] = __0.uniqueId, ["ageBefore"] = before["age"], ["ageAfter"] = BreedingAge(__0),
                    ["enterOrdinal"] = entered, ["returnOrdinal"] = returned, ["deadBefore"] = before["dead"]!.DeepClone(),
                    ["nativeDeathBeforeEntry"] = deaths.Any(row => (int)row["returnOrdinal"]! < entered),
                    ["nativeDeathDuringTick"] = deaths.Any(row => (int)row["enterOrdinal"]! > entered && (int)row["returnOrdinal"]! < returned),
                    ["genesBefore"] = before["genes"]!.DeepClone(), ["weightBefore"] = before["weight"]!.DeepClone(), ["weightAfter"] = (double)BreedingWeight(__0),
                    ["hungerBefore"] = before["hunger"], ["hungerAfter"] = (double)__0.GetTagReadonly("ANIMAL_HUNGER_TAG").GetFloat(),
                    ["redBefore"] = before["redPart"], ["redAfter"] = __0.GetTagReadonly(Liquid.GetCurrentPartFromId("livestock_steroid")).GetInt(),
                    ["greenBefore"] = before["greenPart"], ["greenAfter"] = __0.GetTagReadonly(Liquid.GetCurrentPartFromId("livestock_immunity")).GetInt(),
                    ["sameGenes"] = JToken.DeepEquals(before["genes"], BreedingGenes(__0)), ["deadAfter"] = HusbandryHelper.IsAnimalDead(__0), ["after"] = BreedingRat(__0) });
            });
        }
        private static void BeforeGrow(GameItem __0)
        {
            if (Active is not { } o) return;
            o.Observe(() =>
            {
                var row = BreedingRat(__0); float expressed = BreedingExpression(__0), bonus = __0.GetTagReadonly("ANIMAL_GROWTH_RATE_TAG_BONUS").GetFloat(), percent = __0.GetTagReadonly("ANIMAL_GROWTH_RATE_TAG_BONUS_PERCENT").GetFloat();
                int age = BreedingAge(__0), stage = age >= 14 ? 5 : age >= 11 ? 4 : age >= 5 ? 3 : age >= 3 ? 2 : age >= 1 ? 1 : 0;
                float longevity = ((__0.GetTagReadonly("ANIMAL_LONGEVITY_TAG_ALLELE_A").GetFloat() + __0.GetTagReadonly("ANIMAL_LONGEVITY_TAG_ALLELE_B").GetFloat()) / 2f + __0.GetTagReadonly("ANIMAL_LONGEVITY_TAG_BONUS").GetFloat()) * (1f + __0.GetTagReadonly("ANIMAL_LONGEVITY_TAG_BONUS_PERCENT").GetFloat());
                float baseGain = stage is 1 or 2 ? 75f : stage == 3 ? 100f / 6f : stage == 4 ? -50f / 3f : stage == 0 ? 45f : longevity > 1f && age < 14f * longevity ? -50f / 3f : 0f;
                float gain = baseGain * ((expressed + bonus) * (1f + percent)), hunger = __0.GetTagReadonly("ANIMAL_HUNGER_TAG").GetFloat();
                if (__0.IsTag("ANIMAL_IS_STARVING_TAG")) gain = -Math.Abs(gain) * .5f;
                else if (hunger < 400f) gain *= 1f - (400f - hunger) / 400f * .5f;
                int minimum = Mathf.RoundToInt(new[] { 5, 50, 200, 350, 450, 400 }[stage] * .25f);
                row["expectedGain"] = Mathf.RoundToInt(gain); row["expectedWeight"] = (double)Math.Max(minimum, BreedingWeight(__0) + Mathf.RoundToInt(gain));
                row["source"] = "passive GrowAnimal entry, independent literal Build26317870 stage ledger, not a call to GetAnimalCurrentWeightChange/SimulateWeightAtAge";
                o.BeforeGrowth[__0.uniqueId] = row;
            });
        }
        private static void AfterGrow(GameItem __0)
        {
            if (Active is not { } o) return;
            o.Observe(() =>
            {
                var row = o.BeforeGrowth[__0.uniqueId]; row["actualWeight"] = (double)BreedingWeight(__0); row["valid"] = (float)row["expectedWeight"]! == BreedingWeight(__0);
                o.DayGrowth.Add(row);
            });
        }
        private static string[] OriginalCallers() => new System.Diagnostics.StackTrace().GetFrames().Select(frame => frame.GetMethod()).Where(method => method != null)
            .Select(method => (method!.DeclaringType?.FullName ?? "") + "." + method.Name).Take(40).ToArray();
        public void Dispose()
        {
            if (ReferenceEquals(Active, this)) Active = null;
            foreach (var method in Patched) Hooks.Unpatch(method, HarmonyPatchType.All, Hooks.Id);
        }
    }
}
