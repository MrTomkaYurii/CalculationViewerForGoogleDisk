using System.Net.Http.Json;
using CalculationViewer.Models;

namespace CalculationViewer.Services.Local;

/// <summary>
/// Підключені папки на час розробки: підпапки верхнього рівня реальної папки. Зміни адміністратора (назва, опис, порядок)
/// живуть у пам'яті. У продакшені список зберігається у Firestore.
/// </summary>
internal sealed class LocalFolderCatalog(HttpClient http) : IFolderCatalog
{
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
            if (_folders is null)
            {
                try
                {
                    var roots = await http.GetFromJsonAsync<List<RootDto>>("api/roots", ct) ?? [];
                    _folders = roots.Select(r => new CatalogFolder { Id = r.Id, Title = r.Name, UpdatedUtc = r.ModifiedUtc }).ToList();
                }
                catch (HttpRequestException)
                {
                    throw new DriveAccessException("Джерело даних недоступне. Запущено DevDriveServer?");
                }
            }
            return _folders;
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
