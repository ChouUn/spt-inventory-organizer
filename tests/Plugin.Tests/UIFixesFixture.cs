namespace UIFixes;

// 只模拟已核实的补丁身份与启动行为，不复制 UIFixes 的排序实现。
public static class SortPatches
{
    public static class StackFirstPatch
    {
        public static int Runs;

        public static bool Prefix()
        {
            Runs++;
            return false;
        }
    }
}
