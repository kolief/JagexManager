using System.Runtime.InteropServices;

namespace JagexManager;

public static class Screens
{
    private const int PrimaryFlag = 1;

    public static IReadOnlyList<Area> WorkAreas()
    {
        var found = new List<(Area Work, bool Primary)>();
        _ = EnumDisplayMonitors(0, 0, (monitor, _, _, _) =>
        {
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (GetMonitorInfo(monitor, ref info))
                found.Add((info.Work.Area, (info.Flags & PrimaryFlag) != 0));

            return true;
        }, 0);

        return found
            .OrderByDescending(s => s.Primary)
            .ThenBy(s => s.Work.Left)
            .ThenBy(s => s.Work.Top)
            .Select(s => s.Work)
            .ToList();
    }

    private delegate bool MonitorVisitor(nint monitor, nint context, nint bounds, nint state);

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Bounds Monitor;
        public Bounds Work;
        public int Flags;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(nint context, nint clip, MonitorVisitor visit, nint state);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
}
