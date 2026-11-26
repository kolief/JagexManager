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
    private readonly DispatcherTimer record = new() { Interval = TimeSpan.FromMinutes(10) };
    private readonly bool fontOffered = GameFont.Available(GameFont.Repository);
    private readonly Hiscores hiscores = Hiscores.Connect();
    private readonly Tray tray;
    private readonly Hotkeys hotkeys;
    private Settings settings;
    private bool gameFont;
    private bool launcherBusy;
    private DateTimeOffset refreshed;
    private Release? update;
    private Action? suggested;
    private CancellationTokenSource? finding;

    private AccountView? dragged;
    private bool moved;

    public MainWindow()
    {
        roster = new Roster(vault, jagex);
        tray = new Tray(this, Restore, Close, TrayItems);
        hotkeys = new Hotkeys(FocusNumbered);
        InitializeComponent();

        Title = $"JagexManager {typeof(App).Assembly.GetName().Version?.ToString(3)}";

        List.ItemsSource = roster.Accounts;
        GroupList.ItemsSource = roster.Groups;
        GapChoice.ItemsSource = Settings.Gaps.Select(gap => new ComboBoxItem { Content = gap == 0 ? "No wait" : $"{gap} seconds", Tag = gap }).ToList();
        roster.PropertyChanged += (_, _) => ShowList();
        settings = Settings.Load(SettingsPath);
        App.Show(settings.Theme);
        roster.Sort = settings.Sort;
        roster.KeepPlacement = settings.KeepPlacement;
        ShowFont(settings.UseGameFont);
        ShowSettings();

        watch.Tick += (_, _) =>
        {
            var crashed = roster.Sweep();
            foreach (var character in crashed)
                SuggestRestart(character);
        };
        watch.Start();
        record.Tick += (_, _) => roster.Record();
        record.Start();
        freshen.Tick += async (_, _) =>
        {
            await RefreshAllAsync();
            await FindLevelsAsync();
            Rest();
        };

        Loaded += async (_, _) => await LoadAsync();
        StateChanged += (_, _) =>
        {
            if (WindowState != WindowState.Minimized)
                return;

            HideToTray();
            Rest();
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

        if (!IsVisible)
            roster.Arrange();

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
        Opened();

        freshen.Start();
        await RefreshAllAsync();
        Activated += async (_, _) =>
        {
            if (DateTimeOffset.UtcNow - refreshed >= Fresh)
                await RefreshAllAsync();
        };

        if (settings.CheckUpdates)
            await CheckUpdateAsync();

        await FindLevelsAsync();
        Rest();
    }

    private async Task FindLevelsAsync()
    {
        finding?.Cancel();
        if (!settings.ShowLevels)
            return;

        using var search = new CancellationTokenSource();
        finding = search;
        try
        {
            await roster.FindLevelsAsync(hiscores, search.Token);
        }
        finally
        {
            if (finding == search)
                finding = null;
        }
    }

    private void Rest()
    {
        if (WindowState != WindowState.Minimized)
            return;

        roster.Arrange();
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
        Warn();
    }

    private void Warn()
    {
        if (!settings.WarnMembership || roster.TakeWarning() is not { } warning)
            return;

        if (IsVisible || !tray.Tell("Membership ending", warning))
            Say(warning);
    }

    private void ShowList()
    {
        ShowJumps();
        SearchArea.Visibility = roster.Searchable ? Visibility.Visible : Visibility.Collapsed;
        GroupsCard.Visibility = roster.GroupsOffered ? Visibility.Visible : Visibility.Collapsed;

        var lost = !roster.Matched;
        NoMatch.Text = lost ? $"No characters match \u201c{roster.Search}\u201d." : "";
        if (lost == (NoMatch.Visibility == Visibility.Visible))
            return;

        NoMatch.Visibility = lost ? Visibility.Visible : Visibility.Collapsed;
        if (lost)
            UIElementAutomationPeer.CreatePeerForElement(NoMatch)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

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
        }
        else if (window.Error is { } error)
        {
            Say(error);
        }
    }

    private void Remove(object sender, RoutedEventArgs e)
    {
        var account = (AccountView)((FrameworkElement)sender).DataContext;

        if (!Confirm($"Remove {account.Name} from JagexManager?\n\nRunning clients stay open. To end the session everywhere, use \"End sessions\" in your Jagex account settings.",
            "Remove account"))
            return;

        Try(() =>
        {
            roster.Remove(account);
            Say($"Removed {account.Name}.");
        });
    }

    private async void Refresh(object sender, RoutedEventArgs e)
    {
        var account = (AccountView)((FrameworkElement)sender).DataContext;

        bool? reached = null;
        await TryAsync(async () => reached = await roster.RefreshAsync(account));

        if (!roster.Accounts.Contains(account))
            return;

        if (reached == false)
            Say("Jagex could not be reached. Try again in a moment.");
        else if (reached == true && !account.Expired)
            Say($"{account.Name}: {account.Tally.ToLowerInvariant()}.");
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

        if (!Confirm("Sign the Jagex Launcher out? It closes first if it is open. Your accounts stay in JagexManager.", "Jagex Launcher"))
            return;

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
        Confirm("The Jagex Launcher is signed in to an account that is not in JagexManager. Sign it out and continue?", "Jagex Launcher");

    private bool Confirm(string question, string title)
    {
        var answer = IsVisible
            ? MessageBox.Show(this, question, title, MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel)
            : MessageBox.Show(question, title, MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
        return answer == MessageBoxResult.OK;
    }

    private void Grab(object sender, MouseButtonEventArgs e)
    {
        var grip = (FrameworkElement)sender;
        grip.Focus();
        e.Handled = true;
        if (Searching())
            return;

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
        if (Searching())
            return;

        var account = (AccountView)((FrameworkElement)sender).DataContext;
        if (roster.Move(account, roster.Accounts.IndexOf(account) + step))
            Keep(account);
    }

    private bool Searching()
    {
        if (roster.Search is null)
            return false;

        Say("Clear the search to reorder accounts.");
        return true;
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
                : roster.Search is not null ? $"Every character on {account.Name} that the search shows is hidden, running or starting."
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
        var waiting = group.Playable;
        if (waiting.Count == 0)
        {
            Say($"Every character in {group.Name} is already running or starting.");
            return true;
        }

        return await PlayManyAsync(waiting, $"in {group.Name}");
    }

    private async Task<bool> PlayManyAsync(IReadOnlyList<CharacterView> waiting, string place)
    {
        IReadOnlyList<Outcome>? outcomes = null;
        await TryAsync(async () => outcomes = await roster.PlayAllAsync(waiting, c => settings.Find(c.Choice, c.Options), TimeSpan.FromSeconds(settings.Gap)));
        if (outcomes is null)
            return false;

        Say(outcomes.Count > 0 ? Outcome.Summary(outcomes, place) : $"Every character {place} is already running.");
        return outcomes.All(o => o.Error is null);
    }

    private void Search(object sender, TextChangedEventArgs e) => roster.Search = SearchBox.Text;

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

    private void ToggleShowHidden(object sender, RoutedEventArgs e)
    {
        var account = (AccountView)((FrameworkElement)sender).DataContext;
        account.ShowHidden = !account.ShowHidden;
    }

    private async Task<bool> PlayAsync(CharacterView character)
    {
        var program = settings.Find(character.Choice, character.Options);
        var started = false;

        character.Ready = false;
        try
        {
            await TryAsync(async () =>
            {
                var launched = await roster.PlayAsync(character, program);
                started = true;
                Say(launched ? $"Started {character.Name} in {program.Name}." : $"{character.Name} is already running.");
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

    private void ChangeLevels(object sender, RoutedEventArgs e) =>
        Change(settings with { ShowLevels = LevelsBox.IsChecked == true });

    private void ChangePlacement(object sender, RoutedEventArgs e) =>
        Change(settings with { KeepPlacement = PlacementBox.IsChecked == true });

    private void ChangeHotkeys(object sender, RoutedEventArgs e) =>
        Change(settings with { UseHotkeys = HotkeysBox.IsChecked == true });

    private void ChangeSort(object sender, SelectionChangedEventArgs e)
    {
        if (SortChoice.SelectedValue is Sort sort)
            Change(settings with { Sort = sort });
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
        if (changed.UseGameFont != settings.UseGameFont && changed.UseGameFont != gameFont)
        {
            ShowFont(changed.UseGameFont);
            if (gameFont != changed.UseGameFont)
                Say("The RuneScape font could not be copied from RuneLite, so the default font is used.");

            changed = changed with { UseGameFont = gameFont };
        }

        if (changed == settings)
            return;

        var previous = settings;
        settings = changed;
        ShowSettings();

        if (settings.Theme != previous.Theme)
            App.Show(settings.Theme);

        roster.Sort = settings.Sort;
        roster.KeepPlacement = settings.KeepPlacement;

        if (settings.UseHotkeys != previous.UseHotkeys)
            ApplyHotkeys();

        if (settings.ShowLevels != previous.ShowLevels)
        {
            _ = FindLevelsAsync();
            if (!settings.ShowLevels)
                roster.ForgetLevels();
        }

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

        SortChoice.SelectedValue = settings.Sort;
        GapChoice.SelectedValue = settings.Gap;

        UpdatesBox.IsChecked = settings.CheckUpdates;
        TrayBox.IsChecked = settings.UseTray;
        WarnBox.IsChecked = settings.WarnMembership;
        LevelsBox.IsChecked = settings.ShowLevels;
        PlacementBox.IsChecked = settings.KeepPlacement;
        HotkeysBox.IsChecked = settings.UseHotkeys;
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

    public void Say(string message, string? label = null, Action? act = null)
    {
        Status.Text = message;
        suggested = act;
        StatusAction.Content = label;
        StatusAction.Visibility = act is null ? Visibility.Collapsed : Visibility.Visible;
        StatusBar.Visibility = Visibility.Visible;

        UIElementAutomationPeer.CreatePeerForElement(Status)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    private void TakeSuggestion(object sender, RoutedEventArgs e)
    {
        var act = suggested;
        suggested = null;
        StatusAction.Visibility = Visibility.Collapsed;
        act?.Invoke();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        SaveOptions();
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        closed = true;
        finding?.Cancel();
        watch.Stop();
        freshen.Stop();
        record.Stop();
        tray.Dispose();
        hotkeys.Dispose();
        roster.Record();
        roster.Forget();
        base.OnClosed(e);
    }
}
