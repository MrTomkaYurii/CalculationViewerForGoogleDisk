using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.JSInterop;
using CalculationViewer.Models;

namespace CalculationViewer.Services.Local;

/// <summary>
/// Підключені папки, поки Firestore ще не підключений. Список береться зі статичних файлів сайту:
/// <c>wwwroot/data/folders.json</c> (id папки Google Drive, назва, файл опису) і <c>wwwroot/data/*.md</c> (описи в Markdown).
/// Так список однаковий локально й на GitHub Pages. У режимі розробки до нього додаються папки з диска через DevDriveServer.
/// Зміни адміністратора в інтерфейсі живуть у пам'яті до перезавантаження. У продакшені їх зберігатиме Firestore.
/// </summary>
internal sealed class SeededFolderCatalog(HttpClient app, HttpClient? devServer, IJSRuntime? js = null) : IFolderCatalog
{
    sealed record SeedDto(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("descriptionFile")] string? DescriptionFile = null,
        [property: JsonPropertyName("year")] int? Year = null);
    sealed record RootDto(string Id, string Name, DateTime ModifiedUtc);

    readonly SemaphoreSlim _gate = new(1, 1);
    List<CatalogFolder>? _folders;

    public event Action? Changed;

    async Task<List<CatalogFolder>> LoadAsync(CancellationToken ct)
    {
        if (_folders is not null) return _folders;
        await _gate.WaitAsync(ct);
        try
        {
            if (_folders is not null) return _folders;

            // Спроба відновити зміни адміністратора з localStorage браузера
            List<CatalogFolder>? saved = null;
            if (js is not null)
            {
                try
                {
                    var savedJson = await js.InvokeAsync<string?>("localStorage.getItem", "cv_custom_catalog");
                    if (!string.IsNullOrWhiteSpace(savedJson))
                    {
                        saved = System.Text.Json.JsonSerializer.Deserialize<List<CatalogFolder>>(savedJson);
                    }
                }
                catch { /* Ігноруємо відсутність localStorage під час первинного рендерингу */ }
            }

            var list = new List<CatalogFolder>();

            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, $"data/folders.json?v={DateTime.UtcNow.Ticks}");
                req.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true, NoStore = true };
                using var resp = await app.SendAsync(req, ct);
                resp.EnsureSuccessStatusCode();
                var seeds = await resp.Content.ReadFromJsonAsync<List<SeedDto>>(cancellationToken: ct) ?? [];
                foreach (var s in seeds)
                {
                    var description = "";
                    if (!string.IsNullOrWhiteSpace(s.DescriptionFile))
                    {
                        try
                        {
                            using var descReq = new HttpRequestMessage(HttpMethod.Get, $"data/{s.DescriptionFile}?v={DateTime.UtcNow.Ticks}");
                            descReq.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true, NoStore = true };
                            using var descResp = await app.SendAsync(descReq, ct);
                            if (descResp.IsSuccessStatusCode)
                            {
                                description = await descResp.Content.ReadAsStringAsync(ct);
                            }
                        }
                        catch (HttpRequestException e) { Console.WriteLine($"[SeededFolderCatalog] Не вдалося прочитати опис {s.DescriptionFile}: {e.Message}"); }
                    }
                    list.Add(new CatalogFolder { Id = s.Id, Title = s.Title, Description = description, Year = s.Year });
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"[SeededFolderCatalog] Не вдалося прочитати data/folders.json: {e.Message}");
                if (devServer is null && saved is null) throw new DriveAccessException("Список папок тимчасово недоступний. Спробуйте пізніше.");
            }

            if (saved is { Count: > 0 })
            {
                // Якщо в folders.json з'явилися нові папки, додаємо їх до збереженого списку
                foreach (var seedItem in list)
                {
                    if (saved.All(f => f.Id != seedItem.Id))
                    {
                        saved.Add(seedItem);
                    }
                }
                return _folders = saved;
            }

            if (devServer is not null)
            {
                try
                {
                    var roots = await devServer.GetFromJsonAsync<List<RootDto>>("api/roots", ct) ?? [];
                    list.AddRange(roots.Where(r => list.All(f => f.Id != r.Id))
                        .Select(r => new CatalogFolder { Id = r.Id, Title = r.Name, UpdatedUtc = r.ModifiedUtc }));
                }
                catch (HttpRequestException e)
                {
                    Console.WriteLine($"[SeededFolderCatalog] DevDriveServer не запущений, локальні папки пропущено: {e.Message}");
                }
            }

            return _folders = list;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<CatalogFolder>> GetAllAsync(CancellationToken ct = default) => (await LoadAsync(ct)).ToList();

    public async Task<CatalogFolder?> FindAsync(string id, CancellationToken ct = default)
        => (await LoadAsync(ct)).FirstOrDefault(f => f.Id == id);

    public async Task SaveAsync(CatalogFolder folder, CancellationToken ct = default)
    {
        var list = await LoadAsync(ct);
        var i = list.FindIndex(f => f.Id == folder.Id);
        folder.UpdatedUtc = DateTime.UtcNow;
        if (i >= 0) list[i] = folder; else list.Add(folder);
        await PersistAsync();
        Changed?.Invoke();
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        (await LoadAsync(ct)).RemoveAll(f => f.Id == id);
        await PersistAsync();
        Changed?.Invoke();
    }

    public async Task MoveAsync(string id, int delta, CancellationToken ct = default)
    {
        var list = await LoadAsync(ct);
        var i = list.FindIndex(f => f.Id == id);
        var j = i + delta;
        if (i < 0 || j < 0 || j >= list.Count) return;
        (list[i], list[j]) = (list[j], list[i]);
        await PersistAsync();
        Changed?.Invoke();
    }

    public async Task ResetAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            _folders = null;
            if (js is not null)
            {
                try
                {
                    await js.InvokeVoidAsync("localStorage.removeItem", "cv_custom_catalog");
                }
                catch { }
            }
        }
        finally
        {
            _gate.Release();
        }
        Changed?.Invoke();
    }

    async Task PersistAsync()
    {
        if (js is null || _folders is null) return;
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(_folders);
            await js.InvokeVoidAsync("localStorage.setItem", "cv_custom_catalog", json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SeededFolderCatalog] Не вдалося зберегти в localStorage: {ex.Message}");
        }
    }
}
