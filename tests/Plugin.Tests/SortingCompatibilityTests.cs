using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using ChouUn.StashMaster.Compatibility;
using HarmonyLib;
using Xunit;

namespace ChouUn.StashMaster.Plugin.Tests;

public sealed class SortingCompatibilityTests
{
    private static int _organizerRuns;
    private static int _originalRuns;
    private static int _postfixRuns;

    [Fact]
    public void 接管排序后不再启动另一套排序()
    {
        var organizer = new Harmony("test.organizer");
        var uiFixes = new Harmony("test.uifixes");
        MethodInfo target = Method(nameof(Sort));
        MethodInfo uiPrefix = AccessTools.Method(
            typeof(UIFixes.SortPatches.StackFirstPatch), "Prefix");
        _organizerRuns = _originalRuns = _postfixRuns = 0;
        UIFixes.SortPatches.StackFirstPatch.Runs = 0;
        try
        {
            organizer.Patch(target,
                prefix: new HarmonyMethod(Method(nameof(Organize))));
            uiFixes.Patch(target,
                prefix: new HarmonyMethod(uiPrefix),
                postfix: new HarmonyMethod(Method(nameof(OtherPostfix))));
            uiFixes.Patch(Method(nameof(OtherTarget)),
                prefix: new HarmonyMethod(uiPrefix));

            // 先证明本机 Harmony 下两个前置补丁都会运行，再验证实际兼容实现。
            Sort();
            Assert.Equal(1, _organizerRuns);
            Assert.Equal(1, UIFixes.SortPatches.StackFirstPatch.Runs);
            _organizerRuns = _originalRuns = _postfixRuns = 0;
            UIFixes.SortPatches.StackFirstPatch.Runs = 0;
            Assert.True(UIFixesCompatibility.DisableSortEntry(
                target, typeof(UIFixes.SortPatches).Assembly));

            Sort();

            Assert.Equal(1, _organizerRuns);
            Assert.Equal(0, UIFixes.SortPatches.StackFirstPatch.Runs);
            Assert.Equal(0, _originalRuns);
            Assert.Equal(1, _postfixRuns);
            OtherTarget();
            Assert.Equal(1, UIFixes.SortPatches.StackFirstPatch.Runs);
            Assert.Single(Harmony.GetPatchInfo(target).Prefixes);
            Assert.Single(Harmony.GetPatchInfo(target).Postfixes);
        }
        finally
        {
            organizer.Unpatch(target, HarmonyPatchType.All, organizer.Id);
            uiFixes.Unpatch(target, HarmonyPatchType.All, uiFixes.Id);
            uiFixes.Unpatch(
                Method(nameof(OtherTarget)), HarmonyPatchType.All, uiFixes.Id);
        }
    }

    [Fact]
    public void 未安装UIFixes时保留现有补丁()
    {
        var harmony = new Harmony("test.absent");
        MethodInfo target = Method(nameof(Sort));
        try
        {
            harmony.Patch(target, prefix: new HarmonyMethod(Method(nameof(Organize))));
            Assert.False(UIFixesCompatibility.DisableSortEntry(target, null));
            Assert.Equal(harmony.Id, Assert.Single(
                Harmony.GetPatchInfo(target).Prefixes).owner);
        }
        finally
        {
            harmony.Unpatch(target, HarmonyPatchType.All, harmony.Id);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void 签名或注册不符时报告失败且不动其他补丁(bool missingType)
    {
        var harmony = new Harmony("test.unknown");
        MethodInfo target = Method(nameof(Sort));
        try
        {
            harmony.Patch(target, prefix: new HarmonyMethod(Method(nameof(Organize))));
            Assembly assembly = missingType
                ? typeof(string).Assembly : GetType().Assembly;
            Assert.Throws<InvalidOperationException>(() =>
                UIFixesCompatibility.DisableSortEntry(target, assembly));
            Assert.Equal(
                harmony.Id, Harmony.GetPatchInfo(target).Prefixes.Single().owner);
        }
        finally
        {
            harmony.Unpatch(target, HarmonyPatchType.All, harmony.Id);
        }
    }

    private static MethodInfo Method(string name) =>
        AccessTools.Method(typeof(SortingCompatibilityTests), name);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Sort() => _originalRuns++;

    private static bool Organize()
    {
        _organizerRuns++;
        return false;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void OtherTarget() => _originalRuns++;

    private static void OtherPostfix() => _postfixRuns++;
}
