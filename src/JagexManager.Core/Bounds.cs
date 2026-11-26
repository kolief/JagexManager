using System.Runtime.InteropServices;

namespace JagexManager;

[StructLayout(LayoutKind.Sequential)]
internal struct Bounds
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;

    public readonly Area Area => new(Left, Top, Right - Left, Bottom - Top);

    public static Bounds Of(Area area) => new()
    {
        Left = area.Left,
        Top = area.Top,
        Right = area.Left + area.Width,
        Bottom = area.Top + area.Height,
    };
}
