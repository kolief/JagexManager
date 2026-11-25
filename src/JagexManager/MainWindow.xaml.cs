using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace JagexManager;

[SuppressMessage("Design", "CA1001", Justification = "The tray icon is disposed when the window closes.")]
public partial class MainWindow : Window
{
    public const int SearchLimit = 50;

    private const int NicknameLimit = 32;
    private const int OptionsLimit = 1000;
    private const int GroupLimit = 32;
    private const int SearchFrom = 6;

    private static readonly string SettingsPath = Path.Combine(App.DataFolder, "settings.json");
    private static readonly string FontPath = Path.Combine(App.DataFolder, "fonts", "runescape.ttf");
    private static readonly TimeSpan Fresh = TimeSpan.FromMinutes(5);
    private static readonly HttpClient Updates = new() { Timeout = TimeSpan.FromSeconds(10) };

    private static readonly FontFamily Standard = new(new Uri("pack://application:,,,/Fonts/"), "./#Inter");

    private readonly Vault vault = new(Path.Combine(App.DataFolder, "vault.bin"));
    private readonly Jagex jagex = Jagex.Connect();
    private readonly Roster roster;
    private readonly DispatcherTimer watch = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer freshen = new() { Interval = TimeSpan.FromHours(1) };
    private readonly bool fontOffered = GameFont.Available(GameFont.Repository);
    private readonly Tray tray;
    private readonly HashSet<(string Id, DateTimeOffset Ends)> warned = [];
    private Settings settings;
    private bool gameFont;
    private bool launcherBusy;
    private DateTimeOffset refreshed;
    private Release? update;

    private AccountView? dragged;
    private bool moved;

    public MainWindow()
    {
        roster = new Roster(vault, jagex);
        tray = new Tray(this, Restore, Close, TrayItems);
        InitializeComponent();

        Title = $"JagexManager {typeof(App).Assembly.GetName().Version?.ToString(3)}";

        List.ItemsSource = roster.Accounts;
        GroupList.ItemsSource = roster.Groups;
        roster.Groups.CollectionChanged += (_, _) => ShowGroups();
        settings = Settings.Load(SettingsPath);
        App.Show(settings.Theme);
        roster.ByPlayed = settings.Order == Order.Played;
        ShowFont(settings.UseGameFont);
        ShowSettings();

        watch.Tick += (_, _) => roster.DropClosed();
        watch.Start();
        freshen.Tick += async (_, _) =>
        {
            await RefreshAllAsync();
            TrimHidden();
        };

        Loaded += async (_, _) => await LoadAsync();
        StateChanged += (_, _) =>
        {
            if (WindowState != WindowState.Minimized)
            {
                tray.Hide();
                return;
            }

            HideToTray();
            WorkingSet.Trim();
        };
    }

    public void Start(bool minimised)
    {
        if (minimised)
        {
            WindowState = WindowState.Minimized;
            ShowActivated = false;
        }

        Show();
        if (minimised)
            HideToTray();
    }

    public void Restore()
    {
        if (!IsLoaded)
            return;

        tray.Hide();
        Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;

        Activate();
    }

    private void HideToTray()
    {
        if (settings.UseTray && tray.Show())
            Hide();
    }

    private async Task LoadAsync()
    {
        try
        {
            Open();
        }
        catch (Exception e)
        {
            MessageBox.Show(this,
                $"The saved accounts could not be opened, so JagexManager will close to keep them safe.\n\n{e.Message}\n\nClose whatever is using the file and start JagexManager again.",
                Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            Close();
            return;
        }

        LoginWindow.ClearProfiles();

        freshen.Start();
        await RefreshAllAsync();
        Activated += async (_, _) =>
        {
            if (DateTimeOffset.UtcNow - refreshed >= Fresh)
                await RefreshAllAsync();
        };

        if (settings.CheckUpdates)
            await CheckUpdateAsync();

        TrimHidden();
    }

    private void TrimHidden()
    {
        if (WindowState == WindowState.Minimized)
            WorkingSet.Trim();
    }

    private async Task CheckUpdateAsync()
    {
        if (typeof(App).Assembly.GetName().Version is not { } current
            || await Release.FindNewerAsync(Updates, current) is not { } found)
            return;

        update = found;
        UpdateText.Text = $"JagexManager {found.Version.ToString(3)} is available.";
        UpdateBar.Visibility = Visibility.Visible;
    }

    private void OpenUpdate(object sender, RoutedEventArgs e)
    {
        if (update is { } release)
            Try(() => { using var browser = Process.Start(new ProcessStartInfo(release.Page.AbsoluteUri) { UseShellExecute = true }); });
    }

    private void DismissUpdate(object sender, RoutedEventArgs e) => UpdateBar.Visibility = Visibility.Collapsed;

    private async Task RefreshAllAsync()
    {
        refreshed = DateTimeOffset.UtcNow;
        await TryAsync(roster.RefreshAllAsync);
        ShowSearch();
        Warn();
    }

    private void Warn()
    {
        if (!settings.WarnMembership)
            return;

        var lapsing = roster.Accounts
            .Where(a => !a.Expired)
            .SelectMany(a => a.Characters)
            .Where(c => c.Lapsing && !c.Hidden && c.Character.Membership?.Ends is { } ends && warned.Add((c.Character.Id, ends)))
            .ToList();

        if (lapsing.Count == 0)
            return;

        var text = lapsing.Count == 1
            ? $"Membership for {lapsing[0].Name} ends in {lapsing[0].TimeLeft} and will not renew."
            : $"Membership ends within three days for {Names(lapsing)}. None of them renew.";

        if ((IsVisible && WindowState != WindowState.Minimized) || !tray.Tell("Membership ending", text))
            Say(text);
    }

    private static string Names(List<CharacterView> characters) =>
        $"{string.Join(", ", characters.Take(characters.Count - 1).Select(c => c.Name))} and {characters[^1].Name}";

    private void Open()
    {
        try
        {
            roster.Load();
        }
        catch (InvalidDataException)
        {
            Say($"The saved accounts were damaged and have been moved to {vault.SetAside()}. Add your accounts again.");
        }
    }

    private void SignIn(object sender, RoutedEventArgs e)
    {
        var window = new LoginWindow(jagex) { Owner = this };
        window.ShowDialog();

        if (window.Account is { } account)
        {
            Try(() =>
            {
                roster.Add(account);
                Say($"Signed in as {account.Name}.");
            });
            ShowSearch();
        }
        else if (window.Error is { } error)
        {
            Say(error);
        }
    }

    private void Remove(object sender, RoutedEventArgs e)
    {
        var account = (AccountView)((FrameworkElement)sender).DataContext;

        var answer = MessageBox.Show(this,
            $"Remove {account.Name} from JagexManager?\n\nRunning clients stay open. To end the session everywhere, use \"End sessions\" in your Jagex account settings.",
            "Remove account", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (answer != MessageBoxResult.OK)
            return;

        Try(() =>
        {
            roster.Remove(account);
            Say($"Removed {account.Name}.");
        });
        ShowSearch();
    }

    private async void Refresh(object sender, RoutedEventArgs e)
    {
        var account = (AccountView)((FrameworkElement)sender).DataContext;

        bool? reached = null;
        await TryAsync(async () => reached = await roster.RefreshAsync(account));

        if (!roster.Accounts.Contains(account))
            return;

        ShowSearch();
        if (reached == false)
            Say("Jagex could not be reached. Try again in a moment.");
        else if (reached == true && !account.Expired)
            Say($"{account.Name}: {account.Summary.ToLowerInvariant()}.");
    }

    private async void OpenLauncher(object sender, RoutedEventArgs e) =>
        await UseLauncherAsync(() => HandOverAsync((AccountView)((FrameworkElement)sender).DataContext));

    private async void SignOutLauncher(object sender, RoutedEventArgs e) =>
        await UseLauncherAsync(SignOutAsync);

    private async Task UseLauncherAsync(Func<Task> work)
    {
        if (launcherBusy)
            return;

        if (!Launcher.Installed)
        {
            Say("The Jagex Launcher was not found.");
            return;
        }

        launcherBusy = true;
        try
        {
            await TryAsync(work);
        }
        finally
        {
            launcherBusy = false;
        }
    }

    private async Task HandOverAsync(AccountView account)
    {
        if (Launcher.Find() is not { } launcher)
        {
            Say("Open the Jagex Launcher once, then try again.");
            return;
        }

        var check = roster.Check(account, launcher.Read());
        if (check == Handover.SignIn)
        {
            Say(Roster.Unready(account));
            return;
        }

        var replace = check == Handover.Stranger;
        if (replace && !ConfirmReplace())
            return;

        try
        {
            await Launcher.CloseAsync();
            roster.HandOver(account, launcher, replace);
            Say($"Opened the Jagex Launcher as {account.Name}.");
        }
        finally
        {
            Reopen();
        }
    }

    private async Task SignOutAsync()
    {
        if (Launcher.Find() is not { } launcher)
        {
            Say("The Jagex Launcher is not signed in.");
            return;
        }

        var open = Launcher.Running;
        try
        {
            await Launcher.CloseAsync();
            roster.SignOut(launcher);
            Say("Signed the Jagex Launcher out.");
        }
        finally
        {
            if (open)
                Reopen();
        }
    }

    private void Reopen()
    {
        if (!Launcher.Running)
            Try(Launcher.Start);
    }

    private bool ConfirmReplace() =>
        MessageBox.Show(this,
            "The Jagex Launcher is signed in to an account that is not in JagexManager. Sign it out and continue?",
            "Jagex Launcher", MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK;

    private void Grab(object sender, MouseButtonEventArgs e)
    {
        var grip = (FrameworkElement)sender;
        grip.Focus();
        e.Handled = true;

        if (!List.CaptureMouse())
            return;

        dragged = (AccountView)grip.DataContext;
        moved = false;
        List.Cursor = Cursors.SizeNS;
    }

    private void Drag(object sender, MouseEventArgs e)
    {
        if (dragged is not { } account)
            return;

        var above = roster.Accounts
            .Where(other => other != account)
            .Select(other => List.ItemContainerGenerator.ContainerFromItem(other))
            .OfType<FrameworkElement>()
            .Count(card => e.GetPosition(card).Y > card.ActualHeight / 2);

        moved |= roster.Move(account, above);
    }

    private void EndDrag(object sender, MouseButtonEventArgs e) => List.ReleaseMouseCapture();

    private void Settle(object sender, MouseEventArgs e)
    {
        if (dragged is not { } account)
            return;

        dragged = null;
        List.ClearValue(CursorProperty);

        if (moved)
            Keep(account);
    }

    private void Nudge(object sender, KeyEventArgs e)
    {
        var step = e.Key switch
        {
            Key.Up => -1,
            Key.Down => 1,
            _ => 0,
        };

        if (step == 0 || Keyboard.Modifiers != ModifierKeys.None)
            return;

        e.Handled = true;

        var account = (AccountView)((FrameworkElement)sender).DataContext;
        if (roster.Move(account, roster.Accounts.IndexOf(account) + step))
            Keep(account);
    }

    private void Keep(AccountView account)
    {
        Try(() =>
        {
            roster.Save();
            Say($"Moved {account.Name} to {roster.Accounts.IndexOf(account) + 1} of {roster.Accounts.Count}.");
        });

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (List.ItemContainerGenerator.ContainerFromItem(account) is ContentPresenter card
                && card.ContentTemplate.FindName("Grip", card) is FrameworkElement grip)
            {
                grip.Focus();
                grip.BringIntoView();
            }
        });
    }

    private async void Play(object sender, RoutedEventArgs e) =>
        await PlayAsync((CharacterView)((FrameworkElement)sender).DataContext);

    private async void PlayAll(object sender, RoutedEventArgs e)
    {
        var account = (AccountView)((FrameworkElement)sender).DataContext;
        if (account.Playable.Count == 0)
        {
            Say(account.Characters.Count == 0 ? $"{account.Name} has no characters yet."
                : account.Characters.All(c => c.Hidden) ? $"Every character on {account.Name} is hidden."
                : $"Every shown character on {account.Name} is already running or starting.");
            return;
        }

        await PlayManyAsync(account.Playable, $"on {account.Name}");
    }

    private async void PlayGroup(object sender, RoutedEventArgs e) =>
        await PlayGroupAsync((Group)((FrameworkElement)sender).DataContext);

    private async Task<bool> PlayGroupAsync(Group group)
    {
        var waiting = group.Characters.Where(c => !c.Running && c.Ready).ToList();
        if (waiting.Count == 0)
        {
            Say($"Every character in {group.Name} is already running or starting.");
            return true;
        }

        return await PlayManyAsync(waiting, $"in {group.Name}");
    }

    private async Task<bool> PlayManyAsync(IReadOnlyList<CharacterView> waiting, string place)
    {
        IReadOnlyList<Outcome> outcomes = [];
        await TryAsync(async () => outcomes = await roster.PlayAllAsync(waiting, c => settings.Find(c.Choice, c.Options), settings.Wait));
        if (outcomes.Count > 0)
            Say(Summary(place, outcomes));

        return outcomes.Count > 0 && outcomes.All(o => o.Error is null);
    }

    private static string Summary(string place, IReadOnlyList<Outcome> outcomes)
    {
        var failed = outcomes.Where(o => o.Error is not null).ToList();
        var started = outcomes.Count - failed.Count;
        if (failed.Count == 0)
        {
            return started == 1
                ? $"Started {outcomes[0].Character.Name} in {outcomes[0].Program.Name}."
                : $"Started {started} characters {place}.";
        }

        var first = failed[0];
        if (started == 0 && failed.All(f => f.Error == first.Error))
            return first.Error!;

        return $"Started {started} of {outcomes.Count} characters {place}. {first.Character.Name}: {first.Error}";
    }

    private void OfferGroup(object sender, MouseButtonEventArgs e)
    {
        var row = (FrameworkElement)sender;
        var group = (Group)row.DataContext;
        var menu = MenuBelow(row);
        menu.Placement = PlacementMode.MousePoint;
        menu.Items.Add(Display.Item("_Play", async () => await PlayGroupAsync(group)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Display.Item("_Rename…", () => RenameGroup(group)));
        menu.Items.Add(Display.Item("_Delete group", () => DeleteGroup(group)));
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void RenameGroup(Group group)
    {
        var answer = Preference.Clean(Prompt.Ask(this, title: "Rename group", question: $"New name for {group.Name}",
            note: "Groups with the same name are joined together.", answer: group.Name, limit: GroupLimit));

        if (answer is not null && answer != group.Name && Try(() => roster.RenameGroup(group.Name, answer)))
            Say($"Renamed {group.Name} to {answer}.");
    }

    private void DeleteGroup(Group group)
    {
        if (Try(() => roster.Ungroup(group.Name)))
            Say($"Deleted {group.Name}. Its characters are still on their accounts.");
    }

    private void ShowGroups() =>
        GroupsCard.Visibility = roster.Groups.Count > 0 && roster.Filter is null ? Visibility.Visible : Visibility.Collapsed;

    private void ShowSearch()
    {
        var wanted = roster.Filter is not null || roster.Accounts.Sum(a => a.Characters.Count) >= SearchFrom;
        SearchArea.Visibility = wanted ? Visibility.Visible : Visibility.Collapsed;
        NoMatch.Text = $"No characters match \u201c{roster.Filter}\u201d.";
        NoMatch.Visibility = roster.Found ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Search(object sender, TextChangedEventArgs e)
    {
        roster.Filter = SearchBox.Text;
        ShowGroups();
        ShowSearch();
    }

    private void ClearSearch(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || SearchBox.Text.Length == 0)
            return;

        SearchBox.Clear();
        e.Handled = true;
    }

    private void FindCharacter(object sender, ExecutedRoutedEventArgs e)
    {
        if (SearchArea.Visibility != Visibility.Visible)
            return;

        AccountsTab.IsChecked = true;
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private List<Control> TrayItems()
    {
        var items = new List<Control>();
        foreach (var group in roster.Groups)
            items.Add(Display.Item(Literal($"Play {group.Name}"), async () => await FromTrayAsync(PlayGroupAsync(group))));

        if (items.Count > 0)
            items.Add(new Separator());

        var accounts = roster.Accounts.Where(a => !a.Expired && a.Characters.Any(c => c.Listed)).ToList();
        foreach (var account in accounts)
        {
            var characters = account.Characters.Where(c => c.Listed).Select(TrayItem).ToList();
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

        return items;
    }

    private MenuItem TrayItem(CharacterView character)
    {
        var item = Display.Item(Literal(character.Name), async () =>
        {
            if (character.Running)
                ShowClient(character);
            else
                await FromTrayAsync(PlayAsync(character));
        });
        item.IsChecked = character.Running;
        item.IsEnabled = character.Ready;
        return item;
    }

    private async Task FromTrayAsync(Task<bool> play)
    {
        if (!await play)
            Restore();
    }

    private void ToggleShowHidden(object sender, RoutedEventArgs e)
    {
        var account = (AccountView)((FrameworkElement)sender).DataContext;
        account.ShowHidden = !account.ShowHidden;
    }

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

    private void OfferCharacter(object sender, MouseButtonEventArgs e)
    {
        var row = (FrameworkElement)sender;
        var menu = MenuBelow(row);
        menu.Placement = PlacementMode.MousePoint;
        AddCharacterItems(menu, (CharacterView)row.DataContext);
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void AddCharacterItems(ContextMenu menu, CharacterView character)
    {
        if (character.Running)
        {
            menu.Items.Add(Display.Item("Show _window", () => ShowClient(character)));
            menu.Items.Add(Display.Item("_Stop client", () => StopClient(character)));
            menu.Items.Add(new Separator());
        }

        menu.Items.Add(Display.Item("_Rename…", () => Rename(character)));
        menu.Items.Add(Display.Item("_Launch options…", () => AskOptions(character)));
        if (ProfileItems(character) is { } profiles)
            menu.Items.Add(profiles);

        menu.Items.Add(GroupItems(character));
        menu.Items.Add(Display.Item(character.Hidden ? "_Show in list" : "_Hide", () => ToggleHidden(character)));
        menu.Items.Add(new Separator());

        var copy = Display.Item("_Copy session details", () => CopySession(character));
        copy.IsEnabled = !character.Owner.Expired;
        menu.Items.Add(copy);
    }

    private void Rename(CharacterView character)
    {
        var answer = Prompt.Ask(this, title: "Rename", question: $"Name to show for {character.RealName}",
            note: "Only shown in JagexManager. Leave it empty to show the character's own name.",
            answer: character.Nickname ?? "", limit: NicknameLimit);

        if (answer is not null && Try(() => roster.Rename(character, answer)))
            Say(character.Nickname is { } nickname ? $"{character.RealName} is now shown as {nickname}." : $"{character.RealName} shows its own name again.");
    }

    private void AskOptions(CharacterView character)
    {
        var client = settings.Find(character.Choice).Name;
        var answer = Prompt.Ask(this, title: "Launch options", question: $"Launch options for {character.Name} in {client}",
            note: "Replaces the launch options from Settings for this character. Leave it empty to use Settings.",
            answer: character.Options ?? "", limit: OptionsLimit);

        if (answer is not null && Try(() => roster.SetOptions(character, answer, client)))
            Say(character.Options is { } options ? $"{character.Name} starts {client} with {options}." : $"{character.Name} uses the launch options from Settings.");
    }

    private MenuItem? ProfileItems(CharacterView character)
    {
        var client = settings.Find(character.Choice);
        if (client.Name != Program.RuneLite || Profiles.Read(Profiles.Saved) is not { Count: > 0 } profiles)
            return null;

        var options = character.Options ?? settings.Options.GetValueOrDefault(Program.RuneLite);
        var current = Profiles.Of(options);
        var menu = new MenuItem { Header = "RuneLite _profile" };
        foreach (var profile in profiles)
        {
            var item = Display.Item(Literal(profile), () => UseProfile(character, Profiles.With(options, profile), profile));
            item.IsChecked = string.Equals(profile, current, StringComparison.OrdinalIgnoreCase);
            menu.Items.Add(item);
        }

        if (character.Options is not null && current is not null)
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(Display.Item("_Use Settings", () => UseProfile(character, Profiles.With(character.Options, null), null)));
        }

        return menu;
    }

    private void UseProfile(CharacterView character, string? options, string? profile)
    {
        if (Try(() => roster.SetOptions(character, options, Program.RuneLite)))
            Say(profile is null ? $"{character.Name} uses the RuneLite profile from Settings." : $"{character.Name} starts RuneLite with the {profile} profile.");
    }

    private MenuItem GroupItems(CharacterView character)
    {
        var menu = new MenuItem { Header = "_Groups" };
        foreach (var group in roster.Groups)
        {
            var member = character.InGroup(group.Name);
            var item = Display.Item(Literal(group.Name), () => ToggleGroup(character, group.Name, !member));
            item.IsChecked = member;
            menu.Items.Add(item);
        }

        if (menu.Items.Count > 0)
            menu.Items.Add(new Separator());

        menu.Items.Add(Display.Item("_New group…", () => NewGroup(character)));
        return menu;
    }

    private void NewGroup(CharacterView character)
    {
        var answer = Preference.Clean(Prompt.Ask(this, title: "New group", question: $"Name of the group to add {character.Name} to",
            note: "Play a whole group from the top of the list or the notification area.", answer: "", limit: GroupLimit));

        if (answer is not null)
            ToggleGroup(character, answer, join: true);
    }

    private void ToggleGroup(CharacterView character, string group, bool join)
    {
        void Change()
        {
            if (join)
                roster.Join(character, group);
            else
                roster.Leave(character, group);
        }

        if (Try(Change))
            Say(join ? $"Added {character.Name} to {group}." : $"Took {character.Name} out of {group}.");
    }

    private void ShowClient(object sender, RoutedEventArgs e) =>
        ShowClient((CharacterView)((FrameworkElement)sender).DataContext);

    private void ShowClient(CharacterView character)
    {
        if (!character.Show())
            Say($"The game window for {character.Name} could not be found. It may still be starting.");
    }

    private void ToggleHidden(CharacterView character)
    {
        var hidden = !character.Hidden;
        if (Try(() => roster.SetHidden(character, hidden)))
            Say(hidden ? $"Hid {character.Name}. Use the link under {character.Owner.Name} to show it again." : $"{character.Name} is shown again.");
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

    private async Task<bool> PlayAsync(CharacterView character)
    {
        var program = settings.Find(character.Choice, character.Options);
        var started = false;

        character.Ready = false;
        try
        {
            await TryAsync(async () =>
            {
                await roster.PlayAsync(character, program);
                started = true;
                Say($"Started {character.Name} in {program.Name}.");
            });
        }
        finally
        {
            character.Ready = true;
        }

        return started;
    }

    private void Stop(object sender, RoutedEventArgs e) =>
        StopClient((CharacterView)((FrameworkElement)sender).DataContext);

    private void StopClient(CharacterView character)
    {
        Try(() =>
        {
            character.Stop();
            Say($"Stopping {character.Name}.");
        });
    }

    private void AddClient(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Add a game client",
            Filter = "Programs (*.exe)|*.exe",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog(this) != true)
            return;

        var (added, name) = settings.Add(dialog.FileName);
        Change(added.Switch(name, OptionsBox.Text));
    }

    private void RemoveClient(object sender, RoutedEventArgs e)
    {
        Change(settings.Remove(settings.Default));
        Try(() => roster.Prune(settings.Names));
    }

    private void ChangeClient(object sender, SelectionChangedEventArgs e)
    {
        if (ClientChoice.SelectedItem is string name && name != settings.Default)
            Change(settings.Switch(name, OptionsBox.Text));
    }

    private void LeaveOptions(object sender, RoutedEventArgs e) => SaveOptions();

    private void SubmitOptions(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            SaveOptions();
    }

    private void SaveOptions() => Change(settings.WithOptions(settings.Default, OptionsBox.Text));

    private void ChangeTheme(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeChoice.SelectedValue is Theme theme)
            Change(settings with { Theme = theme });
    }

    private void ChangeFont(object sender, SelectionChangedEventArgs e)
    {
        var wanted = FontChoice.SelectedItem == RuneScapeFont;
        if (wanted == gameFont)
            return;

        ShowFont(wanted);
        if (gameFont == wanted)
            Change(settings with { UseGameFont = wanted });
        else
            Say("The RuneScape font could not be copied from RuneLite, so the default font is used.");

        ShowSettings();
    }

    private void ChangeUpdates(object sender, RoutedEventArgs e) =>
        Change(settings with { CheckUpdates = UpdatesBox.IsChecked == true });

    private void ChangeTray(object sender, RoutedEventArgs e) =>
        Change(settings with { UseTray = TrayBox.IsChecked == true });

    private void ChangeWarn(object sender, RoutedEventArgs e) =>
        Change(settings with { WarnMembership = WarnBox.IsChecked == true });

    private void ChangeOrder(object sender, SelectionChangedEventArgs e)
    {
        if (OrderChoice.SelectedValue is Order order)
            Change(settings with { Order = order });
    }

    private void ChangeGap(object sender, SelectionChangedEventArgs e)
    {
        if (GapChoice.SelectedValue is int gap)
            Change(settings with { Gap = gap });
    }

    private void ChangeAutostart(object sender, RoutedEventArgs e)
    {
        var wanted = AutostartBox.IsChecked == true;
        Try(() => Autostart.Current.Set(wanted));
        ShowSettings();
    }

    private void OpenFolder(object sender, RoutedEventArgs e) => Try(() =>
    {
        Directory.CreateDirectory(App.DataFolder);
        using var explorer = Process.Start(new ProcessStartInfo(App.DataFolder + Path.DirectorySeparatorChar) { UseShellExecute = true });
    });

    private void Change(Settings changed)
    {
        if (changed == settings)
            return;

        var previous = settings;
        settings = changed;
        ShowSettings();

        if (settings.Theme != previous.Theme)
            App.Show(settings.Theme);

        if (settings.Order != previous.Order)
            roster.ByPlayed = settings.Order == Order.Played;

        Try(() => settings.Save(SettingsPath));
    }

    private void ShowSettings()
    {
        var program = settings.Find(settings.Default);
        ClientStatus.Text = program.Status;
        RemoveButton.Visibility = settings.Removable(program.Name) ? Visibility.Visible : Visibility.Collapsed;

        var names = settings.Names.ToList();
        if (!names.SequenceEqual(ClientChoice.Items.Cast<string>()))
            ClientChoice.ItemsSource = names;

        var options = settings.Options.GetValueOrDefault(program.Name, "");
        if (OptionsBox.Text.Trim() != options)
            OptionsBox.Text = options;
        ClientChoice.SelectedItem = program.Name;

        ThemeChoice.SelectedValue = settings.Theme;
        LauncherSection.Visibility = Launcher.Installed ? Visibility.Visible : Visibility.Collapsed;
        RuneScapeFont.IsEnabled = fontOffered || gameFont;
        FontChoice.SelectedItem = gameFont ? RuneScapeFont : DefaultFont;

        OrderChoice.SelectedValue = settings.Order;
        GapChoice.SelectedValue = settings.Gap;

        UpdatesBox.IsChecked = settings.CheckUpdates;
        TrayBox.IsChecked = settings.UseTray;
        WarnBox.IsChecked = settings.WarnMembership;
        AutostartBox.IsChecked = Autostart.Current.Enabled;
    }

    private void ShowFont(bool wanted)
    {
        gameFont = GameFont.Sync(wanted, GameFont.Repository, FontPath);
        if (gameFont)
        {
            FontFamily = new FontFamily(new Uri(Path.GetDirectoryName(FontPath) + Path.DirectorySeparatorChar), $"./#{GameFont.Family}");
            Resources["HeadingFont"] = FontFamily;
            Resources["TabSize"] = 24.0;
            FontSize = 16;
            Resources["Strong"] = FontWeights.Normal;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.Aliased);
        }
        else
        {
            FontFamily = Standard;
            Resources.Remove("HeadingFont");
            Resources.Remove("TabSize");
            ClearValue(FontSizeProperty);
            Resources.Remove("Strong");
            ClearValue(TextOptions.TextFormattingModeProperty);
            ClearValue(TextOptions.TextRenderingModeProperty);
        }
    }

    private bool Try(Action work)
    {
        try
        {
            work();
            return true;
        }
        catch (Exception e) when (Explain(e) is { } message)
        {
            Say(message);
            return false;
        }
    }

    private async Task TryAsync(Func<Task> work)
    {
        try
        {
            await work();
        }
        catch (Exception e) when (Explain(e) is { } message)
        {
            Say(message);
        }
    }

    private static string? Explain(Exception e) => e switch
    {
        PlayException or IOException or InvalidDataException or UnauthorizedAccessException or Win32Exception or SecurityException => e.Message,
        _ => null,
    };

    public void Say(string message)
    {
        Status.Text = message;
        StatusBar.Visibility = Visibility.Visible;

        UIElementAutomationPeer.CreatePeerForElement(Status)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        SaveOptions();
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        watch.Stop();
        freshen.Stop();
        tray.Dispose();
        roster.Forget();
        base.OnClosed(e);
    }
}
