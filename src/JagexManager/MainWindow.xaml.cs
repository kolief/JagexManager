using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace JagexManager;

public partial class MainWindow : Window
{
    private static readonly string SettingsPath = Path.Combine(App.DataFolder, "settings.json");
    private static readonly string FontPath = Path.Combine(App.DataFolder, "fonts", "runescape.ttf");

    private static readonly FontFamily Standard = new(new Uri("pack://application:,,,/Fonts/"), "./#Inter");

    private readonly Vault vault = new(Path.Combine(App.DataFolder, "vault.bin"));
    private readonly Jagex jagex = Jagex.Connect();
    private readonly Roster roster;
    private readonly DispatcherTimer watch = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly bool fontOffered = GameFont.Available(GameFont.Repository);
    private Settings settings;
    private bool gameFont;
    private bool launcherBusy;

    private AccountView? dragged;
    private bool moved;

    public MainWindow()
    {
        roster = new Roster(vault, jagex);
        InitializeComponent();

        Title = $"JagexManager {typeof(App).Assembly.GetName().Version?.ToString(3)}";

        List.ItemsSource = roster.Accounts;
        settings = Settings.Load(SettingsPath);
        App.Show(settings.Theme);
        ShowFont(settings.UseGameFont);
        ShowSettings();

        watch.Tick += (_, _) => roster.DropClosed();
        watch.Start();

        Loaded += async (_, _) => await LoadAsync();
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

        await TryAsync(() => Task.WhenAll(roster.Accounts.ToList().Select(roster.RefreshAsync)));
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

    private void Release(object sender, MouseButtonEventArgs e) => List.ReleaseMouseCapture();

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

    private void OfferClients(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        var character = (CharacterView)button.DataContext;
        var installed = settings.Installed;
        if (installed.Count == 0)
        {
            Say("No game client was found. Choose one under Settings.");
            return;
        }

        var menu = MenuBelow(button);
        menu.Items.Add(ClientItem(character, null, $"Default ({settings.Default})"));
        menu.Items.Add(new Separator());
        foreach (var program in installed)
            menu.Items.Add(ClientItem(character, program.Name, program.Name));

        menu.IsOpen = true;
    }

    private ContextMenu MenuBelow(UIElement target)
    {
        var menu = new ContextMenu
        {
            PlacementTarget = target,
            Placement = PlacementMode.Bottom,
            FontFamily = FontFamily,
            FontSize = FontSize,
        };
        TextOptions.SetTextFormattingMode(menu, TextOptions.GetTextFormattingMode(this));
        TextOptions.SetTextRenderingMode(menu, TextOptions.GetTextRenderingMode(this));
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

    private async Task PlayAsync(CharacterView character)
    {
        var program = settings.Find(character.Choice);

        character.Ready = false;
        try
        {
            await TryAsync(async () =>
            {
                await roster.PlayAsync(character, program);
                Say($"Started {character.Name} in {program.Name}.");
            });
        }
        finally
        {
            character.Ready = true;
        }
    }

    private void Stop(object sender, RoutedEventArgs e)
    {
        var character = (CharacterView)((FrameworkElement)sender).DataContext;

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
        PlayException or IOException or InvalidDataException or UnauthorizedAccessException or Win32Exception => e.Message,
        _ => null,
    };

    private void Say(string message)
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
        roster.Forget();
        base.OnClosed(e);
    }
}
