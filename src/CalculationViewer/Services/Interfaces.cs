using CalculationViewer.Models;

namespace CalculationViewer.Services;

/// <summary>Список папок, які адміністратор підключив до застосунку. Реальна реалізація: Firestore.</summary>
public interface IFolderCatalog
{
    event Action? Changed;
    Task<IReadOnlyList<CatalogFolder>> GetAllAsync(CancellationToken ct = default);
    Task<CatalogFolder?> FindAsync(string id, CancellationToken ct = default);
    Task SaveAsync(CatalogFolder folder, CancellationToken ct = default);
    Task DeleteAsync(string id, CancellationToken ct = default);
    Task MoveAsync(string id, int delta, CancellationToken ct = default);
}

/// <summary>Читання вмісту публічних папок. Реальна реалізація: Google Drive API v3 з API-ключем.</summary>
public interface IDriveBrowser
{
    /// <exception cref="DriveAccessException">Папка закрита, не існує або джерело повернуло помилку.</exception>
    Task<DriveListing> GetListingAsync(string folderId, CancellationToken ct = default);
    /// <summary>
    /// Назви папок за ланцюжком id з адреси сторінки: від підключеної папки до поточної, включно з обома.
    /// Порожній результат, якщо ланцюжок не починається з підключеної папки. Google Drive не віддає батьківську папку
    /// анонімним запитам, тому шлях береться з адреси, а не з API.
    /// </summary>
    Task<IReadOnlyList<PathSegment>> GetPathAsync(IReadOnlyList<string> idChain, CancellationToken ct = default);
    Task<FolderCheckResult> CheckFolderAsync(string linkOrPath, CancellationToken ct = default);

    string GetThumbnailUrl(string fileId, int size = 400);
    /// <summary>Запасні адреси мініатюри по черзі, якщо основна не завантажилась (обмеження Google, збій мережі). Може бути порожнім.</summary>
    IReadOnlyList<string> GetThumbnailFallbacks(string fileId, int size = 400);
    string GetImageUrl(string fileId);
    /// <summary>Запасна адреса великого зображення, незалежна від основної (інший канал Google). Null, якщо її немає.</summary>
    string? GetImageFallbackUrl(string fileId);
    /// <summary>Адреса самого файлу (байти). Потрібна, щоб показати .docx у браузері, коли iframe-перегляд недоступний.</summary>
    string GetContentUrl(string fileId);
    /// <summary>Адреса для iframe (у Drive: /file/d/{id}/preview, працює для PDF, DOCX, TXT). Null, якщо такого перегляду немає.</summary>
    string? GetPreviewUrl(DriveFile file);
    /// <summary>Текст для файлів .txt/.csv. Null, якщо треба показувати через <see cref="GetPreviewUrl"/>.</summary>
    Task<TextPreview?> GetTextPreviewAsync(DriveFile file, CancellationToken ct = default);
    string GetOpenUrl(string fileId);
    string GetDownloadUrl(string fileId);
}

public sealed record TextPreview(string Text, bool Truncated);

/// <summary>Добірки закладок. Спільні для всіх відвідувачів. Реальна реалізація: Firestore.</summary>
public interface ICollectionStore
{
    event Action? Changed;
    Task<IReadOnlyList<BookmarkCollection>> GetAllAsync(CancellationToken ct = default);
    Task<BookmarkCollection?> GetAsync(string id, CancellationToken ct = default);
    Task<BookmarkCollection> CreateAsync(string name, CancellationToken ct = default);
    Task RenameAsync(string id, string name, CancellationToken ct = default);
    Task DeleteAsync(string id, CancellationToken ct = default);
    Task AddItemAsync(string collectionId, CollectionItem item, CancellationToken ct = default);
    Task RenameItemAsync(string collectionId, string itemId, string title, CancellationToken ct = default);
    Task RemoveItemAsync(string collectionId, string itemId, CancellationToken ct = default);
    bool IsBookmarked(BookmarkKind kind, string driveId);
}

/// <summary>Вхід адміністратора. Реальна реалізація: Firebase Authentication (Google).</summary>
public interface IAdminSession
{
    event Action? Changed;
    bool IsSignedIn { get; }
    string? Email { get; }
    Task SignInAsync();
    Task SignOutAsync();
}
