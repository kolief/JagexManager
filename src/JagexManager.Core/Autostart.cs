using System.Security;
using Microsoft.Win32;

namespace JagexManager;

public sealed class Autostart(string key, string? program)
{
    public const string Minimised = "--minimised";

    private const string Name = "JagexManager";
    private const string EarlierFlag = "--hidden";

    public static Autostart Current { get; } = new(@"Software\Microsoft\Windows\CurrentVersion\Run", Environment.ProcessPath);

    public bool Enabled => Saved is not null;

    private bool Locatable => Path.IsPathFullyQualified(program ?? "");

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
        if (enabled && !Locatable)
            throw new IOException("JagexManager could not find its own program file, so it cannot start with Windows.");

        using var run = Registry.CurrentUser.CreateSubKey(key);
        if (enabled)
            run.SetValue(Name, Command);
        else
            run.DeleteValue(Name, throwOnMissingValue: false);
    }

    public bool Repair()
    {
        try
        {
            if (Saved is { } saved && saved != Command && Locatable && Broken(saved))
                Set(true);

            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException)
        {
            return false;
        }
    }

    private static bool Broken(string saved) =>
        saved.EndsWith(EarlierFlag, StringComparison.Ordinal) || !File.Exists(Target(saved));

    private static string Target(string saved) =>
        saved.StartsWith('"') && saved.IndexOf('"', 1) is > 0 and var end ? saved[1..end] : saved.Split(' ')[0];
}
