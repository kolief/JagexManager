using System.Windows;
using System.Windows.Controls;

namespace JagexManager;

public partial class MainWindow
{
    private static readonly TimeSpan SettleWait = TimeSpan.FromMilliseconds(300);

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
        var shown = roster.Running.Where(c => c.HasWindow).ToList();
        if (shown.Count == 0)
        {
            Say("No game windows are open yet.");
            return;
        }

        var normalised = false;
        foreach (var character in shown)
            normalised |= character.Normalise();

        if (normalised)
            await Task.Delay(SettleWait);

        var tiles = Tiles.Lay(shown.Count, Screens.WorkAreas());
        var moved = Move(shown, tiles);
        await Task.Delay(SettleWait);
        moved = Math.Max(moved, Move(shown, tiles));

        Say(moved == shown.Count
            ? moved == 1 ? "Arranged the game window." : $"Arranged {moved} game windows."
            : $"Arranged {moved} of {shown.Count} game windows.");
    }

    private static int Move(IEnumerable<CharacterView> shown, IReadOnlyList<Area> tiles)
    {
        var moved = 0;
        foreach (var (character, tile) in shown.Zip(tiles))
        {
            if (character.Move(tile))
                moved++;
        }

        return moved;
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

    private void SuggestRestart(CharacterView character)
    {
        var message = $"{character.Name} closed unexpectedly.";

        void Again() => _ = FromTrayAsync(PlayAsync(character));

        Say(message, "Start again", Again);
        if (!IsVisible)
            _ = tray.Tell("Game closed", $"{message} Click here to start it again.", Again);
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

    private void FocusNumbered(int number)
    {
        var running = roster.Running;
        var target = number == Hotkeys.Next ? Next(running) : number <= running.Count ? running[number - 1] : null;
        target?.Focus();
    }

    private static CharacterView? Next(IReadOnlyList<CharacterView> running)
    {
        for (var position = 0; position < running.Count; position++)
        {
            if (running[position].Active)
                return running[(position + 1) % running.Count];
        }

        return running.Count > 0 ? running[0] : null;
    }
}
