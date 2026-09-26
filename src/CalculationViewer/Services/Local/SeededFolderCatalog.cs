using System.Net.Http.Json;
using CalculationViewer.Models;

namespace CalculationViewer.Services.Local;

/// <summary>
/// Підключені папки, поки Firestore ще не підключений. Список береться зі статичних файлів сайту:
/// <c>wwwroot/data/folders.json</c> (id папки Google Drive, назва, файл опису) і <c>wwwroot/data/*.md</c> (описи в Markdown).
/// Так список однаковий локально й на GitHub Pages. У режимі розробки до нього додаються папки з диска через DevDriveServer.
/// Зміни адміністратора в інтерфейсі живуть у пам'яті до перезавантаження. У продакшені їх зберігатиме Firestore.
/// </summary>
internal sealed class SeededFolderCatalog(HttpClient app, HttpClient? devServer) : IFolderCatalog
{
    sealed record SeedDto(string Id, string Title, string? DescriptionFile);
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
            var list = new List<CatalogFolder>();

            try
            {
                var seeds = await app.GetFromJsonAsync<List<SeedDto>>("data/folders.json", ct) ?? [];
                foreach (var s in seeds)
                {
                    var description = "";
                    if (!string.IsNullOrWhiteSpace(s.DescriptionFile))
                    {
                        try { description = await app.GetStringAsync($"data/{s.DescriptionFile}", ct); }
                        catch (HttpRequestException e) { Console.WriteLine($"[SeededFolderCatalog] Не вдалося прочитати опис {s.DescriptionFile}: {e.Message}"); }
                    }
                    list.Add(new CatalogFolder { Id = s.Id, Title = s.Title, Description = description });
                }
            }
            catch (HttpRequestException e)
            {
                Console.WriteLine($"[SeededFolderCatalog] Не вдалося прочитати data/folders.json: {e.Message}");
                if (devServer is null) throw new DriveAccessException("Список папок тимчасово недоступний. Спробуйте пізніше.");
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
        Changed?.Invoke();
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        (await LoadAsync(ct)).RemoveAll(f => f.Id == id);
        Changed?.Invoke();
    }

    public async Task MoveAsync(string id, int delta, CancellationToken ct = default)
    {
        var list = await LoadAsync(ct);
        var i = list.FindIndex(f => f.Id == id);
        var j = i + delta;
        if (i < 0 || j < 0 || j >= list.Count) return;
        (list[i], list[j]) = (list[j], list[i]);
        Changed?.Invoke();
    }
}
