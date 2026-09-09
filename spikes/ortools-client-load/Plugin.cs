using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using BepInEx;
using Google.OrTools.Init;
using Google.OrTools.Sat;

namespace ChouUn.Spike.OrTools;

/// <summary>
/// 验证 OR-Tools 能否在游戏客户端（Unity Mono + BepInEx）内加载并求解。
/// 结果以 "RESULT: OK" 或 "RESULT: FAIL" 写入 BepInEx 日志。
/// </summary>
[BepInPlugin("com.chouun.spike.ortools", "OR-Tools Client Load Spike", "0.1.0")]
public sealed class Plugin : BaseUnityPlugin
{
    /// <summary>让 P/Invoke 找到插件目录里原生 DLL 的方式。</summary>
    private enum NativeLoadStrategy
    {
        /// <summary>不做处理，作为基线。</summary>
        None,

        /// <summary>把插件目录加入进程的 DLL 搜索路径。</summary>
        SetDllDirectory,

        /// <summary>按依赖顺序用完整路径逐个 LoadLibrary。</summary>
        Preload,
    }

    // 依赖在前，入口在后。
    // ortools.dll 导入其余全部，google-ortools-native 只导入 ortools。
    private static readonly string[] NativeLoadOrder =
    {
        "zlib1.dll",
        "bz2.dll",
        "libutf8_validity.dll",
        "abseil_dll.dll",
        "libprotobuf.dll",
        "re2.dll",
        "highs.dll",
        "libscip.dll",
        "ortools.dll",
        "google-ortools-native.dll",
    };

    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetDllDirectoryW(string path);

    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryW(string path);

    private void Awake()
    {
        NativeLoadStrategy strategy = Config.Bind(
            "Spike",
            "NativeLoadStrategy",
            NativeLoadStrategy.SetDllDirectory,
            "原生 DLL 的加载方式，改后重启游戏生效。").Value;
        string pluginDir = Path.GetDirectoryName(Info.Location)!;

        Logger.LogInfo($"framework: {RuntimeInformation.FrameworkDescription}");
        Logger.LogInfo($"clr version: {Environment.Version}");
        Logger.LogInfo($"mono runtime: {Type.GetType("Mono.Runtime") != null}");
        Logger.LogInfo($"plugin dir: {pluginDir}");
        Logger.LogInfo($"strategy: {strategy}");

        try
        {
            PrepareNative(strategy, pluginDir);
            Solve();
            Logger.LogInfo("RESULT: OK");
        }
        catch (Exception ex)
        {
            Logger.LogError($"RESULT: FAIL ({ex.GetType().Name})");
            Logger.LogError(ex.ToString());
        }
    }

    private void PrepareNative(NativeLoadStrategy strategy, string pluginDir)
    {
        switch (strategy)
        {
            case NativeLoadStrategy.SetDllDirectory:
                if (!SetDllDirectoryW(pluginDir))
                {
                    throw new Win32Exception(
                        Marshal.GetLastWin32Error(), "SetDllDirectory");
                }
                break;

            case NativeLoadStrategy.Preload:
                foreach (string name in NativeLoadOrder)
                {
                    string path = Path.Combine(pluginDir, name);
                    if (LoadLibraryW(path) == IntPtr.Zero)
                    {
                        throw new Win32Exception(
                            Marshal.GetLastWin32Error(), $"LoadLibrary {name}");
                    }
                    Logger.LogDebug($"loaded {name}");
                }
                break;
        }
    }

    /// <summary>
    /// 4×4 网格放 4 个可转置矩形。走 NoOverlap2D 与可选区间，
    /// 这是排布物品时实际会用的 API 路径。
    /// </summary>
    private void Solve()
    {
        Logger.LogInfo($"or-tools version: {OrToolsVersion.VersionString()}");

        const int gridW = 4;
        const int gridH = 4;
        (int W, int H)[] items = { (3, 1), (3, 1), (2, 2), (1, 2) };

        var model = new CpModel();
        var noOverlap = model.AddNoOverlap2D();
        var xs = new IntVar[items.Length];
        var ys = new IntVar[items.Length];
        var rotated = new BoolVar[items.Length];
        for (int i = 0; i < items.Length; i++)
        {
            (int w, int h) = items[i];
            int longest = Math.Max(w, h);
            xs[i] = model.NewIntVar(0, gridW, $"x{i}");
            ys[i] = model.NewIntVar(0, gridH, $"y{i}");
            rotated[i] = model.NewBoolVar($"r{i}");
            IntVar dx = model.NewIntVar(0, longest, $"dx{i}");
            IntVar dy = model.NewIntVar(0, longest, $"dy{i}");
            model.Add(dx == w).OnlyEnforceIf(rotated[i].Not());
            model.Add(dx == h).OnlyEnforceIf(rotated[i]);
            model.Add(dy == h).OnlyEnforceIf(rotated[i].Not());
            model.Add(dy == w).OnlyEnforceIf(rotated[i]);
            IntVar xEnd = model.NewIntVar(0, gridW, $"xe{i}");
            IntVar yEnd = model.NewIntVar(0, gridH, $"ye{i}");
            model.Add(xEnd == xs[i] + dx);
            model.Add(yEnd == ys[i] + dy);
            noOverlap.AddRectangle(
                model.NewIntervalVar(xs[i], dx, xEnd, $"ix{i}"),
                model.NewIntervalVar(ys[i], dy, yEnd, $"iy{i}"));
        }

        var solver = new CpSolver
        {
            StringParameters = "max_time_in_seconds:5,num_workers:4",
        };
        var stopwatch = Stopwatch.StartNew();
        CpSolverStatus status = solver.Solve(model);
        stopwatch.Stop();

        Logger.LogInfo(
            $"status: {status}, wall: {stopwatch.Elapsed.TotalMilliseconds:F0} ms");
        if (status != CpSolverStatus.Optimal && status != CpSolverStatus.Feasible)
        {
            throw new InvalidOperationException($"solver status {status}");
        }
        for (int i = 0; i < items.Length; i++)
        {
            Logger.LogInfo(
                $"item{i} {items[i].W}x{items[i].H} -> " +
                $"({solver.Value(xs[i])}, {solver.Value(ys[i])}) " +
                $"rotated={solver.BooleanValue(rotated[i])}");
        }
    }
}
