using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ChouUn.InventoryOrganizer;

/// <summary>让 Mono 的 P/Invoke 能找到插件目录里的原生 DLL。</summary>
internal static class NativeLibraries
{
    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetDllDirectoryW(string path);

    public static void AddSearchDirectory(string directory)
    {
        if (!SetDllDirectoryW(directory))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "SetDllDirectory");
        }
    }
}
