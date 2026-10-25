using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;

namespace LinuxDo;

public partial class App : Application
{
    const string RuntimeDownload = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";
    public static CoreWebView2Environment? WebEnvironment { get; private set; }
    public static IEnumerable<MainWindow> MainWindows => Current.Windows.OfType<MainWindow>();
    public static int ActiveDownloads { get; private set; }
    public static int CompletedDownloads { get; private set; }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;
        AppConfig.Preview = e.Args.Contains("--preview");
        // The preview starts from a clean profile every time, like a non-persistent store.
        if (AppConfig.Preview) try { Directory.Delete(AppConfig.DataDirectory, true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        Prefs.Load();
        ApplyAppearance();
        try { WebEnvironment = await CreateEnvironment(); }
        catch (WebView2RuntimeNotFoundException)
        {
            var install = MessageBox.Show("LinuxDo 需要 Microsoft Edge WebView2 运行时。\n是否打开微软的下载页面？", AppConfig.AppName, MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (install == MessageBoxResult.Yes) OpenExternal(new Uri(RuntimeDownload));
            Shutdown(1); return;
        }
        var url = e.Args.Select(AppConfig.TryParse).FirstOrDefault(AppConfig.IsInternal);
        var window = OpenWindow(url, primary: true);
        if (AppConfig.Preview && e.Args.Contains("--self-test")) new SelfTest(window).Start();
    }

    static Task<CoreWebView2Environment> CreateEnvironment()
    {
        var arguments = "";
        if (!AppConfig.Preview && Prefs.Current.ProxyEnabled && ProxyEndpoint.Parse(Prefs.Current.ProxyUrl) is { } proxy) arguments = proxy.BrowserArgument;
        var options = new CoreWebView2EnvironmentOptions(arguments) { Language = "zh-CN" };
        return CoreWebView2Environment.CreateAsync(null, AppConfig.DataDirectory, options);
    }

    public static void ApplyAppearance()
    {
        Current.ThemeMode = Prefs.Current.Appearance switch
        {
            "light" => ThemeMode.Light,
            "dark" => ThemeMode.Dark,
            _ => ThemeMode.System,
        };
    }

    public static MainWindow OpenWindow(Uri? url = null, bool primary = false, CoreWebView2NewWindowRequestedEventArgs? request = null, Window? from = null)
    {
        var window = new MainWindow(url, primary, request);
        if (from is not null && !primary)
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Width = from.ActualWidth; window.Height = from.ActualHeight;
            window.Left = from.Left + 28; window.Top = from.Top + 28;
        }
        window.Show(); window.Activate();
        return window;
    }

    public static void TrackDownload(CoreWebView2DownloadOperation operation)
    {
        ActiveDownloads++;
        var finished = false;
        operation.StateChanged += (_, _) =>
        {
            if (finished || operation.State == CoreWebView2DownloadState.InProgress) return;
            finished = true; ActiveDownloads--;
            if (operation.State == CoreWebView2DownloadState.Completed) CompletedDownloads++;
        };
    }

    public static void OpenExternal(Uri url)
    {
        try { Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { }
    }

    void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try { File.AppendAllText(Path.Combine(AppConfig.DataDirectory, "LinuxDo.log"), $"{DateTime.Now:O} {e.Exception}\n"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        if (AppConfig.Preview) return;
        MessageBox.Show($"出现了一个错误，当前页面会保留。\n\n{e.Exception.Message}", AppConfig.AppName, MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
