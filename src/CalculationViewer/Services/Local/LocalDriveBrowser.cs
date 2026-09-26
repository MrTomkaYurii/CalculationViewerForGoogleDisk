using System.Collections.Concurrent;
using System.Net.Http.Json;
using CalculationViewer.Models;

namespace CalculationViewer.Services.Local;

/// <summary>
/// Читає реальну папку на диску через DevDriveServer. Замінник Google Drive на час розробки інтерфейсу:
/// та сама форма даних, що дасть Drive API (папка → прямі підпапки й файли, шлях, вміст файлу).
/// </summary>
internal sealed class LocalDriveBrowser(HttpClient http) : IDriveBrowser
{
    sealed record FolderDto(string Id, string Name, DateTime ModifiedUtc);
    sealed record FileDto(string Id, string Name, long Size, DateTime ModifiedUtc);
    sealed record ListingDto(string Id, string Name, string? ParentId, List<FolderDto> Folders, List<FileDto> Files);
    sealed record SegmentDto(string Id, string Name);
    sealed record TextDto(string Text, bool Truncated, long Size);
    sealed record ResolveDto(string Id, string Name);

    readonly ConcurrentDictionary<string, ListingDto> _cache = new();

    public async Task<DriveListing> GetListingAsync(string folderId, CancellationToken ct = default)
    {
        if (!_cache.TryGetValue(folderId, out var dto))
        {
            try
            {
                dto = await http.GetFromJsonAsync<ListingDto>($"api/folder/{Uri.EscapeDataString(folderId)}", ct)
                      ?? throw new DriveAccessException("Порожня відповідь від джерела.");
            }
            catch (HttpRequestException e)
            {
                if (e.StatusCode != System.Net.HttpStatusCode.NotFound)
                    Console.WriteLine($"[LocalDriveBrowser] Джерело даних недоступне (запущено DevDriveServer?): {e.Message}");
                throw new DriveAccessException(e.StatusCode == System.Net.HttpStatusCode.NotFound
                    ? "Папку не знайдено."
                    : "Джерело даних тимчасово недоступне. Спробуйте пізніше.");
            }
            _cache[folderId] = dto;
        }

        var folder = new DriveFolder(dto.Id, dto.Name, dto.ParentId, null);
        var folders = dto.Folders.Select(f => new DriveFolder(f.Id, f.Name, folderId, f.ModifiedUtc)).ToList();
        var files = dto.Files.Select(f => new DriveFile(f.Id, f.Name, folderId, FileKinds.FromName(f.Name), f.Size, f.ModifiedUtc)).ToList();
        return new DriveListing(folder, folders, files);
    }

    public async Task<IReadOnlyList<PathSegment>> GetPathAsync(IReadOnlyList<string> idChain, CancellationToken ct = default)
    {
        if (idChain.Count == 0) return [];
        try
        {
            // Локальний сервер сам знає повний шлях за останнім id (id тут це закодований відносний шлях).
            var list = await http.GetFromJsonAsync<List<SegmentDto>>($"api/path/{Uri.EscapeDataString(idChain[^1])}", ct) ?? [];
            return list.Select(s => new PathSegment(s.Id, s.Name)).ToList();
        }
        catch (HttpRequestException)
        {
            return [];
        }
    }

    public async Task<FolderCheckResult> CheckFolderAsync(string linkOrPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(linkOrPath)) return new(FolderCheckStatus.Invalid);
        try
        {
            var r = await http.GetFromJsonAsync<ResolveDto>($"api/resolve?q={Uri.EscapeDataString(linkOrPath)}", ct);
            return r is null ? new(FolderCheckStatus.NotFound) : new(FolderCheckStatus.Ok, r.Id, r.Name);
        }
        catch (HttpRequestException e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return new(FolderCheckStatus.NotFound);
        }
    }

    string Url(string fileId) => new Uri(http.BaseAddress!, $"api/file/{Uri.EscapeDataString(fileId)}").ToString();

    public string GetThumbnailUrl(string fileId, int size = 400) => Url(fileId);
    public string GetImageUrl(string fileId) => Url(fileId);
    public string GetContentUrl(string fileId) => Url(fileId);
    public string? GetPreviewUrl(DriveFile file) => file.Kind == FileKind.Pdf ? Url(file.Id) : null;

    public async Task<TextPreview?> GetTextPreviewAsync(DriveFile file, CancellationToken ct = default)
    {
        if (file.Kind != FileKind.Text) return null;
        var dto = await http.GetFromJsonAsync<TextDto>($"api/text/{Uri.EscapeDataString(file.Id)}", ct);
        return dto is null ? null : new TextPreview(dto.Text, dto.Truncated);
    }

    public string GetOpenUrl(string fileId) => Url(fileId);
    public string GetDownloadUrl(string fileId) => Url(fileId) + "?download=true";
}
