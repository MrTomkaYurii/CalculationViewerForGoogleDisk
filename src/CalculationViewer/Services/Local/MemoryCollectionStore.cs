using CalculationViewer.Models;

namespace CalculationViewer.Services.Local;

/// <summary>Добірки в пам'яті (порожні на старті). У продакшені: Firestore.</summary>
internal sealed class MemoryCollectionStore : ICollectionStore
{
    readonly List<BookmarkCollection> _all = [];

    public event Action? Changed;

    public Task<IReadOnlyList<BookmarkCollection>> GetAllAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<BookmarkCollection>>(_all.ToList());

    public Task<BookmarkCollection?> GetAsync(string id, CancellationToken ct = default)
        => Task.FromResult(_all.FirstOrDefault(c => c.Id == id));

    public Task<BookmarkCollection> CreateAsync(string name, CancellationToken ct = default)
    {
        var c = new BookmarkCollection { Name = name.Trim() };
        _all.Insert(0, c);
        Changed?.Invoke();
        return Task.FromResult(c);
    }

    public Task RenameAsync(string id, string name, CancellationToken ct = default)
    {
        var c = _all.FirstOrDefault(x => x.Id == id);
        if (c is not null) c.Name = name.Trim();
        Changed?.Invoke();
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string id, CancellationToken ct = default)
    {
        _all.RemoveAll(c => c.Id == id);
        Changed?.Invoke();
        return Task.CompletedTask;
    }

    public Task AddItemAsync(string collectionId, CollectionItem item, CancellationToken ct = default)
    {
        _all.FirstOrDefault(c => c.Id == collectionId)?.Items.Add(item);
        Changed?.Invoke();
        return Task.CompletedTask;
    }

    public Task RenameItemAsync(string collectionId, string itemId, string title, CancellationToken ct = default)
    {
        var item = _all.FirstOrDefault(c => c.Id == collectionId)?.Items.FirstOrDefault(i => i.Id == itemId);
        if (item is not null) item.Title = title.Trim();
        Changed?.Invoke();
        return Task.CompletedTask;
    }

    public Task RemoveItemAsync(string collectionId, string itemId, CancellationToken ct = default)
    {
        _all.FirstOrDefault(c => c.Id == collectionId)?.Items.RemoveAll(i => i.Id == itemId);
        Changed?.Invoke();
        return Task.CompletedTask;
    }

    public bool IsBookmarked(BookmarkKind kind, string driveId)
        => _all.Any(c => c.Items.Any(i => i.Kind == kind && i.DriveId == driveId));
}
