using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace JagexManager;

public partial class App : Application
{
    public static string DataFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JagexManager");

    private static readonly ResourceDictionary RuneLite = new()
    {
        Source = new Uri("/JagexManager;component/RuneLite.xaml", UriKind.Relative),
    };

    private static Mutex? instance;

    private static Theme theme;

    public static void Show(Theme chosen)
    {
        theme = chosen;

        Current.ThemeMode = chosen switch
        {
            Theme.Light => ThemeMode.Light,
            Theme.RuneLite or Theme.Dark => ThemeMode.Dark,
            _ => ThemeMode.System,
        };

        var merged = Current.Resources.MergedDictionaries;
        merged.Remove(RuneLite);
        if (chosen == Theme.RuneLite && !SystemParameters.HighContrast)
            merged.Add(RuneLite);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var autostarted = e.Args.Contains(Autostart.Minimised);
        var command = Command.Parse(e.Args);
        instance = new Mutex(initiallyOwned: true, @"Local\JagexManager", out var first);
        if (!first)
        {
            if (command is not null || !autostarted)
                WakeRunning(command ?? Command.Open);

            Shutdown();
            return;
        }

        DispatcherUnhandledException += Report;
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        SystemParameters.StaticPropertyChanged += FollowContrast;

        var window = new MainWindow();
        var relay = new Relay(Relay.Pipe, heard => Dispatcher.BeginInvoke(() => window.Act(heard)));
        window.Closed += (_, _) => relay.Dispose();
        window.Start(autostarted || command is not null);
        if (command is not null)
            window.Act(command);

        if (!Autostart.Current.Repair())
            window.Say("JagexManager could not update its Windows startup entry. Turn Start with Windows off and on again in Settings.");
    }

    private static void WakeRunning(Command command)
    {
        if (!Relay.Send(Relay.Pipe, command))
            MessageBox.Show("JagexManager is already running.", "JagexManager", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static void FollowContrast(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.HighContrast))
            Show(theme);
    }

    private void Report(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show($"Something went wrong: {e.Exception.Message}", "JagexManager", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;

        if (MainWindow is not { IsLoaded: true })
            Shutdown(1);
    }
}
