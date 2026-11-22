// Native graph/Unity boundaries for checks that link the real ItemGraphCleanup.
// The ports record ownership and disposal; traversal remains production code.
public interface GraphNodeStorage
{
    List<GraphNodeStorage> children { get; }
}

public class PixelWindow : GraphNodeStorage
{
    public object? childElement;
    public virtual List<GraphNodeStorage> children { get; } = new();
    public readonly List<GameItem> parentItems = new();
    public readonly TreeNodeRender handler = new();
    public bool Hidden;
    public void Hide() => Hidden = true;
    public void Validate() { }
    public void AddParent(GameItem item)
    {
        if (!parentItems.Contains(item)) parentItems.Add(item);
        GraphHandler.windowGraphNodeRoots.Remove(this);
    }
    public void RemoveParent(GameItem item)
    {
        parentItems.Remove(item);
        if (parentItems.Count == 0 && !GraphHandler.windowGraphNodeRoots.Contains(this))
            GraphHandler.windowGraphNodeRoots.Add(this);
    }
}

public sealed class GridPixelElement(int width, int height)
{
    public readonly int gridWidth = width;
    public readonly int gridHeight = height;
}

public sealed class TreeNodeRender
{
    public readonly UnityEngine.GameObject gameObject = new();
}

public static class GraphHandler
{
    public static readonly List<PixelWindow> windowGraphNodeRoots = new();
}

namespace UnityEngine
{
    public sealed class GameObject { public bool Destroyed; }
    public static class Object
    {
        public static void Destroy(GameObject target) => target.Destroyed = true;
    }
}
