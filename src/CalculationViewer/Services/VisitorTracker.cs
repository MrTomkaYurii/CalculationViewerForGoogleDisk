using System.Net.Http.Json;
using System.Text.Json;
using CalculationViewer.Models;
using Microsoft.JSInterop;

namespace CalculationViewer.Services;

public sealed class VisitorTracker : IVisitorTracker
{
    private const string StorageKey = "cv_visitor_logs";
    private const int MaxLogsToKeep = 500;

    private readonly IJSRuntime _js;
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private List<VisitorLogEntry>? _cachedLogs;
    private IpWhoIsResponse? _cachedGeo;
    private ClientInfoDto? _cachedClient;
    private DateTime _lastTrackedUtc = DateTime.MinValue;
    private string? _lastTrackedPage;

    public event Action? Changed;

    public VisitorTracker(IJSRuntime js, HttpClient? http = null)
    {
        _js = js;
        _http = http ?? new HttpClient();
    }

    public async Task<IReadOnlyList<VisitorLogEntry>> GetLogsAsync(CancellationToken ct = default)
    {
        if (_cachedLogs is not null) return _cachedLogs;

        await _gate.WaitAsync(ct);
        try
        {
            if (_cachedLogs is not null) return _cachedLogs;

            List<VisitorLogEntry>? loaded = null;
            try
            {
                var json = await _js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    loaded = JsonSerializer.Deserialize<List<VisitorLogEntry>>(json);
                }
            }
            catch
            {
                // localStorage недоступний або restricted
            }

            if (loaded is null || loaded.Count == 0)
            {
                loaded = CreateSeedLogs();
                await SaveToStorageAsync(loaded, ct);
            }

            _cachedLogs = loaded.OrderByDescending(x => x.TimestampUtc).ToList();
            return _cachedLogs;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task TrackVisitAsync(string? pagePath = null, CancellationToken ct = default)
    {
        var friendlyPage = FormatPageTitle(pagePath);

        // Дедуплікація повторних однакових заходів протягом 5 хвилин
        if (_lastTrackedPage == friendlyPage && (DateTime.UtcNow - _lastTrackedUtc) < TimeSpan.FromMinutes(5))
        {
            return;
        }

        try
        {
            // 1. Інформація про браузер і екран
            if (_cachedClient is null)
            {
                try
                {
                    _cachedClient = await _js.InvokeAsync<ClientInfoDto>("cvGetClientInfo");
                }
                catch
                {
                    _cachedClient = new ClientInfoDto();
                }
            }

            // 2. Геолокація та IP-адреса клієнта
            if (_cachedGeo is null)
            {
                _cachedGeo = await FetchGeoAsync(ct);
            }

            var ip = !string.IsNullOrWhiteSpace(_cachedGeo?.Ip) ? _cachedGeo.Ip : "127.0.0.1";
            var country = _cachedGeo?.Country ?? "Україна";
            var countryCode = _cachedGeo?.CountryCode ?? "UA";
            var city = _cachedGeo?.City;
            var region = _cachedGeo?.Region;
            var flagEmoji = _cachedGeo?.Flag?.Emoji ?? "🇺🇦";
            var isp = _cachedGeo?.Connection?.Isp;
            var userAgent = _cachedClient?.UserAgent;
            var device = FormatDevice(userAgent);

            var entry = new VisitorLogEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                Ip = ip,
                Country = country,
                CountryCode = countryCode,
                City = city,
                Region = region,
                FlagEmoji = flagEmoji,
                Isp = isp,
                TimestampUtc = DateTime.UtcNow,
                Page = friendlyPage,
                UserAgent = userAgent,
                Device = device
            };

            await _gate.WaitAsync(ct);
            try
            {
                var logs = (await GetLogsAsync(ct)).ToList();

                // Перевіряємо, чи немає вже такого ж запису за останні 5 хвилин
                var recentSame = logs.FirstOrDefault(x => x.Ip == ip && x.Page == friendlyPage && (DateTime.UtcNow - x.TimestampUtc) < TimeSpan.FromMinutes(5));
                if (recentSame is null)
                {
                    logs.Insert(0, entry);
                    if (logs.Count > MaxLogsToKeep)
                    {
                        logs = logs.Take(MaxLogsToKeep).ToList();
                    }
                    _cachedLogs = logs;
                    await SaveToStorageAsync(logs, ct);

                    _lastTrackedUtc = DateTime.UtcNow;
                    _lastTrackedPage = friendlyPage;

                    Changed?.Invoke();
                }
            }
            finally
            {
                _gate.Release();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[VisitorTracker] Track error: {ex.Message}");
        }
    }

    public async Task ClearLogsAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            _cachedLogs = [];
            try
            {
                await _js.InvokeVoidAsync("localStorage.removeItem", StorageKey);
            }
            catch { }
            Changed?.Invoke();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task SaveToStorageAsync(List<VisitorLogEntry> logs, CancellationToken ct)
    {
        try
        {
            var json = JsonSerializer.Serialize(logs);
            await _js.InvokeVoidAsync("localStorage.setItem", StorageKey, json);
        }
        catch { }
    }

    private async Task<IpWhoIsResponse?> FetchGeoAsync(CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(4));

            var res = await _http.GetFromJsonAsync<IpWhoIsResponse>("https://ipwho.is/", cts.Token);
            if (res is not null && res.Success && !string.IsNullOrWhiteSpace(res.Ip))
            {
                return res;
            }
        }
        catch
        {
            // Спробуємо запасний сервіс
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(3));

            var res = await _http.GetFromJsonAsync<IpifyResponse>("https://api.ipify.org?format=json", cts.Token);
            if (res is not null && !string.IsNullOrWhiteSpace(res.Ip))
            {
                return new IpWhoIsResponse
                {
                    Ip = res.Ip,
                    Success = true,
                    Country = "Україна",
                    CountryCode = "UA",
                    Flag = new IpWhoIsFlag { Emoji = "🇺🇦" }
                };
            }
        }
        catch { }

        return null;
    }

    private static string FormatPageTitle(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "Головна сторінка";
        var uri = url;
        if (Uri.TryCreate(url, UriKind.Absolute, out var parsed))
        {
            uri = parsed.PathAndQuery.TrimStart('/');
        }
        else
        {
            uri = uri.TrimStart('/');
        }

        if (string.IsNullOrEmpty(uri)) return "Головна сторінка";
        if (uri.StartsWith("admin/visitors", StringComparison.OrdinalIgnoreCase)) return "Адмін: Відвідувачі";
        if (uri.StartsWith("admin/folders", StringComparison.OrdinalIgnoreCase)) return "Адмін: Папки";
        if (uri.StartsWith("admin/collections", StringComparison.OrdinalIgnoreCase)) return "Адмін: Добірки";
        if (uri.StartsWith("admin", StringComparison.OrdinalIgnoreCase)) return "Адмін-панель";
        if (uri.StartsWith("collections", StringComparison.OrdinalIgnoreCase)) return "Добірки";
        if (uri.StartsWith("folder/", StringComparison.OrdinalIgnoreCase)) return "Перегляд папки";
        return uri;
    }

    public static string FormatDevice(string? ua)
    {
        if (string.IsNullOrWhiteSpace(ua)) return "Невідомий пристрій";

        string os;
        if (ua.Contains("Windows NT 10.0") || ua.Contains("Windows NT 11.0")) os = "Windows";
        else if (ua.Contains("Mac OS X") && !ua.Contains("iPhone") && !ua.Contains("iPad")) os = "macOS";
        else if (ua.Contains("iPhone")) os = "iPhone (iOS)";
        else if (ua.Contains("iPad")) os = "iPad (iPadOS)";
        else if (ua.Contains("Android")) os = "Android";
        else if (ua.Contains("Linux")) os = "Linux";
        else os = "Десктоп/Мобільний";

        string browser;
        if (ua.Contains("Edg/")) browser = "Edge";
        else if (ua.Contains("Chrome/") && !ua.Contains("Edg/")) browser = "Chrome";
        else if (ua.Contains("Safari/") && !ua.Contains("Chrome/")) browser = "Safari";
        else if (ua.Contains("Firefox/")) browser = "Firefox";
        else if (ua.Contains("OPR/") || ua.Contains("Opera/")) browser = "Opera";
        else browser = "Браузер";

        return $"{os} · {browser}";
    }

    private static List<VisitorLogEntry> CreateSeedLogs()
    {
        var now = DateTime.UtcNow;
        return
        [
            new VisitorLogEntry
            {
                Ip = "178.94.120.45",
                Country = "Ukraine",
                CountryCode = "UA",
                City = "Київ",
                Region = "Київська область",
                FlagEmoji = "🇺🇦",
                Isp = "Kyivstar",
                TimestampUtc = now.AddMinutes(-14),
                Page = "Головна сторінка",
                Device = "Windows · Chrome",
                UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36"
            },
            new VisitorLogEntry
            {
                Ip = "91.222.248.12",
                Country = "Ukraine",
                CountryCode = "UA",
                City = "Львів",
                Region = "Львівська область",
                FlagEmoji = "🇺🇦",
                Isp = "Ukrtelecom",
                TimestampUtc = now.AddHours(-1).AddMinutes(-22),
                Page = "Перегляд папки (2026 рік)",
                Device = "iPhone (iOS) · Safari",
                UserAgent = "Mozilla/5.0 (iPhone; CPU iPhone OS 17_5 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.5 Mobile/15E148 Safari/604.1"
            },
            new VisitorLogEntry
            {
                Ip = "185.152.65.18",
                Country = "Poland",
                CountryCode = "PL",
                City = "Варшава",
                Region = "Мазовецьке воєводство",
                FlagEmoji = "🇵🇱",
                Isp = "Orange Polska",
                TimestampUtc = now.AddHours(-3).AddMinutes(-40),
                Page = "Добірки",
                Device = "macOS · Safari",
                UserAgent = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.4 Safari/605.1.15"
            },
            new VisitorLogEntry
            {
                Ip = "84.116.142.33",
                Country = "Germany",
                CountryCode = "DE",
                City = "Берлін",
                Region = "Берлін",
                FlagEmoji = "🇩🇪",
                Isp = "Deutsche Telekom",
                TimestampUtc = now.AddHours(-7).AddMinutes(-5),
                Page = "Головна сторінка",
                Device = "Windows · Firefox",
                UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:129.0) Gecko/20100101 Firefox/129.0"
            },
            new VisitorLogEntry
            {
                Ip = "142.250.180.206",
                Country = "United States",
                CountryCode = "US",
                City = "Маунтін-В'ю",
                Region = "Каліфорнія",
                FlagEmoji = "🇺🇸",
                Isp = "Google LLC",
                TimestampUtc = now.AddDays(-1).AddHours(-2),
                Page = "Головна сторінка",
                Device = "Android · Chrome",
                UserAgent = "Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Mobile Safari/537.36"
            }
        ];
    }
}
