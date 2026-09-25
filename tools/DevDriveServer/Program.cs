// Локальний замінник Google Drive для розробки інтерфейсу.
// Віддає вміст реальної папки на диску так само, як його віддаватиме Drive: папки, файли, шлях, вміст файлу.
// Запуск: dotnet run --project tools/DevDriveServer -- "C:\August 2026-Results"
// Це лише інструмент розробки. У продакшені його немає: застосунок ходить у Google Drive API напряму.

using System.Text;
using Microsoft.AspNetCore.StaticFiles;

var root = Path.GetFullPath(args.Length > 0 ? args[0] : @"C:\August 2026-Results");
if (!Directory.Exists(root))
{
    Console.Error.WriteLine($"Папку не знайдено: {root}");
    return 1;
}

var builder = WebApplication.CreateBuilder();
builder.WebHost.UseUrls("http://localhost:5199");
builder.Services.AddCors();
var app = builder.Build();
app.UseCors(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());

var contentTypes = new FileExtensionContentTypeProvider();
contentTypes.Mappings[".mat"] = "application/octet-stream";
contentTypes.Mappings[".csv"] = "text/csv; charset=utf-8";
contentTypes.Mappings[".txt"] = "text/plain; charset=utf-8";

// id = відносний шлях у Base64Url, щоб клієнт бачив непрозорий рядок, як id у Drive.
static string Encode(string rel) => Convert.ToBase64String(Encoding.UTF8.GetBytes(rel)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

static string Decode(string id)
{
    var s = id.Replace('-', '+').Replace('_', '/');
    s = s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
    return Encoding.UTF8.GetString(Convert.FromBase64String(s));
}

string? Resolve(string id)
{
    try
    {
        var rel = Decode(id).Replace('\\', '/').Trim('/');
        var full = Path.GetFullPath(Path.Combine(root, rel));
        var inside = full.Equals(root, StringComparison.OrdinalIgnoreCase)
                     || full.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        return inside ? full : null;
    }
    catch { return null; }
}

string Rel(string full) => Path.GetRelativePath(root, full).Replace('\\', '/');

static bool Skip(FileSystemInfo i) => i.Name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase) || i.Attributes.HasFlag(FileAttributes.Hidden);

// Папки верхнього рівня — це «підключені» папки.
app.MapGet("/api/roots", () =>
    Results.Json(new DirectoryInfo(root).EnumerateDirectories().Where(d => !Skip(d))
        .Select(d => new { id = Encode(d.Name), name = d.Name, modifiedUtc = d.LastWriteTimeUtc })
        .OrderBy(d => d.name, StringComparer.OrdinalIgnoreCase)));

app.MapGet("/api/folder/{id}", (string id) =>
{
    var full = Resolve(id);
    if (full is null || !Directory.Exists(full) || full.Equals(root, StringComparison.OrdinalIgnoreCase)) return Results.NotFound();
    var di = new DirectoryInfo(full);
    var rel = Rel(full);
    string? parent = rel.Contains('/') ? Encode(rel[..rel.LastIndexOf('/')]) : null;
    var folders = di.EnumerateDirectories().Where(d => !Skip(d))
        .Select(d => new { id = Encode($"{rel}/{d.Name}"), name = d.Name, modifiedUtc = d.LastWriteTimeUtc }).ToList();
    var files = di.EnumerateFiles().Where(f => !Skip(f))
        .Select(f => new { id = Encode($"{rel}/{f.Name}"), name = f.Name, size = f.Length, modifiedUtc = f.LastWriteTimeUtc }).ToList();
    return Results.Json(new { id, name = di.Name, parentId = parent, folders, files });
});

// Шлях від підключеної папки до поточної, включно.
app.MapGet("/api/path/{id}", (string id) =>
{
    var full = Resolve(id);
    if (full is null || !Directory.Exists(full)) return Results.NotFound();
    var parts = Rel(full).Split('/', StringSplitOptions.RemoveEmptyEntries);
    var result = new List<object>();
    for (var i = 0; i < parts.Length; i++)
        result.Add(new { id = Encode(string.Join('/', parts.Take(i + 1))), name = parts[i] });
    return Results.Json(result);
});

app.MapGet("/api/file/{id}", (string id, bool? download) =>
{
    var full = Resolve(id);
    if (full is null || !File.Exists(full)) return Results.NotFound();
    if (!contentTypes.TryGetContentType(full, out var ct)) ct = "application/octet-stream";
    return download == true
        ? Results.File(full, ct, Path.GetFileName(full), enableRangeProcessing: true)
        : Results.File(full, ct, enableRangeProcessing: true);
});

// Перший шматок текстового файлу для перегляду.
app.MapGet("/api/text/{id}", async (string id, int? maxBytes) =>
{
    var full = Resolve(id);
    if (full is null || !File.Exists(full)) return Results.NotFound();
    var limit = Math.Clamp(maxBytes ?? 200_000, 1, 2_000_000);
    await using var fs = File.OpenRead(full);
    var buf = new byte[Math.Min(limit, fs.Length)];
    var read = await fs.ReadAsync(buf);
    return Results.Json(new { text = Encoding.UTF8.GetString(buf, 0, read), truncated = fs.Length > read, size = fs.Length });
});

// Адмін додає папку за шляхом (у Drive це буде посилання).
app.MapGet("/api/resolve", (string q) =>
{
    var text = q.Trim().Trim('"');
    var rel = Path.IsPathRooted(text) ? Path.GetRelativePath(root, Path.GetFullPath(text)) : text;
    var full = Resolve(Encode(rel.Replace('\\', '/')));
    if (full is null || !Directory.Exists(full) || full.Equals(root, StringComparison.OrdinalIgnoreCase)) return Results.NotFound();
    return Results.Json(new { id = Encode(Rel(full)), name = new DirectoryInfo(full).Name });
});

Console.WriteLine($"Dev Drive: {root}\nhttp://localhost:5199");
app.Run();
return 0;
