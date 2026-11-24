using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using DefinitelyStolen.Domain;
using DefinitelyStolen.Integration;
using DefinitelyStolen.Persistence;
using Nicokobo.ModUtilities;

namespace DefinitelyStolen.Runtime;

internal sealed class RunController(Catalog catalog, Action<string> log, Action<string> error, Func<double> manualDifficultyCoefficient, Func<bool> randomizeSuccessZoneAfterSuccess)
{
    internal readonly NativeItems Items = new(catalog);
    internal RunState? State { get; private set; }
    internal bool Busy { get; private set; }
    internal bool DebugEnabled { get; set; }
    internal bool Infinite { get; set; }
    internal DebugTuning Tuning { get; private set; } = new();
    internal DebugTuning? ActiveTuning => DebugEnabled ? Tuning : null;
    internal double ManualDifficultyCoefficient => manualDifficultyCoefficient();
    internal bool RandomizeSuccessZoneAfterSuccess => randomizeSuccessZoneAfterSuccess();
    internal bool Browsing { get; private set; }
    internal string Message { get; private set; } = "";
    internal event Action? Changed;
    internal event Action? BeforeReturn;
    private PlayerStore? _store;
    private readonly Dictionary<string, long> _errorTimes = new(StringComparer.Ordinal);
    private bool _errorNotificationPending;
    private bool _initial;
    private string? _spaceKey;
    private bool _spaceAvailable;
    private (long Start, double Position, long At)? _stopInput;
    internal double? StoppedPosition => State?.Night?.Timing is { } timing
        ? timing.StopPosition ?? (_stopInput is { } input && input.Start == timing.StartedUtcTicks ? input.Position : null) : null;
    internal bool Qualified => Perks.CanSteal(State);
    internal bool CanBrowseOtherRegions => ReadyAction() && Browsing && Qualified && Native.Night &&
        State?.Night is { Started: false, Ended: false, Timing: null } night && night.Day == Native.Day && _store!.visitLeftTonight > 0;
    internal bool OwnCareer => _store != null && Native.IsCareer(_store);
    internal void Notify(string message)
    {
        Message = message;
        // The theft panel closes immediately on capture. Native notifications
        // keep the result visible after returning to the store.
        try { if (StoreUIManager.Instance != null) StoreUIManager.Instance.Notify(Message); }
        catch (Exception ex) { log("[WARN] Native notification failed: " + ex.Message); }
        Changed?.Invoke();
    }
    internal void Reset()
    {
        Tuning = new(); State = null; _store = null; Busy = DebugEnabled = Infinite = Browsing = _initial = false;
        _stopInput = null; _spaceKey = null; Message = ""; Changed?.Invoke();
    }
    private static bool HasPerk(PlayerStore store, string id) => store.startingPerks != null && store.startingPerks.ToArray().Any(x => x.id == id);
    private static RunState Fresh(PlayerStore store)
    {
        bool career = Native.IsCareer(store);
        bool qualified = career || HasPerk(store, Identity.Streetwise);
        var state = new RunState { RunId = store.runID, SlotId = store.saveSlotId, Qualified = qualified,
            QualificationSource = career ? "career" : qualified ? "perk" : "none",
            RandomState = BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(), 0) | 1UL };
        Perks.ApplyTheftAccess(state);
        state.Habitual = state.Qualified && HasPerk(store, Identity.Habitual);
        state.Experience = TheftRules.Threshold(state.Habitual ? 5 : career ? 2 : 0);
        return state;
    }
    internal void BeginInitial(PlayerStore store)
    {
        _initial = true; _store = store;
        State = Fresh(store);
        if (!OwnCareer) return;
        try { GrantOpening(store); }
        catch (Exception ex) { ReportError("Opening rewards failed; theft eligibility retained", ex); }
        Perks.EnsureBuiltIn(store);
    }
    private void GrantOpening(PlayerStore store)
    {
        var lowerReputation = store.storeReputations.ToArray().SingleOrDefault(x => x.factionId == "FACTION_LOWER_LEVEL")
            ?? throw new InvalidOperationException("Lower Level faction is required for the opening");
        var present = NativeGraph.RunItems(store);
        var equipment = new List<ItemState>(); var random = new StableRandom(State!.RandomState);
        foreach (string id in OpeningRules.Fixed)
            if (!present.Any(x => x.identifier == id)) equipment.Add(Items.Create(id, 1, random));
        var planned = OpeningRules.Plan(catalog, equipment, (id, tier, rng) => Items.Create(id, tier, rng), random, states =>
        {
            var temporary = new List<GameItem>();
            try
            {
                foreach (var state in states) temporary.Add(Items.Decode(state, store));
                return NativeItems.Plan(Native.MainGrid, temporary) != null;
            }
            finally { foreach (var item in temporary) ItemGraphCleanup.Destroy(item); }
        }) ?? throw new InvalidOperationException("No valid complete opening within budget and native inventory");
        var created = new List<GameItem>();
        int cashBefore = store.playerCash;
        double reputationBefore = lowerReputation.GetReputationExact();
        try
        {
            foreach (var state in planned.Items) created.Add(Items.Decode(state, store));
            var placement = NativeItems.Plan(Native.MainGrid, created) ?? throw new InvalidOperationException("Opening placement changed");
            NativeItems.Place(Native.MainGrid, placement);
            store.ModCash(planned.CashDelta, false);
            if (store.playerCash != checked(cashBefore + planned.CashDelta))
                log($"[WARN] Native opening cash delta differs: requested={planned.CashDelta}; actual={(long)store.playerCash - cashBefore}");
            foreach (var state in planned.Items.Where(x => !OpeningRules.Fixed.Contains(x.Id)))
            {
                state.Heat = OpeningRules.PriceBand(state.Value) * 25;
                var item = created.Single(x => x.uniqueId == state.UniqueId);
                NativeItems.MarkLoot(item, state.Heat); item.state.DisableTag(Identity.CustodyTag);
                state.Graph = NativeGraph.Encode(item);
            }
            StoreReputation.ModReputationRaw(lowerReputation.factionId, OpeningRules.LowerReputationDelta, false);
            if (lowerReputation.GetReputationExact() != reputationBefore + OpeningRules.LowerReputationDelta)
                log($"[WARN] Native opening reputation delta differs: requested={OpeningRules.LowerReputationDelta}; actual={lowerReputation.GetReputationExact() - reputationBefore}");
            State.OpeningGranted = true; State.OpeningCashDelta = planned.CashDelta; State.OpeningItems = planned.Items.ToList(); State.RandomState = random.State;
        }
        catch
        {
            lowerReputation.SetReputation(reputationBefore);
            foreach (var item in created) { NativeItems.Detach(item); ItemGraphCleanup.Destroy(item); }
            store.ModCash(cashBefore - store.playerCash, false); throw;
        }
    }
    internal void FinishInitial(PlayerStore store, bool returned, Exception? failure = null)
    {
        if (!_initial) return;
        if (State == null || !returned || string.IsNullOrWhiteSpace(store.runID) || store.saveSlotId < 0)
        {
            // Retain eligibility. A later native save can bind the real identity;
            // no reward is retried and no payload is staged under a missing ID.
            ReportError($"Opening could not complete; runIdEmpty={string.IsNullOrWhiteSpace(store.runID)}; slotId={store.saveSlotId}", failure);
            return;
        }
        // InitialSave owns identity allocation; its prefix can run before the
        // final run ID and slot are available, even without a SaveGame callback.
        State.RunId = store.runID; State.SlotId = store.saveSlotId;
        _initial = false;
        log("Opening initialized; run=" + store.runID);
        // Other mods' InitialSave callbacks are still active here. Open and
        // IntelligenceArrived prepare targets after the opening save completes.
    }
    internal void Load(PlayerStore store)
    {
        Reset(); _store = store;
        var fresh = Fresh(store);
        var loaded = RunData.Read(store, fresh, catalog, RecoveryWarning);
        State = loaded;
        if (loaded.QualificationSource == "career" && !OwnCareer)
        {
            loaded.Qualified = fresh.Qualified;
            loaded.QualificationSource = fresh.QualificationSource;
            loaded.Habitual = fresh.Habitual;
            RecoveryWarning("Restored theft eligibility from the current character");
        }
        Perks.ApplyTheftAccess(loaded);
        try
        {
            if (loaded.Night is { } night)
            {
                if (night.Day != Native.Day && (night.Outside || night.Day > Native.Day))
                    throw new InvalidDataException("Saved outing belongs to another day");
                var physicalIds = CaptureItems().Where(x => x.uniqueId > 0).Select(x => x.uniqueId).ToHashSet();
                if (night.Npcs.SelectMany(x => x.Items).SelectMany(CandidateState.Ids).Any(physicalIds.Contains))
                    throw new InvalidDataException("Saved target already exists in player inventory");
                ReserveUniqueIds();
                if (night.Outside) { RestoreCarry(night); Browsing = true; }
            }
        }
        catch (Exception ex)
        {
            RunRecovery.ResetNight(loaded);
            RecoveryWarning("Reset saved theft night: " + ex.Message);
            if (Locations.IsOwn(MapUIManager.Instance?.Current?.Id)) ReturnNative();
        }
        if (loaded.QualificationSource == "career")
        {
            try { Perks.EnsureBuiltIn(store); }
            catch (Exception ex) { RecoveryWarning("Could not restore Streetwise perk: " + ex.Message); }
        }
        RecoverPendingCarry();
        EnsureNight(); Changed?.Invoke();
    }
    private void ReserveUniqueIds()
    {
        var night = State!.Night!;
        int largest = 0;
        foreach (var item in night.Npcs.SelectMany(x => x.Items))
            foreach (int id in CandidateState.Ids(item)) largest = Math.Max(largest, id);
        foreach (var item in State!.OpeningItems.Concat(State.Notes.Where(x => x.Paper != null).Select(x => x.Paper!)))
            foreach (int id in CandidateState.Ids(item)) largest = Math.Max(largest, id);
        var graphs = night.Loot.Concat(night.Abandoned).Select(x => x.Graph).Concat(night.Carry.Select(x => x.Graph));
        foreach (var graph in graphs)
            foreach (int id in GraphIdentity.Ids(graph)) largest = Math.Max(largest, id);
        if (_store!.currentUniqueId <= largest) _store.currentUniqueId = checked(largest + 1);
    }
    private void RestoreCarry(NightState night)
    {
        var map = MapUIManager.Instance;
        if (map == null) throw new InvalidOperationException("Native outside UI unavailable");
        if (map.isOutside && !Locations.IsOwn(map.Current?.Id))
            throw new InvalidOperationException("Another location owns the outside inventory");
        if (!map.isOutside) Native.RestoreOutside(night.LockedRegion ?? 0);
        NativeCarry.Restore(night.Carry, _store!);
    }
    private void RecoveryWarning(string message) => log("[WARN] " + message);
    private void RecoverPendingCarry()
    {
        if (State?.RecoveryCarry is not { Count: > 0 } pending || MapUIManager.Instance is not { IsOutside: false, IsInTransit: false }) return;
        try { NativeCarry.RecoverToStore(pending, _store!, RecoveryWarning); }
        catch (Exception ex) { RecoveryWarning("Carry recovery remains pending: " + ex.Message); }
        if (pending.Count == 0) State.RecoveryCarry = null;
    }
    internal bool EnsureNight()
    {
        if (!Qualified) return false;
        if (State!.Night?.Day == Native.Day) return true;
        if (_initial || !ReadyAction()) return false;
        if (State.Night is { Started: true, Ended: false } || State.Night?.Day > Native.Day)
        {
            RunRecovery.ResetNight(State);
            RecoveryWarning("Reset unfinished theft night after the date changed");
            RecoverPendingCarry();
        }
        try
        {
            RegionEventState[]? events = null;
            try { events = NightEventBridge.Capture(); }
            catch (Exception ex) { RecoveryWarning("Reset regional event modifiers: " + ex.Message); }
            var skipped = new HashSet<string>(StringComparer.Ordinal);
            State.Night = GenerationRules.Night(State, Native.Day, catalog, Items.CreateCandidate, Items.Portrait, events,
                (id, ex) => { if (skipped.Add(id)) RecoveryWarning($"Skipped theft candidate {id}: {ex.Message}"); });
            State.Revision++; Message = ""; Changed?.Invoke();
            return true;
        }
        catch (Exception ex) { ReportError("Night generation failed", ex); return false; }
    }
    internal bool Open()
    {
        if (!Qualified || !Native.Night || !EnsureNight() || State!.Night!.Ended) return false;
        if (!State.Night.Started && _store!.visitLeftTonight <= 0)
        { Notify(Native.Localize("no_outings_remain_tonight")); return false; }
        var map = MapUIManager.Instance; if (map == null) return false;
        if (!map.isOutside) return false;
        Browsing = true; return true;
    }
    internal void SelectMode(TheftMode mode)
    {
        if (!ReadyAction() || !Browsing || State?.Night is not { Timing: null, Ended: false, Escaped: false } || !Enum.IsDefined(typeof(TheftMode), mode)) return;
        if (State.Night.Mode == mode) return;
        var next = StateCodec.Clone(State); if (!TheftRules.SelectMode(next, mode)) return;
        Commit(next, () => { }, () => { }, () => { });
    }
    internal bool BeginTheft(string key)
    {
        if (!ReadyAction() || !Qualified || !Browsing || !Native.Night || State?.Night is not { Ended: false, Escaped: false, Timing: null }) return false;
        var npc = State.Night.Npcs.Single(x => x.Key == key);
        if (!State.Night.Started && _store!.visitLeftTonight <= 0 || !CanCarryTarget(npc)) return false;
        var next = StateCodec.Clone(State);
        TimingRules.Begin(next, next.Night!.Npcs.Single(x => x.Key == key), catalog.Role(npc.RoleId), Infinite, DateTime.UtcNow.Ticks, ActiveTuning, ManualDifficultyCoefficient, RandomizeSuccessZoneAfterSuccess);
        int visits = _store!.visitLeftTonight;
        return Commit(next, () => { if (!State.Night.Started) _store.visitLeftTonight = visits - 1; },
            () => _store.visitLeftTonight = visits, () => { });
    }
    internal TimingAttempt? BeginPractice(string key)
    {
        if (!ReadyAction() || !Qualified || !Browsing || !Native.Night || State == null || State.ManualPracticeUsed ||
            State.Night is not { Mode: TheftMode.Timing, Timing: null, Ended: false, Escaped: false }) return null;
        var npc = State.Night.Npcs.Single(x => x.Key == key);
        var practice = TimingRules.Practice(State, npc, catalog.Role(npc.RoleId), DateTime.UtcNow.Ticks, ActiveTuning, ManualDifficultyCoefficient, RandomizeSuccessZoneAfterSuccess);
        var next = StateCodec.Clone(State); next.ManualPracticeUsed = true; next.Revision++;
        // Only persist the one-time tutorial marker. Never enter the theft/custody transaction.
        return Commit(next, () => { }, () => { }, () => { }) ? practice : null;
    }
    internal void StopTheft(double position)
    {
        if (!ReadyAction() || State?.Night?.Timing is not { } timing) return;
        if (timing.StopPosition == null)
        {
            // Retain the first stop if validation fails; retrying the operation
            // must not give the player another stop on the same attempt.
            if (_stopInput is not { } input || input.Start != timing.StartedUtcTicks)
            {
                long at = DateTime.UtcNow.Ticks;
                _stopInput = (timing.StartedUtcTicks, TimingRules.Elapsed(timing, at) >= TimingRules.Duration ? 0 : position, at);
            }
            var stop = _stopInput.Value;
            var next = StateCodec.Clone(State); TimingRules.Stop(next, stop.Position, stop.At);
            if (!Commit(next, () => { }, () => { }, () => { })) return;
            _stopInput = null;
        }
        Steal(timing.Target);
    }
    private void Steal(string key)
    {
        if (!ReadyAction() || !Qualified || !Browsing || !Native.Night || State?.Night == null || State.Night.Ended || State.Night.Escaped) return;
        var npc = State.Night.Npcs.Single(x => x.Key == key);
        var timing = State.Night.Timing;
        if (!TheftRules.CanSteal(npc) || State.Night.LockedRegion is int region && region != npc.Region) return;
        if (timing != null ? timing.StopPosition == null || timing.Target != key
            : State.Night.Mode == TheftMode.Timing || !Infinite && TheftRules.Remaining(State) <= 0) return;
        if (!State.Night.Started && _store!.visitLeftTonight <= 0) return;
        var decoded = new List<GameItem>();
        try
        {
            // Preflight every possible outcome before rolling. Capacity does not
            // permit knowing which item the RNG would choose.
            foreach (var item in timing == null || timing.Zones.At(timing.StopPosition!.Value) == AttemptOutcome.Success ? npc.Items.Where(item => item.Available) : Enumerable.Empty<ItemState>())
            {
                var preview = NativeItems.Preview(item);
                try
                {
                    if (NativeItems.Plan(Native.CarryGrid, new[] { preview }) == null)
                    { Notify(Native.Localize("make_room_in_your_backpack_first")); return; }
                }
                finally { ItemGraphCleanup.Destroy(preview); }
            }
            var smoke = NativeCarry.FindEscapeSmoke();
            var next = StateCodec.Clone(State); var receipt = TheftRules.Attempt(next, next.Night!.Npcs.Single(x => x.Key == key), catalog.Role(npc.RoleId), timing?.Infinite ?? Infinite,
                continueAfterCaught: DebugEnabled, escapeItemUniqueId: smoke?.GetUniqueID() ?? 0);
            bool caught = receipt.Outcome == AttemptOutcome.Caught;
            bool escaped = receipt.EscapeItemUniqueId > 0;
            bool forcedReturn = caught && next.Night!.Ended;
            int visits = _store!.visitLeftTonight;
            GameItem? award = receipt.Outcome == AttemptOutcome.Success ? Items.Decode(next.Night!.Loot.Single(x => x.UniqueId == receipt.ItemUniqueId), _store!) : null;
            if (award != null) decoded.Add(award);
            var discovered = Array.Empty<GameItem>();
            int crimeAmount = 0;
            if (caught && State.Night.Loot.Count > 0)
            {
                var lootById = State.Night.Loot.ToDictionary(x => x.UniqueId);
                discovered = CaptureItems().Where(item => lootById.ContainsKey(item.uniqueId)).ToArray();
                // Native appraisal includes the container's contents. Appraise each loot root once.
                crimeAmount = checked((int)discovered.Where(item => !lootById[item.uniqueId].DebugInjected).Sum(NativeItems.TrueValue));
            }
            var losses = escaped ? Array.Empty<GameItem>() : discovered;
            var restoreCarry = caught ? NativeCarry.CaptureRollback() : null;
            var nativeReps = _store.storeReputations.ToArray();
            var reputationPenalties = caught
                ? TheftRules.ReputationPenalties(receipt.Faction, receipt.TargetReputationPenalty, nativeReps.Select(x => x.factionId))
                : Array.Empty<(string Faction, int Points)>();
            var repBefore = nativeReps.Where(rep => reputationPenalties.Any(penalty => penalty.Faction == rep.factionId))
                .Select(rep => (Reputation: rep, Value: rep.GetReputationExact())).ToArray();
            var leftBehind = forcedReturn ? Native.CarryGrid.childItems.ToArray() : Array.Empty<GameItem>();
            if (forcedReturn) { next.Night!.Carry.Clear(); next.Night.Outside = false; }
            bool accepted = Commit(next, () =>
            {
                if (!State.Night.Started) _store.visitLeftTonight = visits - 1;
                if (award != null)
                {
                    var saved = next.Night!.Loot.Single(x => x.UniqueId == award.uniqueId);
                    NativeItems.MarkLoot(award, saved.Heat);
                    var plan = NativeItems.Plan(Native.CarryGrid, new[] { award }) ?? throw new InvalidOperationException("Carry space changed"); NativeItems.Place(Native.CarryGrid, plan);
                    saved.Graph = NativeGraph.Encode(award);
                }
                if (escaped) NativeCarry.DetachEscapeSmoke(smoke!);
                foreach (var item in losses) NativeItems.Detach(item);
                foreach (var (faction, points) in reputationPenalties) StoreReputation.ModReputationRaw(faction, -points, false);
            }, () =>
            {
                if (award != null && award.parentInventory != null) NativeItems.Detach(award);
                restoreCarry?.Invoke();
                foreach (var (reputation, value) in repBefore) reputation.SetReputation(value);
                _store.visitLeftTonight = visits;
            }, () =>
            {
                // Only record a crime after the caught outcome and custody changes have been validated.
                if (crimeAmount > 0) _store.secData.CommitCrime(SecData.FENCING, crimeAmount);
                ItemGraphCleanup.Destroy(losses.Concat(leftBehind).Distinct().ToList());
                if (escaped) smoke!.Destroy();
            });
            if (!accepted) return;
            int levelAfter = TheftRules.Level(next.Experience);
            int addedActions = TheftRules.Actions(levelAfter) - TheftRules.Actions(receipt.LevelBefore);
            string upgrade = levelAfter > receipt.LevelBefore
                ? "\n" + Native.Localize(receipt.Mode == TheftMode.Timing ? "level_up.timing" : "level_up.probability", levelAfter)
                    + (addedActions > 0 ? "\n" + Native.Localize("level_up.actions", addedActions) : "")
                : "";
            string stolen = receipt.Outcome == AttemptOutcome.Success ? Native.Localize("stolen", Items.Name(next.Night!.Loot.Last())) : "";
            string debug = caught && !forcedReturn && !escaped
                ? "\n" + Native.Localize("level_up.debug") : "";
            string outcomeText = receipt.Outcome switch
            {
                AttemptOutcome.Success => stolen,
                AttemptOutcome.HiddenFailure => Native.Localize("missed"),
                _ => Native.Localize(escaped ? "escaped" : "caught") + " " + string.Join(" ", reputationPenalties.Select(
                    penalty => Native.Localize("faction_penalty", FactionName(penalty.Faction), penalty.Points)))
            };
            string result = outcomeText + "\n" + Native.Localize("xp", receipt.ExperienceAward);
            Notify(result + upgrade + debug);
            if (forcedReturn) ReturnNative();
        }
        catch (Exception ex) { ReportError("Theft rejected", ex); }
        finally
        {
            foreach (var item in decoded)
                if (Native.Alive(item) && item.parentInventory == null) ItemGraphCleanup.Destroy(item);
        }
    }
    internal bool CanCarryTarget(NpcState npc)
    {
        if (!ReadyAction() || !Qualified || !Browsing || !TheftRules.CanSteal(npc)) return false;
        string key = State!.Revision + ":" + npc.Key + ":" + InventoryLayout.Key(Native.CarryGrid);
        if (_spaceKey == key) return _spaceAvailable;
        _spaceKey = null;
        foreach (var state in npc.Items.Where(item => item.Available))
        {
            var item = NativeItems.Preview(state);
            try
            {
                if (NativeItems.Plan(Native.CarryGrid, new[] { item }) == null)
                { _spaceKey = key; _spaceAvailable = false; return false; }
            }
            finally { ItemGraphCleanup.Destroy(item); }
        }
        _spaceKey = key; _spaceAvailable = true;
        return true;
    }
    internal void Return()
    {
        if (Busy || State?.Night?.Timing != null) return;
        ModHelper.ReturnFromAfterhour();
    }
    internal bool PrepareLeave()
    {
        if (State?.Night == null || !Browsing) return true;
        if (Busy || State.Night.Timing != null) return false;
        try
        {
            var next = StateCodec.Clone(State);
            var returnedIds = NativeCarry.EquippedItems().SelectMany(NativeGraph.Items).Select(x => x.uniqueId).Where(x => x > 0).ToHashSet();
            CustodyRules.FinishReturn(next.Night!, returnedIds); next.Revision++;
            var loot = CaptureItems().Where(x => next.Night!.Loot.Any(s => s.UniqueId == x.uniqueId)).ToArray();
            Browsing = false;
            if (Commit(next, () => { foreach (var item in loot) item.state.DisableTag(Identity.CustodyTag); },
                () => { Browsing = true; foreach (var item in loot) item.state.EnableTag(Identity.CustodyTag); },
                () => { BeforeReturn?.Invoke(); _stopInput = null; Message = ""; },
                ex => RecoveryWarning("Theft return bookkeeping failed: " + ex.Message))) return true;
            RecoveryWarning("Reset theft night after return could not be committed");
        }
        catch (Exception ex) { RecoveryWarning("Reset theft night before returning: " + ex.Message); }
        // Native return still owns live inventory. The old snapshot may no
        // longer describe it and must never be reissued to the player.
        State.Night?.Carry.Clear();
        RunRecovery.ResetNight(State);
        Browsing = false; _stopInput = null; Message = "";
        return true;
    }
    private void ReturnNative() { Browsing = false; ModHelper.ReturnFromAfterhour(false); }
    internal string ReputationPenaltyPreview(string? target, int region)
    {
        var penalties = TheftRules.ReputationPenalties(target, TheftRules.TargetReputationPenalty(region), _store!.storeReputations.Select(x => x.factionId));
        return string.Join(" · ", penalties.Select(penalty => $"{FactionName(penalty.Faction)} −{penalty.Points}"));
    }
    internal static string FactionName(string id) => id switch
    {
        "FACTION_UPPER_LEVEL" => LocHelper.GetLocalizedClientName("faction_name_upper_level"),
        "FACTION_LOWER_LEVEL" => LocHelper.GetLocalizedClientName("faction_name_lower_level"),
        "FACTION_MIDDLE" => Native.Localize("middle_level"),
        "FACTION_TOURIST" => LocHelper.GetLocalizedClientName("name_tourist"),
        "FACTION_SECURITY" => LocHelper.GetLocalizedClientName("faction_name_security"),
        _ => id
    };
    internal void Abandon(int id)
    {
        if (State?.Night?.Timing != null) return;
        if (!ReadyAction() || State?.Night is not { Started: true, Ended: false } night) return;
        var item = CaptureItems().SingleOrDefault(x => x.uniqueId == id);
        var state = night.Loot.SingleOrDefault(x => x.UniqueId == id);
        if (item == null || state == null) return;
        var parent = item.parentInventory; var shape = item.modifiedShape.Clone();
        var next = StateCodec.Clone(State); var removed = next.Night!.Loot.Single(x => x.UniqueId == id); next.Night.Loot.Remove(removed); next.Night.Abandoned.Add(removed); next.Revision++;
        Commit(next, () => NativeItems.Detach(item), () => { item.modifiedShape = shape; parent!.UncheckedAccept(item); }, () => ItemGraphCleanup.Destroy(item));
    }
    internal void Settle()
    {
        if (!ReadyAction() || Browsing) return;
        try
        {
            var next = StateCodec.Clone(State!);
            if (!TheftRules.Settle(next, Native.Day)) return;
            string text = WindText(next.Wind);
            int count = _store!.nightLogs.Count;
            if (Commit(next, () => _store.AddNightLog(text),
                () => { while (_store.nightLogs.Count > count) _store.nightLogs.RemoveAt(_store.nightLogs.Count - 1); }, () => { },
                ex => RecoveryWarning("Theft settlement bookkeeping failed: " + ex.Message))) return;
            RecoveryWarning("Reset theft night after settlement could not be committed");
        }
        catch (Exception ex) { RecoveryWarning("Reset theft night after settlement failed: " + ex.Message); }
        RunRecovery.ResetNight(State!);
    }
    private static string WindText(int wind) => wind switch
    {
        < 20 => Native.Localize("the_streets_are_quiet_nobody_mentions_last"),
        < 40 => Native.Localize("people_in_the_alleys_have_started_talking"),
        < 70 => Native.Localize("people_are_watchful_and_keep_a_closer"),
        _ => Native.Localize("rumours_are_spreading_the_whole_street_is")
    };
    internal void IntelligenceArrived()
    {
        if (!EnsureNight() || State == null || State.Night!.NoteRolled) return;
        var next = StateCodec.Clone(State); GenerationRules.RollNote(next, catalog, x => Items.SnapshotName(x, false), x => Items.SnapshotName(x, true));
        if (Commit(next, () => { }, () => { }, () => { })) DeliverPendingNote();
    }
    internal void DeliverPendingNote()
    {
        if (!ReadyAction() || State == null) return;
        var note = State.Notes.LastOrDefault(x => x.Day == Native.Day && !x.Delivered && !x.Expired && !x.Invalidated);
        if (note == null) return;
        var next = StateCodec.Clone(State); var saved = next.Notes.Single(x => x.Day == note.Day);
        GameItem paper = note.Paper == null ? PreBuiltItemHelper.CreateHandnote(NoteText(note),
            Native.Localize("night_intelligence"), Native.Localize("thief"), 1) : NativeGraph.Decode(note.Paper.Graph);
        try
        {
            paper.unitValue = 0; paper.state.EnableTag(Identity.Owner + ".note"); paper.state.GetTag(Identity.Owner + ".note").valueInt = note.Day;
            saved.Paper = Items.Describe(paper); saved.Paper.Value = 0; saved.Delivered = true; next.Revision++;
            var plan = NativeItems.Plan(Native.MainGrid, new[] { paper });
            if (plan == null)
            {
                // Keep the already chosen paper UID instead of rolling another gift.
                if (note.Paper == null) { saved.Delivered = false; Commit(next, () => { }, () => { }, () => { }); }
                Notify(Native.Localize("night_intelligence_is_waiting_make_room_in"));
                return;
            }
            Commit(next, () => NativeItems.Place(Native.MainGrid, plan), () => NativeItems.Detach(paper), () => { });
        }
        finally { if (paper.parentInventory == null) ItemGraphCleanup.Destroy(paper); }
    }

    private string NoteText(NoteState note)
    {
        var npc = State?.Night?.Npcs.FirstOrDefault(value => value.Key == note.Target);
        var item = npc?.Items.FirstOrDefault(value => value.UniqueId == note.ItemUniqueId);
        if (npc == null || item == null) return Native.Text(note.Body, note.EnglishBody);
        var role = catalog.Role(npc.RoleId);
        return Native.Localize("note.body", note.Day, Locations.Title(npc.Region), role.LocalizedName,
            role.LocalizedBackground, Items.Name(item));
    }
    internal bool Custody(GameItem? item)
    {
        if (!Native.Alive(item) || State?.Night is not { Started: true, Ended: false } night) return false;
        var owned = CustodyRules.OwnedIds(night);
        // Directly held loot and its children need no live graph traversal.
        return owned.Count != 0 && (owned.Contains(item!.uniqueId) || NativeGraph.Items(item).Any(child => owned.Contains(child.uniqueId)));
    }
    internal bool CanMoveCustody(GameItem item, IEnumerable<int> destinationParents, bool carryDestination, bool emptyDestination) =>
        CustodyRules.CanMove(State?.Night, NativeGraph.Items(item).Select(x => x.uniqueId).ToHashSet(),
            destinationParents, carryDestination, emptyDestination);
    internal IReadOnlyList<GameItem> CaptureItems() => NativeGraph.RunItems(_store!);
    private bool ReadyAction() => State != null && _store != null && !Busy && _store.runID == State.RunId && _store.saveSlotId == State.SlotId;
    private void SnapshotCarry(RunState next, bool recover = false)
    {
        if (next.Night is not { Ended: false } night || !Browsing || MapUIManager.Instance == null || !MapUIManager.Instance.isOutside) return;
        bool incomplete = false;
        night.Outside = true;
        if (recover) night.Carry.Clear();
        night.Carry = NativeCarry.Snapshot(recover ? message => { incomplete = true; RecoveryWarning(message); } : null);
        var lootIds = night.Loot.Select(loot => loot.UniqueId).ToHashSet();
        if (incomplete) throw new InvalidDataException("Incomplete carry snapshot");
        if (lootIds.Count == 0) return;
        var physical = NativeCarry.IndexIdentifiedItems(CaptureItems().Where(item => lootIds.Contains(item.uniqueId)));
        foreach (var loot in night.Loot)
            if (physical.TryGetValue(loot.UniqueId, out var item)) loot.Graph = NativeGraph.Encode(item);
            else throw new InvalidDataException("Custody item missing from carry");
    }
    internal void BeforeNativeSave(PlayerStore store)
    {
        if (Busy || State == null || !ReferenceEquals(store, _store) || string.IsNullOrWhiteSpace(store.runID) || store.saveSlotId < 0) return;
        try
        {
            RecoverPendingCarry();
            var next = _initial || State.RunId == store.runID ? StateCodec.Clone(State) : Fresh(store);
            next.RunId = store.runID;
            next.SlotId = store.saveSlotId;
            try { SnapshotCarry(next, recover: true); }
            catch (Exception ex)
            {
                RecoveryWarning("Reset saved theft night: " + ex.Message);
                RunRecovery.ResetNight(next);
            }
            try { StateCodec.TrimReceipts(next); StateCodec.Validate(next, catalog); }
            catch (Exception ex)
            {
                RecoveryWarning("Repairing theft data before save: " + ex.Message);
                next = RunRecovery.Decode(JObject.FromObject(next), Fresh(store), catalog, RecoveryWarning);
            }
            RunData.Stage(store, next);
            // The native save can recover the snapshot independently. Keep
            // the live outing open so the player can still sort and return.
            if (!Browsing || next.Night != null) State = next;
            _initial = false;
        }
        catch (Exception ex) { ReportError("Run data preparation failed", ex); }
    }
    private bool Commit(RunState next, Action apply, Action undo, Action finish, Action<Exception>? onFailure = null)
    {
        if (!ReadyAction()) return false;
        Busy = true;
        try
        {
            var result = CommitRules.Execute(apply, () =>
            {
                SnapshotCarry(next); StateCodec.Validate(next, catalog);
            }, undo, () => { State = next; finish(); }, onFailure ?? (ex => ReportError("Theft operation failed", ex)));
            return result == CommitResult.Committed;
        }
        finally { Busy = false; Changed?.Invoke(); }
    }
    internal void FlushErrorNotification()
    {
        if (!_errorNotificationPending) return;
        Message = Native.Localize("a_theft_operation_encountered_an_error_see");
        if (StoreUIManager.Instance == null) return;
        _errorNotificationPending = false;
        try { StoreUIManager.Instance.Notify(Message); }
        catch (Exception ex) { error("Could not display theft error notification: " + ex); }
    }
    internal void ReportError(string reason, Exception? exception = null)
    {
        string key = reason + ":" + exception?.GetType().FullName + ":" + exception?.Message;
        long now = DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond;
        if (_errorTimes.TryGetValue(key, out long previous) && now - previous < 5000) return;
        _errorTimes[key] = now;
        error(exception == null ? reason : reason + ": " + exception);
        _errorNotificationPending = true;
        // Display next frame so the normal result notification from this
        // operation cannot immediately overwrite the error notification.
    }
    internal void DebugLevel(int level)
    {
        if (!ReadyAction() || !DebugEnabled || !Qualified || State?.Night is not { Timing: null, Ended: false } || level is < 0 or > 10) return;
        var next = StateCodec.Clone(State);
        next.Experience = TheftRules.Threshold(level);
        // Keep spent actions where possible; reducing level must still form a valid save.
        next.Night!.SpentActions = Math.Min(next.Night.SpentActions, TheftRules.Actions(level));
        next.Revision++; Commit(next, () => { }, () => { }, () => { });
    }
    internal void DebugWind(int wind)
    {
        if (!ReadyAction() || !DebugEnabled || !Qualified || State?.Night is not { Timing: null, Ended: false } || wind is < 0 or > 100) return;
        var next = StateCodec.Clone(State);
        next.Wind = wind;
        next.Night!.WindPenalty = TheftRules.WindPenalty(wind);
        next.Revision++; Commit(next, () => { }, () => { }, () => { });
    }
    internal void DebugRefreshActions()
    {
        if (!ReadyAction() || !DebugEnabled || !Qualified || State?.Night is not { Timing: null, Ended: false }) return;
        var next = StateCodec.Clone(State);
        next.Night!.SpentActions = 0; next.Night.Attempts = 0;
        foreach (var npc in next.Night.Npcs) npc.Attempts = 0;
        next.Revision++; Commit(next, () => { }, () => { }, () => { });
    }
    internal void DebugRefreshItems(string? target, int region)
    {
        if (!ReadyAction() || !DebugEnabled || !Qualified || State?.Night is not { Timing: null, Ended: false }) return;
        var next = StateCodec.Clone(State); var random = new StableRandom(next.Night!.RandomState);
        foreach (var npc in next.Night.Npcs.Where(x => target != null ? x.Key == target : x.Region == region))
        {
            var fresh = GenerationRules.Npc(next.RunId, next.Night.Day, npc.Region, npc.Position, TheftRules.Level(next.Experience),
                catalog.Role(npc.RoleId), random, Items.CreateCandidate, (_, _) => npc.Sprite);
            npc.Items = fresh.Items; npc.TokenAdmission = fresh.TokenAdmission; npc.TokenEligible = fresh.TokenEligible;
            npc.Attempts = 0; npc.SuccessfulThefts = 0; npc.Alert = false; npc.DebugGenerated = true;
            foreach (var note in next.Notes.Where(x => x.Day == next.Night.Day && x.Target == npc.Key)) note.Invalidated = true;
        }
        next.Night.RandomState = random.State; next.Revision++;
        Commit(next, () => { }, () => { }, () => { });
    }

    internal bool DebugRefreshNpcs(int region)
    {
        if (!ReadyAction() || !Qualified || !Browsing || !Native.Night || State?.Night is not { Timing: null, Ended: false }) return false;
        var next = StateCodec.Clone(State); var night = next.Night!;
        var random = new StableRandom(night.RandomState);
        var remaining = catalog.Regions.Single(x => x.Id == region).Roles.ToList();
        for (int i = 0; i < night.Npcs.Count; i++)
        {
            var previous = night.Npcs[i]; if (previous.Region != region) continue;
            var chosen = random.Weighted(remaining, x => NightEvents.Weight(night, region, x)); remaining.Remove(chosen);
            var npc = GenerationRules.Npc(next.RunId, night.Day, region, previous.Position, TheftRules.Level(next.Experience),
                catalog.Role(chosen.Id), random, Items.CreateCandidate, Items.Portrait);
            npc.DebugGenerated = true; night.Npcs[i] = npc;
            foreach (var note in next.Notes.Where(x => x.Day == night.Day && x.Target == previous.Key)) note.Invalidated = true;
        }
        night.RandomState = random.State; next.Revision++;
        return Commit(next, () => { }, () => { }, () => DebugEnabled = true);
    }
    internal void DebugNpc(int region, int position, string roleId)
    {
        if (State?.Night?.Timing != null) return;
        if (!ReadyAction() || !Qualified || !DebugEnabled || State?.Night == null || State.Night.Ended) return;
        var role = catalog.Role(roleId); if (!role.Enabled) return;
        var next = StateCodec.Clone(State); var random = new StableRandom(next.Night!.RandomState);
        var npc = GenerationRules.Npc(next.RunId, Native.Day, region, position, TheftRules.Level(next.Experience), role, random, Items.CreateCandidate, Items.Portrait); npc.DebugGenerated = true;
        int index = next.Night.Npcs.FindIndex(x => x.Region == region && x.Position == position); next.Night.Npcs[index] = npc; next.Night.RandomState = random.State;
        foreach (var note in next.Notes.Where(x => x.Day == Native.Day && x.Target == npc.Key)) note.Invalidated = true;
        next.Revision++; Commit(next, () => { }, () => { }, () => { });
    }
    internal void DebugDrop(string id, int? step, int? grade)
    {
        if (State?.Night?.Timing != null) return;
        if (!ReadyAction() || !Qualified || !DebugEnabled || State?.Night == null || State.Night.Ended || !catalog.DropIds.Contains(id)) return;
        var next = StateCodec.Clone(State); var rng = new StableRandom(next.Night!.RandomState);
        var descriptor = Items.Create(id, 1, rng, step, grade ?? 2); descriptor.DebugInjected = true; descriptor.Heat = OpeningRules.PriceBand(descriptor.Value) * 25;
        next.Night.RandomState = rng.State; next.Night.Loot.Add(descriptor); next.Revision++;
        var item = Items.Decode(descriptor, _store!); var grid = Browsing ? Native.CarryGrid : Native.MainGrid;
        try
        {
            var plan = NativeItems.Plan(grid, new[] { item });
            if (plan == null) { Notify(Native.Localize("inventory_is_full")); return; }
            if (!next.Night.Started) next.Night.Loot.Remove(descriptor);
            Commit(next, () => { NativeItems.MarkLoot(item, descriptor.Heat); if (!next.Night.Started) item.state.DisableTag(Identity.CustodyTag);
                NativeItems.Place(grid, plan); descriptor.Graph = NativeGraph.Encode(item); }, () => NativeItems.Detach(item), () => { });
        }
        finally { if (item.parentInventory == null) ItemGraphCleanup.Destroy(item); }
    }
}
