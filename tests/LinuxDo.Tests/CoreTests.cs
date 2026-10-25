using LinuxDo;

var failed = 0;
void Check(bool value, string name) { if (!value) { failed++; Console.WriteLine("FAIL " + name); } }

Check(AppConfig.IsInternal(new Uri("https://linux.do/t/1")), "linux.do");
Check(AppConfig.IsInternal(new Uri("https://cdn.linux.do/a")), "subdomain");
Check(!AppConfig.IsInternal(new Uri("https://evil-linux.do")), "suffix boundary");
Check(!AppConfig.IsInternal(new Uri("https://linux.do.evil")), "host boundary");
Check(!AppConfig.IsInternal(new Uri("javascript://linux.do")), "scheme");
Check(!AppConfig.IsInternal(null), "missing url");

var search = AppConfig.SearchUrl("玻璃 & mac");
Check(search.AbsolutePath == "/search", "search path");
Check(AppConfig.QueryValue(search, "q") == "玻璃 & mac", "search query");

Check(ProxyEndpoint.Parse("socks5://127.0.0.1:7890") == new ProxyEndpoint("127.0.0.1", 7890, true), "socks");
Check(ProxyEndpoint.Parse(" HTTP://proxy.local:8080/ ") == new ProxyEndpoint("proxy.local", 8080, false), "http proxy");
Check(ProxyEndpoint.Parse("http://user:pw@127.0.0.1:7890") == null, "proxy credentials");
Check(ProxyEndpoint.Parse("http://127.0.0.1:70000") == null, "proxy port");
Check(ProxyEndpoint.Parse("http://127.0.0.1") == null, "proxy port required");
Check(ProxyEndpoint.Parse("http://127.0.0.1:7890 --disable-web-security") == null, "proxy argument injection");
Check(ProxyEndpoint.Parse("socks5://[::1]:1080")?.BrowserArgument == "--proxy-server=socks5://[::1]:1080", "ipv6 proxy argument");

Check(DownloadNames.Safe("../../secret.txt") == "secret.txt", "download traversal");
Check(DownloadNames.Safe("..\\..\\a<b>.txt") == "ab.txt", "windows separators and invalid characters");
Check(DownloadNames.Safe("CON.txt") == "_CON.txt", "reserved device name");
Check(DownloadNames.Safe(" .. ") == "download", "empty name");

Check(PageTitle.Parse("(12+) 最新") == new PageTitle("最新", "12+"), "title badge");
Check(PageTitle.Parse("(0) 最新") == new PageTitle("最新", null), "zero badge");
Check(PageTitle.Parse("(公告) 最新").Text == "(公告) 最新", "non-numeric parentheses");
Check(PageTitle.Parse("  ") == new PageTitle(AppConfig.AppName, null), "empty title");

Check(SidebarRoute.IndexFor(new Uri("https://linux.do/")) == 1, "home selects latest");
Check(SidebarRoute.IndexFor(new Uri("https://linux.do/top?period=weekly")) == 2, "route ignores query");
Check(SidebarRoute.IndexFor(new Uri("https://linux.do/t/topic/1")) == -1, "topic has no sidebar route");

var directory = Path.Combine(Path.GetTempPath(), "LinuxDoTests." + Guid.NewGuid().ToString("N"));
var file = Path.Combine(directory, "reading.json");
var store = new ReadingStore(file);
store.Save(new("https://linux.do/t/1", 0, 900, "4", 20, 1));
store.Save(new("https://example.com", 0, 1, null, null, 2));
store.Save(new("https://linux.do/t/2", 0, double.NaN, null, null, 3));
Check(new ReadingStore(file).Position("https://linux.do/t/1")?.PostId == "4", "reading persistence");
Check(store.Positions.Count == 1, "reading validation");
for (var i = 0; i < 120; i++) store.Save(new($"https://linux.do/t/{i + 10}", 0, i, null, null, 10 + i));
Check(store.Positions.Count == 100 && store.Position("https://linux.do/t/1") == null, "reading store keeps newest 100");

var settings = Path.Combine(directory, "settings.json");
Prefs.Load(settings);
Check(Prefs.Current.SidebarCollapsed && Prefs.Current.FontSize == 16, "settings defaults");
Prefs.Current.FontSize = 19;
Prefs.Save(settings);
Prefs.Load(settings);
Check(Prefs.Current.FontSize == 19, "settings persistence");
File.WriteAllText(settings, "{ not json");
Prefs.Load(settings);
Check(Prefs.Current.FontSize == 16, "corrupt settings fall back to defaults");
Check(Prefs.BoundedFont(double.NaN) == 16 && Prefs.BoundedFont(40) == 22 && Prefs.BoundedLineHeight(1) == 1.4, "bounded reading values");
Directory.Delete(directory, true);

Console.WriteLine(failed == 0 ? "PASS core tests" : $"FAILED {failed}");
return failed == 0 ? 0 : 1;
