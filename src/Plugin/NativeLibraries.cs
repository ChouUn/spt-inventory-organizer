using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace ChouUn.InventoryOrganizer;

/// <summary>启动时加载原生入口和依赖，使延迟求解不依赖进程的全局搜索目录。</summary>
internal static class NativeLibraries
{
    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryExW(string path, IntPtr file, uint flags);

    public static void Load(string directory)
    {
        string entry = Path.Combine(directory, "google-ortools-native.dll");
        // DLL_LOAD_DIR 使依赖从入口所在目录解析，DEFAULT_DIRS 保留系统运行库路径。
        // 原生库由插件持有至进程退出，不能在后台求解仍可能使用时 FreeLibrary。
        const uint searchDllLoadDir = 0x00000100;
        const uint searchDefaultDirs = 0x00001000;
        if (LoadLibraryExW(entry, IntPtr.Zero,
            searchDllLoadDir | searchDefaultDirs) == IntPtr.Zero)
        {
            int error = Marshal.GetLastWin32Error();
            throw new Win32Exception(error,
                $"LoadLibraryEx failed ({error}): {entry}");
        }
    }
}
