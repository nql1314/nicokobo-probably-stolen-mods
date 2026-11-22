namespace AugMechanicalAscension.Domain;

internal sealed record GraylineStock(string ItemId, int InitialQuantity, int Remaining, int UnitPrice);
internal sealed record GraylinePurchaseReceipt(string OperationId, string ItemId, int Quantity,
    int UnitPrice, int Day, int CashBefore, int CashAfter, int[] OutputUids, bool NativeTrade = false);
internal sealed record GraylinePendingPurchase(string OperationId, string ItemId, int Quantity,
    int UnitPrice, int Day, int CashBefore, int[] OutputUids, bool NativeTrade = false);

// Supply is independent of the Lost and Found journal and its decision windows.
internal sealed record GraylineSupplyState
{
    public const string SaveKey = "nicokobo.aug.grayline.supply";
    public int SchemaVersion { get; init; } = 2;
    // Missing on historical saves: retain that appointment's original catalog.
    public int CatalogVersion { get; init; } = 1;
    public string GameRunId { get; init; } = "";
    public int SaveSlotId { get; init; } = -1;
    public long Revision { get; init; }
    public bool Unlocked { get; init; }
    public string? AppointmentId { get; init; }
    public int BookedDay { get; init; } = -1;
    public int DueDay { get; init; } = -1;
    public int LastVisitDay { get; init; } = -1;
    public int? DebugVisitDay { get; init; }
    public int PurchaseStartDay => DebugVisitDay ?? DueDay;
    public string VisitState { get; init; } = "none";
    public GraylineStock[] Stock { get; init; } = [];
    public GraylinePurchaseReceipt[] Receipts { get; init; } = [];
    public GraylinePendingPurchase? Pending { get; init; }
    public bool HasAppointment => AppointmentId != null && VisitState is "waiting" or "queued" or "arrived";
}
