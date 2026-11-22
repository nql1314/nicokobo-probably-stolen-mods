namespace Nicokobo.ContentInterop;
public sealed record NativeItemTransferResult(int Moved, bool Indeterminate, string Status);
public sealed record NativeLiquidTransferResult(int PartsMoved, bool Indeterminate, string Status);
public sealed record InventoryPlacementEntry(GameItem Item, GridShape Shape);
public sealed record NativeTransactionStep(string Name, Action Apply, Func<bool> Restore);
public sealed record NativeTransactionResult(bool Committed, bool Restored, string Status);
