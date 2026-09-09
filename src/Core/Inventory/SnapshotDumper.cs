using System.Collections.Generic;

namespace ChouUn.InventoryOrganizer.Core.Inventory;

/// <summary>把快照展开成逐行文本，用于日志核对。</summary>
public static class SnapshotDumper
{
    public static IEnumerable<string> Dump(ItemSnapshot root)
    {
        var lines = new List<string>();
        Append(lines, root, 0);
        return lines;
    }

    private static void Append(List<string> lines, ItemSnapshot item, int depth)
    {
        string indent = new string(' ', depth * 2);
        lines.Add(indent + Describe(item));
        foreach (GridSnapshot grid in item.Grids)
        {
            string size = $"{grid.Width}x{grid.Height}";
            string header = $"grid{grid.Index} {size}: {grid.Items.Count} items";
            lines.Add($"{indent}  {header}");
            foreach (ItemSnapshot child in grid.Items)
            {
                Append(lines, child, depth + 2);
            }
        }
    }

    private static string Describe(ItemSnapshot item)
    {
        string position = item.Position is GridPosition p
            ? $"({p.X},{p.Y}){(p.Rotated ? "R" : string.Empty)} "
            : string.Empty;
        string fold = string.Empty;
        if (item.CanFold)
        {
            fold = item.Folded ? " folded" : " foldable";
        }
        string lockState = item.Lock == LockState.Free
            ? string.Empty
            : $" [{item.Lock}]";
        string tag = item.Tag is null ? string.Empty : $" tag=\"{item.Tag}\"";
        string categories = string.Join(">", item.Categories);
        return $"{position}{item.Width}x{item.Height} {item.Name}" +
               $"{fold}{lockState}{tag} <{categories}>";
    }
}
