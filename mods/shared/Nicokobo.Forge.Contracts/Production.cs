namespace Nicokobo.ContentInterop;

public sealed class MachinePorts
{
    public GameInventory Input { get; }
    public GameInventory Output { get; }
    public GameInventory? Modules { get; }
    public GameSlotInventory? Battery { get; }
    public GameSlotInventory? Water { get; }
    public MachinePorts(GameInventory input, GameInventory output, GameInventory? modules = null,
        GameSlotInventory? battery = null, GameSlotInventory? water = null)
    { Input = input; Output = output; Modules = modules; Battery = battery; Water = water; }
}

public sealed class ConsumedItem
{
    public GameItem Item { get; }
    public int Amount { get; }
    internal ConsumedItem(GameItem item, int amount) { Item = item; Amount = amount; }
}
public sealed class WorkContext
{
    public GameItem Machine { get; }
    public MachinePorts Ports { get; }
    public RecipeView? Recipe { get; }
    public WorkContext(GameItem machine, MachinePorts ports, RecipeView? recipe)
    { Machine = machine; Ports = ports; Recipe = recipe; }
}
public sealed class WorkResult
{
    public WorkContext Context { get; }
    public bool Committed { get; }
    public IReadOnlyList<ConsumedItem> Consumed { get; }
    public int WaterPartsConsumed { get; }
    internal WorkResult(WorkContext context, bool committed, IReadOnlyList<ConsumedItem> consumed, int water)
    { Context = context; Committed = committed; Consumed = consumed; WaterPartsConsumed = water; }
}

