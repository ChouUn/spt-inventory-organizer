using System.Collections.Generic;
using System.Linq;
using ChouUn.InventoryOrganizer.Core.Inventory;
using ChouUn.InventoryOrganizer.Core.Packing;

namespace ChouUn.InventoryOrganizer.Core.Organizing;

/// <summary>收纳阶段的物品与网格索引，成功事务增量更新，失败后由调用方校准。</summary>
internal sealed class CollectionState
{
    private readonly Dictionary<string, ItemSnapshot> _items = new();
    private readonly Dictionary<string, (string Container, int Grid)> _owners = new();
    private readonly Dictionary<(string Container, int Grid), List<string>> _members
        = new();

    public CollectionState(ItemSnapshot root) => Add(root);

    private void Add(ItemSnapshot item)
    {
        _items.Add(item.Id, item);
        foreach (GridSnapshot grid in item.Grids)
        {
            var key = (item.Id, grid.Index);
            _members.Add(key, grid.Items.Select(i => i.Id).ToList());
            foreach (ItemSnapshot child in grid.Items)
            {
                _owners.Add(child.Id, key);
                Add(child);
            }
        }
    }

    public ItemSnapshot Container(string id)
    {
        ItemSnapshot container = _items[id];
        return container with
        {
            Grids = container.Grids.Select(g => g with
            {
                Items = _members[(id, g.Index)].Select(i => _items[i]).ToArray(),
            }).ToArray(),
        };
    }

    public void Remove(IEnumerable<string> ids)
    {
        foreach (string id in ids)
        {
            if (_owners.TryGetValue(id, out var owner))
            {
                _members[owner].Remove(id);
                _owners.Remove(id);
                _items.Remove(id);
            }
        }
    }

    public void Move(string container, int grid, Placement placement)
    {
        var target = (container, grid);
        _members[_owners[placement.Id]].Remove(placement.Id);
        _members[target].Add(placement.Id);
        _owners[placement.Id] = target;
        Arrange(new[] { placement });
    }

    public void Arrange(IEnumerable<Placement> placements)
    {
        foreach (Placement p in placements)
        {
            _items[p.Id] = _items[p.Id] with
            {
                Position = new GridPosition(p.X, p.Y, p.Rotated),
            };
        }
    }
}
