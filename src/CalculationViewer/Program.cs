using CalculationViewer;
using CalculationViewer.Services;
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
builder.Services.AddSingleton<IDriveBrowser>(_ => new LocalDriveBrowser(new HttpClient { BaseAddress = driveServer }));
builder.Services.AddSingleton<IFolderCatalog>(_ => new LocalFolderCatalog(new HttpClient { BaseAddress = driveServer }));
builder.Services.AddSingleton<ICollectionStore, MemoryCollectionStore>();
builder.Services.AddSingleton<IAdminSession, DevAdminSession>();

builder.Services.AddSingleton<UiPrefs>();
builder.Services.AddScoped<BookmarkActions>();

await builder.Build().RunAsync();
