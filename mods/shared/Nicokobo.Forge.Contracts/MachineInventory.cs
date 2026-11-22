namespace Nicokobo.ContentInterop;
    public sealed record NativeMachineInventory(GameSlotInventory? Battery, GameInventory Modules,
        GameInventory? ItemInput, GameInventory Output, IReadOnlyDictionary<string, GameSlotInventory> LiquidInputs,
        GameSlotInventory? Auxiliary = null);
