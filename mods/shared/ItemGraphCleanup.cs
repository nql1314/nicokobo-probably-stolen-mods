#nullable disable

using System;
using System.Collections.Generic;

namespace Nicokobo.ModUtilities
{
    internal static class ItemGraphCleanup
    {
        internal static void Destroy(List<GameItem> stock)
        {
            var nodes = new HashSet<GraphNodeStorage>();
            var windows = new HashSet<GraphNodeStorage>();
            for (int index = stock.Count - 1; index >= 0; index--)
            {
                GameItem item = stock[index];
                if (item != null)
                    DestroyNode(item, nodes, windows);
            }
        }

        internal static void Destroy(GameItem item)
        {
            if (item != null)
                DestroyNode(item, new HashSet<GraphNodeStorage>(), new HashSet<GraphNodeStorage>());
        }

        static void DestroyNode(GraphNodeStorage node, HashSet<GraphNodeStorage> nodes, HashSet<GraphNodeStorage> windows)
        {
            if (node == null || !nodes.Add(node))
                return;

            GameItem item = node as GameItem;
            if (item != null)
            {
                PixelWindow content = item.contentWindow;
                PixelWindow examine = item.examineWindow;
                if (content != null && !HasOtherOwner(content, item))
                    DestroyNode(content, nodes, windows);

                // Native Destroy removes only this item's renderers and hides
                // its content window. The window has an independent scene root.
                item.Destroy();
                if (content != null)
                {
                    item.SetContentWindow(null);
                    if (!HasOtherOwner(content, item))
                        DestroyWindow(content, windows);
                }
                if (examine != null)
                {
                    item.SetExamineWindow(null);
                    if (!HasOtherOwner(examine, item))
                        DestroyWindow(examine, windows);
                }
                return;
            }

            var children = node.children;
            if (children == null)
                return;

            // Destroying a child expels it from its inventory. Snapshot the
            // graph before traversal so adjacent children are not skipped.
            var snapshot = new List<GraphNodeStorage>(children.Count);
            for (int index = 0; index < children.Count; index++)
                snapshot.Add(children[index]);
            foreach (GraphNodeStorage child in snapshot)
                DestroyNode(child, nodes, windows);
        }

        static bool HasOtherOwner(PixelWindow window, GameItem item)
        {
            var parents = window.parentItems;
            for (int index = 0; index < parents.Count; index++)
            {
                GameItem parent = parents[index];
                if (parent != null && !ReferenceEquals(parent, item))
                    return true;
            }
            return false;
        }

        static void DestroyWindow(PixelWindow window, HashSet<GraphNodeStorage> windows)
        {
            if (!windows.Add(window))
                return;

            // Hide unregisters the window from native focus/layer managers.
            // Destroy then releases its inactive hierarchy as well.
            window.Hide();
            GraphHandler.windowGraphNodeRoots.Remove(window);
            TreeNodeRender handler = window.handler;
            if (handler != null)
                UnityEngine.Object.Destroy(handler.gameObject);
        }
    }
}
