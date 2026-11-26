using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace JagexManager;

public sealed class Client : IDisposable
{
    private readonly SafeFileHandle job;
    private readonly Dictionary<uint, SafeProcessHandle> processes = [];

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

            var client = new Client(job);
            client.Watch();
            return client;
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

    public bool Crashed
    {
        get
        {
            (long Exited, uint Code)? last = null;
            foreach (var process in processes.Values)
            {
                if (Ending(process) is { } ending && (last is null || ending.Exited > last.Value.Exited))
                    last = ending;
            }

            return last is { Code: not 0 };
        }
    }

    public bool HasWindow => Game() is not null;

    public bool Active =>
        GetWindowThreadProcessId(GetForegroundWindow(), out var process) != 0 && Processes().Contains(process);

    public void Stop()
    {
        if (!job.IsClosed && !TerminateJobObject(job, 0))
            throw new Win32Exception();
    }

    public void Watch()
    {
        foreach (var id in Processes())
        {
            if (processes.ContainsKey(id))
                continue;

            var process = OpenProcess(QueryLimited, false, id);
            if (!process.IsInvalid && IsProcessInJob(process, job, out var inJob) && inJob)
                processes[id] = process;
            else
                process.Dispose();
        }
    }

    public bool Focus()
    {
        var window = Game()?.Window ?? Windows().OrderByDescending(Size).FirstOrDefault();
        if (window == 0)
            return false;

        if (IsIconic(window))
            _ = ShowWindowAsync(window, RestoreCommand);

        return SetForegroundWindow(window);
    }

    public Placement? Locate() =>
        Game() is { } game
            ? new Placement(game.Placement.Normal.Area, game.Placement.ShowCommand == ShowMaximized
                || (game.Placement.ShowCommand == ShowMinimized && (game.Placement.Flags & RestoreToMaximized) != 0))
            : null;

    public bool Place(Placement placement)
    {
        if (Game() is not { } game)
            return false;

        var target = game.Placement;
        target.Flags = AsyncPlacement;
        target.ShowCommand = placement.Maximised ? ShowMaximized : ShowNoActivate;
        target.Normal = Bounds.Of(placement.Normal);
        return SetWindowPlacement(game.Window, ref target);
    }

    public bool Normalise() =>
        Game() is { } game
        && (IsIconic(game.Window) || IsZoomed(game.Window))
        && ShowWindowAsync(game.Window, RestoreCommand);

    public bool Move(Area area)
    {
        if (Game() is not { } game || !GetWindowRect(game.Window, out var outer))
            return false;

        var frame = DwmGetWindowAttribute(game.Window, ExtendedFrameBounds, out var visible, Marshal.SizeOf<Bounds>()) == 0 ? visible : outer;
        var left = frame.Left - outer.Left;
        var top = frame.Top - outer.Top;
        var right = outer.Right - frame.Right;
        var bottom = outer.Bottom - frame.Bottom;
        return SetWindowPos(game.Window, 0, area.Left - left, area.Top - top, area.Width + left + right, area.Height + top + bottom,
            NoZOrder | NoActivate | AsyncPosition);
    }

    public void Dispose()
    {
        job.Dispose();
        foreach (var process in processes.Values)
            process.Dispose();
    }

    private (nint Window, WindowPlacement Placement)? Game()
    {
        (nint Window, WindowPlacement Placement)? largest = null;
        long largestSize = 0;
        foreach (var window in Windows())
        {
            var placement = new WindowPlacement { Length = Marshal.SizeOf<WindowPlacement>() };
            if (!GetWindowPlacement(window, ref placement))
                continue;

            var normal = placement.Normal.Area;
            var dpi = GetDpiForWindow(window) is var found and > 0 ? (int)found : StandardDpi;
            if (normal.Width * StandardDpi < SmallestGameWidth * dpi || normal.Height * StandardDpi < SmallestGameHeight * dpi)
                continue;

            var size = (long)normal.Width * normal.Height;
            if (size > largestSize)
            {
                largest = (window, placement);
                largestSize = size;
            }
        }

        return largest;
    }

    private List<nint> Windows()
    {
        var ids = Processes();
        var found = new List<nint>();
        if (ids.Count == 0)
            return found;

        _ = EnumWindows((window, _) =>
        {
            if (GetWindowThreadProcessId(window, out var process) != 0
                && ids.Contains(process)
                && IsWindowVisible(window)
                && GetWindow(window, Owner) == 0
                && GetWindowTextLength(window) > 0)
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

    private static (long Exited, uint Code)? Ending(SafeProcessHandle process) =>
        GetExitCodeProcess(process, out var code) && code != StillActive && GetProcessTimes(process, out _, out var exited, out _, out _)
            ? (exited, code)
            : null;

    private static long Size(nint window) =>
        GetWindowRect(window, out var bounds) ? (long)(bounds.Right - bounds.Left) * (bounds.Bottom - bounds.Top) : 0;

    private const int BasicAccounting = 1;
    private const int ProcessList = 3;
    private const int ListHeader = 8;
    private const int ListCapacity = 64;
    private const int Owner = 4;
    private const int QueryLimited = 0x1000;
    private const uint StillActive = 259;
    private const int ShowMinimized = 2;
    private const int ShowMaximized = 3;
    private const int ShowNoActivate = 4;
    private const int RestoreCommand = 9;
    private const int RestoreToMaximized = 2;
    private const int AsyncPlacement = 4;
    private const int ExtendedFrameBounds = 9;
    private const uint NoZOrder = 0x0004;
    private const uint NoActivate = 0x0010;
    private const uint AsyncPosition = 0x4000;
    private const int StandardDpi = 96;
    private const int SmallestGameWidth = 640;
    private const int SmallestGameHeight = 400;

    private delegate bool WindowVisitor(nint window, nint state);

    [StructLayout(LayoutKind.Sequential)]
    private struct Spot
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPlacement
    {
        public int Length;
        public int Flags;
        public int ShowCommand;
        public Spot Minimised;
        public Spot Maximised;
        public Bounds Normal;
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

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(int access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint process);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsProcessInJob(SafeProcessHandle process, SafeFileHandle job, [MarshalAs(UnmanagedType.Bool)] out bool result);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetExitCodeProcess(SafeProcessHandle process, out uint code);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessTimes(SafeProcessHandle process, out long created, out long exited, out long kernel, out long user);

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
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsZoomed(nint window);

    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint window, int relation);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(nint window);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint process);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out Bounds bounds);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowPlacement(nint window, ref WindowPlacement placement);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPlacement(nint window, ref WindowPlacement placement);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint window, nint after, int left, int top, int width, int height, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindowAsync(nint window, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(nint window, int attribute, out Bounds value, int size);
}
