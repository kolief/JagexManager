using Microsoft.Win32;

namespace JagexManager;

public sealed class Autostart(string key, string program)
{
    public const string Hidden = "--hidden";

    private const string Name = "JagexManager";

    public static Autostart Current { get; } = new(
        @"Software\Microsoft\Windows\CurrentVersion\Run", Environment.ProcessPath ?? "");

    private string Command => $"\"{program}\" {Hidden}";

    public bool Enabled
    {
        get
        {
            using var run = Registry.CurrentUser.OpenSubKey(key);
            return run?.GetValue(Name) is string command && command == Command;
        }
    }

    public void Set(bool enabled)
    {
        using var run = Registry.CurrentUser.CreateSubKey(key);
        if (enabled)
            run.SetValue(Name, Command);
        else
            run.DeleteValue(Name, throwOnMissingValue: false);
    }
}
