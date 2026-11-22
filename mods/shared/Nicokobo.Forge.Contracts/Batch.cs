namespace Nicokobo.ContentInterop;

public sealed class BatchInput(GameItem item, int count, int before)
{
    public GameItem Item { get; } = item;
    public int Count { get; } = count;
    public int Before { get; } = before;
}
