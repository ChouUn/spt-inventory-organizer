using System.Collections.Generic;
using ChouUn.StashMaster.Core.Inventory;

namespace ChouUn.StashMaster.Core.Organizing;

/// <summary>
/// 折叠计划：根容器之内所有可折叠、尚未折叠且未被 Locked 的物品，含嵌套容器里的。
/// 根容器自身不折叠。
/// </summary>
public static class FoldPlanner
{
    public static IReadOnlyList<ItemSnapshot> Plan(ItemSnapshot root)
    {
        var planned = new List<ItemSnapshot>();
        CollectChildren(root, planned);
        return planned;
    }

    private static void CollectChildren(
        ItemSnapshot container, List<ItemSnapshot> planned)
    {
        foreach (GridSnapshot grid in container.Grids)
        {
            foreach (ItemSnapshot item in grid.Items)
            {
                if (item.CanFold && !item.Folded && item.Lock != LockState.Locked)
                {
                    planned.Add(item);
                }
                CollectChildren(item, planned);
            }
        }
    }
}
