#if DEBUG
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using EFT.InventoryLogic;
using HarmonyLib;

namespace ChouUn.StashMaster.Diagnostics;

/// <summary>
/// 复制完整的原生空位搜索调用链；只重定向副本内部的调用，不移除第三方补丁。
/// UIFixes 的多选功能修改了下层搜索，单独复制 FindFreeSpace 仍会调用它。
/// </summary>
internal static class NativeGridSearch
{
    private static bool _ready;
    private static readonly Dictionary<MethodInfo, MethodInfo> Copies = new()
    {
        [AccessTools.Method(typeof(Grid), nameof(Grid.GetFreeLocation))] =
            AccessTools.Method(typeof(NativeGridSearch), nameof(GetLocation)),
        [AccessTools.Method(typeof(Grid), nameof(Grid.FindFreeSpaceInGrid))] =
            AccessTools.Method(typeof(NativeGridSearch), nameof(FindInGrid)),
        [AccessTools.Method(typeof(Grid), nameof(Grid.FindFreeSpace))] =
            AccessTools.Method(typeof(NativeGridSearch), nameof(Find)),
    };

    internal static void Prepare()
    {
        if (_ready) { return; }
        foreach (var pair in Copies)
        {
            Harmony.ReversePatch(pair.Key, new HarmonyMethod(pair.Value),
                AccessTools.Method(typeof(NativeGridSearch), nameof(RedirectCalls)),
                ilmanipulator: null);
        }
        _ready = true;
    }

    /// <summary>实例接收者变为静态副本的首个参数，参数栈与原调用保持一致。</summary>
    private static IEnumerable<CodeInstruction> RedirectCalls(
        IEnumerable<CodeInstruction> instructions)
    {
        foreach (CodeInstruction instruction in instructions)
        {
            if ((instruction.opcode == OpCodes.Call
                || instruction.opcode == OpCodes.Callvirt)
                && instruction.operand is MethodInfo original
                && Copies.TryGetValue(original, out MethodInfo copy))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = copy;
            }
            yield return instruction;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static LocationInGrid? Find(Grid grid, Item item) =>
        throw new NotSupportedException("reverse patch not initialized");

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static LocationInGrid? FindInGrid(Grid grid, int width, int height,
        ItemRotation rotation) =>
        throw new NotSupportedException("reverse patch not initialized");

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static LocationInGrid? GetLocation(Grid grid, int itemMainSize,
        int itemSecondSize, ItemRotation rotation, int firstDimensionSize,
        int secondDimensionSize, List<int> firstDimensionSpaces,
        List<int> secondDimensionSpaces, bool invertDimensions) =>
        throw new NotSupportedException("reverse patch not initialized");
}
#endif
