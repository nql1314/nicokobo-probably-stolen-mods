using AugMechanicalAscension.Official;
using AugMechanicalAscension.Domain;
using AugMechanicalAscension.Persistence;

namespace AugMechanicalAscension.Integration;

internal sealed partial class StoryRuntimeFlow
{
    private GameItem[] Owned() => OfficialInventoryApi.CaptureRunItems(_store!).Where(OfficialInventoryApi.IsPlayerOwned).ToArray();
    private GameItem? _actionItem;
    private StoryState? ReadableState(GameItem item) => Current() && _state != null &&
        StoryPhysicalItems.IsEvidence(item.identifier) && item.unitCount == 1 &&
        OfficialInventoryApi.IsPlayerOwned(item) ? _state : null;
    // Receipts prevent duplicate delivery; eligibility uses the actual owned item.
    // Prefer the clicked instance when a handover consumes one of several copies.
    private GameItem? HeldItem(string id, int originalUid = 0)
    {
        var matches = Owned().Where(x => x.identifier == id && x.unitCount == 1 && x.uniqueId > 0).ToArray();
        return matches.FirstOrDefault(x => NativeObject.Handle(x) == NativeObject.Handle(_actionItem)) ??
            matches.FirstOrDefault(x => x.uniqueId == originalUid) ?? matches.FirstOrDefault();
    }
    private GameItem? Evidence(string key) => HeldItem(StoryEvidence.Prefix + key, _state?.Items.FirstOrDefault(x => x.Key == key)?.Uid ?? 0);
    private GameItem? Core() => _state?.CoreDisposition == "held" ? HeldItem(StoryState.CoreId, _state.CoreUid) : null;
    private bool HasPhysicalInputs(string action, GameItem[] owned)
    {
        bool Has(params string[] keys) => keys.All(key => owned.Any(x =>
            x.identifier == StoryEvidence.Prefix + key && x.unitCount == 1 && x.uniqueId > 0));
        bool Part() => owned.Any(x => x.identifier == "common_electronic" && x.unitCount > 0);
        bool Receiver() => owned.Any(x => x.identifier == CyberneticProductDefinitions.ItemId("communication_receiver") && x.unitCount > 0);
        return action switch
        {
            "repair_parts" or "copy_parts" => Has("recording") && Part(),
            "repair_cash" or "copy_cash" => Has("recording") && _store!.playerCash >= 100,
            "deliver_copy" => Has("copy", "recording"),
            "salvage" => Has("broken", "repair"),
            "x1" => Has("receipt", "index", "repair", "contract") && (_state!.ProstheticMarksTransferred || Has("broken")) &&
                (!_state.Items.Any(x => x.Key == "investigation") || Has("investigation")),
            "x2_start" => Receiver() && Has("recording", "investigation") && Part(),
            "x2_read" => Receiver() && Has("recording", "investigation"),
            "x3" => Receiver() && Has("recording", "investigation", "credential"),
            "beyond_the_world" => Receiver() && Has(StoryEvidence.EndingInputs(action)),
            _ => true
        };
    }
    private bool Require(params string[] keys)
    {
        var owned = Owned();
        var missing = keys.Where(key => !owned.Any(x =>
            x.identifier == StoryEvidence.Prefix + key && x.unitCount == 1 && x.uniqueId > 0)).ToArray();
        if (missing.Length == 0) return true;
        _message = AugText.Get("aug.retrieve_evidence") + string.Join("、", missing.Select(x => StoryEvidence.Name(x, null)));
        return false;
    }
    private bool Communicator() => Owned().Any(x => x.identifier == CyberneticProductDefinitions.ItemId("communication_receiver") && x.unitCount > 0);
    private bool EnsureRecordingCarrier()
    {
        if (!Ready || _state?.ActiveEventId != "E07") return false;
        if (_state.Items.Any(x => x.Key == "recording")) return Require("recording");
        if (Evidence("recording") is { } carrier)
            return Stage(_state with { Items = [.. _state.Items, new("recording", carrier.uniqueId)], Revision = _state.Revision + 1 });
        return Transact("carrier", ["recording"], [], 0, x => x with { Revision = x.Revision + 1 });
    }
    private bool InspectEvidence(string key)
    {
        if (!Ready || _state == null || !Require(key)) return false;
        if (!Owned().Any(x => x.identifier == "magnifier" && x.unitCount > 0))
        { _message = AugText.Get("aug.a_magnifier_must_be_owned"); return false; }
        var s = _state;
        string action = key == "broken" ? "inspect_prosthetic" : "inspect_photo";
        bool inspected = key == "broken" ? s.ProstheticInspected : s.PhotoInspected;
        int day = StoreStation.GetDayCounter();
        if (!inspected && (!StoryInvestigation.Allowed(s, day, action) ||
            !Transact(action, [], [], 0, x => StoryInvestigation.Commit(x, day, action)))) return false;
        if (inspected) StoryPhysicalItems.Write(Evidence(key)!, _state);
        _message = StoryEvidence.InspectionNote(key, _state, null);
        return true;
    }
    private bool ApplyPhysicalChoice(StoryState before, StoryContext context, string action)
    {
        if (before.ContentVersion != 4) return false;
        string ev = before.ActiveEventId ?? "";
        if (action is StoryRules.SellCore or StoryRules.ArchiveCore)
            return Transact(action, [], [Core()!], action == StoryRules.SellCore ? 300 : 0, s => StoryRules.Commit(s, context, action));
        if (ev == "E07" && (!before.RecordingRepaired || !Require("recording", "credential")))
        { _message = AugText.Get("aug.retrieve_the_recorder_then_right_click_it"); return false; }
        if (ev == "E08" && action == "share" && !Require("copy", "recording"))
        { _message += AugText.Get("aug.right_click_the_repaired_recorder_to_make"); return false; }
        if (ev == "E10")
        {
            if (!Require(StoryEvidence.EndingInputs(action))) return false;
            if (action == "beyond_the_world" && !Communicator())
            { _message = AugText.Get("aug.the_holographic_communicator_must_be_owned"); return false; }
        }
        var grants = StoryEvidence.Grants(ev, action).ToList();
        if (ev == "E06" && action == "buy") grants.Add("@" + StoryState.CoreId);
        var remove = new List<GameItem>();
        if (ev == "E08" && action == "share") remove.Add(Evidence("copy")!);
        if (StoryRules.RemovesCore(before, action)) remove.Add(Core()!);
        if (ev == "E10")
        {
            var keys = action == "legal_property" ? StoryEvidence.EndingInputs(action) : new[] { "credential" };
            remove.AddRange(keys.Select(x => Evidence(x)!));
        }
        return Transact(action, grants.ToArray(), remove.ToArray(), StoryRules.CashDelta(before, action), s =>
            StoryRules.Commit(s with { CopyDelivered = s.CopyDelivered || ev == "E08" && action == "share" }, context, action, ev == "E06" && action == "buy" ? _createdCoreUid : 0));
    }
    private int _createdCoreUid;
    private bool DeliverContactReceipt()
    {
        if (!Ready || _state?.EndingId != "lost_and_found" || _state.Pending != null ||
            StoreStation.GetDayCounter() <= _state.EndingDay || _state.Items.Any(x => x.Key == "contact")) return false;
        if (!Transact("contact_receipt", ["contact"], [], 0, x => x with { Revision = x.Revision + 1 })) return false;
        bool onCounter = NativeObject.Handle(Evidence("contact")?.parentInventory) == NativeObject.Handle(EmporiumEntry.Instance?.backInvinvElement);
        _message = onCounter
            ? AugText.Get("aug.the_contact_receipt_has_arrived_on_the")
            : AugText.Get("aug.the_counter_had_no_room_the_contact");
        return true;
    }

    private bool EnsureJournal()
    {
        if (!Ready || _state == null) return false;
        if (_state.Items.Any(x => x.Key == StoryJournal.Key)) return Require(StoryJournal.Key);
        if (Evidence(StoryJournal.Key) is { } journal)
            return Stage(_state with { Items = [.. _state.Items, new(StoryJournal.Key, journal.uniqueId)], Revision = _state.Revision + 1 });
        return Transact("journal", [StoryJournal.Key], [], 0, x => x with { Revision = x.Revision + 1 });
    }

    // A single synchronous batch: plan every output before staging or mutation.
    // Uncertain native effects retain Pending and stop replay; saves cannot run inside the batch.
    private static void ConsumeStoryItem(GameItem item)
    {
        var parent = item.parentInventory;
        var originalShape = parent == null ? null : new GridShapeBuilder(item.modifiedShape).Clone();
        if (parent != null)
        {
            bool locked = parent.overrideLockRemove; parent.overrideLockRemove = true;
            try
            {
                if (!parent.Expel(item) || item.parentInventory != null)
                    throw new InvalidOperationException("Story input detachment unconfirmed");
            }
            finally { parent.overrideLockRemove = locked; }
        }
        try { item.Destroy(); }
        catch (Exception destroy)
        {
            if (parent != null && item.parentInventory == null)
            {
                var slot = parent.TryInventorySlot(item, 1, originalShape!);
                if (slot == null || slot.targetItem != null || !slot.IsValid() || slot.numTransfer != 1 ||
                    slot.TryAcceptOnce(1) != 1 || NativeObject.Handle(item.parentInventory) != NativeObject.Handle(parent))
                    throw new InvalidOperationException("Story input restoration unconfirmed", destroy);
            }
            throw;
        }
    }

    private bool Transact(string action, string[] grants, GameItem[] removals, int cash,
        Func<StoryState, StoryState> commit, GameItem? material = null)
    {
        if (!Ready || _store == null || _state == null) return false;
        var before = _state; int cashBefore = _store.playerCash;
        if ((long)cashBefore + cash is < 0 or > int.MaxValue)
        { _message = AugText.Get("aug.insufficient_cash_or_cash_limit_reached"); return false; }
        var removedUids = removals.Where(x => x != null).Select(x => x.uniqueId).ToHashSet();
        var products = new List<GameItem>(); bool prepared = false, changed = false;
        GameItem? markedDossier = null;
        _busy = true;
        try
        {
            var owned = Owned();
            if (removals.Any(x => x == null || x.unitCount != 1 || !owned.Any(y => NativeObject.Handle(y) == NativeObject.Handle(x))) ||
                material != null && (material.unitCount < 1 || !owned.Any(x => NativeObject.Handle(x) == NativeObject.Handle(material))))
                throw new InvalidOperationException("Input ownership changed");
            var consumedEvidence = removals.ToDictionary(x => x.identifier, x => x.uniqueId);
            foreach (string key in grants)
            {
                if (!key.StartsWith('@') && before.Items.Any(x => x.Key == key)) throw new InvalidOperationException("Duplicate evidence delivery");
                string id = key.StartsWith('@') ? key[1..] : StoryEvidence.Prefix + key;
                var item = DirectoryMaster.Item(id, true);
                if (item == null) throw new InvalidOperationException("Missing item factory: " + id);
                products.Add(item);
                if (item.identifier != id || item.uniqueId <= 0 || item.unitCount != 1 || item.parentInventory != null ||
                    owned.Any(x => x.uniqueId == item.uniqueId) || products.Count(x => x.uniqueId == item.uniqueId) != 1)
                    throw new InvalidOperationException("Invalid item factory identity: " + id);
                GeneralHelper.SetItemOwned(item, true);
            }
            // Prefer the player's counter outside the showcase, never the visitor's
            // front counter. Reserve the whole batch across counter and storage before mutation.
            var entry = EmporiumEntry.Instance;
            bool openingJournal = action == "journal";
            var counter = entry != null && !openingJournal ? entry.backInvinvElement : null;
            var storage = openingJournal
                ? (entry?.dossier?.contentWindow?.childElement as GameGridInventory)
                : entry?.invElement;
            var counterProducts = new List<GameItem>();
            var storageProducts = new List<GameItem>();
            IReadOnlyList<OfficialInventoryPlacement> counterPlan = [];
            foreach (var product in products)
            {
                var proposed = counter == null ? null :
                    OfficialInventoryPlacementApi.PlanWholeGrid(counter, [.. counterProducts, product]);
                if (proposed == null) storageProducts.Add(product);
                else { counterProducts.Add(product); counterPlan = proposed; }
            }
            // Pure refusals and handovers do not require any insertion space.
            IReadOnlyList<OfficialInventoryPlacement>? storagePlan = storageProducts.Count == 0 ? [] :
                storage == null ? null : OfficialInventoryPlacementApi.PlanWholeGrid(storage, storageProducts);
            if (storagePlan == null)
            {
                _message = openingJournal
                    ? AugText.Get("aug.the_dossier_cannot_fit_the_story_journal")
                    : AugText.Get("aug.the_counter_and_storage_cannot_fit_the");
                return false;
            }
            if (!Current() || _state != before || _store.playerCash != cashBefore) return false;
            var pending = before with { Pending = new(Guid.NewGuid().ToString("D"), "physical:" + action,
                StoreStation.GetDayCounter(), cashBefore, cash, 0) { OutputUids = products.Select(x => x.uniqueId).ToArray(), RemovedUids = removedUids.ToArray(), MaterialUid = material?.uniqueId ?? 0, MaterialUnitsBefore = material?.unitCount ?? 0 }, Revision = before.Revision + 1 };
            if (!Stage(pending)) return false;
            prepared = true;
            if (action == "salvage" && before.ProstheticInspected)
            {
                // Preserve and verify the actual dossier before destroying the original.
                markedDossier = Evidence("repair") ?? throw new InvalidOperationException("Repair dossier missing");
                StoryPhysicalItems.Write(markedDossier, pending with { ProstheticMarksTransferred = true });
            }
            if (counterPlan.Count != 0 && !WorkshopAchievementDelivery.Apply(counter!, counterPlan) ||
                storagePlan.Count != 0 && !WorkshopAchievementDelivery.Apply(storage!, storagePlan))
            {
                if (!WorkshopAchievementDelivery.Recall(products)) throw new InvalidOperationException("Batch recall unconfirmed");
                products.Clear();
                if (markedDossier != null) { StoryPhysicalItems.Write(markedDossier, before); markedDossier = null; }
                if (!Stage(before with { Revision = pending.Revision + 1 })) return false;
                _message = AugText.Get("aug.delivery_failed_the_whole_batch_was_recalled");
                return false;
            }
            changed = products.Count != 0;
            foreach (var item in removals)
            {
                int uid = item.uniqueId;
                changed = true;
                ConsumeStoryItem(item);
                if (Owned().Any(x => x.uniqueId == uid)) throw new InvalidOperationException("Removal unconfirmed");
            }
            if (material != null)
            {
                int uid = material.uniqueId, count = material.unitCount;
                changed = true;
                if (count == 1)
                {
                    ConsumeStoryItem(material);
                }
                else material.SetUnitCount(count - 1);
                if (Owned().Where(x => x.uniqueId == uid).Sum(x => x.unitCount) != count - 1) throw new InvalidOperationException("Material consumption unconfirmed");
            }
            if (cash != 0) { changed = true; _store.ModCash(cash, false); }
            if (_store.playerCash != checked(cashBefore + cash)) throw new InvalidOperationException("Cash readback mismatch");
            _createdCoreUid = products.FirstOrDefault(x => x.identifier == StoryState.CoreId)?.uniqueId ?? 0;
            var receipts = before.Items.Select(r => consumedEvidence.TryGetValue(StoryEvidence.Prefix + r.Key, out int consumedUid)
                ? r with { Uid = consumedUid, Removed = true } : r).ToList();
            for (int i = 0; i < grants.Length; i++)
                if (!grants[i].StartsWith('@')) receipts.Add(new(grants[i], products[i].uniqueId));
            var next = commit(pending with { Pending = null, Items = receipts.ToArray() });
            foreach (var item in Owned().Where(x => StoryPhysicalItems.IsEvidence(x.identifier) && next.Items.Any(r => StoryEvidence.Prefix + r.Key == x.identifier)))
                StoryPhysicalItems.Write(item, next);
            if (!CommitChoice(next)) return false;
            if (next.EndingId == "beyond_the_world" && before.EndingId == null) _letterArmed = true;
            return true;
        }
        catch (Exception ex)
        {
            if (prepared && changed) Block("Physical story transaction unconfirmed: " + ex.Message);
            else if (prepared)
            {
                if (WorkshopAchievementDelivery.Recall(products))
                {
                    products.Clear();
                    if (markedDossier != null)
                    {
                        try { StoryPhysicalItems.Write(markedDossier, before); }
                        catch (Exception rollback) { Block("Dossier restoration unconfirmed: " + rollback.Message); }
                    }
                    if (!_blocked) Stage(before with { Revision = _state!.Revision + 1 });
                }
                else Block("Physical batch recall unconfirmed");
            }
            _message = _blocked ? _message : AugText.Get("aug.operation_incomplete") + ex.Message;
            log("[ERROR] [Story] action=" + action + "; " + ex);
            return false;
        }
        finally
        {
            try
            {
                if (!prepared)
                    foreach (var item in products)
                        try { if (item.parentInventory == null) item.Destroy(); }
                        catch (Exception ex) { log("[WARN] [Story] Detached product cleanup: " + ex.Message); }
            }
            finally { _busy = false; }
        }
    }

    private bool PayService(string action, bool useMaterial, int price, string[] grants, Func<StoryState, StoryState> commit)
    {
        var material = useMaterial ? Owned().FirstOrDefault(x => x.identifier == "common_electronic" && x.unitCount > 0) : null;
        if (useMaterial && material == null) { _message = AugText.Get("aug.one_common_electronic_part_is_required"); return false; }
        return Transact(action, grants, [], useMaterial ? 0 : -price, commit, material);
    }

    private string PhysicalAction(string action)
    {
        try
        {
            if (!Ready || _state == null) return AugText.Get("aug.story_session_not_ready");
            if (action.StartsWith("deadline:", StringComparison.Ordinal)) return ApplyDueDecision(action[9..]);
            if (action == "request_visit") { RequestVisit(); return _message; }
            if (action == "core_visit") { RequestVisit(true); return _message; }
            if (action == "claim_journal") { EnsureJournal(); return _message; }
            if (action == StoryRules.ArchiveCore)
            { if (Apply(action)) _message = StoryReplies.Get("core", action, null); return _message; }
            var s = _state; int day = StoreStation.GetDayCounter();
            StoryState Rev(StoryState x) => x with { Revision = x.Revision + 1 };
            bool Allow(bool condition) { if (!condition) _message = AugText.Get("aug.prerequisites_missing_or_operation_already_complete"); return condition; }
            if (action == "letter") { if (!Allow(s.EndingId == "beyond_the_world")) return _message; _letterArmed = true; WriteLetter(); return _message; }
            if (!Allow(s.ContentVersion == 4 &&
                (s.EndingId == null || action is "read_core" or "inspect_prosthetic" or "inspect_photo" or StoryRules.RequestCoreCustody))) return _message;
            switch (action)
            {
                case "carrier":
                    if (Allow(s.Choice("E06") != "" && s.ActiveEventId == "E07" && !s.Items.Any(x => x.Key == "recording")))
                        EnsureRecordingCarrier();
                    break;
                case "repair_parts": case "repair_cash":
                    if (Allow(!s.RecordingRepaired && s.ActiveEventId == "E07") && Require("recording"))
                        if (PayService(action, action == "repair_parts", 100, ["credential"], x => StoryInvestigation.Repair(x, action == "repair_parts")))
                            _message = AugText.Get("aug.the_recorder_is_fixed_the_main_record");
                    break;
                case "copy_parts": case "copy_cash":
                    if (Allow(s.Choice("E07") != "" && s.ConfessionHeard && s.Choice("E08") == "" && s.RecordingRepaired && !s.Items.Any(x => x.Key == "copy")) && Require("recording"))
                        if (PayService(action, action == "copy_parts", 100, ["copy"], Rev))
                            _message = AugText.Get("aug.the_evidence_copy_is_ready_on_the");
                    break;
                case "deliver_copy":
                    if (Allow(s.Choice("E07") != "" && s.Choice("E08") == "") && Require("copy", "recording"))
                    { RequestVisit(); }
                    break;
                case "read_core":
                    if (Allow(Core() != null))
                    {
                        OfficialReadingApi.Open(Owner, Owner + ".story.core_record", s.GameRunId, s.SaveSlotId,
                            AugText.Get("aug.distress_core"), StoryEvidence.CoreBody(s, null), AugText.Get("aug.core_communication_record"));
                    }
                    break;
                case StoryRules.RequestCoreAid: case StoryRules.RequestCoreCustody:
                    if (StoryRules.Denial(s, Context(), action) is { } denial)
                    { _message = AugText.Get("aug.core_request_unavailable") + denial; break; }
                    var agreed = StoryRules.Consent(s, Context(), action);
                    var shown = OfficialReadingApi.Open(Owner, Owner + ".story.core_request", s.GameRunId, s.SaveSlotId,
                        AugText.Get("aug.core_communication"), StoryEvidence.CoreBody(agreed, null), AugText.Get("aug.core_communication_record"));
                    if (shown.Status != OfficialPresentationStatus.Opened)
                    { _message = AugText.Get("aug.the_core_s_reply_could_not_be"); break; }
                    if (Ready && _state == s && Core() != null && CommitChoice(agreed))
                        _message = action == StoryRules.RequestCoreAid ? AugText.Get("aug.the_core_agreed_to_help_contact_the") : AugText.Get("aug.the_core_wants_to_go_to_the");
                    break;
                case "salvage":
                    if (Allow(s.Choice("E04") != "" && !s.Items.Any(x => x.Key == "broken" && x.Removed)) && Require("broken", "repair"))
                        if (Transact(action, ["@nicokobo.mechcore.synthesis.precision_parts"], [Evidence("broken")!], 0,
                            x => Rev(x.ProstheticInspected ? x with { ProstheticMarksTransferred = true } : StoryInvestigation.Close(x, "prosthetic_salvaged"))))
                            _message = AugText.Get("aug.the_old_prosthetic_came_apart_you_got");
                    break;
                case "inspect_prosthetic":
                    InspectEvidence("broken");
                    break;
                case "inspect_photo":
                    InspectEvidence("photo");
                    break;
                case "x1":
                    if (Allow(StoryInvestigation.Allowed(s, day, action)) && Require("receipt", "index", "repair", "contract"))
                    {
                        if (!s.ProstheticMarksTransferred && !Require("broken")) break;
                        bool existing = s.Items.Any(x => x.Key == "investigation");
                        if (!existing || Require("investigation"))
                            if (Transact(action, existing ? [] : ["investigation"], [], 0, x => StoryInvestigation.Commit(x, day, action)))
                                _message = AugText.Get("aug.the_four_documents_and_the_old_prosthetic");
                    }
                    break;
                case "x2_start":
                    if (Allow(StoryInvestigation.Allowed(s, day, action) && Communicator()) && Require("recording", "investigation"))
                        if (PayService(action, true, 0, [], x => StoryInvestigation.Commit(x, day, action)))
                            _message = AugText.Get("aug.the_diagnostics_say_when_the_next_pause");
                    break;
                case "x2_read":
                    if (Allow(StoryInvestigation.Allowed(s, day, action) && Communicator()) && Require("recording", "investigation"))
                        if (CommitChoice(StoryInvestigation.Commit(s, day, action)))
                            _message = AugText.Get("aug.even_offline_both_records_still_paused_together");
                    break;
                case "x3":
                    if (Allow(StoryInvestigation.Allowed(s, day, action) && Communicator()) && Require("recording", "investigation", "credential"))
                        if (CommitChoice(StoryInvestigation.Commit(s, day, action)))
                            _message = AugText.Get("aug.an_address_that_isn_t_in_the");
                    break;
            }
            return _message;
        }
        catch (Exception ex) { log("[Story] Item action: " + ex); return AugText.Get("aug.operation_unconfirmed_read_the_journal"); }
    }

}
