# SPT 4.1 客户端：可折叠、Pin/Lock 与原生排序的判定来源

> Parent: [../index.md](../index.md)
> Status: 调研完成（2026-09-10）
> Related: [../requirements.md](../requirements.md)

## 问题

1. 游戏如何判定一个物品可折叠，折叠如何执行，受什么限制。
2. 游戏内置的 move lock / sort lock 是什么，存在哪里，哪些操作会检查它。
3. 原生排序按钮绑到哪里，排序算法如何对待被锁物品。
4. 顺带：tag 的读写位置与长度限制所在。

## 方法

- 用 ilspycmd 9.1 反编译 SPT 4.1 客户端的 `Assembly-CSharp.dll`，按类型名 grep。
- 以 IOF 4.1 的补丁目标为线索定位游戏侧逻辑。
- 只读客户端。服务端对相应命令的校验未看。

## 结论摘要

| 主题 | 结论 |
| --- | --- |
| 可折叠 | `FoldableComponent`，只有 `Weapon` 和 `Stock` 两类物品会创建 |
| 折叠范围 | 带可折叠或可伸缩枪托的武器，以及可折叠枪托本体 |
| 锁状态 | `Item.PinLockState`，枚举 `Free` / `Pinned` / `Locked` |
| Locked | 阻止移动、作为目标、快速移动，以及交易、上交等消费 |
| Pinned | 只让排序跳过；移出所在容器时游戏自动解除 |
| 原生排序 | `ItemManipulator.Sort` 只处理 `Free` 物品，其余原地不动 |
| tag 长度 | 代码中无赋值，16 来自 UI 预制体的 `InputField.characterLimit` |

背包、披挂、耳机没有 `FoldableComponent`，游戏侧没有折叠概念。

## 1. 可折叠

### 1.1 判定

组件与模板都在 `EFT.InventoryLogic` 命名空间。

```csharp
public class FoldableComponent : ItemComponent
{
    public readonly Slot FoldedSlot;      // 可为 null
    public bool Folded;                   // [Diffable]，随物品持久化
    public int SizeReduceRight { get; }   // 来自模板
    // FoldedSlot == null || FoldedSlot.ContainedItem != null
    public bool CanBeFolded { get; }
    public void SetFolded(bool value);
}

public interface IFoldableComponentTemplate
{
    string FoldedSlot { get; }
    int SizeReduceRight { get; }
}
```

- 实现模板接口的只有 `WeaponTemplate` 和 `StockTemplate`，各带 `bool Foldable`。
- 创建组件的只有两处构造函数：
  - `Weapon`：`template.Foldable || template.Retractable`。
  - `Stock`：`template.Foldable`。
- 取组件用 `item.GetItemComponentsInChildren<FoldableComponent>().FirstOrDefault()`，
  武器本体或其枪托上的组件都算。`Weapon.GetFoldable()` 就是这一句。
- `CanBeFolded`：若模板指定了 `FoldedSlot`，该槽位必须装着东西（枪托在位）。

综合判定入口：

```csharp
public static bool ItemManipulator.CanFold(Item item, out FoldableComponent foldable)
```

条件：有组件，且 `CanBeFolded`，且武器上没有任何 `Mod.BlocksFolding` 为真的配件。
`BlocksFolding` 来自 `ModTemplate.BlocksFolding`。

### 1.2 执行

```csharp
public static OperationResult<FoldResult> ItemManipulator.Fold(
    FoldableComponent foldable, bool folded, bool simulate)
```

- 内部先走 `Resize_Helper(item, item.Parent, EResizeAction.Fold | Unfold)`。
  物品在网格中时经 `Grid.Resize(item, oldSize, newSize, simulate)` 校验新尺寸。
- 校验通过且非模拟时 `SetFolded(folded)` 并刷新图标。
- 网络命令：`FoldOperation.FoldCommand`，`Action = "Fold"`，字段 `item`、`value`。
- UI 入口：`ItemUiContext.FoldItem(Item)`，右键菜单 `EItemInfoButton.Fold` / `Unfold`。
  保险和跳蚤界面不执行。
- 折叠状态下向武器安装 `BlocksFolding` 配件会得到 `InstallToFoldedError`。

### 1.3 尺寸

- `CompoundItem.CalculateExtraSize`：`Folded` 为真时右侧扩展减去 `SizeReduceRight`。
  组件在武器本体上时减 `ForcedRight`，在枪托上时减 `Right`。
- 不改状态预先算尺寸：

```csharp
public IntVec2 CompoundItem.GetSizeAfterFolding(
    ItemAddress location, FoldableComponent foldedItem, bool folded)
public virtual IntVec2 Item.CalculateCellSize()
public IntVec2 Item.CalculateRotatedSize(ItemRotation rotation)
```

## 2. Pin / Lock

### 2.1 状态与设置

```csharp
public enum EItemPinLockState { Free, Pinned, Locked }

// Item 上的公共字段，[DefaultValue(Free)]
public EItemPinLockState Item.PinLockState;

public static OperationResult<SetPinLockResult> ItemManipulator.SetPinLockState(
    InventoryController controller, Item item, EItemPinLockState state, bool simulate)
public static bool ItemManipulator.IsPinLockStateAllowed(
    EItemPinLockState state, ItemAddress address, InventoryController controller)
```

`IsPinLockStateAllowed` 规则：

| 目标状态 | 位置 | 允许 |
| --- | --- | --- |
| `Free` | 任意 | 是 |
| 非 `Free` | 非 `GridItemAddress` | 否 |
| 非 `Free` | 仓库（`Inventory.Stash`）子孙 | 是 |
| `Pinned` | 分拣台直属格子 | 是 |
| `Locked` | 分拣台直属格子 | 否 |
| 非 `Free` | 分拣台内嵌容器 | 是 |
| 非 `Free` | 其他 | 否 |

- 同状态重设返回 `ItemLockSameStateError`。
- 位置不允许返回 `ItemLockWrongContainerError`。
- 网络命令：`SetPinLockOperation.SetPinLockCommand`，`Action = "PinLock"`，
  字段 `Item`、`State`。
- UI：右键 `EItemInfoButton.SetPin` / `SetUnPin` / `SetLock` / `SetUnLock`，
  经 `BaseItemContextInteractions.SetPinLock` 到 `ItemUiContext.SetPinLockState`。
  仓库面板 `SimpleStashPanel` 另有 pin / lock 批量模式按钮，
  快捷键 `ECommand.TogglePinMode` / `ToggleLockMode`。

### 2.2 Locked 的效果

核心前置检查两处，所有移动类操作都经过：

```csharp
public static bool ItemManipulator.CanModifyItem(
    Item item, ItemAddress from, ItemController controller, out Error error)
public static bool ItemManipulator.CanTransferTo(
    ItemAddress to, ItemController controller, out Error error)
```

- `CanModifyItem`：物品本身或任一 merged 父物品为 `Locked`，
  返回 `ItemManuallyLockedError`。
- `CanTransferTo`：目标地址的任一父物品为 `Locked`，同样拒绝。
- `Move` 额外检查：目标位置不允许 `Locked` 时，
  `HasLockedContent(item, out error)` 为真则拒绝。
  容器内含锁物品时给 `ContainerWithLockedItemError`，
  因此不能把含有 `Locked` 物品的容器搬出仓库。
- `QuickFindAppropriatePlace`：物品 `Locked` 直接返回 `ItemManuallyLockedError`。
- 其他消费方一律跳过 `Locked` 物品：交易、任务上交、藏身处需求、修理、治疗、
  改枪预设、货币统计。
- 服务端删除物品时客户端先强制置 `Free`（`ProfileUpdatesHandler.ManageDeletedItems`）。

### 2.3 Pinned 的效果

- 只有排序会跳过 `Pinned` 物品，见第 3 节。
- `Move` 到另一个父容器或非网格地址时，游戏自动把该物品置 `Free`，
  结果记录在 `MoveResult._setPinLockResults`，回滚时一并恢复。
- 目标位置不允许 `Pinned` 时，物品内部的 `Pinned` 子物品也被解除。

## 3. 原生排序

### 3.1 入口

`EFT.UI.DragAndDrop.GridSortPanel`：

- `Show(InventoryController controller, CompoundItem item)` 记录目标。
- `_button.onClick` 绑 `ButtonClickHandler()`，弹确认框
  `UI/Inventory/SortAcceptConfirmation`，确认后 `Sort()` 转 `SortAsync()`。
- `SortAsync()`：`ItemManipulator.Sort(_item, _controller, simulate: true)`，
  成功则 `_controller.TryRunNetworkTransaction(result)`，失败弹本地化警告。

### 3.2 算法

```csharp
public static OperationResult<ApplySortItemsPositionResult> ItemManipulator.Sort(
    CompoundItem sortedItem, InventoryController controller, bool simulate)
```

1. 任一网格内有物品不被 `grid.CanAccept` 接受，
   返回 `AutomaticSortNonFilteredItemError`。
2. `controller.IsAllowedToSort(sortedItem)` 为假返回 `CannotSortItemError`。
   基类恒为假，`BackEndInventoryController` 覆写为「物品归本控制器所有」。
3. 收集 `sortedItem.Grids.SelectMany(g => g.Items)` 中 `PinLockState == Free` 的物品，
   逐个 `Parent.Remove`。
4. `ItemSorter.Sort(list)` 决定放置顺序。
5. 逐个对每个网格调 `grid.AddAnywhere(item, EErrorHandlingType.Ignore)`。
   放不下时回退最近放入的物品再试，最多 5 轮。
6. 仍有剩余则全部回滚，返回 `AutomaticSortFailedError`。

对锁的处理：`Pinned` 和 `Locked` 物品从未被取出，位置不变，
并作为已占格子约束 `AddAnywhere`。排序只覆盖该 `CompoundItem` 自己的网格，不递归子容器。

### 3.3 ItemSorter 顺序

- 先按类型索引：`AmmoBox`、`Ammo`、`ThrowWeap`、`Magazine`、`Meds`、`Mod`、
  `Weapon` 及其子类为优先组，其余 `Item` 子类靠后。
- 同类型内按 `TemplateId`，再按 `Id`。
- 非优先组按组内平均格子面积升序排列。

## 4. tag

```csharp
public class TagComponent : ItemComponent
{
    public int Color;
    public string Name;
    public bool IsEmpty { get; }
    public OperationResult<SetTagResult> Set(string name, int color, bool simulate);
}
```

- 网络命令：`TagOperation`，`Action = "Tag"`。
- UI：`ItemUiContext.EditTag` 打开 `EditTagWindow.Show(TagComponent)`，
  保存时 `tagComponent.Set(...)` 后 `TryRunNetworkTransaction`。
- 长度限制：`EditTagWindow` 只读 `_tagInput.characterLimit` 用于显示计数，
  代码中没有赋值。16 这个值来自预制体序列化字段。IOF 在 `Show` 之后覆写它。

## 5. 与需求的对应

- F2 折叠：可折叠范围由 `ItemManipulator.CanFold` 判定。
  原版中披挂、背包、耳机没有 `FoldableComponent`；其他 mod 可以给它们加上，
  因此实现只能走 `CanFold`，不能按物品类型硬编码。
  真机观察（2026-09-10）：用户安装的 mod 已让胸挂、背包、耳机带上该组件，
  快照中它们的 `CanFold` 为真且处于折叠态。
- F4 兼容内置锁：`Locked` 物品与含 `Locked` 物品的容器既不能移动也不能作为目标；
  `Pinned` 物品排序时不动，移出容器会触发游戏自动解除 `Pinned`，
  因此需求已定为收纳时跳过 `Pinned` 物品。
- F7 替代排序按钮：目标是 `GridSortPanel` 的
  `ButtonClickHandler` / `Sort` / `SortAsync`。

## 未查清

- tag 上限 16 在预制体中的具体位置未验证，只确认代码中无赋值。
- `Grid.Resize` 对尺寸冲突的处理细节未细读。
- 服务端对 `Fold`、`PinLock`、`Tag` 命令的校验未看。
