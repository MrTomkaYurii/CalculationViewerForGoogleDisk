using CalculationViewer;
using CalculationViewer.Services;
using CalculationViewer.Services.Google;
using CalculationViewer.Services.Local;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor;
using MudBlazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddMudServices(c =>
{
    c.SnackbarConfiguration.PositionClass = Defaults.Classes.Position.BottomCenter;
    c.SnackbarConfiguration.VisibleStateDuration = 3500;
    c.SnackbarConfiguration.ShowCloseIcon = false;
});

// Джерело даних. Зараз: реальна папка через DevDriveServer. Далі: Google Drive API, Firestore, Firebase Auth.
var driveServer = new Uri(builder.Configuration["DriveServer"] ?? "http://localhost:5199");
// Файли: Google Drive, якщо задано ключ API (GoogleApiKey), інакше локальний DevDriveServer.
// GoogleApiBase і GoogleThumbnailBase потрібні лише для тестів із підставним сервером.
var googleKey = builder.Configuration["GoogleApiKey"];
builder.Services.AddSingleton<IDriveBrowser>(sp => string.IsNullOrWhiteSpace(googleKey)
    ? new LocalDriveBrowser(new HttpClient { BaseAddress = driveServer })
    : new GoogleDriveBrowser(new HttpClient(), googleKey, sp.GetRequiredService<IFolderCatalog>(),
        builder.Configuration["GoogleApiBase"], builder.Configuration["GoogleThumbnailBase"]));
// Список папок береться зі статичних файлів сайту (wwwroot/data). Папки з диска (DevDriveServer) за замовчуванням вимкнені:
// щоб знову побачити їх у списку під час розробки, у wwwroot/appsettings.json поставте "IncludeLocalFolders": true.
var appBase = new Uri(builder.HostEnvironment.BaseAddress);
var includeLocalFolders = builder.HostEnvironment.IsDevelopment() && builder.Configuration.GetValue<bool>("IncludeLocalFolders");
builder.Services.AddSingleton<IFolderCatalog>(_ => new SeededFolderCatalog(
    new HttpClient { BaseAddress = appBase },
    includeLocalFolders ? new HttpClient { BaseAddress = driveServer } : null));
builder.Services.AddSingleton<ICollectionStore, MemoryCollectionStore>();
builder.Services.AddSingleton<IAdminSession, DevAdminSession>();

builder.Services.AddSingleton<UiPrefs>();
builder.Services.AddScoped<BookmarkActions>();
builder.Services.AddScoped<FileDownloads>();

await builder.Build().RunAsync();
