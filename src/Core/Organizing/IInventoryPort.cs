using System.Threading.Tasks;

namespace ChouUn.InventoryOrganizer.Core.Organizing;

/// <summary>
/// 编排层对游戏的出口。每个操作由实现方先模拟再提交，失败时返回原因。
/// </summary>
public interface IInventoryPort
{
    Task<PortResult> FoldAsync(string itemId);
}

public sealed record PortResult(bool Succeeded, string? Error)
{
    public static PortResult Ok { get; } = new PortResult(true, null);

    public static PortResult Fail(string error) => new PortResult(false, error);
}
