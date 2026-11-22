using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Nicokobo.ContentNativeProbe;

internal sealed partial class ProbeContext
{
    private const string StoryEvidencePrefix = "nicokobo.aug.story.evidence.";
    private const string StoryCoreId = "nicokobo.aug.story.distress_core";
    private const string StoryReceiverId = "nicokobo.aug.cybernetic.communication_receiver";
    private readonly JArray _fullStoryActions = new(), _fullStoryFixtures = new(), _fullStoryVisits = new();
    private readonly HashSet<string> _fullStoryCompleted = new(StringComparer.Ordinal);
    private readonly List<GameItem> _fullStoryStores = new();
    private readonly Dictionary<string, int> _fullStoryProofUids = new(StringComparer.Ordinal);
    private readonly JArray _fullStoryNightStorage = new();
    private Type FullStoryWorkshop => Reflect.Type("AugMechanicalAscension.Integration.NicoWorkshop");
    private Type FullStoryWorkshopUi => Reflect.Type("Nicokobo.ContentInterop.OfficialWorkshopApi");
    private JObject FullStoryWorkshopState() => (JObject)Reflect.Json(Reflect.Get(FullStoryWorkshop, "_state"));
    private static readonly string[] FullStoryEndings = { "legal_property", "settled_account", "lost_and_found", "beyond_the_world" };
    // These are independent earliest dates for the selected ordinary new-run
    // choices. No StoryRules.VisitDay call supplies the expected calendar.
    private static readonly int[] FullStoryEventDays = { 2, 9, 15, 15, 15, 22, 22, 29, 29, 36 };
    private static readonly (float X, float Y)[] FullStoryNodePositions =
    { (-.72f, -.85f), (-.36f, -.50f), (.94f, -.85f), (.72f, -.56f), (.50f, -.27f),
      (.94f, .85f), (.72f, .56f), (.50f, .27f), (-.98f, .84f), (-.78f, .52f), (-.60f, .20f), (-.44f, -.12f), (0f, 0f) };
    private sealed class FullStoryGoal(string suffix, string proof, string prerequisite, params (string Id, int Count)[] rewards)
    {
        internal readonly string Id = "nicokobo.aug." + suffix, Proof = proof, Prerequisite = prerequisite;
        internal readonly (string Id, int Count)[] Rewards = rewards;
    }
    private static readonly FullStoryGoal[] FullStoryGoals =
    {
        new("infrared_vision", "nicokobo.aug.cybernetic.infrared_vision", "", ("metal_scanner", 1)),
        new("cyber_eye", GraylineMaterial + "cyber_eye", "nicokobo.aug.infrared_vision", ("energy_credit_ext", 1)),
        new("artificial_ankle", "nicokobo.aug.cybernetic.artificial_ankle", "", ("welder", 1)),
        new("sleep_control", GraylineMaterial + "sleep_compression_module", "nicokobo.aug.artificial_ankle", ("turbo_booster_adv", 1)),
        new("communication_receiver", StoryReceiverId, "nicokobo.aug.sleep_control", ("energy_credit_breeder", 1)),
        new("titanium_skeleton", GraylineMaterial + "titanium_exoskeleton", "", ("nicokobo.mechcore_protocol.module_matrix.performance_conversion_node", 1)),
        new("kinetic_recovery", GraylineMaterial + "kinetic_recovery", "nicokobo.aug.titanium_skeleton", ("nicokobo.mechcore_protocol.module_matrix.efficiency_conversion_node", 1)),
        new("fine_motor_assistant", GraylineMaterial + "fine_motor_assistant", "nicokobo.aug.kinetic_recovery", ("nicokobo.mechcore_protocol.module_matrix.quality_conversion_node", 1)),
        new("synthetic_vocal_cords", "nicokobo.aug.cybernetic.synthetic_vocal_cords", "", ("surgery_tool", 1)),
        new("mimetic_skin", "nicokobo.aug.cybernetic.mimetic_skin", "nicokobo.aug.synthetic_vocal_cords", ("surgery_tool", 1)),
        new("ai_assistant_core", GraylineMaterial + "ai_core", "nicokobo.aug.mimetic_skin", ("nicokobo.aug.brain_computer_interface_module", 1)),
        new("artificial_heart", "nicokobo.aug.cybernetic.artificial_heart", "nicokobo.aug.ai_assistant_core", ("nicokobo.aug.brain_computer_interface_module", 1)),
        new("mechanical_ascension", "", "", ("nicokobo.aug.brain_computer_interface_module", 1), ("system_uncapped_neural_core", 1))
    };

    // Four independent fresh slots are required. DuplicateSlot is a persistence
    // check for the same ending; it is never used to branch or replay a decision.
    internal IEnumerable<object?> FullStory(string endingId)
    {
        if (!FullStoryEndings.Contains(endingId)) throw new ArgumentException("Unknown story ending", nameof(endingId));
        bool hidden = endingId == "beyond_the_world";
        string[] required = Enumerable.Range(1, 10).Select(i => "main.E" + i.ToString("00"))
            .Concat(FullStoryGoals.Select(goal => "achievement." + goal.Id))
            .Concat(hidden ? new[] { "hidden.x1", "hidden.x2", "hidden.x3" } : Array.Empty<string>())
            .Concat(new[] { "fixture.ten-events", "ending." + endingId, "epilogue." + endingId }).ToArray();
        R.environment["fullStoryScope"] = "Independent fresh Aug MODDED run, supported InitialCredits=10000 config; real factory machines/materials/batteries/native smuggler storage, twelve manufactured proofs, visible workshop node/claim button events, directly requested live test visitors; only the required overnight experiment/epilogue cycles use EndNight/ES3. Story item actions invoke the same guarded ExecuteItemAction business callback as the context menu; those menu rows and mouse targeting are not UI coverage. Generated visitor requests use RequestTestVisit(real=true), the shipped guarded live-event entry, not preview or DebugUnlock. Unrelated customer queues are cleared as fixture setup; no story completion/claim/result flags are assigned.";
        R.environment["fullStoryEnding"] = endingId;
        R.environment["fullStoryRequired"] = required;
        R.environment["fullStoryActions"] = _fullStoryActions;
        R.environment["fullStoryFixtures"] = _fullStoryFixtures;
        R.environment["fullStoryVisits"] = _fullStoryVisits;
        R.environment["graylineActions"] = _graylineActions;
        R.environment["fullStoryNightStorage"] = _fullStoryNightStorage;
        R.environment["fullStoryStoragePreparation"] = "Test-fixture preparation only: original manufactured UID custody in real 8x8 smuggler_bay_mod grids via native slot admission. Fixed natively ineligible devices may remain in place. Ordinary supplies/rewards/tools and all live evidence are checked before every night; native theft stays enabled. First old run reached D22 with three proofs absent and burglary losses, but its first R.error remains unrecoverable behind the old Vector2 writer failure.";
        try
        {
            foreach (object? wait in FullStoryWait("story.opening-ready", () => AugReady() && Reflect.Get(GraylineFlow, "State") != null && Reflect.Get(FullStoryWorkshop, "_state") != null)) yield return wait;
            object run = AugState()!;
            var initial = GraylineStoryState();
            FullStoryRequire("story.natural-fresh-opening", "Fresh D1, 10000 supported starting credits, no story choices or debug game mode", new
            { day = StoreStation.GetDayCounter(), Store.playerCash, Store.devMode, state = initial, config = Reflect.Json(Reflect.Get(run, "ConfigSnapshot")), Store.runID, Store.saveSlotId },
                !O.Reload && StoreStation.GetDayCounter() == 1 && Store.playerCash == 10000 && !Store.devMode &&
                Reflect.Int(Reflect.Get(run, "ConfigSnapshot")!, "InitialCredits") == 10000 &&
                Reflect.Str(run, "StartId") == "nicokobo.aug.mechanical_ascension.start" &&
                (bool?)initial["CatchUpOpening"] == false && ((JArray)initial["Choices"]!).Count == 0 && initial["EndingId"]!.Type == JTokenType.Null);
            Reflect.Call(Reflect.Type("SpeedUp.SpeedUpSettings", "SpeedUp"), "RestoreNormalSpeed");
            yield return null;
            var speed = Reflect.Type("SpeedUp.SpeedUpSettings", "SpeedUp");
            FullStoryRequire("story.normal-time-and-dialogue", "1x native time and noninstant dialogue", new { Time.timeScale, instant = Reflect.Get(speed, "Instant"), global = Reflect.Get(speed, "GlobalEnabled") },
                Time.timeScale == 1f && !(bool)Reflect.Get(speed, "Instant")! && !(bool)Reflect.Get(speed, "GlobalEnabled")! &&
                new[] { "Multiplier", "QueueMultiplier", "ClientArriveSpeed", "ClientLeaveSpeed" }.All(name => Convert.ToSingle(Reflect.Get(speed, name)) == 1f));
            // Two real 8x8 bays replace the former ordinary backpacks. A
            // further bay may be prepared only after real cargo admission fails.
            for (int i = 0; i < 2; i++) FullStoryCreateStorage("Initial original-proof/supply custody " + i);
            var magnifier = Spawn("magnifier"); Retain(magnifier);
            _fullStoryFixtures.Add(JObject.FromObject(new { kind = "factory-inspection-tool", item = ItemState(magnifier) }));
            foreach (object? wait in FullStoryManufactureAndClaim()) yield return wait;
            // E07 captures offer eligibility when the supplier page is built.
            // Prepare its material before the automatic visitor is ever queued;
            // adding one after a disabled choice is rendered cannot enable it.
            if (endingId != "legal_property") FullStoryProvideElectronic("E07-before-visitor-offer-capture");
            if (hidden)
            {
                FullStoryProvideElectronic("evidence-copy-before-visit");
                FullStoryProvideElectronic("overnight-experiment-before-visit");
            }
            using var nights = new GraylineObservation(this);
            using var dialogue = new FullStoryDialogueObservation(this);
            R.environment["fullStoryNightObservation"] = nights.Evidence;
            R.environment["fullStoryNativeSelections"] = dialogue.Selections;
            R.environment["fullStoryObserverErrors"] = dialogue.Errors;
            string[] choices = FullStoryRouteChoices(endingId);
            int storyDayEventStart = nights.Events.Count;
            foreach (object? wait in GraylineOpen()) yield return wait;
            int nextEvent = 0;
            while (nextEvent < 10)
            {
                int day = StoreStation.GetDayCounter();
                string eventId = "E" + (nextEvent + 1).ToString("00");
                foreach (object? wait in FullStoryVisit(eventId, choices[nextEvent], endingId, dialogue)) yield return wait;

                if (eventId == "E01" && hidden)
                    foreach (object? wait in FullStoryInspect("broken", "ProstheticInspected")) yield return wait;
                if (eventId == "E03" && hidden)
                    foreach (object? wait in FullStoryInspect("photo", "PhotoInspected")) yield return wait;
                if (eventId == "E04" && endingId == "lost_and_found")
                {
                    int parts = FullStoryUnits(GraylineMaterial + "precision_parts");
                    int uid = FullStoryEvidence("broken").uniqueId;
                    foreach (object? wait in FullStoryItemAction(FullStoryEvidence("broken"), "salvage", "story.salvage-without-inspection", state => (string?)state["HiddenClosedReason"] == "prosthetic_salvaged")) yield return wait;
                    FullStoryRequire("story.salvage-real-removal-and-output", new { removedUid = uid, precisionParts = parts + 1 }, GraylineStoryState(),
                        !Store.FindAllItem().Any(item => item.uniqueId == uid) && FullStoryUnits(GraylineMaterial + "precision_parts") == parts + 1);
                }
                if (eventId == "E05" && hidden)
                    foreach (object? wait in FullStoryItemAction(FullStoryEvidence("repair"), "x1", "hidden.x1", state => (int?)state["Investigation"] == 1 && FullStoryHasEvidence("investigation"))) yield return wait;
                if (eventId == "E07")
                {
                    if (choices[7] == "share")
                    {
                        string copyAction = endingId == "lost_and_found" ? "copy_cash" : "copy_parts";
                        foreach (object? wait in FullStoryItemAction(FullStoryEvidence("recording"), copyAction, "story.evidence-copy", _ => FullStoryHasEvidence("copy"))) yield return wait;
                    }
                    if (hidden)
                        foreach (object? wait in FullStoryItemAction(FullStoryOwned(StoryReceiverId), "x2_start", "story.hidden-overnight-start", state => (int?)state["ExperimentDay"] == day && (int?)state["Investigation"] == 1)) yield return wait;
                }
                if (eventId == "E08" && choices[7] == "share")
                    foreach (object? wait in FullStoryItemAction(FullStoryOwned(StoryCoreId), "request_core_aid", "story.core-consent", state => (bool?)state["CoreConsented"] == true)) yield return wait;
                if (eventId == "E09" && hidden)
                    foreach (object? wait in FullStoryItemAction(FullStoryEvidence("credential"), "x3", "hidden.x3", state => (int?)state["Investigation"] == 3 && (bool?)state["OfferReserved"] == true)) yield return wait;
                nextEvent++;
                if (hidden && nextEvent == 7)
                {
                    FullStoryPackOwned();
                    int eventStart = storyDayEventStart;
                    foreach (object? wait in FixturePrepareNight()) yield return wait;
                    foreach (object? wait in GraylineNight(day, eventStart, nights)) yield return wait;
                    storyDayEventStart = nights.Events.Count;
                    foreach (object? wait in GraylineOpen()) yield return wait;
                    foreach (object? wait in FullStoryItemAction(FullStoryOwned(StoryReceiverId), "x2_read", "hidden.x2", state => (int?)state["Investigation"] == 2)) yield return wait;
                }
            }
            FullStoryRequire("story.fixture-ten-real-transactions", 10, _fullStoryVisits.Count,
                _fullStoryVisits.Count == 10 && nights.ObserverErrors.Count == 0 && dialogue.Errors.Count == 0);
            _fullStoryCompleted.Add("fixture.ten-events");
            FullStoryPackOwned();
            int endingDay = StoreStation.GetDayCounter(), endingEventStart = storyDayEventStart;
            foreach (object? wait in FixturePrepareNight()) yield return wait;
            foreach (object? wait in GraylineNight(endingDay, endingEventStart, nights)) yield return wait;
            foreach (object? wait in FullStoryEpilogue(endingId)) yield return wait;
            FullStoryPackOwned();
            R.persistence["fullStory"] = new JObject
            {
                ["fixtureMode"] = true, ["ending"] = endingId, ["story"] = GraylineStoryState(), ["workshop"] = FullStoryWorkshopState(),
                ["day"] = StoreStation.GetDayCounter(), ["cash"] = Store.playerCash, ["visits"] = _fullStoryVisits.DeepClone(),
                ["nights"] = nights.Nights.DeepClone(), ["nativeSelections"] = dialogue.Selections.DeepClone(),
                ["completed"] = new JArray(_fullStoryCompleted.OrderBy(value => value, StringComparer.Ordinal)),
                ["proofs"] = new JArray(FullStoryGoals.Where(goal => goal.Proof.Length > 0).Select(goal => ItemState(FullStoryOwned(goal.Proof)))),
                ["evidence"] = FullStoryEvidenceSnapshot(), ["copyPurpose"] = "same-ending-persistence-only; endings require independent fresh slots",
                ["manufacturedProofUids"] = JObject.FromObject(_fullStoryProofUids), ["nativeStorageNights"] = _fullStoryNightStorage.DeepClone()
            };
        }
        finally
        {
            R.environment["fullStoryCompleted"] = _fullStoryCompleted.OrderBy(value => value, StringComparer.Ordinal).ToArray();
            string[] unreached = required.Where(key => !_fullStoryCompleted.Contains(key)).ToArray();
            if (unreached.Length > 0)
            {
                try { R.environment["fullStoryBlocker"] = FullStoryStatus(); }
                catch (Exception error) { R.environment["fullStoryDiagnosticError"] = error.ToString(); }
            }
            foreach (string missing in unreached)
                R.Skip("story.unreached." + missing, "This required business outcome was not reached; inspect fullStoryActions, fullStoryVisits, fullStoryBlocker and the first failed check.");
        }
    }

    private static string[] FullStoryRouteChoices(string ending) => new[]
    {
        ending == "settled_account" ? "decline" : "investigate", "accept",
        ending is "legal_property" or "lost_and_found" ? "pretend" : "honest", "compare", "keep",
        ending == "legal_property" ? "refuse" : "buy",
        ending is "legal_property" or "lost_and_found" ? "withhold_name" : "disclose",
        ending is "lost_and_found" or "beyond_the_world" ? "share" : "hide", "read", ending
    };

    private IEnumerable<object?> FullStoryManufactureAndClaim()
    {
        object[] definitions = Reflect.Each(Reflect.Get(Reflect.Type("AugMechanicalAscension.Domain.WorkshopAchievements"), "Definitions")).ToArray();
        FullStoryRequire("story.workshop.catalog-denominator", "Exactly the twelve independently specified cybernetic goals and one final mixed reward", Reflect.Json(definitions),
            definitions.Length == 13 && definitions.Select(definition => Reflect.Str(definition, "Id")).SequenceEqual(FullStoryGoals.Select(goal => goal.Id)) &&
            definitions.Select((definition, index) => Reflect.Str(definition, "ProofItemId") == FullStoryGoals[index].Proof &&
                Reflect.Str(definition, "Prerequisite") == FullStoryGoals[index].Prerequisite &&
                Reflect.Each(Reflect.Get(definition, "Rewards")).Select(reward => (Reflect.Str(reward, "ItemId"), Reflect.Int(reward, "Count"))).SequenceEqual(FullStoryGoals[index].Rewards)).All(value => value));
        object[] recipes = Reflect.Each(Reflect.Call(MachineCatalog, "MachineIds")).Cast<string>()
            .SelectMany(machine => Reflect.Each(Reflect.Call(MachineCatalog, "Recipes", machine))).ToArray();
        for (int index = 0; index < FullStoryGoals.Length; index++)
        {
            var goal = FullStoryGoals[index];
            FullStoryPackOwned();
            if (goal.Proof.Length > 0)
            {
                object[] matching = recipes.Where(recipe => Reflect.Str(recipe, "OutputItemId") == goal.Proof).ToArray();
                FullStoryRequire("story.manufacturing.unique-recipe." + goal.Id, "One live recipe producing the required proof", matching.Select(recipe => Reflect.Str(recipe, "Id")).ToArray(), matching.Length == 1);
                using var fixture = CreateRecipeFixture(matching[0]);
                _fullStoryFixtures.Add(JObject.FromObject(new
                {
                    kind = "factory-machine-inputs-and-battery", recipe = Reflect.Str(fixture.View, "Id"), machine = ItemState(fixture.Machine),
                    battery = ItemState(fixture.Battery), inputs = fixture.Inputs.Select(input => new { input.Required, item = ItemState(input.Item) }).ToArray()
                }));
                int checkStart = R.checks.Count;
                ObserveBatch(fixture, "story-proof." + goal.Id, true);
                FullStoryRequire("story.manufacturing.completed." + goal.Id, "Actual installed native cycle consumed the declared invoice and produced the proof", new { output = fixture.Output.childItems.Select(ItemState).ToArray() },
                    R.checks.Skip(checkStart).All(check => check.passed) && fixture.Output.childItems.Count == 1 && fixture.Output.childItems[0].identifier == goal.Proof,
                    "Proof products are never spawned. Factory preparation is limited to the actual machine, battery and recipe inputs.");
                GameItem proof = fixture.Output.childItems[0];
                GraylineTransfer(proof, EmporiumEntry.Instance.invElement, "manufactured-story-proof"); Retain(proof);
                FullStoryRequire("story.manufacturing.bind-original-uid." + goal.Id, "One original actually manufactured proof UID, never a substitute", ItemState(proof),
                    proof.uniqueId > 0 && !_fullStoryProofUids.ContainsKey(goal.Proof));
                _fullStoryProofUids.Add(goal.Proof, proof.uniqueId);
            }
            FullStoryPackOwned();
            var oldUids = Store.FindAllItem().Select(item => item.uniqueId).ToHashSet();
            var oldUnits = goal.Rewards.Select(reward => (reward.Id, Units: FullStoryUnits(reward.Id))).ToArray();
            int cash = Store.playerCash;
            FullStoryAction("workshop-open-chain", new { goal.Id });
            bool opened = (bool)Reflect.Call(FullStoryWorkshopUi, "OpenChain", "nicokobo.aug.workshop")!;
            yield return null;
            FullStoryRequire("story.workshop.actual-page." + goal.Id, "Visible real Aug workshop page and latched actual manufactured proof", FullStoryWorkshopState(),
                opened && FullStoryWorkshopWindow()?.IsVisible == true && (goal.Proof.Length == 0 ||
                    (int?)FullStoryWorkshopState()["Completed"]?[goal.Id]?["UniqueId"] == FullStoryOwned(goal.Proof).uniqueId));
            Button node = FullStoryNode(index);
            FullStoryAction("workshop-node-button", new { goal.Id, index }); node.onClick.Invoke();
            yield return null;
            FullStoryRequire("story.workshop.selected-node." + goal.Id, goal.Id, Reflect.Get(FullStoryWorkshopUi, "_selected"), Reflect.Str(FullStoryWorkshopUi, "_selected") == goal.Id);
            Button claim = FullStoryClaimButton(false);
            FullStoryRequire("story.workshop.claim-enabled." + goal.Id, "Visible enabled reward button after real prerequisites", new { claim.interactable, goal.Prerequisite },
                claim.gameObject.activeInHierarchy && claim.IsInteractable() && (goal.Prerequisite.Length == 0 || ((JArray)FullStoryWorkshopState()["Claimed"]!).Any(id => (string?)id == goal.Prerequisite)));
            FullStoryAction("workshop-claim-button", new { goal.Id }); claim.onClick.Invoke();
            yield return null;
            JObject state = FullStoryWorkshopState();
            var newItems = Store.FindAllItem().Where(item => !oldUids.Contains(item.uniqueId)).OrderBy(item => item.uniqueId).ToArray();
            bool rewardsMatch = newItems.Length == goal.Rewards.Sum(reward => reward.Count) && goal.Rewards.All(reward =>
                newItems.Count(item => item.identifier == reward.Id && item.unitCount == 1 && GeneralHelper.IsItemOwned(item)) == reward.Count &&
                FullStoryUnits(reward.Id) - oldUnits.Single(old => old.Id == reward.Id).Units == reward.Count);
            FullStoryRequire("story.workshop.reward-and-journal." + goal.Id, new { claimCount = index + 1, cash, rewards = goal.Rewards.Select(reward => new { reward.Id, reward.Count }).ToArray() },
                new { state, cash = Store.playerCash, newItems = newItems.Select(ItemState).ToArray() },
                ((JArray)state["Claimed"]!).Count == index + 1 && ((JArray)state["Claimed"]!).Any(id => (string?)id == goal.Id) &&
                (bool?)state["DebugAscensionUnlocked"] == false && state["Pending"]!.Type == JTokenType.Null && Store.playerCash == cash && rewardsMatch &&
                (goal.Proof.Length == 0 || FullStoryOwned(goal.Proof).unitCount == 1) &&
                newItems.Where(item => item.identifier is "energy_credit_ext" or "energy_credit_breeder").All(item => Energy(item) > 0 && Energy(item) == item.GetTagReadonly("power_source_item_max_energy").GetInt()));
            JToken ledger = FullStoryLedger();
            FullStoryRequire("story.workshop.claimed-button-disabled." + goal.Id, "Claimed button is disabled", FullStoryClaimButton(true).interactable, !FullStoryClaimButton(true).IsInteractable());
            FullStoryAction("explicit-workshop-claim-replay", new { goal.Id }); Reflect.Call(FullStoryWorkshop, "Claim", goal.Id);
            FullStoryRequire("story.workshop.no-repeat-reward." + goal.Id, ledger, FullStoryLedger(), JToken.DeepEquals(ledger, FullStoryLedger()));
            if (goal.Id == "nicokobo.aug.mechanical_ascension")
                foreach (object? wait in CaptureEvidenceFrame("all-thirteen-workshop-claims")) yield return wait;
            Reflect.Call(FullStoryWorkshopUi, "CloseWindow");
            _fullStoryCompleted.Add("achievement." + goal.Id);
            yield return null;
        }
        FullStoryRequire("story.workshop.twelve-real-proofs-final-ascension", "12 distinct owned manufactured proofs, 13 real claims, no debug unlock", FullStoryWorkshopState(),
            FullStoryGoals.Count(goal => goal.Proof.Length > 0 && FullStoryUnits(goal.Proof) == 1) == 12 &&
            (bool)Reflect.Call(FullStoryWorkshop, "HasMechanicalAscension", AugState()!)!);
    }

    private CustomUIWindow? FullStoryWorkshopWindow() => Reflect.Get(FullStoryWorkshopUi, "_window") as CustomUIWindow;
    private Button FullStoryNode(int index)
    {
        var p = FullStoryNodePositions[index]; var expected = new Vector2(324 + p.X * 250, -(310 + p.Y * 225));
        float size = index == 12 ? 80 : 48;
        var nodes = FullStoryWorkshopWindow()!.root.GetComponentsInChildren<Button>().Where(button =>
            button.gameObject.activeInHierarchy && button.IsInteractable() && button.transform is RectTransform rect &&
            Vector2.Distance(rect.anchoredPosition, expected) < .5f && Mathf.Abs(rect.sizeDelta.x - size) < .5f && Mathf.Abs(rect.sizeDelta.y - size) < .5f).ToArray();
        FullStoryRequire("story.workshop.real-node-locator." + index, new { expected, size, unique = true }, nodes.Length, nodes.Length == 1);
        return nodes[0];
    }
    private Button FullStoryClaimButton(bool claimed)
    {
        string[] labels = claimed ? new[] { "已领取", "Claimed" } : new[] { "领取奖励", "Claim reward" };
        var buttons = FullStoryWorkshopWindow()!.root.GetComponentsInChildren<Button>().Where(button => button.gameObject.activeInHierarchy &&
            button.GetComponentsInChildren<TextMeshProUGUI>(true).Any(label => labels.Contains(label.text))).ToArray();
        FullStoryRequire("story.workshop.real-claim-locator", labels, buttons.Length, buttons.Length == 1);
        return buttons[0];
    }

    private IEnumerable<object?> FullStoryVisit(string eventId, string option, string ending, FullStoryDialogueObservation observation)
    {
        bool requested = (bool)Reflect.Call(GraylineFlow, "RequestTestVisit", eventId, true)!;
        FullStoryRequire("story." + eventId + ".generated-live-visitor", true, requested, requested);
        foreach (object? wait in FullStoryServeUntil(() => Store.currentClientInstance?.GetClientBlueprint()?.identifier == GraylineStoryActor && Store.isClientArrived &&
            (string?)GraylineStoryState()["ActiveEventId"] == eventId && (int?)GraylineStoryState()["EncounterDay"] == StoreStation.GetDayCounter(), GraylineStoryActor, "story." + eventId + ".natural-arrival")) yield return wait;
        FullStoryRequire("story." + eventId + ".calendar-and-ownership", new { day = StoreStation.GetDayCounter(), eventId, ownedKinds = 12, fixtureMode = true }, GraylineStoryState(),
            StoreStation.GetDayCounter() >= 1 &&
            FullStoryGoals.Count(goal => goal.Proof.Length > 0 && FullStoryUnits(goal.Proof) > 0) == 12);
        if (eventId == "E07")
            foreach (object? wait in FullStoryRecordingVisit(ending)) yield return wait;
        JObject before = GraylineStoryState(); int cash = Store.playerCash;
        var beforePhysical = Store.FindAllItem().Where(GeneralHelper.IsItemOwned).ToDictionary(item => item.uniqueId, item => item.identifier);
        int nativeStart = observation.Selections.Count;
        if (eventId == "E10" && ending == "beyond_the_world")
        {
            FullStoryRequire("story.E10.hidden-preconditions", "Three investigations complete, real communicator and one-use card retained", before,
                (int?)before["Investigation"] == 3 && (bool?)before["ProstheticInspected"] == true && (bool?)before["DiagnosticRetained"] == true &&
                (bool?)before["OfferReserved"] == true && (string?)before["HiddenClosedReason"] == "none" && FullStoryUnits(StoryReceiverId) == 1 && FullStoryHasEvidence("credential"));
            FullStoryAction("await-native-automatic-E10", new { choiceInputByProbe = false, automaticDelaySeconds = .6 });
            foreach (object? wait in FullStoryWait("story.E10.automatic-native-commit", () => (string?)GraylineStoryState()["EndingId"] == ending, 150f))
            { GraylineAdvanceDialogue(); yield return wait; }
        }
        else
        {
            var label = FullStoryChoiceLabels(eventId, option, before);
            foreach (object? wait in FullStoryChoose(label.Zh, label.En, eventId + "." + option)) yield return wait;
            foreach (object? wait in FullStoryWait("story." + eventId + ".commit", () => ((JArray)GraylineStoryState()["Choices"]!).Any(choice => (string?)choice["EventId"] == eventId && (string?)choice["OptionId"] == option))) yield return wait;
        }
        JObject after = GraylineStoryState();
        int delta = eventId == "E01" && option == "investigate" ? -60 : eventId == "E06" && option == "buy" ? -200 : 0;
        var recorded = ((JArray)after["Choices"]!).Last();
        bool nativeSelection = observation.Selections.Skip(nativeStart).Any(selection => (string?)selection["eventId"] == eventId &&
            ((string?)selection["label"] == FullStoryChoiceLabels(eventId, option, before).Zh || (string?)selection["label"] == FullStoryChoiceLabels(eventId, option, before).En));
        FullStoryRequire("story." + eventId + ".real-choice-and-payment", new { option, day = StoreStation.GetDayCounter(), cash = cash + delta, nativeSelection = true }, new { after, cash = Store.playerCash, nativeSelections = observation.Selections.Skip(nativeStart).ToArray() },
            Store.playerCash == cash + delta && after["Pending"]!.Type == JTokenType.Null && ((JArray)after["Choices"]!).Count == ((JArray)before["Choices"]!).Count + 1 &&
            JToken.DeepEquals(new JArray(((JArray)after["Choices"]!).Take(((JArray)before["Choices"]!).Count)), before["Choices"]) &&
            (string?)recorded["EventId"] == eventId && (string?)recorded["OptionId"] == option && (int?)recorded["Day"] == StoreStation.GetDayCounter() && nativeSelection);
        string[] grants = eventId switch
        {
            "E01" => option == "decline" ? new[] { "receipt" } : new[] { "receipt", "broken" }, "E02" => new[] { "index" },
            "E03" => new[] { "photo" }, "E04" => new[] { "repair" }, "E05" => new[] { "contract" }, "E09" => new[] { "offer" },
            "E10" when option == "legal_property" => new[] { "ownership" }, "E10" when option == "settled_account" => new[] { "execution" }, _ => Array.Empty<string>()
        };
        FullStoryRequire("story." + eventId + ".physical-evidence", grants, FullStoryEvidenceSnapshot(), grants.All(FullStoryHasEvidence) &&
            ((JArray)after["Items"]!).Where(receipt => (bool?)receipt["Removed"] != true).All(receipt => FullStoryHasEvidence((string)receipt["Key"]!)));
        if (eventId == "E06") FullStoryRequire("story.E06.core-physical-result", option == "buy" ? "Exactly one real core with the committed UID" : "No core delivered", after,
            option == "buy" ? FullStoryUnits(StoryCoreId) == 1 && FullStoryOwned(StoryCoreId).uniqueId == (int?)after["CoreUid"] && (string?)after["CoreDisposition"] == "held" : FullStoryUnits(StoryCoreId) == 0 && (string?)after["CoreDisposition"] == "none");
        if (eventId == "E08") FullStoryRequire("story.E08.copy-handover", new { shared = option == "share" }, after,
            option == "share" ? (bool?)after["CopyDelivered"] == true && FullStoryUnits(StoryEvidencePrefix + "copy") == 0 &&
                beforePhysical.Any(item => item.Value == StoryEvidencePrefix + "copy" && !Store.FindAllItem().Any(current => current.uniqueId == item.Key)) : (bool?)after["CopyDelivered"] == false);
        if (eventId == "E10")
        {
            string[] removals = option == "legal_property" ? new[] { "credential", "receipt", "index", "repair", "contract", "recording", "offer" } : new[] { "credential" };
            FullStoryRequire("story.E10.once-only-physical-closure", new { ending = option, removals, coreDelivered = option == "lost_and_found" }, after,
                (string?)after["EndingId"] == option && (int?)after["EndingDay"] == StoreStation.GetDayCounter() && removals.All(key =>
                    beforePhysical.Any(item => item.Value == StoryEvidencePrefix + key) && !Store.FindAllItem().Any(item => item.identifier == StoryEvidencePrefix + key && GeneralHelper.IsItemOwned(item))) &&
                (option != "lost_and_found" || FullStoryUnits(StoryCoreId) == 0 && (string?)after["CoreDisposition"] == "delivered"));
            _fullStoryCompleted.Add("ending." + ending);
        }
        string[] bonusGrants = eventId switch
        {
            "E02" => new[] { GraylineMaterial + "titanium_alloy" },
            "E04" => new[] { GraylineMaterial + "advanced_circuit" },
            "E06" when option == "buy" => new[] { StoryCoreId },
            "E07" when option == "withhold_name" => new[] { GraylineMaterial + "control_chip" },
            "E09" => new[] { GraylineMaterial + "power_regulator" }, _ => Array.Empty<string>()
        };
        string[] expectedNewIds = grants.Select(key => StoryEvidencePrefix + key).Concat(bonusGrants).OrderBy(id => id, StringComparer.Ordinal).ToArray();
        string[] removalKeys = eventId == "E08" && option == "share" ? new[] { "copy" } :
            eventId != "E10" ? Array.Empty<string>() : option == "legal_property" ?
                new[] { "credential", "receipt", "index", "repair", "contract", "recording", "offer" } : new[] { "credential" };
        string[] expectedRemovedIds = removalKeys.Select(key => StoryEvidencePrefix + key)
            .Concat(eventId == "E10" && option == "lost_and_found" ? new[] { StoryCoreId } : Array.Empty<string>()).OrderBy(id => id, StringComparer.Ordinal).ToArray();
        var currentItems = Store.FindAllItem().Where(GeneralHelper.IsItemOwned).ToArray();
        var currentUids = currentItems.Select(item => item.uniqueId).ToHashSet();
        var newItems = currentItems.Where(item => !beforePhysical.ContainsKey(item.uniqueId)).ToArray();
        string[] removedIds = beforePhysical.Where(item => !currentUids.Contains(item.Key)).Select(item => item.Value).OrderBy(id => id, StringComparer.Ordinal).ToArray();
        FullStoryRequire("story." + eventId + ".complete-physical-invoice", new { grants = expectedNewIds, removals = expectedRemovedIds, allOtherOwnedUidsRetained = true },
            new { grants = newItems.Select(ItemState).ToArray(), removals = removedIds },
            newItems.All(item => item.uniqueId > 0 && item.unitCount == 1) &&
            newItems.Select(item => item.identifier).OrderBy(id => id, StringComparer.Ordinal).SequenceEqual(expectedNewIds) && removedIds.SequenceEqual(expectedRemovedIds),
            "Independent complete before/after UID invoice also verifies every untouched original/proof/reward remains, and the E02/E04/E07/E09 material bonuses are actually delivered.");
        JToken ledger = FullStoryLedger();
        FullStoryAction("explicit-completed-visit-callback-replay", new { eventId, option });
        Reflect.Call(GraylineFlow, "ReplyToVisitChoice", eventId, option);
        FullStoryRequire("story." + eventId + ".no-replayed-payment-or-grant", ledger, FullStoryLedger(), JToken.DeepEquals(ledger, FullStoryLedger()));
        _fullStoryVisits.Add(new JObject { ["eventId"] = eventId, ["option"] = option, ["day"] = StoreStation.GetDayCounter(), ["before"] = before, ["after"] = after,
            ["nativeSelections"] = new JArray(observation.Selections.Skip(nativeStart)) });
        _fullStoryCompleted.Add("main." + eventId);
        foreach (object? wait in FullStoryChoose("结束会面", "End the visit", eventId + ".leave")) yield return wait;
        foreach (object? wait in FullStoryWait("story." + eventId + ".native-departure", () => Store.currentClientInstance?.GetClientBlueprint()?.identifier != GraylineStoryActor)) yield return wait;
        FullStoryPackOwned();
    }

    private IEnumerable<object?> FullStoryRecordingVisit(string ending)
    {
        // An early supplier-page capture can legitimately offer collection even
        // if the arrival update has just placed the same carrier. Both callbacks
        // use the live receipt and cannot manufacture a second recorder.
        foreach (object? wait in FullStoryWait("story.E07.recording-menu", () => GraylineChoicesVisible(), 120f)) { GraylineAdvanceDialogue(); yield return wait; }
        if (GraylineChoice("[领取记录器（占2×2）]", "[Collect the recorder (2x2)]") != null)
            foreach (object? wait in FullStoryChoose("[领取记录器（占2×2）]", "[Collect the recorder (2x2)]", "E07.collect")) yield return wait;
        foreach (object? wait in FullStoryWait("story.E07.real-carrier", () => FullStoryHasEvidence("recording"))) yield return wait;
        bool cashRepair = ending == "legal_property";
        int cash = Store.playerCash, parts = FullStoryUnits("common_electronic");
        if (!cashRepair && parts == 0) FullStoryProvideElectronic("recording-repair");
        parts = FullStoryUnits("common_electronic");
        foreach (object? wait in FullStoryChoose(cashRepair ? "[交给灰线修（100信用点）]：清掉受损的诊断数据" : "[自己修（普通电子元件×1）]：保留全部数据",
            cashRepair ? "[Have Grayline repair it (100 Credits)]: Wipe the damaged diagnostics" : "[Repair it yourself (1 common electronic part)]: Keep all the data", "E07.repair")) yield return wait;
        foreach (object? wait in FullStoryWait("story.E07.repair-transaction", () => (bool?)GraylineStoryState()["RecordingRepaired"] == true)) yield return wait;
        FullStoryRequire("story.E07.repair-invoice-and-diagnostic-data", new { cash = cash - (cashRepair ? 100 : 0), parts = parts - (cashRepair ? 0 : 1), diagnosticRetained = !cashRepair }, GraylineStoryState(),
            Store.playerCash == cash - (cashRepair ? 100 : 0) && FullStoryUnits("common_electronic") == parts - (cashRepair ? 0 : 1) &&
            (bool?)GraylineStoryState()["DiagnosticRetained"] == !cashRepair && FullStoryHasEvidence("credential") && (cashRepair ? (string?)GraylineStoryState()["HiddenClosedReason"] == "diagnostics_lost" : true));
        foreach (object? wait in FullStoryChoose("[听完了，谈谈副本怎么写]", "[I've heard you. Let's talk about the copy.]", "E07.heard-confession")) yield return wait;
        foreach (object? wait in FullStoryWait("story.E07.visible-confession-committed", () => (bool?)GraylineStoryState()["ConfessionHeard"] == true)) yield return wait;
    }

    private IEnumerable<object?> FullStoryItemAction(GameItem item, string action, string coverage, Func<JObject, bool> committed)
    {
        if (action is "copy_parts" or "x2_start") if (FullStoryUnits("common_electronic") == 0) FullStoryProvideElectronic(action);
        object[] actions = Reflect.Each(Reflect.Call(GraylineFlow, "ItemActions", item)).ToArray();
        FullStoryRequire("story.item-action-enabled." + action, "Actual owned item and one enabled production context action", new { item = ItemState(item), actions = Reflect.Json(actions) },
            actions.Count(candidate => Reflect.Str(candidate, "Id") == action && (bool)Reflect.Get(candidate, "Enabled")!) == 1);
        JObject before = GraylineStoryState(); int cash = Store.playerCash, parts = FullStoryUnits("common_electronic"), uid = item.uniqueId;
        FullStoryAction("guarded-item-business-callback", new { action, uid, before });
        Reflect.Call(GraylineFlow, "ExecuteItemAction", item, action);
        foreach (object? wait in FullStoryWait("story.item-action-committed." + action, () => committed(GraylineStoryState()))) yield return wait;
        var after = GraylineStoryState();
        FullStoryRequire("story.item-action-invoice." + action, new { cash = cash - (action == "copy_cash" ? 100 : 0), parts = parts - (action is "copy_parts" or "x2_start" ? 1 : 0) },
            new { cash = Store.playerCash, parts = FullStoryUnits("common_electronic"), state = after },
            Store.playerCash == cash - (action == "copy_cash" ? 100 : 0) && FullStoryUnits("common_electronic") == parts - (action is "copy_parts" or "x2_start" ? 1 : 0) &&
            (long?)after["Revision"] > (long?)before["Revision"] && after["Pending"]!.Type == JTokenType.Null);
        if (coverage.StartsWith("hidden.", StringComparison.Ordinal)) _fullStoryCompleted.Add(coverage);
        FullStoryCloseReading();
        yield return null;
    }
    private void FullStoryProvideElectronic(string purpose)
    {
        var part = Spawn("common_electronic"); Retain(part);
        _fullStoryFixtures.Add(JObject.FromObject(new { kind = "factory-service-material", purpose, item = ItemState(part) }));
    }
    private IEnumerable<object?> FullStoryInspect(string key, string flag)
    {
        var item = FullStoryEvidence(key); JObject before = GraylineStoryState();
        FullStoryRequire("story.inspection-eligible." + key, "Actual owned magnifier and inspectable story evidence", ItemState(item), FullStoryUnits("magnifier") > 0 && item.IsTag("INSPECTABLE_TAG"));
        FullStoryAction("native-InspectionUIManager.OnInspectItem", new { key, item.uniqueId });
        InspectionUIManager.Instance.OnInspectItem(item);
        foreach (object? wait in FullStoryWait("story.native-magnifier." + key, () => (bool?)GraylineStoryState()[flag] == true &&
            InspectionUIManager.Instance.panel.activeInHierarchy && ReferenceEquals(InspectionUIManager.Instance.currentInspectingItem, item) &&
            Reflect.Get(GraylineFlow, "_magnifierWindow") is CustomUIWindow window && window.IsVisible)) yield return wait;
        FullStoryRequire("story.inspection-preserves-item." + key, new { item.uniqueId, noConsumption = true, investigated = true }, ItemState(item),
            item.unitCount == 1 && GeneralHelper.IsItemOwned(item) && (long?)GraylineStoryState()["Revision"] > (long?)before["Revision"]);
        Reflect.Call(GraylineFlow, "CloseMagnifier");
    }

    private IEnumerable<object?> FullStoryEpilogue(string ending)
    {
        string key = ending switch { "legal_property" => "ownership", "settled_account" => "execution", "lost_and_found" => "contact", _ => "investigation" };
        foreach (object? wait in FullStoryWait("story.next-day-epilogue-delivered", () => FullStoryHasEvidence(key) &&
            StoreStation.GetDayCounter() == (int?)GraylineStoryState()["EndingDay"] + 1, 30f)) yield return wait;
        FullStoryRequire("story.epilogue-not-preread", false, GraylineStoryState()["EpilogueSeen"], (bool?)GraylineStoryState()["EpilogueSeen"] == false);
        GameItem item = FullStoryEvidence(key); int uid = item.uniqueId;
        FullStoryAction("native-HandnoteUIManager.OpenPanel", new { key, uid }); HandnoteUIManager.Instance.OpenPanel(item);
        foreach (object? wait in FullStoryWait("story.actual-epilogue-read." + ending, () => (bool?)GraylineStoryState()["EpilogueSeen"] == true &&
            (HandnoteUIManager.Instance.panel.activeInHierarchy || (bool)Reflect.Get(Reflect.Type("AugMechanicalAscension.Integration.StoryJournalWindow"), "ReadingVisible")!))) yield return wait;
        FullStoryRequire("story.epilogue-original-retained." + ending, uid, FullStoryEvidence(key).uniqueId, FullStoryEvidence(key).uniqueId == uid);
        _fullStoryCompleted.Add("epilogue." + ending);
        foreach (object? wait in CaptureEvidenceFrame("epilogue-" + ending)) yield return wait;
        FullStoryCloseReading();
    }
    private void FullStoryCloseReading()
    {
        if (HandnoteUIManager.Instance.panel.activeSelf) HandnoteUIManager.Instance.ClosePanel();
        Reflect.Call(Reflect.Type("AugMechanicalAscension.Integration.StoryJournalWindow"), "Close");
        // Closes only Aug-owned reading/result windows through their normal
        // close entry; does not alter native dialogue or story progress.
        Reflect.Call(Reflect.Type("AugMechanicalAscension.Integration.OfficialStoryUi"), "CloseAll");
    }

    private IEnumerable<object?> FullStoryChoose(string zh, string en, string name)
    {
        float deadline = Time.realtimeSinceStartup + 150f;
        var paged = new HashSet<Dialogue>();
        while (GraylineChoice(zh, en) == null)
        {
            FullStoryCheckWaiting("story.choice." + name, deadline);
            GraylineAdvanceDialogue();
            // Supplier continuation is a real visible choice, reached only
            // after its preceding reply has finished. No dialogue is skipped.
            if (GraylineChoice("继续谈", "Continue") is { } continuation)
            { FullStoryAction("supplier-continue-button", new { name, continuation.index }); continuation.OnDialogChoiceClick(); }
            else if (GraylineChoice("更多选项", "More choices") is { } next)
            {
                FullStoryRequire("story.choice.unique-pagination." + name, "Visit each actual page at most once while looking for the specified choice", GraylineUiState(), paged.Add(DialogUIManager.Instance.currentDialog));
                FullStoryAction("supplier-next-page-button", new { name, next.index }); next.OnDialogChoiceClick();
            }
            yield return null;
        }
        FullStoryAction("dialogue-choice-button", new { name, zh, en }); GraylineClickChoice(zh, en, "story." + name);
        yield return null;
    }
    private IEnumerable<object?> FullStoryServeUntil(Func<bool> ready, string target, string name)
    {
        float deadline = Time.realtimeSinceStartup + 300f;
        while (!ready())
        {
            FullStoryCheckWaiting(name, deadline);
            StoreClient? client = Store.currentClientInstance?.GetClientBlueprint();
            if (client == null)
            {
                if (Store.CanNextClient() && Store.lockNextBellFlag == 0 && DialogUIManager.Instance.currentDialog == null) GraylineRingBell();
                yield return null;
            }
            else if (!Store.isClientArrived || client.identifier == target) yield return null;
            else if (client.identifier == GraylineStoryActor)
            { FullStoryRequire(name + ".unexpected-story-visitor", "Only the explicitly scheduled next story event", GraylineStoryState(), false); }
            else throw new FixtureUnavailable("Unexpected nonfixture customer during generated story visit: " + client.identifier);
        }
        R.Check(name, "Actual native visitor/calendar condition", FullStoryStatus(), true);
    }
    private IEnumerable<object?> FullStoryWait(string name, Func<bool> ready, float seconds = 30f)
    {
        R.phase = name; float deadline = Time.realtimeSinceStartup + seconds;
        while (!ready()) { FullStoryCheckWaiting(name, deadline); yield return null; }
        R.Check(name, "Actual native condition reached", FullStoryStatus(), true);
    }
    private void FullStoryCheckWaiting(string name, float deadline)
    {
        R.phase = name;
        if ((bool)Reflect.Get(GraylineFlow, "_blocked")! || (bool)Reflect.Get(FullStoryWorkshop, "_blocked")! ||
            GameOverUIManager.Instance?.IsShowing == true || Time.realtimeSinceStartup >= deadline)
            FullStoryRequire(name, "Native condition before timeout, no blocked story/workshop or game-over", FullStoryStatus(), false);
    }
    private void FullStoryRequire(string name, object? expected, object? actual, bool passed, string evidence = "")
    {
        R.Check(name, expected, actual, passed, evidence);
        if (passed) return;
        R.environment["fullStoryBlocker"] = FullStoryStatus();
        throw new InvalidOperationException(name + " failed; this mode cannot generate a successful full-story save baseline");
    }
    private object FullStoryStatus() => new
    {
        native = GraylineUiState(), story = Reflect.Json(Reflect.Get(GraylineFlow, "State")), workshop = Reflect.Json(Reflect.Get(FullStoryWorkshop, "_state")),
        message = Reflect.Get(GraylineFlow, "Message"), storyBlocked = Reflect.Get(GraylineFlow, "_blocked"), storyBusy = Reflect.Get(GraylineFlow, "_busy"),
        visitRequest = Reflect.Get(GraylineFlow, "_visitRequest"), visitEvent = Reflect.Get(GraylineFlow, "_visitEventId"),
        workshopBlocked = Reflect.Get(FullStoryWorkshop, "_blocked"), workshopMessage = Reflect.Get(FullStoryWorkshop, "_message")
    };
    private void FullStoryAction(string action, object? detail = null) => _fullStoryActions.Add(JObject.FromObject(new
    { action, day = StoreStation.GetDayCounter(), frame = Time.frameCount, time = Time.realtimeSinceStartup, cash = Store.playerCash, detail }));
    private int FullStoryUnits(string id) => Store.FindAllItem().Where(item => item.identifier == id && GeneralHelper.IsItemOwned(item)).Sum(item => item.unitCount);
    private GameItem FullStoryOwned(string id)
    {
        var items = Store.FindAllItem().Where(item => item.identifier == id && item.unitCount == 1 && item.uniqueId > 0 && GeneralHelper.IsItemOwned(item)).ToArray();
        if (items.Length != 1) throw new InvalidOperationException("Expected exactly one actual owned story/proof item: " + id + "; found=" + items.Length);
        return items[0];
    }
    private GameItem FullStoryEvidence(string key)
    {
        int uid = (int?)((JArray)GraylineStoryState()["Items"]!).SingleOrDefault(receipt => (string?)receipt["Key"] == key && (bool?)receipt["Removed"] != true)?["Uid"] ?? 0;
        return Store.FindAllItem().Single(item => item.identifier == StoryEvidencePrefix + key && item.uniqueId == uid && item.unitCount == 1 && GeneralHelper.IsItemOwned(item));
    }
    private bool FullStoryHasEvidence(string key)
    {
        var receipts = ((JArray)GraylineStoryState()["Items"]!).Where(receipt => (string?)receipt["Key"] == key && (bool?)receipt["Removed"] != true).ToArray();
        return receipts.Length == 1 && Store.FindAllItem().Count(item => item.identifier == StoryEvidencePrefix + key && item.uniqueId == (int?)receipts[0]["Uid"] && item.unitCount == 1 && GeneralHelper.IsItemOwned(item)) == 1;
    }
    private JArray FullStoryEvidenceSnapshot() => new(Store.FindAllItem().Where(item => item.identifier.StartsWith(StoryEvidencePrefix, StringComparison.Ordinal) || item.identifier == StoryCoreId).OrderBy(item => item.uniqueId).Select(ItemState));
    private JObject FullStoryLedger() => new() { ["cash"] = Store.playerCash, ["story"] = GraylineStoryState(), ["workshop"] = FullStoryWorkshopState(), ["items"] = SnapshotItems() };
    // All factory storage retains the registered model/admission rules. It is
    // test-fixture preparation, not a production anti-theft flag or proof grant.
    private GameItem FullStoryCreateStorage(string purpose)
    {
        const int maximumStores = 4;
        FullStoryRequire("story.storage.native-capacity-bound", "At most four real 8x8 smuggler bays; fail on actual floor/cargo capacity", new
        { purpose, stores = _fullStoryStores.Count }, _fullStoryStores.Count < maximumStores);
        var bay = Spawn("smuggler_bay_mod");
        try
        {
            FullStoryRequire("story.storage.registered-native-grid", "Owned native smuggler model with its original inventory identifier", ItemState(bay),
                GeneralHelper.IsItemOwned(bay) && bay.contentWindow?.childElement is GameGridInventory grid && grid.identifier == "smuggler_bay");
            var marker = EmporiumEntry.Instance.invElement.TryFindOneValidInventorySlot(bay);
            FullStoryRequire("story.storage.real-floor-space", "A real admissible main-floor slot; no back-counter/parent bypass", new
            { purpose, item = ItemState(bay), valid = marker?.IsValid() == true }, marker?.IsValid() == true);
            Place(EmporiumEntry.Instance.invElement, bay, "story-native-smuggler-floor");
            Retain(bay); _fullStoryStores.Add(bay);
            _fullStoryFixtures.Add(JObject.FromObject(new
            {
                kind = "factory-native-smuggler-storage", purpose, item = ItemState(bay),
                source = "Registered smuggler_bay_mod: native 8x8 contents, real slot admission, no state/tag/anti-theft writes"
            }));
            return bay;
        }
        catch
        {
            if (bay.parentInventory == null) Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(bay);
            throw;
        }
    }
    private static bool FullStoryFixedNativeDevice(GameItem item) =>
        item.IsGameItemType("MACHINE") || item.IsTag("MODULE_STUCK_TAG") ||
        item.parentInventory is { } inventory && inventory.IsInsertLocked() && inventory.IsRemoveLocked();
    private JObject FullStoryStorageFact(GameItem item) => new()
    {
        ["item"] = ItemState(item), ["rootedInNativeSmuggler"] = item.IsRootedInSmugglerBay(),
        ["nativeRepoEligible"] = Store.IsEligibleForRepo(item), ["fixedNativeDevice"] = FullStoryFixedNativeDevice(item)
    };
    private void FullStoryPackOwned()
    {
        var dossier = EmporiumEntry.Instance.dossier.contentWindow.childElement as GameGridInventory;
        foreach (var document in EmporiumEntry.Instance.invElement.childItems.Concat(EmporiumEntry.Instance.backInvinvElement.childItems)
                     .Where(item => GeneralHelper.IsItemOwned(item) && item.IsGameItemType("DOCUMENT")).ToArray())
            if (dossier != null && dossier.TryFindOneValidInventorySlot(document)?.IsValid() == true)
                GraylineTransfer(document, dossier, "story-document-to-native-dossier");
        // Use the actual native candidate selector, not a copied list of
        // eligibility flags. Fixed machines/locked installed parts stay put.
        var candidates = Store.GetAllRepoEligibleItems().Select(item => item.uniqueId).ToHashSet();
        var originalProofUids = _fullStoryProofUids.Values.ToHashSet();
        var owned = Store.FindAllItem().Where(GeneralHelper.IsItemOwned).Distinct()
            .OrderBy(item => originalProofUids.Contains(item.uniqueId) ? 0 : 1)
            .ThenByDescending(item => item.modifiedShape.width * item.modifiedShape.height).ToArray();
        foreach (GameItem item in owned)
        {
            if (_fullStoryStores.Contains(item) || item.IsRootedInSmugglerBay()) continue;
            bool proof = originalProofUids.Contains(item.uniqueId);
            if (!candidates.Contains(item.uniqueId) && (!proof || FullStoryFixedNativeDevice(item) && !Store.IsEligibleForRepo(item))) continue;
            GameInventory? destination = _fullStoryStores.Select(store => store.contentWindow?.childElement as GameInventory)
                .FirstOrDefault(inventory => inventory?.TryFindOneValidInventorySlot(item)?.IsValid() == true);
            if (destination == null && _fullStoryStores.Count < 4)
            {
                var bay = FullStoryCreateStorage("Actual prior grids lack space/admission for UID " + item.uniqueId);
                destination = bay.contentWindow?.childElement as GameInventory;
            }
            FullStoryRequire("story.storage.actual-payload-admission", "An actual legal native cargo slot for the original owned item", new
            {
                item = FullStoryStorageFact(item), proof, stores = _fullStoryStores.Select(ItemState).ToArray(),
                valid = destination?.TryFindOneValidInventorySlot(item)?.IsValid() == true
            }, item.MayRemove() && destination?.TryFindOneValidInventorySlot(item)?.IsValid() == true,
                "Space/admission failure stops explicitly. No replacement proof, forged parent, resized grid or admission override is used.");
            int uid = item.uniqueId, units = item.unitCount;
            GraylineTransfer(item, destination!, "story-original-owned-item-to-native-smuggler");
            FullStoryRequire("story.storage.native-location-and-eligibility", new { uid, units, owned = true, smuggler = true, eligibleForRepo = false },
                FullStoryStorageFact(item), item.uniqueId == uid && item.unitCount == units && GeneralHelper.IsItemOwned(item) &&
                    item.IsRootedInSmugglerBay() && !Store.IsEligibleForRepo(item));
        }
        GraylineStowOwnedCounter();
    }
    private void FullStoryCheckNightStorage(int day, string stage, bool checkAllCandidates)
    {
        var owned = Store.FindAllItem().Where(GeneralHelper.IsItemOwned).ToArray();
        var proofs = FullStoryGoals.Where(goal => goal.Proof.Length > 0).Select(goal =>
        {
            _fullStoryProofUids.TryGetValue(goal.Proof, out int uid);
            var matches = owned.Where(item => item.identifier == goal.Proof).ToArray();
            bool preserved = uid > 0 && matches.Length == 1 && matches[0].uniqueId == uid && matches[0].unitCount == 1 &&
                !Store.IsEligibleForRepo(matches[0]) &&
                (matches[0].IsRootedInSmugglerBay() || FullStoryFixedNativeDevice(matches[0]));
            return new JObject
            {
                ["id"] = goal.Proof, ["manufacturedUid"] = uid, ["preserved"] = preserved,
                ["actual"] = new JArray(matches.Select(FullStoryStorageFact))
            };
        }).ToArray();
        var story = GraylineStoryState();
        var evidence = ((JArray)story["Items"]!).Where(row => (bool?)row["Removed"] != true).ToArray();
        bool evidencePresent = evidence.All(row => FullStoryHasEvidence((string)row["Key"]!));
        bool coreHeld = (string?)story["CoreDisposition"] != "held" ||
            owned.Count(item => item.identifier == StoryCoreId && item.uniqueId == (int?)story["CoreUid"] && item.unitCount == 1) == 1;
        var candidates = Store.GetAllRepoEligibleItems().ToArray();
        var entry = new JObject
        {
            ["day"] = day, ["stage"] = stage, ["proofs"] = new JArray(proofs),
            ["liveStoryEvidence"] = new JArray(evidence), ["storyEvidencePresent"] = evidencePresent, ["heldCorePresent"] = coreHeld,
            ["allActualNativeCandidatesChecked"] = checkAllCandidates,
            ["actualNativeCandidates"] = new JArray(candidates.Select(FullStoryStorageFact)),
            ["storageRoots"] = new JArray(_fullStoryStores.Select(ItemState))
        };
        _fullStoryNightStorage.Add(entry);
        FullStoryRequire("story.storage.original-twelve-and-story-custody." + stage + "." + day,
            "All twelve original manufactured UIDs remain uniquely owned and natively nonstealable; all live story evidence/core remain", entry,
            _fullStoryProofUids.Count == 12 && proofs.All(row => (bool?)row["preserved"] == true) && evidencePresent && coreHeld);
        if (checkAllCandidates)
            FullStoryRequire("story.storage.no-native-theft-candidate.before-night." + day,
                "Actual native candidate selector is empty after legal storage; ordinary supplies/rewards/tools are covered too", entry,
                candidates.Length == 0,
                "Readonly IsRootedInSmugglerBay/IsEligibleForRepo/GetAllRepoEligibleItems. Native theft/night/service behavior stays enabled.");
    }
    private static (string Zh, string En) FullStoryChoiceLabels(string ev, string option, JObject state) => (ev, option) switch
    {
        ("E01", "investigate") => ("[按废件价收下（60信用点）]", "[Buy it as scrap (60 Credits)]"),
        ("E01", "decline") => ("[不接这笔退货]", "[Refuse the return]"),
        ("E02", _) => ("[收下目录和钛合金]", "[Take the index and the titanium]"),
        ("E03", "honest") => ("[告诉她，你不认识她]", "[Tell her you don't know her]"),
        ("E03", "pretend") => ("[假装是她哥哥，说自己记不清了]", "[Pretend to be her brother and say you can't remember]"),
        ("E04", _) => ("[收下档案，接着查]", "[Take the file and keep digging]"),
        ("E05", _) => ("[收下合同，帮她核对]", "[Take the contract and help her check it]"),
        ("E06", "buy") => ("[付200信用点，留下核心]", "[Pay 200 Credits, keep the core]"),
        ("E06", "refuse") => ("[不接这笔]", "[Pass on it]"),
        ("E07", "disclose") => ("[照实写上他的名字]", "[Put his name in the copy]"),
        ("E07", "withhold_name") => ("[不写他的名字]", "[Leave his name out]"),
        ("E08", "share") when ((JArray)state["Choices"]!).Any(choice => (string?)choice["EventId"] == "E03" && (string?)choice["OptionId"] == "pretend")
            => ("[先承认上次骗了她，再交出副本]", "[Admit you lied to her, then give her the copy]"),
        ("E08", "share") => ("[把证据副本交给她]", "[Give her the evidence copy]"),
        ("E08", "hide") => ("[不给她看]", "[Don't show her]"),
        ("E09", _) => ("[收下报价，回头再答复]", "[Take the offer and answer later]"),
        ("E10", "beyond_the_world") => ("[上送诊断]：就这个地址。", "[Diagnostic upload]: This address."),
        ("E10", "legal_property") => ("[交还公司]：用卡和原件换回身体", "[Return to the company]: Trade the card and originals for your body"),
        ("E10", "settled_account") => ("[销毁控制系统]：先把证据公开", "[Destroy the control system]: Publish the evidence first"),
        ("E10", "lost_and_found") => ("[分给本人]：让同一批的每个人自己选", "[Divide it among them]: Let everyone in the batch choose for themselves"),
        _ => throw new InvalidOperationException("No independent expected dialogue label for " + ev + "/" + option)
    };

    internal IEnumerable<object?> FullStoryReload(JObject baseline)
    {
        var expected = baseline["persistence"]?["fullStory"] as JObject ?? throw new InvalidOperationException("Missing full-story baseline");
        string ending = (string?)expected["ending"] ?? "";
        string requiredEnding = O.Family switch { "storylegal" => "legal_property", "storysettled" => "settled_account", "storylost" => "lost_and_found", "storybeyond" => "beyond_the_world", _ => "" };
        FullStoryRequire("story.reload.independent-ending-family", requiredEnding, ending, ending.Length > 0 && ending == requiredEnding);
        foreach (object? wait in FullStoryWait("story.reload.runtime-ready", () => AugReady() && Reflect.Get(GraylineFlow, "State") != null && Reflect.Get(FullStoryWorkshop, "_state") != null)) yield return wait;
        var story = (JObject)expected["story"]!.DeepClone(); var workshop = (JObject)expected["workshop"]!.DeepClone();
        if (O.UseCopy) { story["SaveSlotId"] = Store.saveSlotId; workshop["SlotId"] = Store.saveSlotId; }
        FullStoryRequire("story.reload.real-story-and-all-thirteen-claims", new { story, workshop, day = (int?)expected["day"] }, new { story = GraylineStoryState(), workshop = FullStoryWorkshopState(), day = StoreStation.GetDayCounter() },
            (bool?)expected["fixtureMode"] == true && StoreStation.GetDayCounter() == (int?)expected["day"] && ((JArray)expected["visits"]!).Count == 10 &&
            ((JArray)expected["nights"]!).Count == (ending == "beyond_the_world" ? 2 : 1) &&
            (bool?)story["EpilogueSeen"] == true && (string?)story["EndingId"] == ending && ((JArray)workshop["Claimed"]!).Count == 13 &&
            (bool?)workshop["DebugAscensionUnlocked"] == false && JToken.DeepEquals(story, GraylineStoryState()) && JToken.DeepEquals(workshop, FullStoryWorkshopState()),
            O.UseCopy ? "Actual native DuplicateSlot then new-process LoadGame; only official slot rebinding is permitted. Copy retains this ending and is not an ending branch." : "New-process native ES3 LoadGame; no story action or reward claim replay is invoked.");
        FullStoryRequire("story.reload.physical-proofs-and-evidence", new { proofs = expected["proofs"], evidence = expected["evidence"] }, new
        { proofs = new JArray(FullStoryGoals.Where(goal => goal.Proof.Length > 0).Select(goal => ItemState(FullStoryOwned(goal.Proof)))), evidence = FullStoryEvidenceSnapshot() },
            JToken.DeepEquals(expected["proofs"], new JArray(FullStoryGoals.Where(goal => goal.Proof.Length > 0).Select(goal => ItemState(FullStoryOwned(goal.Proof))))) &&
            JToken.DeepEquals(expected["evidence"], FullStoryEvidenceSnapshot()));
        JToken ledger = FullStoryLedger();
        float deadline = Time.realtimeSinceStartup + 2.5f;
        while (Time.realtimeSinceStartup < deadline) yield return null;
        FullStoryRequire("story.reload.no-regrant-or-implicit-progression", ledger, FullStoryLedger(), JToken.DeepEquals(ledger, FullStoryLedger()));
        var persisted = (JObject)expected.DeepClone();
        persisted["story"] = GraylineStoryState(); persisted["workshop"] = FullStoryWorkshopState();
        R.persistence["fullStory"] = persisted;
    }

    // Passive native SelectChoice observation proves the actual dialog callback
    // was entered, including E10's automatic selection. It cannot select, patch
    // a return value, change state, or suppress an exception.
    private sealed class FullStoryDialogueObservation : IDisposable
    {
        private static FullStoryDialogueObservation? Active;
        private readonly ProbeContext _context;
        private readonly Harmony _harmony;
        private readonly MethodInfo _method;
        internal readonly JArray Selections = new(), Errors = new();
        internal FullStoryDialogueObservation(ProbeContext context)
        {
            if (Active != null) throw new InvalidOperationException("Full-story dialogue observer already active");
            _context = context; _harmony = new Harmony("nicokobo.content_native_probe.fullstory." + context.O.RunId);
            _method = typeof(DialogUIManager).GetMethod(nameof(DialogUIManager.SelectChoice), new[] { typeof(int) })!;
            _harmony.Patch(_method, prefix: new HarmonyMethod(typeof(FullStoryDialogueObservation), nameof(Observe)));
            Active = this;
        }
        private static void Observe(DialogUIManager __instance, int __0)
        {
            if (Active is not { } self) return;
            try
            {
                if (self._context.Store.currentClientInstance?.GetClientBlueprint()?.identifier != GraylineStoryActor) return;
                var dialog = __instance.currentDialog;
                self.Selections.Add(JObject.FromObject(new
                {
                    eventId = (string?)self._context.GraylineStoryState()["ActiveEventId"], index = __0,
                    label = dialog != null && __0 >= 0 && __0 < dialog.choices.Count ? dialog.choices[__0].Item2.plainText : null,
                    visible = __0 >= 0 && __0 < __instance.dialogChoiceBubbles.Count && __instance.dialogChoiceBubbles[__0].gameObject.activeInHierarchy,
                    day = StoreStation.GetDayCounter(), frame = Time.frameCount, time = Time.realtimeSinceStartup
                }));
            }
            catch (Exception error) { self.Errors.Add(error.ToString()); }
        }
        public void Dispose()
        {
            if (ReferenceEquals(Active, this)) Active = null;
            _harmony.Unpatch(_method, HarmonyPatchType.All, _harmony.Id);
        }
    }
}
