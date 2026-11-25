using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
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
    private static EventWaitHandle? opening;

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

        var hidden = e.Args.Contains(Autostart.Hidden);
        opening = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\JagexManager.Open", out var unheard);
        instance = new Mutex(initiallyOwned: true, @"Local\JagexManager", out var first);
        if (!first)
        {
            if (!hidden && unheard)
            {
                MessageBox.Show("JagexManager is already running.", "JagexManager", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else if (!hidden)
            {
                AllowSetForegroundWindow(-1);
                opening.Set();
            }

            Shutdown();
            return;
        }

        DispatcherUnhandledException += Report;
        SystemParameters.StaticPropertyChanged += FollowContrast;

        var window = new MainWindow();
        ThreadPool.RegisterWaitForSingleObject(opening, (_, _) => Dispatcher.BeginInvoke(window.Bring), null, Timeout.Infinite, executeOnlyOnce: false);
        window.Start(hidden);
    }

    private static void FollowContrast(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.HighContrast))
            Show(theme);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int process);

    private void Report(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show($"Something went wrong: {e.Exception.Message}", "JagexManager", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;

        if (MainWindow is not { IsLoaded: true })
            Shutdown(1);
    }
}
