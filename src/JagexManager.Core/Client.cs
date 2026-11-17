using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace JagexManager;

public sealed class Client : IDisposable
{
    private readonly SafeFileHandle job;

    private Client(SafeFileHandle job) => this.job = job;

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

        start.Environment["JX_SESSION_ID"] = sessionId;
        start.Environment["JX_CHARACTER_ID"] = character.Id;
        start.Environment["JX_DISPLAY_NAME"] = character.Name;

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

    public void Dispose() => job.Dispose();

    private const int BasicAccounting = 1;

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
}
