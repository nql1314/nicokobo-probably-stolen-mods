using AugMechanicalAscension.Serialization;
using AugMechanicalAscension.Domain;

namespace AugMechanicalAscension.Persistence;

internal static class GraylineSupplyCodec
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    internal static string Write(GraylineSupplyState state) => JsonSerializer.Serialize(state, Options);
    internal static GraylineSupplyState Read(string json, string run, int slot)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 24 });
        NoDuplicateKeys(document.RootElement);
        foreach (string field in new[] { "schemaVersion", "gameRunId", "saveSlotId", "revision", "unlocked", "visitState", "stock", "receipts" })
            if (!document.RootElement.TryGetProperty(field, out _)) throw new InvalidDataException("Missing supply field: " + field);
        var s = JsonSerializer.Deserialize<GraylineSupplyState>(json, Options) ?? throw new InvalidDataException("Empty supply state");
        if (s.SchemaVersion == 2 && (!document.RootElement.TryGetProperty("lastVisitDay", out _) ||
            s.VisitState == "closed" && s.LastVisitDay < 0))
            throw new InvalidDataException("Missing last supply visit day");
        if (s.SchemaVersion is not (1 or 2) || s.CatalogVersion is not (1 or 2 or 3 or 4 or 5) || s.GameRunId != run || s.SaveSlotId != slot || string.IsNullOrWhiteSpace(run) || slot < 0 ||
            s.Revision < 0 || s.LastVisitDay < -1 || s.Stock == null || s.Receipts == null || s.VisitState is not ("none" or "waiting" or "queued" or "arrived" or "closed"))
            throw new InvalidDataException("Supply identity or version mismatch");
        if (s.AppointmentId == null)
        {
            if (s.BookedDay != -1 || s.DueDay != -1 || s.LastVisitDay != -1 || s.DebugVisitDay != null || s.VisitState != "none" || s.Stock.Length != 0 || s.Receipts.Length != 0 || s.Pending != null)
                throw new InvalidDataException("Supply state without an appointment");
        }
        else if (!s.Unlocked || !Guid.TryParseExact(s.AppointmentId, "D", out _) || s.BookedDay < 0 ||
            s.DueDay != (s.SchemaVersion == 1 ? GraylineSupplyRules.NextNaturalTuesday(s.BookedDay) : GraylineSupplyRules.NextTuesday(s.BookedDay)) ||
            s.VisitState == "none" || s.Stock.Length == 0)
            throw new InvalidDataException("Invalid supply appointment");
        if (s.LastVisitDay >= 0 && (s.VisitState == "closed" ? s.LastVisitDay < s.PurchaseStartDay : s.LastVisitDay >= s.BookedDay))
            throw new InvalidDataException("Invalid last supply visit day");
        if (s.DebugVisitDay is { } debugDay && debugDay < s.BookedDay)
            throw new InvalidDataException("Invalid immediate visit day");
        // A saved shipment owns its quantities and prices; generation rules apply only to new shipments.
        if (s.Stock.Any(x => x == null || GraylineSupplyRules.Product(x.ItemId, s.CatalogVersion) == null ||
                x.InitialQuantity <= 0 || x.Remaining < 0 || x.Remaining > x.InitialQuantity || x.UnitPrice <= 0) ||
            s.Stock.Select(x => x.ItemId).Distinct(StringComparer.Ordinal).Count() != s.Stock.Length)
            throw new InvalidDataException("Invalid supply stock");
        long maxUnits = s.Stock.Sum(x => (long)x.InitialQuantity);
        if (s.Receipts.Length > maxUnits || s.Receipts.Any(x => x == null || !ValidPurchase(x.OperationId, x.ItemId, x.Quantity,
                x.UnitPrice, x.Day, x.CashBefore, x.OutputUids, s, x.NativeTrade) || x.CashAfter != (long)x.CashBefore - (long)x.Quantity * x.UnitPrice) ||
            s.Receipts.Select(x => x.OperationId).Distinct(StringComparer.Ordinal).Count() != s.Receipts.Length ||
            s.Receipts.SelectMany(x => x.OutputUids).Distinct().Count() != s.Receipts.Sum(x => (long)x.Quantity) ||
            s.Stock.Any(x => x.Remaining != x.InitialQuantity - s.Receipts.Where(r => r.ItemId == x.ItemId).Sum(r => (long)r.Quantity)))
            throw new InvalidDataException("Invalid supply receipts or stock balance");
        if (s.Pending is { } p && (s.VisitState != "arrived" ||
            !ValidPurchase(p.OperationId, p.ItemId, p.Quantity, p.UnitPrice, p.Day, p.CashBefore, p.OutputUids, s, p.NativeTrade) ||
            s.Receipts.Any(x => x.OperationId == p.OperationId) || s.Receipts.SelectMany(x => x.OutputUids).Intersect(p.OutputUids).Any() ||
            s.Stock.Single(x => x.ItemId == p.ItemId).Remaining < p.Quantity ||
            !p.NativeTrade && s.Stock.Single(x => x.ItemId == p.ItemId).UnitPrice != p.UnitPrice))
            throw new InvalidDataException("Invalid pending supply purchase");
        return s;
    }
    private static bool ValidPurchase(string operation, string item, int quantity, int price, int day,
        int cash, int[] uids, GraylineSupplyState state, bool nativeTrade) =>
        Guid.TryParseExact(operation, "D", out _) && state.Stock.FirstOrDefault(x => x.ItemId == item) is { } stock &&
        price >= (nativeTrade ? 0 : 1) &&
        quantity > 0 && quantity <= stock.InitialQuantity && day >= state.PurchaseStartDay && cash >= 0 && (long)quantity * price <= cash &&
        uids != null && uids.Length == quantity && uids.All(x => x > 0) && uids.Distinct().Count() == quantity;
    private static void NoDuplicateKeys(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!seen.Add(property.Name)) throw new InvalidDataException("Duplicate supply field");
                NoDuplicateKeys(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array) foreach (var child in value.EnumerateArray()) NoDuplicateKeys(child);
    }
}
