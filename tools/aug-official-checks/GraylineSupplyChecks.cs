using AugMechanicalAscension.Domain;
using AugMechanicalAscension.Persistence;

internal static class GraylineSupplyChecks
{
    internal static void Run(Action<bool, string> check)
    {
        const string run = "grayline-run";
        const int slot = 4;
        GraylineSupplyState Fresh(bool unlocked = true) => new() { GameRunId = run, SaveSlotId = slot, Unlocked = unlocked };
        string Id() => Guid.NewGuid().ToString("D");
        string[] all = GraylineSupplyRules.Products.Select(x => x.ItemId).ToArray();
        Dictionary<string, int> Values(IEnumerable<string> ids) => ids.ToDictionary(id => id, id => GraylineSupplyRules.Product(id)?.BaseValue ?? 1);
        GraylineSupplyState Book(int day, string[]? ids = null) => GraylineSupplyRules.Schedule(Fresh(), day, Id(), Values(ids ?? all));
        GraylineSupplyState Roundtrip(GraylineSupplyState state) => GraylineSupplyCodec.Read(GraylineSupplyCodec.Write(state), run, slot);
        bool Rejects(GraylineSupplyState state)
        {
            try { Roundtrip(state); return false; }
            catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or OverflowException) { return true; }
        }
        bool Throws(Action action)
        { try { action(); return false; } catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or OverflowException) { return true; } }
        for (int monday = 1; monday < 30; monday += 7)
        {
            check(GraylineSupplyRules.NextTuesday(monday) == monday + 1, "weekly supply uses the nearest Tuesday");
            check(GraylineSupplyRules.NextTuesday(monday + 1) == monday + 1, "E02 completed on Tuesday can unlock that day's final visit");
            for (int day = monday + 2; day < monday + 7; day++)
                check(GraylineSupplyRules.NextTuesday(day) == monday + 8, "later weekdays wait for the following Tuesday");
            check(GraylineSupplyRules.NextNaturalTuesday(monday) == monday + 8, "Monday books next week's Tuesday, not tomorrow");
            check(GraylineSupplyRules.NextNaturalTuesday(monday + 1) == monday + 8, "Tuesday books exactly seven days later");
            check(GraylineSupplyRules.NextNaturalTuesday(monday + 6) == monday + 8, "Sunday books Tuesday two days later");
            for (int day = monday; day < monday + 7; day++)
                check(GraylineSupplyRules.NextNaturalTuesday(day) == monday + 8, "one natural week shares the following Tuesday");
        }
        check(Throws(() => GraylineSupplyRules.NextNaturalTuesday(-1)) && Throws(() => GraylineSupplyRules.NextNaturalTuesday(int.MaxValue)) &&
            Throws(() => GraylineSupplyRules.NextTuesday(-1)) && Throws(() => GraylineSupplyRules.NextTuesday(int.MaxValue)),
            "invalid/overflow dates cannot become appointments");
        check(Throws(() => GraylineSupplyRules.Schedule(Fresh(false), 1, Id(), Values(all))), "E02-unlocked supply is required for booking");
        check(Throws(() => GraylineSupplyRules.Schedule(Fresh(), 1, Id(), new Dictionary<string, int>())), "missing providers do not create placeholder stock");
        var booked = Book(8);
        var currentValues = Values(all);
        currentValues[GraylineSupplyRules.MaterialPrefix + "quartz"] = 10;
        var repriced = GraylineSupplyRules.Schedule(Fresh(), 8, Id(), currentValues);
        check(repriced.Stock.Single(item => item.ItemId == GraylineSupplyRules.MaterialPrefix + "quartz").UnitPrice == 13 &&
            Roundtrip(repriced).Stock.Single(item => item.ItemId == GraylineSupplyRules.MaterialPrefix + "quartz").UnitPrice == 13,
            "shipments quote the current provider value and preserve that quote across a normal reload");
        check(booked.Stock.Any(x => x.ItemId.EndsWith(".quartz", StringComparison.Ordinal) && x.InitialQuantity == 2) &&
            booked.Stock.Any(x => x.ItemId.EndsWith(".titanium_ore", StringComparison.Ordinal) && x.InitialQuantity == 2),
            "new appointments reserve two quartz and two titanium ore instead of finished components");
        check(booked.CatalogVersion == GraylineSupplyRules.CurrentCatalogVersion && booked.Stock.Sum(x => x.InitialQuantity) == 10 &&
            booked.Stock.Count(x => GraylineSupplyRules.Product(x.ItemId)!.Kind == GraylineProductKind.Machine) == 2 &&
            booked.Stock.Count(x => GraylineSupplyRules.Product(x.ItemId)!.Kind is GraylineProductKind.BasicNode or GraylineProductKind.RareNode) == 2 &&
            booked.Stock.Count(x => GraylineSupplyRules.Product(x.ItemId)!.Kind is GraylineProductKind.BasicModule or GraylineProductKind.RareModule) == 2 &&
            booked.Stock.Select(x => x.ItemId).Distinct().Count() == booked.Stock.Length,
            "new shipments contain two distinct machines, two distinct nodes and two distinct modules, ten units total");
        string[] matrixNodes = ["performance_conversion_node", "efficiency_conversion_node", "quality_conversion_node",
            "isolation_node", "learning_acceleration_node"];
        check(GraylineSupplyRules.Products.Where(x => x.Kind is GraylineProductKind.BasicNode or GraylineProductKind.RareNode)
            .Select(x => x.ItemId).OrderBy(x => x).SequenceEqual(matrixNodes.Select(x => "nicokobo.mechcore_protocol.module_matrix." + x).OrderBy(x => x)),
            "new node stock is restricted to the five fixed Module Matrix node IDs");
        check(!all.Contains("node_small") && !all.Contains("node_medium"),
            "native small and medium nodes are absent from the entire new appointment pool");
        check(!booked.Stock.Any(x => GraylineSupplyRules.LegacyProducts.Any(p => p.ItemId == x.ItemId)),
            "new shipments do not sell finished materials or components");
        GraylineSupplyState Draw(int tier) => GraylineSupplyRules.Schedule(Fresh(), 1, Id(), Values(all), bound => bound == 3 ? tier : 0);
        check(Draw(0).Stock.Count(x => GraylineSupplyRules.Product(x.ItemId)!.Kind == GraylineProductKind.RareNode) == 2 &&
            Draw(1).Stock.Count(x => GraylineSupplyRules.Product(x.ItemId)!.Kind == GraylineProductKind.RareNode) == 2 &&
            Draw(2).Stock.Count(x => GraylineSupplyRules.Product(x.ItemId)!.Kind == GraylineProductKind.RareNode) == 2,
            "node selection draws two distinct Matrix nodes independently of module tier weights");
        check(Draw(0).Stock.Count(x => GraylineSupplyRules.Product(x.ItemId)!.Kind == GraylineProductKind.BasicModule) == 2 &&
            Draw(1).Stock.Count(x => GraylineSupplyRules.Product(x.ItemId)!.Kind == GraylineProductKind.BasicModule) == 2 &&
            Draw(2).Stock.Count(x => GraylineSupplyRules.Product(x.ItemId)!.Kind == GraylineProductKind.RareModule) == 2,
            "modules independently use the night-shop basic-to-rare weight of two to one");
        check(Throws(() => GraylineSupplyRules.Schedule(Fresh(), 1, Id(), Values(all), n => n)), "invalid random indexes cannot produce a shipment");
        check(booked.SchemaVersion == 2 && booked.DueDay == 9 && booked.BookedDay == 8 && booked.VisitState == "waiting", "weekly supply records the nearest Tuesday");
        check(booked.Receipts.Length == 0 && booked.Pending == null, "booking creates no money/delivery receipt");
        check(ReferenceEquals(GraylineSupplyRules.Schedule(booked, 8, Id(), Values(all)), booked), "repeated scheduling does not change date or refresh stock");
        check(!GraylineSupplyRules.IsDue(booked, 8, true) && GraylineSupplyRules.IsDue(booked, 9, true), "arrival starts on the due-day boundary");
        check(!GraylineSupplyRules.IsDue(booked, 9, false) && !GraylineSupplyRules.IsDue(booked, 10, true) &&
            GraylineSupplyRules.IsDue(booked, 16, true), "a closed Tuesday waits for another Tuesday, never an ordinary weekday");
        var restoredAppointment = Roundtrip(booked);
        check(restoredAppointment.AppointmentId == booked.AppointmentId && restoredAppointment.DueDay == 9 &&
            restoredAppointment.Stock.SequenceEqual(booked.Stock), "save/load preserves the appointment and its exact stock");
        string quartz = GraylineSupplyRules.MaterialPrefix + "quartz";
        var narrow = Book(7, [quartz]);
        check(narrow.Stock.Length == 1 && narrow.Stock[0].ItemId == quartz && narrow.DueDay == 9,
            "stock contains only available registered whitelist products");
        var rareOnly = GraylineSupplyRules.Products.Where(x => x.Kind == GraylineProductKind.RareNode).Take(1).Select(x => x.ItemId).ToArray();
        check(Book(7, rareOnly).Stock.Length == 1, "missing pools yield available nodes without duplicate or placeholder goods");
        string[] oneModule = ["system_module_overclock"];
        check(Book(7, oneModule).Stock.Single().ItemId == oneModule[0],
            "an unavailable basic tier yields a registered rare module without duplication");
        check(Throws(() => Book(7, ["node_small"])) && Throws(() => Book(7, ["node_medium"])),
            "native nodes alone cannot create new stock");
        check(GraylineSupplyRules.Products.All(x => !x.ItemId.StartsWith("nicokobo.aug.cybernetic.", StringComparison.Ordinal) &&
            !x.ItemId.StartsWith("nicokobo.aug.story.", StringComparison.Ordinal)), "supply excludes complete cybernetics and story items");
        check(GraylineSupplyRules.Quote(33) == 42 && GraylineSupplyRules.Quote(100) == 125 &&
            GraylineSupplyRules.Quote(200) == 250, "price follows the current item's value without a historical floor");
        // Keep the earlier four-quartz shipment's purchase/readback coverage.
        var arrived = narrow with { CatalogVersion = 2, VisitState = "arrived",
            Stock = [narrow.Stock[0] with { InitialQuantity = 4, Remaining = 4 }] };
        var row = arrived.Stock[0]; string operation = Id();
        string? Denial(GraylineSupplyState s, int amount = 2, int price = 25, int cash = 1000, string? request = null) =>
            GraylineSupplyRules.PurchaseDenial(s, request ?? s.AppointmentId!, operation, row.ItemId, amount, price, cash);
        check(Denial(booked) == "visit" && Denial(arrived, request: Id()) == "visit", "waiting appointments and stale visit callbacks cannot buy");
        check(Denial(arrived, amount: 0) == "stock" && Denial(arrived, amount: 5) == "stock", "requested quantity must fit this shipment");
        check(Denial(arrived, price: 24) == "price", "stale quoted prices cannot commit");
        check(Denial(arrived, cash: 49) == "cash" && Denial(arrived, cash: 50) == null, "batch affordability checks the exact total");
        var prepared = GraylineSupplyRules.Prepare(arrived, operation, row.ItemId, 2, 25, 9, 1000, [11, 12]);
        check(prepared.Stock[0].Remaining == 4 && prepared.Receipts.Length == 0 && Denial(prepared) == "pending",
            "preparation does not spend stock and blocks a second transaction");
        var loadedPending = Roundtrip(prepared);
        check(loadedPending.Pending?.OperationId == operation && loadedPending.Pending.OutputUids.SequenceEqual([11, 12]),
            "unconfirmed output identities survive load without automatic replay");
        check(Throws(() => GraylineSupplyRules.Prepare(arrived, Id(), row.ItemId, 2, 25, 9, 1000, [11, 11])),
            "duplicate output identities reject the whole batch");
        var purchased = GraylineSupplyRules.Commit(prepared);
        check(purchased.Pending == null && purchased.Stock[0].Remaining == 2 && purchased.Receipts[0].CashAfter == 950 &&
            purchased.Receipts[0].Quantity == 2, "only commitment deducts the full stock quantity and records the exact payment");
        check(Denial(purchased) == "duplicate", "a committed purchase token cannot buy again");
        var loaded = Roundtrip(purchased);
        check(loaded.Stock[0].Remaining == 2 && loaded.Receipts[0].OutputUids.SequenceEqual([11, 12]), "stock debit and receipt persist together");
        check(GraylineSupplyCodec.Read(GraylineSupplyCodec.Write(purchased).Replace(",\"nativeTrade\":false", ""), run, slot)
            .Receipts[0] is { NativeTrade: false, UnitPrice: 25 }, "historical receipts without the native trade marker remain readable");
        var negotiated = GraylineSupplyRules.Prepare(arrived, Id(), row.ItemId, 2, 17, 9, 1000, [21, 22], nativeTrade: true);
        check(Roundtrip(negotiated).Pending is { NativeTrade: true, UnitPrice: 17 },
            "native pending purchases retain their negotiated price below the historical fixed quote");
        var nativeBought = Roundtrip(GraylineSupplyRules.Commit(negotiated));
        check(nativeBought.Receipts[0] is { NativeTrade: true, UnitPrice: 17, CashAfter: 966 } && nativeBought.Stock[0].Remaining == 2,
            "native receipt balances negotiated payment with the finite shipment");
        check(Roundtrip(nativeBought with { Receipts = [nativeBought.Receipts[0] with { NativeTrade = false }] })
            .Receipts[0] is { UnitPrice: 17, CashAfter: 966 },
            "completed receipts keep their own saved transaction price without reapplying the catalog quote");
        var free = GraylineSupplyRules.Commit(GraylineSupplyRules.Prepare(arrived, Id(), row.ItemId, 1, 0, 9, 0, [23], nativeTrade: true));
        check(Roundtrip(free).Receipts[0].CashAfter == 0 && free.Stock[0].Remaining == 3,
            "a zero native price still consumes one unit and records its unique identity");
        check(Throws(() => GraylineSupplyRules.Prepare(arrived, Id(), row.ItemId, 1, -1, 9, 1000, [24], nativeTrade: true)) &&
            Throws(() => GraylineSupplyRules.Prepare(arrived, Id(), row.ItemId, 2, 17, 9, 33, [24, 25], nativeTrade: true)),
            "native price purchases still reject negative prices and insufficient aggregate cash");
        check(Rejects(purchased with { Stock = [row with { Remaining = 4 }] }), "forged refreshed stock is rejected against its receipts");
        check(Rejects(purchased with { Receipts = [purchased.Receipts[0] with { CashAfter = 951 }] }), "a contradictory money receipt is rejected");
        check(Rejects(purchased with { Receipts = [purchased.Receipts[0], purchased.Receipts[0]] }), "duplicate saved purchase receipts are rejected");
        var second = GraylineSupplyRules.Prepare(purchased, Id(), row.ItemId, 2, 25, 9, 950, [13, 14]);
        var interrupted = Roundtrip(GraylineSupplyRules.Prepare(purchased, Id(), row.ItemId, 1, 17, 9, 950, [15], nativeTrade: true));
        var closedAfterError = Roundtrip(GraylineSupplyRules.Close(interrupted, 9));
        check(!closedAfterError.HasAppointment && closedAfterError.Pending == null &&
            closedAfterError.Stock[0].Remaining == 2 &&
            closedAfterError.Receipts.Single().OutputUids.SequenceEqual([11, 12]),
            "closing a failed visit retires the pending purchase and preserves earlier sales");
        check(GraylineSupplyRules.Schedule(closedAfterError, 9, Id(), Values(all)) == closedAfterError &&
            GraylineSupplyRules.Schedule(closedAfterError, 10, Id(), Values(all)).DueDay == 16,
            "a failed visit cannot be replayed on the same day and the next weekly visit is available");
        var empty = GraylineSupplyRules.Commit(second);
        check(empty.Stock[0].Remaining == 0 && empty.Receipts.Length == 2 && Roundtrip(empty).Stock[0].Remaining == 0,
            "multiple distinct purchases share one finite saved shipment");
        var closed = GraylineSupplyRules.Close(empty, 9);
        check(!closed.HasAppointment && closed.Stock[0].Remaining == 0 && !GraylineSupplyRules.IsDue(closed, 20, true),
            "dismissal closes the old shipment without restoring stock or requeuing it");
        check(GraylineSupplyRules.Close(closed, 9) == closed, "duplicate dismissal is idempotent");
        var restoredClosed = Roundtrip(closed);
        int valuationCalls = 0;
        IReadOnlyDictionary<string, int> ReadValues()
        {
            valuationCalls++;
            return Values(all);
        }
        var sameDay = GraylineSupplyRules.Schedule(restoredClosed, 9, Id(), ReadValues);
        check(restoredClosed.LastVisitDay == 9 && ReferenceEquals(sameDay, restoredClosed) && valuationCalls == 0,
            "a saved completed visit skips stock valuation and cannot return on the same Tuesday");
        bool missingVisitDay = false;
        try { GraylineSupplyCodec.Read(GraylineSupplyCodec.Write(closed).Replace("\"lastVisitDay\":9,", ""), run, slot); }
        catch (Exception ex) when(ex is InvalidDataException or AugMechanicalAscension.Serialization.JsonException) { missingVisitDay = true; }
        check(missingVisitDay && Rejects(closed with { LastVisitDay = -1 }), "new saved visits require a completion day to preserve same-day deduplication");
        var next = GraylineSupplyRules.Schedule(closed, 10, Id(), ReadValues);
        check(valuationCalls == 1 && next.AppointmentId != closed.AppointmentId && next.DueDay == 16 && next.Receipts.Length == 0 && next.Stock[0].Remaining == 2,
            "the next weekly visit starts a fresh finite shipment without a phone call");
        var old = booked with { SchemaVersion = 1, BookedDay = 1, CatalogVersion = 1, Stock = GraylineSupplyRules.LegacyProducts.Select(x =>
            new GraylineStock(x.ItemId, x.Quantity, x.Quantity, GraylineSupplyRules.Quote(x.BaseValue))).ToArray() };
        var oldJson = GraylineSupplyCodec.Write(old).Replace("\"catalogVersion\":1,", "").Replace("\"lastVisitDay\":-1,", "");
        var historical = GraylineSupplyCodec.Read(oldJson, run, slot);
        check(historical.CatalogVersion == 1 && historical.Stock.SequenceEqual(old.Stock), "historical saves keep the exact legacy shipment");
        var migrated = GraylineSupplyRules.Schedule(historical, 1, Id(),
            () => throw new InvalidOperationException("A historical appointment already has fixed stock"));
        check(migrated.SchemaVersion == 2 && migrated.DueDay == 2 && migrated.AppointmentId == historical.AppointmentId &&
            Roundtrip(migrated).Stock.SequenceEqual(historical.Stock), "unserved historical bookings adopt the weekly schedule without changing goods");
        var oldBought = GraylineSupplyRules.Commit(GraylineSupplyRules.Prepare(historical with { VisitState = "arrived" },
            Id(), "common_electronic", 1, 42, 9, 1000, [21]));
        check(Roundtrip(oldBought).Stock[0].Remaining == 2 && Roundtrip(oldBought).Receipts[0].CashAfter == 958,
            "legacy stock still supports purchases and receipt readback");
        check(GraylineSupplyRules.Schedule(GraylineSupplyRules.Close(oldBought, 9), 10, Id(), Values(all)).CatalogVersion == GraylineSupplyRules.CurrentCatalogVersion,
            "the next appointment switches a legacy save to the new catalog");
        check(Rejects(old with { CatalogVersion = 2 }) && Rejects(booked with { CatalogVersion = 1 }), "catalog versions cannot reinterpret saved goods");
        var extraMachine = GraylineSupplyRules.Products.First(x => x.Kind == GraylineProductKind.Machine && !booked.Stock.Any(s => s.ItemId == x.ItemId));
        check(Roundtrip(booked with { Stock = [.. booked.Stock, new(extraMachine.ItemId, 1, 1, GraylineSupplyRules.Quote(extraMachine.BaseValue))] }).Stock.Length == booked.Stock.Length + 1,
            "saved shipments preserve machines without reapplying current generation quotas");
        var extraModule = GraylineSupplyRules.Products.First(x => x.Kind is GraylineProductKind.BasicModule or GraylineProductKind.RareModule &&
            !booked.Stock.Any(s => s.ItemId == x.ItemId));
        check(Roundtrip(booked with { Stock = [.. booked.Stock, new(extraModule.ItemId, 1, 1, GraylineSupplyRules.Quote(extraModule.BaseValue))] }).Stock.Length == booked.Stock.Length + 1,
            "saved shipments preserve modules without reapplying current generation quotas");
        var changedStock = booked.Stock[0] with { InitialQuantity = 5, Remaining = 5, UnitPrice = 1 };
        var changedShipment = booked with { VisitState = "arrived", Stock = [changedStock, .. booked.Stock.Skip(1)] };
        var changedPending = GraylineSupplyRules.Prepare(changedShipment, Id(), changedStock.ItemId, 3, 1,
            booked.DueDay, 100, [51, 52, 53]);
        check(Roundtrip(changedPending).Stock[0] == changedStock && Roundtrip(changedPending).Pending!.Quantity == 3,
            "saved prices, quantities and pending purchases survive catalog balance changes unchanged");
        var changedBought = Roundtrip(GraylineSupplyRules.Commit(changedPending));
        check(changedBought.Stock[0].Remaining == 2 && changedBought.Receipts.Single().CashAfter == 97,
            "historical purchases retain their own saved stock and cash balance");
        check(Rejects(changedBought with { Stock = [changedBought.Stock[0] with { Remaining = 3 }, .. changedBought.Stock.Skip(1)] }) &&
            Rejects(changedBought with { Receipts = [changedBought.Receipts[0] with { CashAfter = 98 }] }),
            "catalog-independent loading still rejects inconsistent stock and cash receipts");
        foreach (int version in new[] { 2, 3 })
        {
            var medium = GraylineSupplyRules.Product("node_medium", version)!;
            var mediumAppointment = booked with { CatalogVersion = version, VisitState = "arrived",
                Stock = [new(medium.ItemId, 1, 1, GraylineSupplyRules.Quote(medium.BaseValue))] };
            check(Roundtrip(mediumAppointment).Stock.Single().ItemId == "node_medium",
                "existing medium-node appointments still load with their historical catalog");
            var mediumBought = GraylineSupplyRules.Commit(GraylineSupplyRules.Prepare(mediumAppointment, Id(), medium.ItemId,
                1, mediumAppointment.Stock[0].UnitPrice, 9, 1000, [41]));
            check(Roundtrip(mediumBought).Stock[0].Remaining == 0 && Roundtrip(mediumBought).Receipts[0].OutputUids.Single() == 41,
                "old medium nodes remain purchasable without resetting stock or receipts");
            check(Rejects(mediumAppointment with { CatalogVersion = GraylineSupplyRules.CurrentCatalogVersion }),
                "new catalogs cannot claim old medium-node stock");
            check(Rejects(booked with { CatalogVersion = version }), "older catalogs cannot accept newly added modules");
        }
        var oldSmall = GraylineSupplyRules.Product("node_small", 4)!;
        var oldSmallAppointment = booked with { CatalogVersion = 4, VisitState = "arrived",
            Stock = [new(oldSmall.ItemId, 1, 1, GraylineSupplyRules.Quote(oldSmall.BaseValue))] };
        check(Roundtrip(oldSmallAppointment).Stock.Single().ItemId == "node_small" &&
            Rejects(oldSmallAppointment with { CatalogVersion = GraylineSupplyRules.CurrentCatalogVersion }),
            "the previous catalog retains saved small-node rows while new catalogs reject them");
        var oldSmallBought = GraylineSupplyRules.Commit(GraylineSupplyRules.Prepare(oldSmallAppointment, Id(), oldSmall.ItemId,
            1, oldSmallAppointment.Stock[0].UnitPrice, 9, 1000, [43]));
        check(Roundtrip(oldSmallBought).Receipts.Single().OutputUids.Single() == 43,
            "historical native-node purchase receipts remain readable without restocking them");
        var moduleAppointment = Book(1, oneModule) with { VisitState = "arrived" };
        var moduleBought = GraylineSupplyRules.Commit(GraylineSupplyRules.Prepare(moduleAppointment, Id(), oneModule[0], 1,
            moduleAppointment.Stock[0].UnitPrice, 9, 1000, [42]));
        check(Roundtrip(moduleBought).Stock[0].Remaining == 0 && Roundtrip(moduleBought).Receipts[0].OutputUids.Single() == 42,
            "new module purchases retain finite stock and output identities through save/load");
        check(Rejects(booked with { DueDay = 2 }) && Rejects(booked with { DueDay = 10 }), "saved appointments must remain next-natural-week Tuesdays");
        check(Rejects(booked with { Unlocked = false }) && Rejects(booked with { Stock = [] }), "impossible unlock/stock combinations cannot load");
        bool wrongRun = false, duplicateField = false;
        try { GraylineSupplyCodec.Read(GraylineSupplyCodec.Write(booked), "other-run", slot); }
        catch (Exception ex) when(ex is InvalidDataException or AugMechanicalAscension.Serialization.JsonException) { wrongRun = true; }
        try { GraylineSupplyCodec.Read(GraylineSupplyCodec.Write(booked).Replace("\"schemaVersion\":2", "\"schemaVersion\":2,\"schemaVersion\":2"), run, slot); }
        catch (Exception ex) when(ex is InvalidDataException or AugMechanicalAscension.Serialization.JsonException) { duplicateField = true; }
        check(wrongRun && duplicateField, "run identity and duplicate JSON fields fail closed without overwriting native data");
        check(Roundtrip(Fresh(false)).AppointmentId == null, "locked fresh runs have no schedule or stock");
        var earlySchedule = Book(1);
        var immediate = GraylineSupplyRules.Expedite(earlySchedule, 1);
        check(GraylineSupplyRules.IsDue(immediate, 1, true) && !GraylineSupplyRules.IsDue(immediate, 1, false) &&
            immediate.AppointmentId == earlySchedule.AppointmentId && immediate.Stock.SequenceEqual(earlySchedule.Stock) && immediate.DueDay == 2,
            "debug direct visits bypass Tuesday while preserving the appointment, stock and normal schedule");
        check(ReferenceEquals(GraylineSupplyRules.Expedite(immediate, 2), immediate), "repeated debug clicks cannot refresh stock or reset the immediate visit date");
        var early = immediate with { VisitState = "arrived" };
        var earlyBought = GraylineSupplyRules.Commit(GraylineSupplyRules.Prepare(early, Id(), quartz, 1, 25, 1, 1000, [31]));
        check(Roundtrip(earlyBought).Receipts[0].Day == 1 && Roundtrip(earlyBought).Stock[0].Remaining == 1,
            "purchases before Tuesday have durable immediate-visit receipts and finite stock");
        var normalNext = GraylineSupplyRules.Schedule(GraylineSupplyRules.Close(earlyBought, 1), 2, Id(), Values(all));
        check(normalNext.DebugVisitDay == null && GraylineSupplyRules.IsDue(normalNext, 2, true), "the next normal visit restores weekly Tuesday scheduling");
        check(Rejects(immediate with { DebugVisitDay = 0 }) && Throws(() => GraylineSupplyRules.Expedite(earlySchedule, 0)) &&
            Throws(() => GraylineSupplyRules.Expedite(GraylineSupplyRules.Prepare(early, Id(), quartz, 1, 25, 1, 1000, [32]), 1)),
            "invalid early dates and pending transactions cannot be expedited");
    }
}
