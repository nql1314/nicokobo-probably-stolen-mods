using System.Diagnostics;
using AugMechanicalAscension.Domain;
using AugMechanicalAscension.Persistence;
using Nicokobo.ContentInterop;
namespace AugMechanicalAscension.Integration;

internal sealed partial class StoryRuntimeFlow
{
    private const string GraylineContactId = "nicokobo.aug.grayline.phone";
    private const string GraylineActorId = "nicokobo.aug.grayline.supplier";
    private const string GraylineModulePrefix = "nicokobo.mechcore_protocol.module_matrix.";
    private const long GraylinePhoneNumber = 704021;
    private GraylineSupplyState? _graylineState;
    private PlayerStore? _graylineStore;
    private string? _graylineJson, _graylineRequest;
    private long? _graylineGeneration;
    private bool _graylineBlocked, _graylineBusy;
    private string _graylineMessage = "";
    private string _graylineUnavailable = "";
    private long _nextSlowGraylineLog;

    internal void InstallGraylineHooks()
    {
        if (!OfficialClientVisitApi.IsAvailable || !InstallGraylineCounterHooks())
        { log("[WARN] [Grayline] Forge visitor capability unavailable"); return; }
        if (!OfficialPhoneApi.IsAvailable) return;
        var result = OfficialPhoneApi.Register(Owner, new(GraylineContactId, GraylinePhoneNumber,
            context => GraylineSession() && _graylineState?.Unlocked == true &&
                context.RunId == _graylineStore!.runID && context.SlotId == _graylineStore.saveSlotId,
            _ => AugText.Get("aug.grayline_tuesday_supplies"), _ => GraylinePhoneDialogue()));
        if (result.Status != OfficialPresentationStatus.Queued)
            log("[WARN] [Grayline] Contact registration: " + result.Status + "; " + result.Reason);
    }

    private bool GraylineSession()
    {
        var view = bridge.CurrentSession;
        var store = (PlayerStore.HasInstance ? PlayerStore.Instance : null);
        return Current() && view != null && view.Generation == _graylineGeneration && store != null &&
            _graylineStore != null && NativeObject.Handle(store) == NativeObject.Handle(_graylineStore) &&
            store.runID == view.State.GameRunId && store.saveSlotId == view.State.SaveSlotId;
    }
    private bool GraylineReady => _graylineCounterHooks && GraylineSession() && _graylineState != null && !_graylineBlocked && !_graylineBusy;

    internal void UpdateGrayline()
    {
        var view = bridge.CurrentSession;
        if (view == null || _graylineGeneration != view.Generation)
        {
            ClearGrayline();
            if (view == null) return;
            _graylineGeneration = view.Generation;
        }
        if (!Current()) return;
        var timing = new AugUpdateTiming();
        try
        {
            if (_graylineState == null && !_graylineBlocked) BindGrayline();
            if (!GraylineReady) return;
            // A separately verified historical E02 choice unlocks weekly supply once.
            // An existing supply journal then operates even if the story is paused.
            if (!_graylineState!.Unlocked && _state is { } story && story.Choice("E02") != "")
                if (!StageGrayline(_graylineState with { Unlocked = true, Revision = _graylineState.Revision + 1 })) return;
            var s = _graylineState!;
            if (!s.Unlocked) return;
            int today = StoreStation.GetDayCounter();
            if (_graylineRequest != null && s.HasAppointment)
            {
                timing.Step("visit preparation");
                var status = OfficialClientVisitApi.Status(Owner, _graylineRequest).Status;
                timing.Step("visit status");
                if (status == OfficialPresentationStatus.Arrived)
                {
                    if (_graylinePurchaseFailed) return;
                    if (s.VisitState != "arrived" &&
                        !StageGrayline(s with { VisitState = "arrived", Revision = s.Revision + 1 })) return;
                    timing.Step(nameof(StageGrayline));
                    PlaceGraylineCounter();
                    timing.Step(nameof(PlaceGraylineCounter));
                    return;
                }
                if (status == OfficialPresentationStatus.Dismissed)
                {
                    // A failed receipt write may have left a newer journal in the store.
                    if (_graylinePurchaseFailed) BindGrayline();
                    if (_graylineState is { } current && current.AppointmentId == s.AppointmentId)
                        StageGrayline(GraylineSupplyRules.Close(current, today));
                    _graylineRequest = null;
                    ForgetGraylineCounter();
                    return;
                }
                if (status == OfficialPresentationStatus.Queued) return;
                if (status is OfficialPresentationStatus.Indeterminate or OfficialPresentationStatus.Conflict)
                { BlockGrayline("Visitor state is unconfirmed: " + status); return; }
            }
            if (!s.HasAppointment || s.SchemaVersion == 1 && s.Receipts.Length == 0 && s.DebugVisitDay == null)
            {
                var next = GraylineSupplyRules.Schedule(s, today, Guid.NewGuid().ToString("D"), () =>
                {
                    var available = GraylineAvailableValues();
                    return available.Count == 0 ? null : available;
                });
                if (next != s && !StageGrayline(next)) return;
                s = _graylineState!;
            }
            // Forge inserts at the front. Wait until every other visitor has left
            // the waiting queue so Grayline follows the day's final customer.
            if (GraylineSupplyRules.IsDue(s, today, _graylineStore!.storeState == PlayerStore.StoreState.OPEN) &&
                _graylineStore.storeClientManager?.clientStack?.Count == 0)
                QueueGrayline();
        }
        catch (Exception ex) { BlockGrayline(ex.Message); }
        finally
        {
            timing.Step("supply update");
            timing.LogIfSlow(nameof(UpdateGrayline), ref _nextSlowGraylineLog);
        }
    }

    private void BindGrayline()
    {
        _graylineStore = (PlayerStore.HasInstance ? PlayerStore.Instance : null);
        if(_graylineStore==null){BlockGrayline("Current store unavailable");return;}
        var read = OfficialRunDataApi.Read(_graylineStore, Owner, GraylineSupplyState.SaveKey);
        if (read.Status == RunDataStatus.Missing)
            StageGrayline(new() { GameRunId = _graylineStore.runID, SaveSlotId = _graylineStore.saveSlotId });
        else if (read.Status == RunDataStatus.Present)
        {
            _graylineState = GraylineSupplyCodec.Read(read.Json!, _graylineStore.runID, _graylineStore.saveSlotId);
            _graylineJson = read.Json;
            if (_graylineRequest == null && _graylineState.Pending is { } pending)
            {
                // A loaded visit has no live counter. Retire it without reissuing goods.
                if (StageGrayline(GraylineSupplyRules.Close(_graylineState, pending.Day)))
                    log("[WARN] [Grayline] Closed loaded visit with unfinished purchase " + pending.OperationId);
            }
        }
        else BlockGrayline(read.Reason ?? "Supply journal unavailable");
    }
    internal void ClearGrayline()
    {
        _graylineState = null; _graylineStore = null; _graylineJson = null; _graylineRequest = null;
        _graylineGeneration = null; _graylineBlocked = false; _graylineBusy = false;
        _graylineMessage = ""; _graylineUnavailable = ""; ForgetGraylineCounter();
    }
    private bool StageGrayline(GraylineSupplyState next, bool purchasing = false)
    {
        if (!GraylineSession() || _graylineStore == null || _graylineBlocked) return false;
        string json = GraylineSupplyCodec.Write(next);
        var write = OfficialRunDataApi.Stage(_graylineStore, Owner, GraylineSupplyState.SaveKey,
            next.GameRunId, next.SaveSlotId, _graylineJson, json);
        if (write.Status != RunDataStatus.StagedInMemory)
        {
            string reason = write.Reason ?? $"Supply journal write failed: {write.Status}";
            if (purchasing) throw new InvalidOperationException(reason);
            BlockGrayline(reason);
            return false;
        }
        _graylineState = next; _graylineJson = json; return true;
    }
    private void BlockGrayline(string reason)
    {
        _graylineBlocked = true;
        _graylineMessage = AugText.Get("aug.grayline_supply_is_paused_its_visit_record");
        log("[ERROR] [Grayline] " + reason);
    }
    private static IReadOnlyList<StockOffer> GraylineModuleOffers() =>
        SupplyCatalog.Available(StockCategory.Module, StockSupplier.Thief | StockSupplier.Inventor);

    private bool GraylineRegistered(string id) => OfficialItemApi.Exists(id) &&
        (!id.StartsWith(GraylineModulePrefix, StringComparison.Ordinal) ||
         GraylineModuleOffers().Any(offer => offer.ItemId == id));

    private IReadOnlyDictionary<string, int> GraylineAvailableValues()
    {
        var values = new Dictionary<string, int>(StringComparer.Ordinal);
        var unavailable = new List<string>();
        long started = Stopwatch.GetTimestamp();
        var moduleIds = GraylineModuleOffers().Select(offer => offer.ItemId).ToHashSet(StringComparer.Ordinal);
        long queryTicks = Stopwatch.GetTimestamp() - started;
        long valuationTicks = 0;
        int valued = 0;
        foreach (var product in GraylineSupplyRules.Products)
        {
            string id = product.ItemId;
            started = Stopwatch.GetTimestamp();
            bool registered = OfficialItemApi.Exists(id) &&
                (!id.StartsWith(GraylineModulePrefix, StringComparison.Ordinal) || moduleIds.Contains(id));
            queryTicks += Stopwatch.GetTimestamp() - started;
            if (!registered)
            {
                unavailable.Add(GraylineName(id) + AugText.Get("aug.provider_unavailable_or_category_disabled"));
                continue;
            }

            started = Stopwatch.GetTimestamp();
            // Read the current factory without assigning a UID or firing owned-item grants.
            var item = DirectoryMaster.Create<GameItem>(id);
            long basis;
            try
            {
                basis = item?.GetCurrentValue(includeEvents: false) ?? 0;
            }
            finally { if (item != null && item.parentInventory == null) Nicokobo.ModUtilities.ItemGraphCleanup.Destroy(item); }
            valuationTicks += Stopwatch.GetTimestamp() - started;
            valued++;
            if (basis <= 0 || basis > int.MaxValue)
            {
                unavailable.Add(GraylineName(id) + AugText.Get("aug.item_value_unavailable"));
                continue;
            }
            values.Add(id, checked((int)basis));
        }
        _graylineUnavailable = unavailable.Count == 0 ? "" : AugText.Get("aug.unavailable_stock") + string.Join(AugText.Get("aug.format"), unavailable);
        if (values.Count != 0)
        {
            double msPerTick = 1000.0 / Stopwatch.Frequency;
            log($"[perf] Grayline shipment query={queryTicks * msPerTick:F1}ms; valuation={valuationTicks * msPerTick:F1}ms; priced={values.Count}/{valued}");
        }
        return values;
    }
    private static string GraylineName(string id)
    {
        var product = GraylineSupplyRules.Product(id) ?? GraylineSupplyRules.Product(id, 3) ?? GraylineSupplyRules.Product(id, 1);
        if (product == null) return id;
        return id == OfficialManufacturingTerminal.ItemId
            ? Nicokobo.ModUtilities.ModText.Get("nicokobo.forge", product.NameKey)
            : AugText.Get(product.NameKey);
    }
    private string GraylineDate(int day) => AugText.Get("aug.day_tuesday", day);

    private OfficialDialogueScript GraylinePhoneDialogue()
    {
        var generation = _graylineGeneration;
        string run = _graylineStore!.runID; int slot = _graylineStore.saveSlotId;
        return OfficialDialogueApi.Create(Owner, new(GraylineContactId + ".call", AugText.Get("aug.grayline"),
            (_graylineBlocked || _graylinePurchaseFailed ? _graylineMessage + "\n\n" : "") +
            AugText.Get("aug.grayline_i_ll_come_tuesday_after_your"),
            [new("schedule", AugText.Get("aug.ask_which_day_he_s_next_coming"), "", context =>
            {
                if (generation != _graylineGeneration || context.RunId != run || context.SlotId != slot || !GraylineReady)
                    return AugText.Get("aug.the_supply_session_is_unavailable");
                int next = GraylineSupplyRules.NextTuesday(StoreStation.GetDayCounter());
                if (_graylineState!.LastVisitDay >= next) next = GraylineSupplyRules.NextTuesday(checked(_graylineState.LastVisitDay + 1));
                if (_graylineState.HasAppointment && _graylineState.DueDay > next) next = _graylineState.DueDay;
                return AugText.Get("aug.i_ll_be_last_in_if_you", GraylineDate(next));
            })], AugText.Get("aug.confirm"), AugText.Get("aug.back"), AugText.Get("aug.hang_up"), AugText.Get("aug.hang_up"),
            AugText.Get("aug.the_visit_schedule_hasn_t_changed"))
            { RequireConfirmation = false });
    }

    private void QueueGrayline()
    {
        if (!GraylineReady || _graylineState?.HasAppointment != true) return;
        var s = _graylineState;
        _graylineRequest ??= s.AppointmentId + ":" + _graylineGeneration;
        var dialogue = OfficialDialogueApi.Create(Owner, new(GraylineContactId + ".shop", AugText.Get("aug.grayline"),
            AugText.Get("aug.the_goods_are_on_the_counter_take"),
            [new("leave", AugText.Get("aug.pack_up_the_rest_and_send_him"), "", _ => "")],
            AugText.Get("aug.confirm"), AugText.Get("aug.back"), AugText.Get("aug.dismiss"), AugText.Get("aug.dismiss"),
            AugText.Get("aug.no_purchase_this_time"))
            { RequireConfirmation = false });
        string sprite = StoryPortraits.Register("grayline");
        var result = OfficialClientVisitApi.Queue(Owner, _graylineRequest, s.GameRunId, s.SaveSlotId, GraylineActorId, sprite, dialogue);
        if (result.Status == OfficialPresentationStatus.Queued)
        {
            ConfigureGraylineCounterClient();
            if (s.VisitState != "queued") StageGrayline(s with { VisitState = "queued", Revision = s.Revision + 1 });
            _graylineMessage = AugText.Get("aug.grayline_is_waiting_with_a_fixed_shipment");
        }
        else if (result.Status is OfficialPresentationStatus.Indeterminate or OfficialPresentationStatus.Conflict)
            BlockGrayline("Queue unconfirmed: " + result.Status + "; " + result.Reason);
    }

    internal bool RequestGraylineDebugVisit()
    {
        if (!Enabled || !Current())
        { _message = AugText.Get("aug.use_this_in_the_current_run_with"); return false; }
        UpdateGrayline();
        if (!GraylineReady || _graylinePurchaseFailed)
        { _message = _graylineMessage.Length == 0 ? AugText.Get("aug.grayline_supply_is_not_ready") : _graylineMessage; return false; }
        if (!_graylineState!.Unlocked)
        { _message = AugText.Get("aug.complete_e02_to_unlock_grayline_s_supplies"); return false; }
        if (_graylineStore!.storeState != PlayerStore.StoreState.OPEN)
        { _message = AugText.Get("aug.open_the_shop_before_calling_grayline_in"); return false; }
        try
        {
            int today = StoreStation.GetDayCounter();
            var next = _graylineState.HasAppointment ? _graylineState : GraylineSupplyRules.Schedule(
                _graylineState, today, Guid.NewGuid().ToString("D"),
                GraylineAvailableValues);
            if (!next.HasAppointment)
            { _message = AugText.Get("aug.today_s_supply_visit_is_over_the"); return false; }
            next = GraylineSupplyRules.Expedite(next, today);
            if (next != _graylineState && !StageGrayline(next)) { _message = _graylineMessage; return false; }
            var status = _graylineRequest == null ? OfficialPresentationStatus.Unavailable : OfficialClientVisitApi.Status(Owner, _graylineRequest).Status;
            if (status == OfficialPresentationStatus.Arrived)
            {
                PlaceGraylineCounter();
                _message = _graylineBlocked ? _graylineMessage : AugText.Get("aug.grayline_is_at_the_counter_with_the");
                return !_graylineBlocked;
            }
            if (status != OfficialPresentationStatus.Queued) QueueGrayline();
            if (_graylineBlocked || _graylineRequest == null) { _message = _graylineMessage; return false; }
            var queued = OfficialClientVisitApi.Status(Owner, _graylineRequest).Status;
            if (queued is not (OfficialPresentationStatus.Queued or OfficialPresentationStatus.Arrived))
            { _message = AugText.Get("aug.grayline_could_not_enter_the_visitor_queue") + queued; return false; }
            if (_graylineStore.currentClientInstance == null && !_graylineStore.isClientArrived)
                _graylineStore.TryCallNextClient();
            _message = AugText.Get("aug.grayline_is_next_without_waiting_for_tuesday");
            log("[Debug] Grayline immediate visit; appointment=" + next.AppointmentId + "; day=" + today);
            return true;
        }
        catch (Exception ex) { _message = AugText.Get("aug.grayline_visit_incomplete") + ex.Message; log("[ERROR] [Grayline] Immediate visit: " + ex); return false; }
    }

    internal string GraylineSummary()
    {
        var s = _graylineState;
        if (s == null) return AugText.Get("aug.grayline_supply_not_ready");
        return AugText.Get("aug.grayline_supply_last_visitor_every_tuesday_phone", GraylinePhoneNumber, s.Unlocked, _graylineBlocked, _graylineBusy, s.AppointmentId ?? "—", s.VisitState, s.BookedDay, s.DueDay, s.LastVisitDay, s.Revision, s.Receipts.Length, s.Pending?.OperationId ?? "—") +
            string.Join("\n", s.Stock.Select(x => GraylineName(x.ItemId) + $" {x.Remaining}/{x.InitialQuantity}")) + "\n" + _graylineUnavailable + "\n" + _graylineMessage;
    }
}
