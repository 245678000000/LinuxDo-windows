using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace LinuxDo;

// Keep the Pake identifier: its WebView2 profile, and the login inside it, lives in this folder.
public static class AppConfig
{
    public const string AppName = "LinuxDo";
    public const string Identifier = "com.pake.acea3c3";
    public const string DefaultProxy = "http://127.0.0.1:7890";
    public static readonly Uri HomeUrl = new("https://linux.do/");
    public static bool Preview { get; set; }
    public static string PreviewPath => Path.Combine(AppContext.BaseDirectory, "Resources", "Preview.html");
    public static Uri PreviewUrl => new(PreviewPath);
    public static string DataDirectory => Preview
        ? Path.Combine(Path.GetTempPath(), "LinuxDo-preview")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Identifier);

    public static bool IsInternal(Uri? url)
    {
        if (url is not { IsAbsoluteUri: true } || (url.Scheme != Uri.UriSchemeHttps && url.Scheme != Uri.UriSchemeHttp)) return false;
        var host = url.Host.ToLowerInvariant();
        return host == "linux.do" || host.EndsWith(".linux.do", StringComparison.Ordinal);
    }
    public static bool IsPreview(Uri? url) =>
        Preview && url is { IsAbsoluteUri: true, IsFile: true } &&
        string.Equals(Path.GetFullPath(url.LocalPath), Path.GetFullPath(PreviewPath), StringComparison.OrdinalIgnoreCase);
    public static bool IsAllowed(Uri? url) => IsInternal(url) || IsPreview(url);
    public static Uri? TryParse(string? text) => Uri.TryCreate(text, UriKind.Absolute, out var url) ? url : null;

    public static Uri Route(string path) => Preview
        ? new UriBuilder(PreviewUrl) { Query = "route=" + Uri.EscapeDataString(path) }.Uri
        : new Uri(HomeUrl, path);
    public static Uri SearchUrl(string query)
    {
        var builder = new UriBuilder(Route("/search"));
        var existing = builder.Query.TrimStart('?');
        builder.Query = (existing.Length > 0 ? existing + "&" : "") + "q=" + Uri.EscapeDataString(query);
        return builder.Uri;
    }
    public static string? QueryValue(Uri url, string name)
    {
        foreach (var pair in url.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (Uri.UnescapeDataString(parts[0]) != name) continue;
            return parts.Length > 1 ? Uri.UnescapeDataString(parts[1].Replace('+', ' ')) : "";
        }
        return null;
    }
}

public sealed record WindowBounds(double Left, double Top, double Width, double Height, bool Maximized);

public sealed class Settings
{
    public string Appearance { get; set; } = "system";
    public bool Reading { get; set; } = true;
    public double FontSize { get; set; } = 16;
    public double LineHeight { get; set; } = 1.75;
    public bool HideWebSidebar { get; set; } = true;
    public bool SidebarCollapsed { get; set; } = true;
    public double SidebarWidth { get; set; } = 248;
    public bool RestoreReading { get; set; } = true;
    public bool AskDownloadLocation { get; set; }
    public bool ProxyEnabled { get; set; }
    public string ProxyUrl { get; set; } = AppConfig.DefaultProxy;
    public double PageZoom { get; set; } = 1;
    public string? LastUrl { get; set; }
    public WindowBounds? Window { get; set; }
}

public static class Prefs
{
    public const string PositionsCleared = "positionsCleared";
    public static string FilePath => Path.Combine(AppConfig.DataDirectory, "LinuxDo.settings.json");
    public static Settings Current { get; private set; } = new();
    /// Raised after a user-facing preference changes. Window state and the last URL save quietly.
    public static event Action<string?>? Changed;

    public static void Load(string? path = null)
    {
        try { Current = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path ?? FilePath)) ?? new(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { Current = new(); }
    }
    public static void Save(string? path = null) => Storage.WriteJson(path ?? FilePath, Current);
    public static void Update(Action<Settings> change, string? reason = null)
    {
        change(Current); Save(); Changed?.Invoke(reason);
    }
    public static double BoundedFont(double value) => double.IsFinite(value) ? Math.Clamp(value, 13, 22) : 16;
    public static double BoundedLineHeight(double value) => double.IsFinite(value) ? Math.Clamp(value, 1.4, 2) : 1.75;
    public static double BoundedZoom(double value) => double.IsFinite(value) ? Math.Clamp(value, 0.5, 3) : 1;
}

static class Storage
{
    /// Writes through a temporary file so a crash never leaves half a settings file behind.
    public static void WriteJson<T>(string path, T value)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}

public sealed record ProxyEndpoint(string Host, int Port, bool Socks)
{
    static readonly Regex Pattern = new(@"^(?<scheme>http|socks5)://(?<host>\[[0-9a-f:.]+\]|[a-z0-9](?:[a-z0-9.-]*[a-z0-9])?):(?<port>[0-9]{1,5})/?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    public static ProxyEndpoint? Parse(string? text)
    {
        var match = Pattern.Match((text ?? "").Trim());
        if (!match.Success || !int.TryParse(match.Groups["port"].Value, out var port) || port is < 1 or > 65535) return null;
        return new(match.Groups["host"].Value, port, match.Groups["scheme"].Value.Equals("socks5", StringComparison.OrdinalIgnoreCase));
    }
    public string BrowserArgument => $"--proxy-server={(Socks ? "socks5" : "http")}://{Host}:{Port}";
}

public sealed record PageTitle(string Text, string? Badge)
{
    static readonly Regex Count = new(@"^\((?<count>[0-9]+\+?)\)\s*(?<rest>.*)$", RegexOptions.Singleline);
    public static PageTitle Parse(string? raw)
    {
        var title = (raw ?? "").Trim();
        string? badge = null;
        var match = Count.Match(title);
        if (match.Success)
        {
            badge = match.Groups["count"].Value == "0" ? null : match.Groups["count"].Value;
            title = match.Groups["rest"].Value.Trim();
        }
        return new(title.Length == 0 ? AppConfig.AppName : title, badge);
    }
}

public sealed record SidebarRoute(string Title, string Glyph, string? Path)
{
    public static readonly SidebarRoute[] Items =
    [
        new("社区", "", null),
        new("最新", "", "/latest"),
        new("热门", "", "/top"),
        new("分类", "", "/categories"),
        new("我的空间", "", null),
        new("未读", "", "/unread"),
        new("新话题", "", "/new"),
        new("收藏", "", "/my/bookmarks"),
        new("通知", "", "/my/notifications"),
        new("我的动态", "", "/my/activity"),
    ];
    public static int IndexFor(Uri? url)
    {
        if (url is null) return -1;
        var path = AppConfig.IsPreview(url) ? AppConfig.QueryValue(url, "route") ?? "/latest" : url.AbsolutePath;
        if (path == "/") path = "/latest";
        return Array.FindIndex(Items, item => item.Path == path);
    }
}

public sealed record ReadingPosition(string Url, double X, double Y, string? PostId, double? Offset, double Updated)
{
    [JsonIgnore] public bool IsValid => double.IsFinite(X) && double.IsFinite(Y) && X >= 0 && Y >= 0 && (Offset is not { } o || double.IsFinite(o));
}

/// Keyed by the page's own location.href so restores match what the bridge reports.
public sealed class ReadingStore
{
    static ReadingStore? shared;
    public static ReadingStore Shared => shared ??= new(Path.Combine(AppConfig.DataDirectory, "LinuxDo.reading.json"));
    readonly string path;
    public Dictionary<string, ReadingPosition> Positions { get; private set; }
    public ReadingStore(string path)
    {
        this.path = path;
        try { Positions = JsonSerializer.Deserialize<Dictionary<string, ReadingPosition>>(File.ReadAllText(path)) ?? []; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { Positions = []; }
    }
    public void Save(ReadingPosition position)
    {
        if (!position.IsValid || !AppConfig.IsAllowed(AppConfig.TryParse(position.Url))) return;
        Positions[position.Url] = position;
        if (Positions.Count > 100) Positions = Positions.Values.OrderByDescending(p => p.Updated).Take(100).ToDictionary(p => p.Url);
        Storage.WriteJson(path, Positions);
    }
    public ReadingPosition? Position(string? url) => url is null ? null : Positions.GetValueOrDefault(url);
    public void Clear()
    {
        Positions = [];
        try { File.Delete(path); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}

public static class DownloadNames
{
    static readonly char[] Invalid = "<>:\"/\\|?*".ToCharArray();
    static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };
    public static string Safe(string? suggested)
    {
        var name = (suggested ?? "").Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..];
        name = new string(name.Where(c => !char.IsControl(c) && Array.IndexOf(Invalid, c) < 0).ToArray()).Trim().TrimEnd('.', ' ');
        if (name.Length == 0) return "download";
        if (Reserved.Contains(name.Split('.')[0])) name = "_" + name;
        return name.Length > 200 ? name[..200] : name;
    }
}
