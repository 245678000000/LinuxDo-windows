using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace LinuxDo;

/// Opt-in offline GUI smoke test: LinuxDo.exe --preview --self-test
/// Writes %TEMP%\LinuxDoSelfTest.txt and window screenshots to %TEMP%\LinuxDoSelfTest\, then exits 0 or 1.
sealed class SelfTest(MainWindow window)
{
    static readonly string Output = Path.Combine(Path.GetTempPath(), "LinuxDoSelfTest.txt");
    static readonly string Shots = Path.Combine(Path.GetTempPath(), "LinuxDoSelfTest");
    readonly List<string> failures = [];
    int steps;

    public async void Start()
    {
        try
        {
            if (Directory.Exists(Shots)) Directory.Delete(Shots, true);
            Directory.CreateDirectory(Shots);
            await Run();
        }
        catch (Exception e) { failures.Add($"step {steps}: {e}"); }
        Finish();
    }

    void Expect(bool ok, string message) { steps++; if (!ok) failures.Add(message); }

    static async Task WaitUntil(Func<bool> condition, string what, int timeoutMs = 15000)
    {
        var started = Environment.TickCount64;
        while (!condition())
        {
            if (Environment.TickCount64 - started > timeoutMs) throw new TimeoutException("timed out waiting for " + what);
            await Task.Delay(100);
        }
    }

    Task Settle(int ms = 600) => Task.Delay(ms);

    async Task Run()
    {
        await WaitUntil(() => window.HasLoadedContent && !window.IsLoading, "first page", 30000);
        // The reading checks assume the expanded native sidebar and the default 16 px body text.
        window.SetSidebarVisible(true);
        await Settle(800);
        Expect(window.Title.StartsWith("LinuxDo 离线界面预览", StringComparison.Ordinal), $"window title from page: {window.Title}");
        Expect(window.Badge == "3", $"taskbar badge from title: {window.Badge}");
        var css = await window.Evaluate("[getComputedStyle(document.querySelector('.cooked')).fontSize, getComputedStyle(document.querySelector('.sidebar-wrapper')).display].join('|')");
        Expect(css == "\"16px|none\"", $"reading CSS and web sidebar integration: {css}");
        Capture(window, "01-light.png");

        window.Navigate(AppConfig.Route("/top"));
        await WaitUntil(() => window.CurrentUrl?.Contains("route=%2Ftop") == true && !window.IsLoading, "route navigation");
        await Settle();
        Expect(window.SidebarSelection == "/top", $"sidebar selection follows route: {window.SidebarSelection}");
        window.GoBack();
        await WaitUntil(() => window.CurrentUrl?.Contains("route=%2Flatest") == true && !window.IsLoading, "back navigation");
        await Settle();
        Expect(window.SidebarSelection == "/latest", $"sidebar selection after back: {window.SidebarSelection}");

        var matches = await window.FindAsync("玻璃阅读样例");
        for (var i = 0; i < 20 && matches < 1; i++) { await Task.Delay(100); matches = window.Core?.Find.MatchCount ?? 0; }
        Expect(matches >= 1, $"native find: {matches} matches");
        Capture(window, "02-find.png");
        window.StopFind();

        await window.Evaluate("scrollTo(0, 2200); window.__linuxDoNative.capture(); scrollY");
        await Settle(1000);
        var position = ReadingStore.Shared.Position(window.CurrentUrl);
        Expect(position?.Y > 1500, $"reading position captured: {position?.Y}");
        var second = App.OpenWindow(AppConfig.TryParse(window.CurrentUrl), from: window);
        await WaitUntil(() => second.HasLoadedContent && !second.IsLoading, "second window");
        await Settle(1800);
        var restored = await second.Evaluate("scrollY");
        Expect(double.TryParse(restored, NumberStyles.Float, CultureInfo.InvariantCulture, out var y) && y > 1500, $"reading position restored: {restored}");
        second.Close();
        await Settle(400);

        await window.Evaluate("document.querySelector('#draft').value='草稿'; document.querySelector('#draft').dispatchEvent(new Event('input', {bubbles:true})); true");
        await Settle(500);
        var eventSignal = window.HasUnsavedDraft;
        var live = await window.RefreshDraftState();
        Expect(live, "live draft detection");
        Expect(eventSignal, $"input event draft signal (live={live})");
        await window.Evaluate("document.querySelector('#draft').value=''; true");
        Expect(!await window.RefreshDraftState(), "stale draft cleared");

        await window.Evaluate("document.getElementById('download-link').click(); true");
        var downloaded = Path.Combine(AppConfig.DataDirectory, "Downloads", "LinuxDo 离线样例.txt");
        await WaitUntil(() => File.Exists(downloaded) && App.CompletedDownloads > 0 && App.ActiveDownloads == 0, "download", 10000);
        Expect(File.ReadAllText(downloaded) == "LinuxDo offline download fixture", "download content");
        await Settle(400);
        Capture(window, "03-download.png");
        window.Core?.CloseDefaultDownloadDialog();

        window.SearchFor("SwiftUI & 玻璃");
        await WaitUntil(() => window.CurrentUrl?.Contains("SwiftUI") == true && !window.IsLoading, "toolbar search");
        Expect(AppConfig.QueryValue(new Uri(window.CurrentUrl!), "q") == "SwiftUI & 玻璃", $"search query encoding: {window.CurrentUrl}");

        Prefs.Update(s => s.Appearance = "dark");
        App.ApplyAppearance();
        await Settle(1200);
        var scheme = await window.Evaluate("matchMedia('(prefers-color-scheme: dark)').matches");
        Expect(scheme == "true", $"page follows dark appearance: {scheme}");
        Capture(window, "04-dark.png");

        var settings = SettingsWindow.ShowShared();
        await Settle(900);
        Capture(settings, "05-settings-dark.png");
        settings.Close();

        window.ToggleSidebar();
        await Settle(600);
        var sidebarFlag = await window.Evaluate("document.documentElement.dataset.linuxdoSidebar");
        Expect(!window.SidebarVisible && sidebarFlag == "\"false\"", $"collapsing the native sidebar restores the web sidebar: {sidebarFlag}");
        Capture(window, "06-sidebar-collapsed.png");
        Expect(File.Exists(Prefs.FilePath), "settings saved");
    }

    void Finish()
    {
        var text = failures.Count == 0 ? $"PASS {steps} self-test checks\n" : "FAIL\n" + string.Join("\n", failures) + "\n";
        File.WriteAllText(Output, text);
        Application.Current.Shutdown(failures.Count == 0 ? 0 : 1);
    }

    void Capture(Window target, string name)
    {
        try { Snapshot.Save(target, Path.Combine(Shots, name)); }
        catch (Exception e) { failures.Add($"screenshot {name}: {e.Message}"); }
    }
}

/// PrintWindow with PW_RENDERFULLCONTENT also captures the WebView2 child window, which WPF rendering cannot.
static class Snapshot
{
    [StructLayout(LayoutKind.Sequential)] struct Rect32 { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out Rect32 rect);
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int width, int height);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);

    public static void Save(Window window, string path)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        GetWindowRect(hwnd, out var r);
        int width = r.Right - r.Left, height = r.Bottom - r.Top;
        var screen = GetDC(IntPtr.Zero);
        var dc = CreateCompatibleDC(screen);
        var bitmap = CreateCompatibleBitmap(screen, width, height);
        var previous = SelectObject(dc, bitmap);
        try
        {
            PrintWindow(hwnd, dc, 2);
            SelectObject(dc, previous);
            var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using var file = File.Create(path);
            encoder.Save(file);
        }
        finally
        {
            DeleteObject(bitmap); DeleteDC(dc); ReleaseDC(IntPtr.Zero, screen);
        }
    }
}
