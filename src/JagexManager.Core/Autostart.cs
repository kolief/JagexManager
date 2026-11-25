using Microsoft.Win32;

namespace JagexManager;

public sealed class Autostart(string key, string? program)
{
    public const string Minimised = "--minimised";

    private const string Name = "JagexManager";

    public static Autostart Current { get; } = new(@"Software\Microsoft\Windows\CurrentVersion\Run", Environment.ProcessPath);

    public bool Enabled => Saved is not null;

    private string Command => $"\"{program}\" {Minimised}";

    private string? Saved
    {
        get
        {
            using var run = Registry.CurrentUser.OpenSubKey(key);
            return run?.GetValue(Name) as string;
        }
    }

    public void Set(bool enabled)
    {
        if (enabled && !Path.IsPathFullyQualified(program ?? ""))
            throw new IOException("JagexManager could not find its own program file, so it cannot start with Windows.");

        using var run = Registry.CurrentUser.CreateSubKey(key);
        if (enabled)
            run.SetValue(Name, Command);
        else
            run.DeleteValue(Name, throwOnMissingValue: false);
    }

    public bool Follow()
    {
        try
        {
            if (Saved is { } saved && saved != Command && Path.IsPathFullyQualified(program ?? ""))
                Set(true);

            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }
}
