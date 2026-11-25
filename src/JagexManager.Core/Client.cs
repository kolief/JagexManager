using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace JagexManager;

public sealed class Client : IDisposable
{
    private readonly SafeFileHandle job;

    private Client(SafeFileHandle job) => this.job = job;

    public static IReadOnlyDictionary<string, string> Variables(string sessionId, Character character) =>
        new Dictionary<string, string>
        {
            ["JX_SESSION_ID"] = sessionId,
            ["JX_CHARACTER_ID"] = character.Id,
            ["JX_DISPLAY_NAME"] = character.Name,
        };

    public static Client Start(string path, string arguments, string sessionId, Character character)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("The game client was not found.", path);

        var start = new ProcessStartInfo(path, arguments)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(path)),
        };

        foreach (var name in start.Environment.Keys.Where(k => k.StartsWith("JX_", StringComparison.OrdinalIgnoreCase)).ToList())
            start.Environment.Remove(name);

        foreach (var (name, value) in Variables(sessionId, character))
            start.Environment[name] = value;

        var job = CreateJobObject(0, null);
        if (job.IsInvalid)
            throw new Win32Exception();

        try
        {
            using var process = Process.Start(start)
                ?? throw new IOException("The game client did not start.");

            if (!AssignProcessToJobObject(job, process.Handle))
            {
                var error = new Win32Exception();
                process.Kill(entireProcessTree: true);
                throw error;
            }

            return new Client(job);
        }
        catch
        {
            job.Dispose();
            throw;
        }
    }

    public bool Running =>
        !job.IsClosed
        && QueryInformationJobObject(job, BasicAccounting, out var info, Marshal.SizeOf<Accounting>(), 0)
        && info.ActiveProcesses > 0;

    public void Stop()
    {
        if (!job.IsClosed && !TerminateJobObject(job, 0))
            throw new Win32Exception();
    }

    public bool Show()
    {
        var window = Windows().OrderByDescending(Area).FirstOrDefault();
        if (window == 0)
            return false;

        if (IsIconic(window))
            _ = ShowWindow(window, Restore);

        return SetForegroundWindow(window);
    }

    public void Dispose() => job.Dispose();

    private List<nint> Windows()
    {
        var processes = Processes();
        var found = new List<nint>();
        if (processes.Count == 0)
            return found;

        _ = EnumWindows((window, _) =>
        {
            if (IsWindowVisible(window)
                && GetWindow(window, Owner) == 0
                && GetWindowTextLength(window) > 0
                && GetWindowThreadProcessId(window, out var process) != 0
                && processes.Contains(process))
                found.Add(window);

            return true;
        }, 0);

        return found;
    }

    private HashSet<uint> Processes()
    {
        var size = ListHeader + (ListCapacity * nint.Size);
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (job.IsClosed || !QueryInformationJobObject(job, ProcessList, buffer, size, 0))
                return [];

            var count = Math.Min(Marshal.ReadInt32(buffer, sizeof(int)), ListCapacity);
            return Enumerable.Range(0, count)
                .Select(i => (uint)Marshal.ReadIntPtr(buffer, ListHeader + (i * nint.Size)))
                .ToHashSet();
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static long Area(nint window) =>
        GetWindowRect(window, out var bounds) ? (long)(bounds.Right - bounds.Left) * (bounds.Bottom - bounds.Top) : 0;

    private const int BasicAccounting = 1;
    private const int ProcessList = 3;
    private const int ListHeader = 8;
    private const int ListCapacity = 64;
    private const int Owner = 4;
    private const int Restore = 9;

    private delegate bool WindowVisitor(nint window, nint state);

    [StructLayout(LayoutKind.Sequential)]
    private struct Bounds
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Accounting
    {
        public long TotalUserTime;
        public long TotalKernelTime;
        public long ThisPeriodTotalUserTime;
        public long ThisPeriodTotalKernelTime;
        public uint TotalPageFaultCount;
        public uint TotalProcesses;
        public uint ActiveProcesses;
        public uint TotalTerminatedProcesses;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateJobObject(nint attributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(SafeFileHandle job, nint process);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateJobObject(SafeFileHandle job, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryInformationJobObject(SafeFileHandle job, int infoClass, out Accounting info, int length, nint returned);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryInformationJobObject(SafeFileHandle job, int infoClass, nint info, int length, nint returned);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(WindowVisitor visit, nint state);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint window);

    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint window, int relation);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(nint window);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint process);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out Bounds bounds);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint window, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);
}
