using System.Windows;
using System.Windows.Controls;

namespace LinuxDo;

public partial class SettingsWindow : Window
{
    static SettingsWindow? shared;
    bool synchronizing;

    public static SettingsWindow ShowShared()
    {
        shared ??= new SettingsWindow();
        shared.Synchronize();
        shared.Show();
        if (shared.WindowState == WindowState.Minimized) shared.WindowState = WindowState.Normal;
        shared.Activate();
        return shared;
    }

    SettingsWindow()
    {
        InitializeComponent();
        Closed += (_, _) => shared = null;
    }

    void Synchronize()
    {
        synchronizing = true;
        var s = Prefs.Current;
        (s.Appearance switch { "light" => AppearanceLight, "dark" => AppearanceDark, _ => AppearanceSystem }).IsChecked = true;
        ReadingCheck.IsChecked = s.Reading;
        FontSlider.Value = Prefs.BoundedFont(s.FontSize);
        FontValue.Text = $"{(int)FontSlider.Value} px";
        var line = Prefs.BoundedLineHeight(s.LineHeight);
        (line < 1.65 ? LineCompact : line > 1.85 ? LineLoose : LineComfortable).IsChecked = true;
        HideWebSidebarCheck.IsChecked = s.HideWebSidebar;
        RestoreCheck.IsChecked = s.RestoreReading;
        AskDownloadCheck.IsChecked = s.AskDownloadLocation;
        ProxyCheck.IsChecked = s.ProxyEnabled;
        ProxyBox.Text = s.ProxyUrl;
        if (AppConfig.Preview) { ProxyCheck.IsEnabled = false; ProxyBox.IsEnabled = false; ProxyStatus.Text = "离线预览不使用网络设置。"; }
        synchronizing = false;
    }

    void Update(Action<Settings> change, string? reason = null)
    {
        if (!synchronizing) Prefs.Update(change, reason);
    }

    void OnAppearanceChecked(object sender, RoutedEventArgs e)
    {
        if (synchronizing) return;
        var value = sender == AppearanceLight ? "light" : sender == AppearanceDark ? "dark" : "system";
        Update(s => s.Appearance = value);
        App.ApplyAppearance();
    }

    void OnReadingClick(object sender, RoutedEventArgs e) => Update(s => s.Reading = ReadingCheck.IsChecked == true);
    void OnHideWebSidebarClick(object sender, RoutedEventArgs e) => Update(s => s.HideWebSidebar = HideWebSidebarCheck.IsChecked == true);
    void OnRestoreClick(object sender, RoutedEventArgs e) => Update(s => s.RestoreReading = RestoreCheck.IsChecked == true);
    void OnAskDownloadClick(object sender, RoutedEventArgs e) => Update(s => s.AskDownloadLocation = AskDownloadCheck.IsChecked == true);

    void OnFontChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (FontValue is null) return;
        FontValue.Text = $"{(int)FontSlider.Value} px";
        Update(s => s.FontSize = FontSlider.Value);
    }

    void OnLineChecked(object sender, RoutedEventArgs e)
    {
        var value = sender == LineCompact ? 1.55 : sender == LineLoose ? 1.95 : 1.75;
        Update(s => s.LineHeight = value);
    }

    void OnClearPositions(object sender, RoutedEventArgs e)
    {
        if (!Dialogs.Confirm(this, "清除本地阅读位置？", "账号、Cookie、收藏和网站数据都会保留。", "取消", "清除阅读位置")) return;
        ReadingStore.Shared.Clear();
        Prefs.Update(_ => { }, Prefs.PositionsCleared);
    }

    void OnSaveNetwork(object sender, RoutedEventArgs e)
    {
        var value = ProxyBox.Text.Trim();
        var enabled = ProxyCheck.IsChecked == true;
        if (enabled && ProxyEndpoint.Parse(value) is null)
        {
            ProxyStatus.Text = "请输入 http://主机:端口 或 socks5://主机:端口（1–65535）";
            ProxyStatus.SetResourceReference(ForegroundProperty, "SystemFillColorCriticalBrush");
            return;
        }
        var changed = enabled != Prefs.Current.ProxyEnabled || (enabled && value != Prefs.Current.ProxyUrl);
        Update(s => { s.ProxyUrl = value; s.ProxyEnabled = enabled; });
        ProxyStatus.SetResourceReference(ForegroundProperty, "TextFillColorSecondaryBrush");
        // WebView2 reads proxy flags once per process, so the change waits for the next launch.
        ProxyStatus.Text = changed ? "已保存。关闭全部 LinuxDo 窗口并重新打开后生效。" : "已保存。";
    }
}
