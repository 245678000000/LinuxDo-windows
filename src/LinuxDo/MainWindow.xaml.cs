using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;

namespace LinuxDo;

public partial class MainWindow : Window
{
    readonly bool primary;
    readonly Uri? initialUrl;
    CoreWebView2NewWindowRequestedEventArgs? popupRequest;
    readonly CoreWebView2Deferral? popupDeferral;
    ReadingPosition? pendingRestore;
    bool sidebarVisible, hasLoadedContent, draftPresent, closingApproved, closePromptOpen, syncingSidebar;
    int noticeGeneration, progressGeneration, crashCount;
    long lastDownloadStarted, downloadDialogClosed;
    Action? pendingNoticeAction;
    string? lastObservedUrl;

    public bool HasUnsavedDraft => draftPresent;
    public bool HasLoadedContent => hasLoadedContent;
    public bool IsLoading { get; private set; } = true;
    public string? Badge { get; private set; }
    public CoreWebView2? Core => Web.CoreWebView2;
    public string? CurrentUrl => Web.CoreWebView2?.Source;
    public string? SidebarSelection => (SidebarList.SelectedItem as ListBoxItem)?.Tag as string;
    public bool SidebarVisible => sidebarVisible;
    Uri? SourceUri => AppConfig.TryParse(CurrentUrl);
    static CoreWebView2PreferredColorScheme ColorScheme => Prefs.Current.Appearance switch
    {
        "light" => CoreWebView2PreferredColorScheme.Light,
        "dark" => CoreWebView2PreferredColorScheme.Dark,
        _ => CoreWebView2PreferredColorScheme.Auto,
    };

    public MainWindow(Uri? url = null, bool primary = false, CoreWebView2NewWindowRequestedEventArgs? request = null)
    {
        InitializeComponent();
        this.primary = primary; initialUrl = url;
        // window.open must get a real WebView so authentication callbacks keep their opener.
        popupRequest = request; popupDeferral = request?.GetDeferral();
        Web.DefaultBackgroundColor = System.Drawing.Color.Transparent;
        if (AppConfig.Preview) SidebarSubtitle.Text = "离线界面预览";
        BuildSidebar();
        FitToWorkArea();
        if (primary) ApplySavedBounds();
        SetSidebarVisible(!Prefs.Current.SidebarCollapsed);
        Prefs.Changed += OnPreferencesChanged;
        Loaded += async (_, _) => await InitializeWebView();
        Closed += (_, _) => { Prefs.Changed -= OnPreferencesChanged; Web.Dispose(); };
    }

    async Task InitializeWebView()
    {
        try { await Web.EnsureCoreWebView2Async(App.WebEnvironment); }
        catch (Exception e)
        {
            popupDeferral?.Complete();
            ShowError($"网页引擎没有启动：{e.Message}");
            return;
        }
        var core = Web.CoreWebView2;
        var settings = core.Settings;
#if DEBUG
        settings.AreDevToolsEnabled = true;
#else
        settings.AreDevToolsEnabled = false;
#endif
        settings.AreBrowserAcceleratorKeysEnabled = true;
        settings.IsZoomControlEnabled = true;
        settings.IsStatusBarEnabled = true;
        core.Profile.PreferredColorScheme = ColorScheme;
        if (AppConfig.Preview)
        {
            var downloads = Path.Combine(AppConfig.DataDirectory, "Downloads");
            Directory.CreateDirectory(downloads);
            core.Profile.DefaultDownloadFolderPath = downloads;
        }
        core.DefaultDownloadDialogCornerAlignment = CoreWebView2DefaultDownloadDialogCornerAlignment.TopRight;
        core.DefaultDownloadDialogMargin = new System.Drawing.Point(8, 4);
        await core.AddScriptToExecuteOnDocumentCreatedAsync(PageBridge.Source);
        core.WebMessageReceived += OnWebMessage;
        core.NavigationStarting += OnNavigationStarting;
        core.ContentLoading += (_, _) => AnimateProgress(0.7, 1);
        core.NavigationCompleted += OnNavigationCompleted;
        core.SourceChanged += (_, _) => UrlChanged();
        core.HistoryChanged += (_, _) => UpdateNavigationButtons();
        core.DocumentTitleChanged += (_, _) => UpdateTitle();
        core.NewWindowRequested += OnNewWindowRequested;
        core.WindowCloseRequested += (_, _) => Close();
        core.DownloadStarting += OnDownloadStarting;
        core.IsDefaultDownloadDialogOpenChanged += (_, _) => { if (!core.IsDefaultDownloadDialogOpen) downloadDialogClosed = Environment.TickCount64; };
        core.ProcessFailed += OnProcessFailed;
        Web.ZoomFactor = Prefs.BoundedZoom(Prefs.Current.PageZoom);
        Web.ZoomFactorChanged += (_, _) => { Prefs.Current.PageZoom = Prefs.BoundedZoom(Web.ZoomFactor); Prefs.Save(); };

        if (popupRequest is { } request)
        {
            request.NewWindow = core; request.Handled = true;
            popupDeferral?.Complete(); popupRequest = null;
            return;
        }
        var saved = AppConfig.TryParse(Prefs.Current.LastUrl);
        var initial = initialUrl ?? (AppConfig.IsAllowed(saved) ? saved : null) ?? AppConfig.Route("/latest");
        if (Prefs.Current.RestoreReading) pendingRestore = ReadingStore.Shared.Position(initial.AbsoluteUri);
        Load(initial);
    }

    // MARK: Layout

    void FitToWorkArea()
    {
        var area = SystemParameters.WorkArea;
        Width = Math.Max(MinWidth, Math.Min(Width, area.Width - 32));
        Height = Math.Max(MinHeight, Math.Min(Height, area.Height - 32));
    }

    void ApplySavedBounds()
    {
        if (Prefs.Current.Window is not { } b || b.Width < MinWidth || b.Height < MinHeight) return;
        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        var visible = Rect.Intersect(screen, new Rect(b.Left, b.Top, b.Width, b.Height));
        if (visible.IsEmpty || visible.Width < 200 || visible.Height < 120) return;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = b.Left; Top = b.Top; Width = b.Width; Height = b.Height;
        if (b.Maximized) WindowState = WindowState.Maximized;
    }

    void SaveBounds()
    {
        if (!primary) return;
        var r = WindowState == WindowState.Normal ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;
        if (r.IsEmpty) return;
        Prefs.Current.Window = new(r.Left, r.Top, r.Width, r.Height, WindowState == WindowState.Maximized);
        Prefs.Save();
    }

    void BuildSidebar()
    {
        var iconFont = (FontFamily)FindResource("IconFont");
        foreach (var route in SidebarRoute.Items)
        {
            if (route.Path is null)
            {
                var header = new TextBlock { Text = route.Title, FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(4, 14, 0, 2) };
                header.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
                SidebarList.Items.Add(new ListBoxItem { Content = header, IsHitTestVisible = false, Focusable = false, IsTabStop = false });
                continue;
            }
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock { Text = route.Glyph, FontFamily = iconFont, FontSize = 16, Width = 22, VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(new TextBlock { Text = route.Title, FontSize = 14, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            var item = new ListBoxItem { Content = row, Tag = route.Path, MinHeight = 36 };
            AutomationProperties.SetName(item, route.Title);
            SidebarList.Items.Add(item);
        }
    }

    public void SetSidebarVisible(bool visible)
    {
        sidebarVisible = visible;
        SidebarColumn.MinWidth = 0;
        SidebarColumn.Width = visible ? new GridLength(Math.Clamp(Prefs.Current.SidebarWidth, 200, 320)) : new GridLength(0);
        SidebarColumn.MinWidth = visible ? 200 : 0;
        Sidebar.Visibility = SidebarSplitter.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        ConfigurePage();
    }

    public void ToggleSidebar()
    {
        SetSidebarVisible(!sidebarVisible);
        Prefs.Current.SidebarCollapsed = !sidebarVisible;
        Prefs.Save();
    }

    void SyncSidebar()
    {
        syncingSidebar = true;
        try { SidebarList.SelectedIndex = SidebarRoute.IndexFor(SourceUri); }
        finally { syncingSidebar = false; }
    }

    // MARK: Page state

    void UrlChanged()
    {
        SyncSidebar();
        var url = CurrentUrl;
        if (url != lastObservedUrl)
        {
            lastObservedUrl = url;
            if (AppConfig.IsAllowed(SourceUri)) { Prefs.Current.LastUrl = url; Prefs.Save(); }
        }
        ConfigurePage(); UpdateNavigationButtons();
    }

    void UpdateNavigationButtons()
    {
        BackButton.IsEnabled = Core?.CanGoBack == true;
        ForwardButton.IsEnabled = Core?.CanGoForward == true;
    }

    void UpdateTitle()
    {
        var parsed = PageTitle.Parse(Core?.DocumentTitle);
        Title = parsed.Text == AppConfig.AppName ? AppConfig.AppName : $"{parsed.Text} - {AppConfig.AppName}";
        Badge = parsed.Badge;
        Taskbar.Overlay = Badge is null ? null : Components.RenderBadge(Badge);
        Taskbar.Description = Badge is null ? Title : $"{Title}（{Badge} 条未读）";
    }

    void OnPreferencesChanged(string? reason)
    {
        if (Core is { } core) core.Profile.PreferredColorScheme = ColorScheme;
        ConfigurePage();
    }

    void Script(string source)
    {
        if (Core is { } core && AppConfig.IsAllowed(SourceUri)) _ = core.ExecuteScriptAsync(source);
    }
    void ConfigurePage() => Script(PageBridge.ConfigureScript(sidebarVisible));
    public void CapturePosition() => Script("window.__linuxDoNative?.capture();");
    public Task<string> Evaluate(string source) => Core?.ExecuteScriptAsync(source) ?? Task.FromResult("null");

    void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!AppConfig.IsAllowed(AppConfig.TryParse(e.Source))) return;
        try
        {
            using var document = JsonDocument.Parse(e.WebMessageAsJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("type", out var type)) return;
            switch (type.GetString())
            {
                case "ready": ConfigurePage(); RestoreIfNeeded(); break;
                case "draft": draftPresent = root.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.True; break;
                case "position":
                    if (!Prefs.Current.RestoreReading || !root.TryGetProperty("url", out var url) || url.ValueKind != JsonValueKind.String) return;
                    if (Number(root, "x") is not { } x || Number(root, "y") is not { } y) return;
                    var postId = root.TryGetProperty("postID", out var post) && post.ValueKind == JsonValueKind.String ? post.GetString() : null;
                    ReadingStore.Shared.Save(new(url.GetString()!, x, y, postId, Number(root, "offset"), DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0));
                    break;
            }
        }
        catch (JsonException) { }
    }
    static double? Number(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;

    void RestoreIfNeeded()
    {
        if (pendingRestore is not { } position || position.Url != CurrentUrl) return;
        pendingRestore = null;
        Script(PageBridge.RestoreScript(position));
    }

    // MARK: Navigation

    public void Load(Uri url)
    {
        HideError();
        Core?.Navigate(url.AbsoluteUri);
    }

    public async void Navigate(Uri url)
    {
        if (!await ConfirmLeave()) { SyncSidebar(); return; }
        CapturePosition(); Load(url);
    }

    async Task<bool> ConfirmLeave()
    {
        if (!draftPresent) return true;
        // The site can clear its editor without an input event after posting, so confirm live state first.
        if (!await RefreshDraftState()) return true;
        return Dialogs.Confirm(this, "当前有未发送的回复", "继续会离开当前编辑页面。你可以先留在这里保存草稿。", "留在页面", "继续离开");
    }

    public async Task<bool> RefreshDraftState()
    {
        if (Core is not { } core || !AppConfig.IsAllowed(SourceUri)) return draftPresent = false;
        try { draftPresent = await core.ExecuteScriptAsync("window.__linuxDoNative ? window.__linuxDoNative.hasDraft() : false") == "true"; }
        catch (Exception e) when (e is InvalidOperationException or COMException) { draftPresent = false; }
        return draftPresent;
    }

    public async void GoBack()
    {
        if (Core?.CanGoBack != true || !await ConfirmLeave()) return;
        CapturePosition(); Core?.GoBack();
    }
    public async void GoForward()
    {
        if (Core?.CanGoForward != true || !await ConfirmLeave()) return;
        CapturePosition(); Core?.GoForward();
    }
    public void GoHome() => Navigate(AppConfig.Route("/latest"));

    public async void Reload()
    {
        if (!await ConfirmLeave()) return;
        DismissNotice();
        if (Core is not { } core) return;
        if (SourceUri is null || CurrentUrl == "about:blank") { Load(AppConfig.Route("/latest")); return; }
        HideError();
        try { core.Reload(); } catch (InvalidOperationException) { }
    }

    async void HardReload()
    {
        if (Core is not { } core || !await ConfirmLeave()) return;
        HideError();
        try { await core.CallDevToolsProtocolMethodAsync("Page.reload", """{"ignoreCache":true}"""); }
        catch (Exception e) when (e is InvalidOperationException or COMException) { }
    }

    void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (AppConfig.TryParse(e.Uri) is not { } url) return;
        if (AppConfig.Preview && url.Scheme is not ("file" or "blob" or "data" or "about")) { e.Cancel = true; return; }
        // Keep redirects and sign-in flows in the web view. Links from the forum to other sites open in the browser.
        if (e.IsUserInitiated && !e.IsRedirected && url.Scheme is ("http" or "https") && !AppConfig.IsInternal(url) && AppConfig.IsInternal(SourceUri))
        {
            e.Cancel = true; App.OpenExternal(url); return;
        }
        DismissNotice(); StartProgress();
    }

    void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        FinishProgress();
        // Discourse renders its own 404 and error pages; those still count as content.
        if (e.IsSuccess || e.HttpStatusCode >= 400)
        {
            hasLoadedContent = true; HideError(); draftPresent = false;
            UrlChanged(); RestoreIfNeeded();
            return;
        }
        // A navigation that turned into a download also ends here.
        if (e.WebErrorStatus == CoreWebView2WebErrorStatus.OperationCanceled || Environment.TickCount64 - lastDownloadStarted < 3000) return;
        var message = Describe(e.WebErrorStatus);
        if (hasLoadedContent) ShowNotice(message, "重试", Reload);
        else ShowError(message);
    }

    static string Describe(CoreWebView2WebErrorStatus status) => status switch
    {
        CoreWebView2WebErrorStatus.HostNameNotResolved => "找不到 linux.do 的服务器地址。请检查网络或代理设置。",
        CoreWebView2WebErrorStatus.Timeout => "连接超时。请检查网络后重试。",
        CoreWebView2WebErrorStatus.CannotConnect or CoreWebView2WebErrorStatus.ConnectionAborted or
            CoreWebView2WebErrorStatus.ConnectionReset or CoreWebView2WebErrorStatus.Disconnected =>
            "无法连接到服务器。如果设置了代理，请确认代理正在运行。",
        CoreWebView2WebErrorStatus.CertificateCommonNameIsIncorrect or CoreWebView2WebErrorStatus.CertificateExpired or
            CoreWebView2WebErrorStatus.ClientCertificateContainsErrors or CoreWebView2WebErrorStatus.CertificateRevoked or
            CoreWebView2WebErrorStatus.CertificateIsInvalid => "服务器证书无效，已停止加载。",
        CoreWebView2WebErrorStatus.ServerUnreachable => "服务器暂时无法访问。",
        _ => $"页面没有加载完成（{status}）。",
    };

    void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        var url = AppConfig.TryParse(e.Uri);
        if (AppConfig.Preview && url is not null && !url.IsFile && url.Scheme is not ("blob" or "data" or "about")) { e.Handled = true; return; }
        // A plain target=_blank link to another site belongs in the browser; popups with window features keep their opener.
        if (url is { Scheme: "http" or "https" } && !AppConfig.IsInternal(url) && !e.WindowFeatures.HasPosition && !e.WindowFeatures.HasSize)
        {
            e.Handled = true; App.OpenExternal(url); return;
        }
        App.OpenWindow(request: e, from: this);
    }

    void OnDownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs e)
    {
        lastDownloadStarted = Environment.TickCount64;
        if (AppConfig.Preview || !Prefs.Current.AskDownloadLocation) { App.TrackDownload(e.DownloadOperation); return; }
        var deferral = e.GetDeferral();
        // Show the save dialog after the event handler returns, as WebView2 expects for modal UI.
        Dispatcher.BeginInvoke(() =>
        {
            var dialog = new SaveFileDialog
            {
                Title = "保存下载",
                FileName = DownloadNames.Safe(Path.GetFileName(e.ResultFilePath)),
                InitialDirectory = Path.GetDirectoryName(e.ResultFilePath),
                OverwritePrompt = true,
            };
            if (dialog.ShowDialog(this) == true) { e.ResultFilePath = dialog.FileName; App.TrackDownload(e.DownloadOperation); }
            else e.Cancel = true;
            deferral.Complete();
        });
    }

    void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        switch (e.ProcessFailedKind)
        {
            case CoreWebView2ProcessFailedKind.BrowserProcessExited:
                ShowError("网页引擎已退出。请关闭这个窗口后重新打开 LinuxDo。");
                RetryButton.Visibility = Visibility.Collapsed;
                break;
            case CoreWebView2ProcessFailedKind.RenderProcessUnresponsive:
                ShowNotice("网页暂时没有响应。", "重新加载", ReloadAfterCrash);
                break;
            case CoreWebView2ProcessFailedKind.RenderProcessExited:
                crashCount++;
                // Never silently reload an editor after a process failure.
                if (draftPresent || crashCount > 1) ShowNotice("网页进程已停止。重新加载后请检查站点保存的草稿。", "重新加载", ReloadAfterCrash);
                else { pendingRestore = ReadingStore.Shared.Position(CurrentUrl); Core?.Reload(); }
                break;
        }
    }
    void ReloadAfterCrash() { draftPresent = false; Reload(); }

    // MARK: Commands

    public void FocusSearch() { SearchBox.Focus(); SearchBox.SelectAll(); }

    public void SearchFor(string text)
    {
        var query = text.Trim();
        if (query.Length == 0) return;
        SearchBox.Text = query;
        Navigate(AppConfig.SearchUrl(query));
        Web.Focus();
    }

    public async void ShowFind() => await FindAsync("");

    /// Opens the WebView2 find bar; with a term it also searches and returns the match count.
    public async Task<int> FindAsync(string term)
    {
        if (Core is not { } core) return 0;
        var options = core.Environment.CreateFindOptions();
        options.FindTerm = term;
        try { await core.Find.StartAsync(options); }
        catch (Exception e) when (e is InvalidOperationException or COMException or ArgumentException) { return 0; }
        return core.Find.MatchCount;
    }
    public void StopFind() => Core?.Find.Stop();

    void ToggleDownloads()
    {
        if (Core is not { } core) return;
        if (core.IsDefaultDownloadDialogOpen) core.CloseDefaultDownloadDialog();
        // Clicking the toolbar button first light-dismisses the panel; don't reopen it right away.
        else if (Environment.TickCount64 - downloadDialogClosed > 300) core.OpenDefaultDownloadDialog();
    }

    void CopyPageUrl()
    {
        if (CurrentUrl is not { } url) return;
        try { Clipboard.SetText(url); ShowNotice("页面链接已复制", temporary: true); }
        catch (COMException) { ShowNotice("剪贴板暂时不可用，请稍后再试", temporary: true); }
    }

    void OpenInBrowser() => App.OpenExternal(SourceUri is { Scheme: "http" or "https" or "file" } url ? url : AppConfig.HomeUrl);

    void SetZoom(double value) => Web.ZoomFactor = Math.Round(Prefs.BoundedZoom(value) * 10) / 10;

    void ShowAbout()
    {
        var version = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "";
        Dialogs.Info(this, $"LinuxDo {version}", "为 Windows 打造的 LinuxDo 社区客户端\n原生导航 · 舒适阅读 · Fluent 设计\n\n独立客户端项目，与 linux.do 官方的关联关系未经确认。");
    }

    void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // WebView2 forwards accelerator keys here first; handling one stops the page and browser defaults.
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        const ModifierKeys ctrl = ModifierKeys.Control, ctrlShift = ModifierKeys.Control | ModifierKeys.Shift;
        Action? command = (Keyboard.Modifiers, key) switch
        {
            (ctrl, Key.N) => () => App.OpenWindow(AppConfig.Route("/latest"), from: this),
            (ctrl, Key.W) => Close,
            (ctrl, Key.L) => FocusSearch,
            (ctrl, Key.F) => ShowFind,
            (ModifierKeys.None, Key.F5) or (ctrl, Key.R) => Reload,
            (ctrl, Key.F5) or (ctrlShift, Key.R) => HardReload,
            (ModifierKeys.Alt, Key.Left) => GoBack,
            (ModifierKeys.Alt, Key.Right) => GoForward,
            (ModifierKeys.Alt, Key.Home) => GoHome,
            (ctrlShift, Key.S) => ToggleSidebar,
            (ctrl, Key.J) => ToggleDownloads,
            (ctrl, Key.OemComma) => () => SettingsWindow.ShowShared(),
            _ => null,
        };
        if (command is null) return;
        e.Handled = true;
        command();
    }

    // MARK: Notices and progress

    public void ShowNotice(string text, string? actionTitle = null, Action? action = null, bool temporary = false)
    {
        var sequence = ++noticeGeneration;
        NoticeText.Text = text; NoticeText.ToolTip = text;
        NoticeAction.Visibility = actionTitle is null ? Visibility.Collapsed : Visibility.Visible;
        NoticeAction.Content = actionTitle;
        pendingNoticeAction = action;
        Notice.Visibility = Visibility.Visible;
        if (temporary) _ = DismissLater(sequence);
    }
    async Task DismissLater(int sequence)
    {
        await Task.Delay(2500);
        if (sequence == noticeGeneration) DismissNotice();
    }
    void DismissNotice() { noticeGeneration++; Notice.Visibility = Visibility.Collapsed; pendingNoticeAction = null; }

    void ShowError(string message)
    {
        FinishProgress();
        ErrorMessage.Text = message;
        ErrorOverlay.Visibility = Visibility.Visible;
        Web.Visibility = Visibility.Hidden;
    }
    void HideError()
    {
        ErrorOverlay.Visibility = Visibility.Collapsed;
        Web.Visibility = Visibility.Visible;
    }

    void StartProgress()
    {
        IsLoading = true;
        ProgressScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        AnimateProgress(0.25, 1);
    }
    async void FinishProgress()
    {
        IsLoading = false;
        var generation = AnimateProgress(1, 1);
        await Task.Delay(220);
        if (generation == progressGeneration) AnimateProgress(1, 0);
    }
    int AnimateProgress(double value, double opacity)
    {
        var duration = SystemParameters.ClientAreaAnimation ? TimeSpan.FromMilliseconds(180) : TimeSpan.Zero;
        ProgressScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(value, duration));
        ProgressLine.BeginAnimation(OpacityProperty, new DoubleAnimation(opacity, duration));
        return ++progressGeneration;
    }

    // MARK: Events

    void OnSidebarClick(object sender, RoutedEventArgs e) => ToggleSidebar();
    void OnBackClick(object sender, RoutedEventArgs e) => GoBack();
    void OnForwardClick(object sender, RoutedEventArgs e) => GoForward();
    void OnReloadClick(object sender, RoutedEventArgs e) => Reload();
    void OnDownloadsClick(object sender, RoutedEventArgs e) => ToggleDownloads();
    void OnCopyLinkClick(object sender, RoutedEventArgs e) => CopyPageUrl();
    void OnSettingsClick(object sender, RoutedEventArgs e) => SettingsWindow.ShowShared();
    void OnNoticeDismiss(object sender, RoutedEventArgs e) => DismissNotice();
    void OnNoticeAction(object sender, RoutedEventArgs e) { var action = pendingNoticeAction; DismissNotice(); action?.Invoke(); }

    void OnMoreClick(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = MoreButton, Placement = PlacementMode.Bottom };
        void Add(string header, string gesture, Action action, bool enabled = true)
        {
            var item = new MenuItem { Header = header, InputGestureText = gesture, IsEnabled = enabled };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }
        Add("新建窗口", "Ctrl+N", () => App.OpenWindow(AppConfig.Route("/latest"), from: this));
        Add("首页", "Alt+Home", GoHome);
        Add("页内查找…", "Ctrl+F", ShowFind);
        menu.Items.Add(new Separator());
        Add("复制页面链接", "", CopyPageUrl);
        Add("在浏览器中打开", "", OpenInBrowser);
        menu.Items.Add(new Separator());
        Add("放大", "Ctrl++", () => SetZoom(Web.ZoomFactor + 0.1));
        Add("缩小", "Ctrl+-", () => SetZoom(Web.ZoomFactor - 0.1));
        Add("实际大小", "Ctrl+0", () => SetZoom(1), Math.Abs(Web.ZoomFactor - 1) > 0.001);
        menu.Items.Add(new Separator());
        Add("设置…", "Ctrl+,", () => SettingsWindow.ShowShared());
        Add("关于 LinuxDo", "", ShowAbout);
        menu.IsOpen = true;
    }

    void OnSidebarSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (syncingSidebar || SidebarList.SelectedItem is not ListBoxItem { Tag: string path }) return;
        Navigate(AppConfig.Route(path));
    }

    void OnSplitterDragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (!sidebarVisible) return;
        Prefs.Current.SidebarWidth = SidebarColumn.ActualWidth;
        Prefs.Save();
    }

    void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { e.Handled = true; SearchFor(SearchBox.Text); }
        else if (e.Key == Key.Escape) { e.Handled = true; Web.Focus(); }
    }

    void OnSearchTextChanged(object sender, TextChangedEventArgs e) =>
        SearchPlaceholder.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    protected override async void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel) return;
        CapturePosition();
        var downloading = App.ActiveDownloads > 0 && App.MainWindows.Count() == 1;
        if (closingApproved || (!draftPresent && !downloading)) { SaveBounds(); return; }
        e.Cancel = true;
        if (closePromptOpen) return;
        closePromptOpen = true;
        var hasDraft = draftPresent && await RefreshDraftState();
        var close = hasDraft
            ? Dialogs.Confirm(this, "关闭前保存回复？", "当前有未发送的回复。关闭后本应用不会保留编辑内容。", "继续编辑", "关闭窗口")
            : !downloading || Dialogs.Confirm(this, "下载仍在进行", "关闭最后一个窗口会中断进行中的下载。", "继续使用", "关闭窗口");
        closePromptOpen = false;
        if (close) { closingApproved = true; Close(); }
    }
}
