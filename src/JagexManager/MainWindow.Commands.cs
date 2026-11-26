using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Shell;

namespace JagexManager;

public partial class MainWindow
{
    private Command? waiting;
    private bool opened;
    private bool closed;
    private string? shownJumps;

    public void Act(Command command)
    {
        if (closed)
            return;

        if (opened)
            _ = ActAsync(command);
        else
            waiting = command;
    }

    private void Opened()
    {
        opened = true;
        ApplyHotkeys();
        if (waiting is { } command)
        {
            waiting = null;
            _ = ActAsync(command);
        }
    }

    private async Task ActAsync(Command command)
    {
        switch (command)
        {
            case { Kind: CommandKind.Play, Target: { } target }:
                if (roster.Find(target) is not { } character)
                {
                    Restore();
                    Say("The character for this shortcut is no longer in JagexManager.");
                }
                else if (!character.Running)
                {
                    await FromTrayAsync(PlayAsync(character));
                }
                else if (!FocusClient(character))
                {
                    Restore();
                }

                break;

            case { Kind: CommandKind.PlayGroup, Target: { } name }:
                if (roster.FindGroup(name) is { } group)
                {
                    await FromTrayAsync(PlayGroupAsync(group));
                }
                else
                {
                    Restore();
                    Say($"There is no group called {name} any more, so its shortcut does nothing.");
                }

                break;

            default:
                Restore();
                break;
        }
    }

    private void ShowJumps()
    {
        if (Environment.ProcessPath is not { } program)
            return;

        var entries = roster.Groups
            .Select(g => Entry(program, $"Play {g.Name}", new Command(CommandKind.PlayGroup, g.Name), "Groups"))
            .Concat(roster.Recent.Select(c => Entry(program, $"Play {c.Name}", new Command(CommandKind.Play, c.Character.Id), "Recently played")))
            .ToList();

        var shown = string.Join('\n', entries.Select(e => $"{e.CustomCategory}\t{e.Title}\t{e.Arguments}"));
        if (shown == shownJumps)
            return;

        shownJumps = shown;
        var list = new JumpList(entries, showFrequent: false, showRecent: false);
        JumpList.SetJumpList(Application.Current, list);
        list.Apply();
    }

    private static JumpTask Entry(string program, string title, Command command, string category) => new()
    {
        Title = title,
        Arguments = command.Arguments,
        ApplicationPath = program,
        IconResourcePath = program,
        CustomCategory = category,
    };

    private void CreateShortcut(string name, Command command)
    {
        if (Environment.ProcessPath is not { } program)
        {
            Say("JagexManager could not find its own program file, so it cannot make a shortcut.");
            return;
        }

        try
        {
            var path = Shortcut.Create(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                $"Play {name}", program, command.Arguments, $"Play {name} with JagexManager");
            Say($"Added {Path.GetFileNameWithoutExtension(path)} to your desktop. It stops working if you move JagexManager.exe.");
        }
        catch (Exception e) when (e is COMException or IOException or UnauthorizedAccessException)
        {
            Say("The shortcut could not be added to your desktop.");
        }
    }
}
