using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shell;

namespace JagexManager;

public partial class MainWindow
{
    private static readonly TimeSpan RestoreWait = TimeSpan.FromMilliseconds(300);

    private Command? waiting;
    private bool opened;
    private string? jumps;

    public void Act(Command command)
    {
        if (opened)
            _ = ActAsync(command);
        else
            waiting = command;
    }

    private void Opened()
    {
        opened = true;
        ShowJumps();
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
                    Say($"No character called {target} was found.");
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
                    Say($"No group called {name} was found.");
                }

                break;

            default:
                Restore();
                break;
        }
    }

    private void OfferWindows(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        var menu = MenuBelow(button);
        foreach (var item in WindowItems())
            menu.Items.Add(item);

        menu.IsOpen = true;
    }

    private List<Control> WindowItems()
    {
        var running = roster.Running.Count > 0;
        var arrange = Display.Item("_Arrange game windows", async () => await TileAsync());
        var stop = Display.Item("S_top all clients", () => StopAll(roster.Running, null));
        arrange.IsEnabled = running;
        stop.IsEnabled = running;
        return [arrange, stop];
    }

    private async Task TileAsync()
    {
        var shown = roster.Running.Where(c => c.Windowed).ToList();
        if (shown.Count == 0)
        {
            Say("No game windows are open yet.");
            return;
        }

        var restored = false;
        foreach (var character in shown)
            restored |= character.Restore();

        if (restored)
            await Task.Delay(RestoreWait);

        var tiles = Tiles.Lay(shown.Count, Screens.Work());
        var moved = 0;
        foreach (var (character, tile) in shown.Zip(tiles))
        {
            if (character.Move(tile))
                moved++;
        }

        Say(moved == shown.Count
            ? moved == 1 ? "Arranged the game window." : $"Arranged {moved} game windows."
            : $"Arranged {moved} of {shown.Count} game windows.");
    }

    private void StopAll(IReadOnlyList<CharacterView> running, Group? group)
    {
        if (running.Count == 0)
        {
            Say(group is null ? "No clients are running." : $"No clients in {group.Name} are running.");
            return;
        }

        var question = group is null
            ? $"Stop all {running.Count} running clients?"
            : $"Stop the {running.Count} running clients in {group.Name}?";
        if (running.Count > 1 && !Confirm(question, "Stop clients"))
            return;

        foreach (var character in running)
        {
            if (!Try(character.Stop))
                return;
        }

        Say(running.Count == 1 ? $"Stopping {running[0].Name}." : $"Stopping {running.Count} clients.");
    }

    private void OfferRestart(CharacterView character)
    {
        var message = $"{character.Name} closed unexpectedly.";

        void Again() => _ = FromTrayAsync(PlayAsync(character));

        if (!IsVisible && tray.Tell("Game closed", $"{message} Click here to start it again.", Again))
            return;

        Suggest(message, "Start again", Again);
    }

    private void ApplyHotkeys()
    {
        if (!settings.UseHotkeys)
        {
            hotkeys.Unregister();
            return;
        }

        var taken = hotkeys.Register();
        if (taken.Count > 0)
        {
            var keys = string.Join(", ", taken.Select(number => $"Ctrl+Alt+{number}"));
            Say(taken.Count == 1
                ? $"{keys} is already used by another program, so it does nothing here."
                : $"{keys} are already used by another program, so they do nothing here.");
        }
    }

    private void ShowNumbered(int number)
    {
        var running = roster.Running;
        var target = number == Hotkeys.Next ? Next(running) : number <= running.Count ? running[number - 1] : null;
        target?.Focus();
    }

    private static CharacterView? Next(IReadOnlyList<CharacterView> running)
    {
        if (running.Count == 0)
            return null;

        var current = running.ToList().FindIndex(c => c.Active);
        return running[(current + 1) % running.Count];
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
        if (shown == jumps)
            return;

        jumps = shown;
        var list = new JumpList(entries, showFrequent: false, showRecent: false);
        JumpList.SetJumpList(Application.Current, list);
        list.Apply();
    }

    private static JumpTask Entry(string program, string title, Command command, string category) => new()
    {
        Title = title,
        Description = title,
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
