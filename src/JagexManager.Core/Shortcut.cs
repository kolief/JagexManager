using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace JagexManager;

public static class Shortcut
{
    public static string Create(string folder, string name, string program, string arguments, string description)
    {
        var path = Path.Combine(folder, $"{Safe(name)}.lnk");
        var link = (IShellLink)new ShellLink();
        try
        {
            link.SetPath(program);
            link.SetArguments(arguments);
            link.SetDescription(description);
            link.SetIconLocation(program, 0);
            link.SetWorkingDirectory(Path.GetDirectoryName(program) ?? "");
            ((IPersistFile)link).Save(path, true);
        }
        finally
        {
            Marshal.ReleaseComObject(link);
        }

        return path;
    }

    private static string Safe(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c)).Trim().TrimEnd('.', ' ');
        return safe.Length > 0 ? safe : "JagexManager";
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink;

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLink
    {
        void GetPath([MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int size, nint data, int flags);

        void GetIDList(out nint list);

        void SetIDList(nint list);

        void GetDescription([MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int size);

        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);

        void GetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int size);

        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);

        void GetArguments([MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int size);

        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);

        void GetHotkey(out short hotkey);

        void SetHotkey(short hotkey);

        void GetShowCmd(out int command);

        void SetShowCmd(int command);

        void GetIconLocation([MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int size, out int index);

        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);

        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, int reserved);

        void Resolve(nint window, int flags);

        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }
}
