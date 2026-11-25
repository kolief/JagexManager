using System.Diagnostics;
using System.Runtime.InteropServices;

namespace JagexManager;

public static class WorkingSet
{
    public static void Trim()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();

        using var self = Process.GetCurrentProcess();
        _ = SetProcessWorkingSetSize(self.Handle, -1, -1);
    }

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessWorkingSetSize(nint process, nint minimum, nint maximum);
}
