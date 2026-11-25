using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
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

    private const string OpenSignal = @"Local\JagexManager.Open";
    private const int AnyProcess = -1;

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

        var minimised = e.Args.Contains(Autostart.Minimised);
        instance = new Mutex(initiallyOwned: true, @"Local\JagexManager", out var first);
        if (!first)
        {
            if (!minimised)
                WakeRunning();

            Shutdown();
            return;
        }

        DispatcherUnhandledException += Report;
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        SystemParameters.StaticPropertyChanged += FollowContrast;

        var window = new MainWindow();
        Listen(window);
        window.Start(minimised);

        if (!Autostart.Current.Repair())
            window.Say("JagexManager could not update its Windows startup entry. Turn Start with Windows off and on again in Settings.");
    }

    private static void WakeRunning()
    {
        if (!Signalled())
            MessageBox.Show("JagexManager is already running.", "JagexManager", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static bool Signalled()
    {
        try
        {
            if (!EventWaitHandle.TryOpenExisting(OpenSignal, out var signal))
                return false;

            using (signal)
            {
                AllowSetForegroundWindow(AnyProcess);
                return signal.Set();
            }
        }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    private void Listen(MainWindow window)
    {
        try
        {
            opening = new EventWaitHandle(false, EventResetMode.AutoReset, OpenSignal);
        }
        catch (Exception error) when (error is WaitHandleCannotBeOpenedException or UnauthorizedAccessException or IOException)
        {
            return;
        }

        var waiting = ThreadPool.RegisterWaitForSingleObject(opening, (_, _) => Dispatcher.BeginInvoke(window.Restore), null, Timeout.Infinite, executeOnlyOnce: false);
        window.Closed += (_, _) => waiting.Unregister(null);
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
