using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ChouUn.InventoryOrganizer.Core.Organizing;
using Comfort.Common;
using Diz.LanguageExtensions;
using EFT.InventoryLogic;

namespace ChouUn.InventoryOrganizer.Adapter;

/// <summary>用游戏 API 实现编排层的出口：先模拟，再交给网络事务执行并同步。</summary>
internal sealed class GameInventoryPort : IInventoryPort
{
    private readonly InventoryController _controller;
    private readonly Dictionary<string, Item> _items;

    public GameInventoryPort(CompoundItem root, InventoryController controller)
    {
        _controller = controller;
        _items = root.GetAllItems().ToDictionary(item => item.Id);
    }

    public async Task<PortResult> FoldAsync(string itemId)
    {
        if (!_items.TryGetValue(itemId, out Item item))
        {
            return PortResult.Fail("物品已不在范围内");
        }
        if (!ItemManipulator.CanFold(item, out FoldableComponent foldable))
        {
            return PortResult.Fail("当前不可折叠");
        }
        OperationResult<FoldResult> simulated =
            ItemManipulator.Fold(foldable, folded: true, simulate: true);
        if (!simulated.Succeeded)
        {
            return PortResult.Fail(Describe(simulated.Error));
        }
        return await CommitAsync(simulated);
    }

    private async Task<PortResult> CommitAsync(OperationResult simulated)
    {
        try
        {
            IResult result = await _controller.TryRunNetworkTransaction(simulated);
            return string.IsNullOrEmpty(result.Error)
                ? PortResult.Ok
                : PortResult.Fail(result.Error);
        }
        catch (Exception ex)
        {
            return PortResult.Fail(ex.Message);
        }
    }

    private static string Describe(Error error)
    {
        return error is InventoryError inventoryError
            ? inventoryError.GetLocalizedDescription()
            : error.ToString();
    }
}
