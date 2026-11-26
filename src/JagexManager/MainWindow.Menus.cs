using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace JagexManager;

public partial class MainWindow
{
    private void OfferClients(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        var character = (CharacterView)button.DataContext;
        var installed = settings.Installed;
        var menu = MenuBelow(button);
        if (installed.Count == 0)
        {
            Say("No game client was found. Choose one under Settings.");
        }
        else
        {
            menu.Items.Add(ClientItem(character, null, $"Default ({settings.Default})"));
            menu.Items.Add(new Separator());
            foreach (var program in installed)
                menu.Items.Add(ClientItem(character, program.Name, program.Name));
            menu.Items.Add(new Separator());
        }

        AddCharacterItems(menu, character);

        menu.IsOpen = true;
    }

    private void OfferCharacter(object sender, MouseButtonEventArgs e) =>
        OfferAtMouse(sender, e, menu => AddCharacterItems(menu, (CharacterView)((FrameworkElement)sender).DataContext));

    private void AddCharacterItems(ContextMenu menu, CharacterView character)
    {
        if (character.Running)
        {
            menu.Items.Add(Display.Item("Show _window", () => FocusClient(character)));
            menu.Items.Add(Display.Item("S_top client", () => StopClient(character)));
            menu.Items.Add(new Separator());
        }

        menu.Items.Add(Display.Item("_Rename…", () => Rename(character)));
        menu.Items.Add(Display.Item("_Note…", () => AskNote(character)));
        menu.Items.Add(Display.Item("_Launch options…", () => AskOptions(character)));
        if (ProfileMenu(character) is { } profiles)
            menu.Items.Add(profiles);

        menu.Items.Add(JoinMenu(character));
        menu.Items.Add(Display.Item(character.Hidden ? "_Show in list" : "_Hide", () => ToggleHidden(character)));
        menu.Items.Add(new Separator());

        menu.Items.Add(Display.Item("Create desktop shortc_ut", () => CreateShortcut(character.Name, new Command(CommandKind.Play, character.Character.Id))));
        var copy = Display.Item("_Copy session details", () => CopySession(character));
        copy.IsEnabled = !character.Owner.Expired;
        menu.Items.Add(copy);
    }

    private MenuItem? ProfileMenu(CharacterView character)
    {
        if (settings.Find(character.Choice).Name != Program.RuneLite || Profiles.Read(Profiles.Saved) is not { Count: > 0 } profiles)
            return null;

        var shared = settings.Options.GetValueOrDefault(Program.RuneLite);
        var current = Profiles.Of(character.Options ?? shared);
        var menu = new MenuItem { Header = "RuneLite _profile" };
        foreach (var profile in profiles)
        {
            var ticked = string.Equals(profile, current, StringComparison.OrdinalIgnoreCase);
            menu.Items.Add(Display.Check(Literal(profile), ticked, () => UseProfile(character, profile)));
        }

        if (character.Options is not null)
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(Display.Item("_Use Settings", () => UseSettings(character)));
        }

        return menu;
    }

    private void UseProfile(CharacterView character, string profile)
    {
        var options = Profiles.Pick(character.Options, settings.Options.GetValueOrDefault(Program.RuneLite), profile);
        if (Try(() => roster.SetOptions(character, options, Program.RuneLite)))
            Say($"{character.Name} starts RuneLite with the {profile} profile.");
    }

    private void UseSettings(CharacterView character)
    {
        if (Try(() => roster.SetOptions(character, null, Program.RuneLite)))
            Say($"{character.Name} uses the launch options from Settings.");
    }

    private MenuItem JoinMenu(CharacterView character)
    {
        var menu = new MenuItem { Header = "_Groups" };
        foreach (var group in roster.Groups)
        {
            var member = character.InGroup(group.Name);
            menu.Items.Add(Display.Check(Literal(group.Name), member, () => ToggleGroup(character, group.Name, !member)));
        }

        if (menu.Items.Count > 0)
            menu.Items.Add(new Separator());

        menu.Items.Add(Display.Item("_New group…", () => NewGroup(character)));
        return menu;
    }

    private void NewGroup(CharacterView character)
    {
        var answer = Preference.Clean(Prompt.Ask(this, title: "New group", question: $"Name of the group to add {character.Name} to",
            note: "Play a whole group from the top of the list or the notification area.", answer: "", limit: Preference.GroupLimit));

        if (answer is not null)
            ToggleGroup(character, answer, join: true);
    }

    private void ToggleGroup(CharacterView character, string group, bool join)
    {
        if (Try(join ? () => roster.Join(character, group) : () => roster.Leave(character, group)))
            Say(join ? $"Added {character.Name} to {group}." : $"Took {character.Name} out of {group}.");
    }

    private void FocusClient(object sender, RoutedEventArgs e) =>
        FocusClient((CharacterView)((FrameworkElement)sender).DataContext);

    private bool FocusClient(CharacterView character)
    {
        if (character.Focus())
            return true;

        Say($"The game window for {character.Name} could not be brought to the front. It may still be starting.");
        return false;
    }

    private void ToggleHidden(CharacterView character)
    {
        var hidden = !character.Hidden;
        if (!Try(() => roster.SetHidden(character, hidden)))
            return;

        if (!hidden)
            Say($"{character.Name} is shown again.");
        else if (roster.Search is null)
            Say($"Hid {character.Name}. Use the link under {character.Owner.Name} to show it again.");
        else
            Say($"Hid {character.Name}. Clear the search, then use the link under {character.Owner.Name} to show it again.");
    }

    private void Rename(CharacterView character)
    {
        var answer = Prompt.Ask(this, title: "Rename", question: $"Name to show for {character.RealName}",
            note: "Only shown in JagexManager. Leave it empty to show the character's own name.",
            answer: character.Nickname ?? "", limit: Preference.NicknameLimit);

        if (answer is not null && Try(() => roster.Rename(character, answer)))
            Say(character.Nickname is { } nickname ? $"{character.RealName} is now shown as {nickname}." : $"{character.RealName} shows its own name again.");
    }

    private void AskNote(CharacterView character)
    {
        var answer = Prompt.Ask(this, title: "Note", question: $"Note for {character.Name}",
            note: "Shown under the character and found by search. Leave it empty to remove it.",
            answer: character.Note ?? "", limit: Preference.NoteLimit);

        if (answer is not null && Try(() => roster.SetNote(character, answer)))
            Say(character.Note is null ? $"Removed the note for {character.Name}." : $"Saved the note for {character.Name}.");
    }

    private void AskOptions(CharacterView character)
    {
        var client = settings.Find(character.Choice).Name;
        var answer = Prompt.Ask(this, title: "Launch options", question: $"Launch options for {character.Name} in {client}",
            note: "Replaces the launch options from Settings for this character. Leave it empty to use Settings.",
            answer: character.Options ?? "", limit: Preference.OptionsLimit);

        if (answer is not null && Try(() => roster.SetOptions(character, answer, client)))
            Say(character.Options is { } options ? $"{character.Name} starts {client} with {options}." : $"{character.Name} uses the launch options from Settings.");
    }

    private void CopySession(CharacterView character)
    {
        var lines = Client.Variables(character.Owner.Account.SessionId, character.Character).Select(v => $"{v.Key}={v.Value}");
        var data = new DataObject(DataFormats.UnicodeText, string.Join(Environment.NewLine, lines));
        foreach (var format in new[] { "ExcludeClipboardContentFromMonitorProcessing", "CanIncludeInClipboardHistory", "CanUploadToCloudClipboard" })
            data.SetData(format, new MemoryStream(BitConverter.GetBytes(0)));

        try
        {
            Clipboard.SetDataObject(data, copy: true);
        }
        catch (ExternalException)
        {
            Say("The clipboard is in use by another program. Try again.");
            return;
        }

        Say($"Copied the session details for {character.Name}. Anyone with them can play as this character, so only paste them into programs you trust.");
    }

    private void OfferGroup(object sender, MouseButtonEventArgs e) =>
        OfferAtMouse(sender, e, menu => AddGroupItems(menu, (Group)((FrameworkElement)sender).DataContext));

    private void OfferGroupOptions(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        var menu = MenuBelow(button);
        AddGroupItems(menu, (Group)button.DataContext);
        menu.IsOpen = true;
    }

    private void AddGroupItems(ContextMenu menu, Group group)
    {
        menu.Items.Add(Display.Item("_Play", async () => await PlayGroupAsync(group)));
        var stop = Display.Item("S_top", () => StopAll(group.Running, group));
        stop.IsEnabled = group.Running.Count > 0;
        menu.Items.Add(stop);
        menu.Items.Add(new Separator());
        menu.Items.Add(Display.Item("_Rename…", () => RenameGroup(group)));
        menu.Items.Add(Display.Item("Create desktop shortc_ut", () => CreateShortcut(group.Name, new Command(CommandKind.PlayGroup, group.Name))));
        menu.Items.Add(Display.Item("_Delete group", () => DeleteGroup(group)));
    }

    private void RenameGroup(Group group)
    {
        var answer = Preference.Clean(Prompt.Ask(this, title: "Rename group", question: $"New name for {group.Name}",
            note: "Groups with the same name are joined together.", answer: group.Name, limit: Preference.GroupLimit));

        string? renamed = null;
        if (answer is not null && answer != group.Name && Try(() => renamed = roster.RenameGroup(group.Name, answer)))
            Say($"Renamed {group.Name} to {renamed}.");
    }

    private void DeleteGroup(Group group)
    {
        IReadOnlyList<CharacterView> members = [];
        if (!Try(() => members = roster.DeleteGroup(group.Name)))
            return;

        Suggest($"Deleted {group.Name}. Its characters are still on their accounts.", "Undo", () =>
        {
            if (Try(() => roster.Join(members, group.Name)))
                Say($"{group.Name} is back.");
        });
    }

    private List<Control> TrayItems()
    {
        var items = new List<Control>();
        foreach (var group in roster.Groups)
            items.Add(Display.Item(Literal($"Play {group.Name}"), async () => await FromTrayAsync(PlayGroupAsync(group))));

        if (items.Count > 0)
            items.Add(new Separator());

        static bool Offered(CharacterView character) => !character.Hidden || character.Running;

        var accounts = roster.Accounts.Where(a => !a.Expired && a.Characters.Any(Offered)).ToList();
        foreach (var account in accounts)
        {
            var characters = account.Characters.Where(Offered).Select(TrayItem).ToList();
            if (accounts.Count == 1)
            {
                items.AddRange(characters);
                continue;
            }

            var submenu = new MenuItem { Header = Literal(account.Name) };
            foreach (var item in characters)
                submenu.Items.Add(item);
            items.Add(submenu);
        }

        if (accounts.Count > 0)
            items.Add(new Separator());

        if (roster.Running.Count > 0)
        {
            items.AddRange(WindowItems());
            items.Add(new Separator());
        }

        return items;
    }

    private MenuItem TrayItem(CharacterView character)
    {
        var item = Display.Check(Literal(character.Name), character.Running, async () =>
        {
            if (!character.Running)
                await FromTrayAsync(PlayAsync(character));
            else if (!FocusClient(character))
                Restore();
        });
        item.IsEnabled = character.Ready;
        return item;
    }

    private async Task FromTrayAsync(Task<bool> play)
    {
        if (!await play)
            Restore();
    }

    private void OfferAtMouse(object sender, MouseButtonEventArgs e, Action<ContextMenu> fill)
    {
        var menu = MenuBelow((UIElement)sender);
        menu.Placement = PlacementMode.MousePoint;
        fill(menu);
        menu.IsOpen = true;
        e.Handled = true;
    }

    private ContextMenu MenuBelow(UIElement target)
    {
        var menu = new ContextMenu { PlacementTarget = target, Placement = PlacementMode.Bottom };
        Display.Match(menu, this);
        return menu;
    }

    private MenuItem ClientItem(CharacterView character, string? name, string label)
    {
        var item = new MenuItem
        {
            Header = Literal(label),
            IsCheckable = true,
            IsChecked = character.Choice == name,
        };
        item.Click += async (_, _) =>
        {
            if (Try(() => roster.Choose(character, name)))
                await PlayAsync(character);
        };
        return item;
    }

    private static string Literal(string label) => label.Replace("_", "__");
}
