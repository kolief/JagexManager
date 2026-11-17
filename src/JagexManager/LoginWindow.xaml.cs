using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net.Http;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace JagexManager;

[SuppressMessage("Design", "CA1001", Justification = "The window's life ends in OnClosed, which disposes its fields.")]
public partial class LoginWindow : Window
{
    private const string ProfilePrefix = "login-";

    private readonly Login login;
    private readonly string profile = Path.Combine(App.DataFolder, ProfilePrefix + Guid.NewGuid().ToString("N"));
    private readonly CancellationTokenSource closing = new();
    private bool working;

    public LoginWindow(Jagex jagex)
    {
        login = new Login(jagex);
        InitializeComponent();
        Loaded += async (_, _) => await StartAsync();
    }

    public Account? Account => login.Account;

    public string? Error { get; private set; }

    public static void ClearProfiles()
    {
        try
        {
            foreach (var folder in Directory.GetDirectories(App.DataFolder, ProfilePrefix + "*"))
                Delete(folder);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private bool Abandoned => closing.IsCancellationRequested;

    private async Task StartAsync()
    {
        try
        {
            Browser.CreationProperties = new CoreWebView2CreationProperties
            {
                UserDataFolder = profile,
                IsInPrivateModeEnabled = true,
            };
            await Browser.EnsureCoreWebView2Async();
        }
        catch (Exception) when (Abandoned)
        {
        }
        catch (WebView2RuntimeNotFoundException)
        {
            Fail("Signing in needs the Microsoft Edge WebView2 Runtime. Install it from https://developer.microsoft.com/microsoft-edge/webview2 and try again.");
        }
        catch (Exception e)
        {
            Fail($"The sign-in browser could not start: {e.Message}");
        }

        if (Abandoned)
        {
            _ = DiscardAsync(profile);
            return;
        }

        var browser = Browser.CoreWebView2;
        browser.Settings.AreDevToolsEnabled = false;
        browser.Settings.AreDefaultContextMenusEnabled = false;
        browser.Settings.AreHostObjectsAllowed = false;
        browser.Settings.IsWebMessageEnabled = false;
        browser.Settings.IsStatusBarEnabled = false;
        browser.Settings.IsPasswordAutosaveEnabled = false;
        browser.Settings.IsGeneralAutofillEnabled = false;
        browser.Settings.IsReputationCheckingRequired = false;

        browser.NavigationStarting += Visit;
        browser.SourceChanged += (_, _) => ShowAddress(browser.Source);
        browser.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var page) && page.Scheme == Uri.UriSchemeHttps)
                browser.Navigate(page.AbsoluteUri);
        };
        browser.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
        browser.DownloadStarting += (_, e) => e.Cancel = true;
        browser.LaunchingExternalUriScheme += (_, e) => e.Cancel = true;
        browser.ServerCertificateErrorDetected += (_, e) => e.Action = CoreWebView2ServerCertificateErrorAction.Cancel;

        browser.Navigate(login.StartUrl.AbsoluteUri);
    }

    private async void Visit(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (!login.Intercepts(e.Uri))
            return;

        e.Cancel = true;
        if (working)
            return;

        working = true;
        Address.Text = "Signing in…";
        try
        {
            var next = await login.ContinueAsync(e.Uri, closing.Token);
            if (Abandoned)
                return;

            if (next is null)
                Close();
            else
                Browser.CoreWebView2.Navigate(next.AbsoluteUri);
        }
        catch (OperationCanceledException) when (Abandoned)
        {
        }
        catch (OperationCanceledException)
        {
            Fail("Jagex took too long to answer. Please try again.");
        }
        catch (Exception error) when (error is AuthException or JagexException or HttpRequestException)
        {
            Fail(error.Message);
        }
        finally
        {
            working = false;
        }
    }

    private void ShowAddress(string source) =>
        Address.Text = Uri.TryCreate(source, UriKind.Absolute, out var page) ? $"{page.Scheme}://{page.Host}" : "";

    private void Fail(string message)
    {
        Error = message;
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);

        closing.Cancel();
        closing.Dispose();
        Browser.Dispose();
        _ = DiscardAsync(profile);
    }

    private static async Task DiscardAsync(string folder)
    {
        for (var attempt = 0; attempt < 10 && !Delete(folder); attempt++)
            await Task.Delay(500);
    }

    private static bool Delete(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
