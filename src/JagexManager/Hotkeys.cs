using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace JagexManager;

public sealed class Hotkeys : IDisposable
{
    public const int Next = 0;

    private const int Pressed = 0x0312;
    private const int Alt = 0x0001;
    private const int Control = 0x0002;
    private const int NoRepeat = 0x4000;
    private const int FirstKey = '0';
    private const int Count = 10;

    private readonly HwndSource window = new(new HwndSourceParameters("JagexManager hotkeys") { Width = 0, Height = 0, WindowStyle = 0 });
    private readonly Action<int> press;
    private bool registered;

    public Hotkeys(Action<int> press)
    {
        this.press = press;
        window.AddHook(Listen);
    }

    public IReadOnlyList<int> Register()
    {
        var taken = new List<int>();
        if (registered)
            return taken;

        registered = true;
        for (var number = 0; number < Count; number++)
        {
            if (!RegisterHotKey(window.Handle, Id(number), Control | Alt | NoRepeat, FirstKey + number))
                taken.Add(number);
        }

        return taken;
    }

    public void Unregister()
    {
        if (!registered)
            return;

        registered = false;
        for (var number = 0; number < Count; number++)
            _ = UnregisterHotKey(window.Handle, Id(number));
    }

    public void Dispose()
    {
        Unregister();
        window.Dispose();
    }

    private static int Id(int number) => number + 1;

    private nint Listen(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == Pressed && (int)wParam - 1 is >= 0 and < Count and var number)
        {
            press(number);
            handled = true;
        }

        return 0;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint window, int id, int modifiers, int key);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint window, int id);
}
