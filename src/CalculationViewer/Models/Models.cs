namespace CalculationViewer.Models;

public enum FileKind { Image, Pdf, Docx, Text, Other }

public static class FileKinds
{
    public static FileKind FromName(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".png" or ".jpg" or ".jpeg" or ".bmp" or ".svg" or ".gif" or ".webp" => FileKind.Image,
        ".pdf" => FileKind.Pdf,
        ".docx" or ".doc" => FileKind.Docx,
        ".txt" or ".csv" or ".md" or ".log" => FileKind.Text,
        _ => FileKind.Other,
    };
}

public sealed record DriveFolder(string Id, string Name, string? ParentId, DateTime? ModifiedUtc);

public sealed record DriveFile(string Id, string Name, string FolderId, FileKind Kind, long SizeBytes, DateTime ModifiedUtc, string? MimeType = null)
{
    /// <summary>Розширення для значка: «PNG», «PDF». Документи Google без розширення отримують «GDOC», «GSHEET», «GSLIDES».</summary>
    public string Extension => MimeType switch
    {
        "application/vnd.google-apps.document" => "GDOC",
        "application/vnd.google-apps.spreadsheet" => "GSHEET",
        "application/vnd.google-apps.presentation" => "GSLIDES",
        _ => Name.Contains('.') ? Name[(Name.LastIndexOf('.') + 1)..].ToUpperInvariant() : "",
    };

    public bool IsImage => Kind == FileKind.Image;
}

/// <summary>Вміст однієї папки: лише прямі підпапки й файли. Drive API не віддає лічильники вкладеного вмісту, тому їх немає й тут.</summary>
public sealed record DriveListing(DriveFolder Folder, IReadOnlyList<DriveFolder> Folders, IReadOnlyList<DriveFile> Files);

public sealed record PathSegment(string Id, string Name);

/// <summary>Папка, яку адміністратор підключив до застосунку (зберігається у Firestore).</summary>
public sealed class CatalogFolder
{
    /// <summary>Id папки в Google Drive.</summary>
    public required string Id { get; set; }
    public required string Title { get; set; }
    public string Description { get; set; } = "";
    public DateTime? UpdatedUtc { get; set; }
}

public enum BookmarkKind { File, Folder }

public sealed class CollectionItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public BookmarkKind Kind { get; set; }
    public required string DriveId { get; set; }
    /// <summary>Для файлу: папка, в якій він лежить. Для папки: вона сама.</summary>
    public required string FolderId { get; set; }
    /// <summary>Шлях до папки з id через «/», від підключеної папки до потрібної. Сторінка папки відкривається саме за ним.</summary>
    public string FolderPath { get; set; } = "";
    public required string Title { get; set; }
    public string OriginalName { get; set; } = "";
    public FileKind? FileKind { get; set; }
    /// <summary>Шлях текстом: «Розрахунки › 02 Схеми».</summary>
    public string Path { get; set; } = "";

    public string Href
    {
        get
        {
            var path = string.IsNullOrEmpty(FolderPath) ? FolderId : FolderPath;
            return Kind == BookmarkKind.Folder ? $"folder/{path}" : $"folder/{path}?file={Uri.EscapeDataString(DriveId)}";
        }
    }
}

public sealed class BookmarkCollection
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public required string Name { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public List<CollectionItem> Items { get; set; } = [];
}

public enum FolderCheckStatus { Ok, NotPublic, NotFound, Invalid }

public sealed record FolderCheckResult(FolderCheckStatus Status, string? DriveId = null, string? Name = null);

public sealed class DriveAccessException(string message) : Exception(message);
