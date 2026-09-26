using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.RegularExpressions;
using CalculationViewer.Models;

namespace CalculationViewer.Services.Google;

/// <summary>
/// Читає публічні папки Google Drive через Drive API v3 з API-ключем (без входу, без сервера).
/// Папки мають бути відкриті для всіх, хто має посилання. Ключ обмежують за HTTP-referrer і лише на Drive API.
/// </summary>
internal sealed partial class GoogleDriveBrowser(HttpClient http, string apiKey, IFolderCatalog catalog, string? apiBase = null, string? thumbnailBase = null)
    : IDriveBrowser
{
    const string FolderMime = "application/vnd.google-apps.folder";
    const string ShortcutMime = "application/vnd.google-apps.shortcut";
    const int TextLimit = 200_000;
    const int MaxPages = 20;

    readonly string _api = (apiBase ?? "https://www.googleapis.com/drive/v3/").TrimEnd('/') + "/";
    readonly string _thumbnail = thumbnailBase ?? "https://drive.google.com/thumbnail";

    sealed record FileDto(string Id, string Name, string MimeType, string? Size, DateTime? ModifiedTime, List<string>? Parents);
    sealed record ListDto(List<FileDto>? Files, string? NextPageToken);
    sealed record ErrorDto(ErrorBody? Error);
    sealed record ErrorBody(List<ErrorItem>? Errors);
    sealed record ErrorItem(string? Reason);

    /// <summary>Назва й батько кожної папки, яку ми вже бачили: з цього збирається шлях без зайвих запитів.</summary>
    readonly ConcurrentDictionary<string, (string Name, string? ParentId)> _meta = new();
    readonly ConcurrentDictionary<string, DriveListing> _listings = new();

    [GeneratedRegex(@"^[\w-]{10,}$")]
    private static partial Regex IdPattern();

    [GeneratedRegex(@"folders/([\w-]{10,})")]
    private static partial Regex FolderLinkPattern();

    // ---------- запити ----------

    async Task<T> GetAsync<T>(string path, string query, CancellationToken ct)
    {
        var url = $"{_api}{path}?{query}&key={Uri.EscapeDataString(apiKey)}";
        HttpResponseMessage res;
        try
        {
            res = await http.GetAsync(url, ct);
        }
        catch (HttpRequestException e)
        {
            Console.WriteLine($"[GoogleDriveBrowser] Немає з'єднання з Google Drive: {e.Message}");
            throw new DriveAccessException("Не вдалося з’єднатися з Google Drive. Перевірте інтернет і спробуйте ще раз.");
        }

        using (res)
        {
            if (res.IsSuccessStatusCode)
                return (await res.Content.ReadFromJsonAsync<T>(cancellationToken: ct))!;

            var reason = await ReadReasonAsync(res, ct);
            Console.WriteLine($"[GoogleDriveBrowser] {(int)res.StatusCode} {reason} для {path}");
            throw new DriveAccessException(Explain(res.StatusCode, reason));
        }
    }

    static async Task<string?> ReadReasonAsync(HttpResponseMessage res, CancellationToken ct)
    {
        try { return (await res.Content.ReadFromJsonAsync<ErrorDto>(cancellationToken: ct))?.Error?.Errors?.FirstOrDefault()?.Reason; }
        catch { return null; }
    }

    static string Explain(HttpStatusCode status, string? reason) => (status, reason) switch
    {
        (HttpStatusCode.NotFound, _) =>
            "Папку не знайдено або вона закрита. У Google Drive відкрийте доступ «Усі, хто має посилання».",
        (HttpStatusCode.Forbidden, "rateLimitExceeded" or "userRateLimitExceeded" or "dailyLimitExceeded" or "quotaExceeded") =>
            "Google Drive тимчасово обмежив кількість запитів. Спробуйте за кілька хвилин.",
        (HttpStatusCode.Forbidden, "forbidden" or "insufficientFilePermissions") =>
            "Папка закрита. У Google Drive відкрийте доступ «Усі, хто має посилання».",
        (HttpStatusCode.Forbidden or HttpStatusCode.BadRequest, _) =>
            "Google Drive відхилив запит. Найімовірніше, ключ API не дозволяє доступ з цього сайту або Drive API не увімкнено.",
        _ => "Google Drive повернув помилку. Спробуйте пізніше.",
    };

    // ---------- вміст папки ----------

    async Task<DriveFolder> GetFolderMetaAsync(string id, CancellationToken ct)
    {
        if (!IdPattern().IsMatch(id)) throw new DriveAccessException("Папку не знайдено.");
        if (_meta.TryGetValue(id, out var known)) return new DriveFolder(id, known.Name, known.ParentId, null);

        var f = await GetAsync<FileDto>($"files/{id}", "supportsAllDrives=true&fields=id,name,mimeType,modifiedTime", ct);
        if (f.MimeType != FolderMime) throw new DriveAccessException("Це не папка.");
        _meta[id] = (f.Name, null);
        return new DriveFolder(id, f.Name, null, f.ModifiedTime);
    }

    public async Task<DriveListing> GetListingAsync(string folderId, CancellationToken ct = default)
    {
        if (_listings.TryGetValue(folderId, out var cached)) return cached;

        var self = await GetFolderMetaAsync(folderId, ct);
        var folders = new List<DriveFolder>();
        var files = new List<DriveFile>();

        string? pageToken = null;
        var pages = 0;
        do
        {
            var query = "q=" + Uri.EscapeDataString($"'{folderId}' in parents and trashed = false")
                        + "&pageSize=1000&supportsAllDrives=true&includeItemsFromAllDrives=true"
                        + "&fields=" + Uri.EscapeDataString("nextPageToken,files(id,name,mimeType,size,modifiedTime)")
                        + (pageToken is null ? "" : "&pageToken=" + Uri.EscapeDataString(pageToken));
            var page = await GetAsync<ListDto>("files", query, ct);

            foreach (var f in page.Files ?? [])
            {
                if (f.MimeType == FolderMime)
                {
                    folders.Add(new DriveFolder(f.Id, f.Name, folderId, f.ModifiedTime));
                    _meta[f.Id] = (f.Name, folderId);
                }
                else if (f.MimeType != ShortcutMime)
                {
                    long.TryParse(f.Size, out var size);
                    files.Add(new DriveFile(f.Id, f.Name, folderId, KindOf(f.MimeType, f.Name), size,
                        f.ModifiedTime ?? DateTime.MinValue, f.MimeType));
                }
            }

            pageToken = page.NextPageToken;
        } while (pageToken is not null && ++pages < MaxPages);

        var listing = new DriveListing(self, folders, files);
        _listings[folderId] = listing;
        return listing;
    }

    static FileKind KindOf(string mime, string name) => mime switch
    {
        "image/png" or "image/jpeg" or "image/gif" or "image/webp" or "image/bmp" or "image/svg+xml" or "image/avif" => FileKind.Image,
        "application/pdf" => FileKind.Pdf,
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document" or "application/msword"
            or "application/vnd.google-apps.document" or "application/vnd.google-apps.spreadsheet"
            or "application/vnd.google-apps.presentation" => FileKind.Docx,
        "text/plain" or "text/csv" or "text/markdown" => FileKind.Text,
        // Файли без чіткого типу (application/octet-stream) визначаємо за розширенням: .mat лишається службовим.
        _ => FileKinds.FromName(name),
    };

    // ---------- шлях ----------

    /// <summary>
    /// Google Drive не віддає поле parents анонімним запитам, тому шлях збирається з адреси сторінки (ланцюжок id),
    /// а від Drive потрібні лише назви папок. Перша ланка має бути підключеною папкою з каталогу, її назва береться з каталогу.
    /// </summary>
    public async Task<IReadOnlyList<PathSegment>> GetPathAsync(IReadOnlyList<string> idChain, CancellationToken ct = default)
    {
        if (idChain.Count == 0) return [];
        var roots = (await catalog.GetAllAsync(ct)).ToDictionary(r => r.Id, r => r.Title);
        if (!roots.TryGetValue(idChain[0], out var rootTitle)) return [];

        // Назви решти папок беруться паралельно; ті, що вже бачили у списках, повертаються з кешу без запитів.
        string[] names;
        try { names = await Task.WhenAll(idChain.Skip(1).Select(id => NameOfAsync(id, ct))); }
        catch (DriveAccessException) { return []; }

        var segments = new List<PathSegment>(idChain.Count) { new(idChain[0], rootTitle) };
        for (var i = 1; i < idChain.Count; i++) segments.Add(new PathSegment(idChain[i], names[i - 1]));
        return segments;
    }

    async Task<string> NameOfAsync(string id, CancellationToken ct) => (await GetFolderMetaAsync(id, ct)).Name;

    // ---------- перевірка посилання (форма адміна) ----------

    public async Task<FolderCheckResult> CheckFolderAsync(string linkOrPath, CancellationToken ct = default)
    {
        var text = (linkOrPath ?? "").Trim();
        var match = FolderLinkPattern().Match(text);
        var id = match.Success ? match.Groups[1].Value : IdPattern().IsMatch(text) ? text : null;
        if (id is null) return new(FolderCheckStatus.Invalid);

        try
        {
            var f = await GetAsync<FileDto>($"files/{id}", "supportsAllDrives=true&fields=id,name,mimeType", ct);
            return f.MimeType == FolderMime ? new(FolderCheckStatus.Ok, id, f.Name) : new(FolderCheckStatus.Invalid, id, f.Name);
        }
        catch (DriveAccessException e)
        {
            // Explain() для 404 і 403 однаково радить відкрити доступ; тут розрізняємо «немає» і «закрито» за текстом.
            return new(e.Message.Contains("не знайдено") ? FolderCheckStatus.NotFound : FolderCheckStatus.NotPublic, id);
        }
    }

    // ---------- адреси файлів ----------

    public string GetThumbnailUrl(string fileId, int size = 400) => $"{_thumbnail}?id={Uri.EscapeDataString(fileId)}&sz=w{size}";

    public string GetImageUrl(string fileId) => GetContentUrl(fileId);

    public string GetContentUrl(string fileId)
        => $"{_api}files/{Uri.EscapeDataString(fileId)}?alt=media&supportsAllDrives=true&key={Uri.EscapeDataString(apiKey)}";

    /// <summary>Перегляд у iframe самого Google: працює для PDF, Word і документів Google. Текст показуємо власним переглядачем.</summary>
    public string? GetPreviewUrl(DriveFile file) => (file.MimeType, file.Kind) switch
    {
        ("application/vnd.google-apps.document", _) => $"https://docs.google.com/document/d/{file.Id}/preview",
        ("application/vnd.google-apps.spreadsheet", _) => $"https://docs.google.com/spreadsheets/d/{file.Id}/preview",
        ("application/vnd.google-apps.presentation", _) => $"https://docs.google.com/presentation/d/{file.Id}/preview",
        (_, FileKind.Pdf or FileKind.Docx) => $"https://drive.google.com/file/d/{file.Id}/preview",
        _ => null,
    };

    public async Task<TextPreview?> GetTextPreviewAsync(DriveFile file, CancellationToken ct = default)
    {
        if (file.Kind != FileKind.Text) return null;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, GetContentUrl(file.Id));
            request.Headers.Range = new RangeHeaderValue(0, TextLimit - 1);
            using var res = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!res.IsSuccessStatusCode) return null;

            await using var stream = await res.Content.ReadAsStreamAsync(ct);
            var buffer = new byte[TextLimit];
            var total = 0;
            int read;
            while (total < TextLimit && (read = await stream.ReadAsync(buffer.AsMemory(total, TextLimit - total), ct)) > 0) total += read;
            return new TextPreview(Encoding.UTF8.GetString(buffer, 0, total), file.SizeBytes > total);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public string GetOpenUrl(string fileId) => $"https://drive.google.com/file/d/{Uri.EscapeDataString(fileId)}/view";

    public string GetDownloadUrl(string fileId) => $"https://drive.google.com/uc?export=download&id={Uri.EscapeDataString(fileId)}";
}
