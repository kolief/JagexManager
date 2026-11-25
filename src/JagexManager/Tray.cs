using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;

namespace JagexManager;

public sealed class Tray : IDisposable
{
    private const int Callback = 0x8001;
    private const int LeftUp = 0x0202;
    private const int RightUp = 0x0205;
    private const int Add = 0;
    private const int Modify = 1;
    private const int Delete = 2;
    private const int MessageFlag = 1;
    private const int IconFlag = 2;
    private const int TipFlag = 4;

    private static readonly int TaskbarCreated = RegisterWindowMessage("TaskbarCreated");

    private readonly HwndSource window = new(new HwndSourceParameters("JagexManager tray") { Width = 0, Height = 0, WindowStyle = 0 });
    private readonly Window owner;
    private readonly Action open;
    private readonly Action exit;
    private readonly nint icon;
    private bool wanted;
    private bool shown;

    public Tray(Window owner, Action open, Action exit)
    {
        this.owner = owner;
        this.open = open;
        this.exit = exit;
        window.AddHook(Listen);

        nint small = 0;
        if (Environment.ProcessPath is { } path && ExtractIconEx(path, 0, 0, ref small, 1) > 0)
            icon = small;
    }

    public bool Show()
    {
        wanted = true;
        if (!shown)
            shown = Notify(Add) || Notify(Modify);

        return shown;
    }

    public void Hide()
    {
        wanted = false;
        _ = Notify(Delete);
        shown = false;
    }

    public void Dispose()
    {
        Hide();
        window.Dispose();
        if (icon != 0)
            _ = DestroyIcon(icon);
    }

    private bool Notify(int action)
    {
        var data = new IconData
        {
            Size = Marshal.SizeOf<IconData>(),
            Window = window.Handle,
            Flags = MessageFlag | IconFlag | TipFlag,
            Message = Callback,
            Icon = icon,
            Tip = "JagexManager",
        };
        return Shell_NotifyIcon(action, ref data);
    }

    private nint Listen(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == TaskbarCreated && wanted)
        {
            shown = false;
            _ = Show();
        }
        else if (message == Callback && (int)lParam == LeftUp)
        {
            open();
        }
        else if (message == Callback && (int)lParam == RightUp)
        {
            Offer();
        }

        return 0;
    }

    private void Offer()
    {
        var menu = new ContextMenu { Placement = PlacementMode.MousePoint };
        Display.Match(menu, owner);
        menu.Items.Add(Display.Item("_Open JagexManager", open));
        menu.Items.Add(Display.Item("E_xit", exit));
        menu.IsOpen = true;

        if (PresentationSource.FromVisual(menu) is HwndSource popup)
            _ = SetForegroundWindow(popup.Handle);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct IconData
    {
        public int Size;
        public nint Window;
        public int Id;
        public int Flags;
        public int Message;
        public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Tip;
        public int State;
        public int StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Info;
        public int Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string InfoTitle;
        public int InfoFlags;
        public Guid Item;
        public nint BalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIcon(int action, ref IconData data);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int ExtractIconEx(string file, int index, nint large, ref nint small, int count);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint icon);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegisterWindowMessage(string name);
}
