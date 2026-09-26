using CalculationViewer.Models;
using Microsoft.JSInterop;
using MudBlazor;

namespace CalculationViewer.Services;

/// <summary>Зберігає файл на комп'ютер користувача штатним скачуванням Google (оригінал під його іменем).</summary>
public sealed class FileDownloads(IJSRuntime js, IDriveBrowser drive, ISnackbar snackbar)
{
    IJSObjectReference? _module;

    public async Task DownloadAsync(DriveFile file)
    {
        try
        {
            _module ??= await js.InvokeAsync<IJSObjectReference>("import", "./js/download.js");
            var result = await _module.InvokeAsync<string>("save", drive.GetDownloadUrl(file));
            snackbar.Add(result == "started"
                ? $"Скачування «{file.Name}» розпочато"
                : $"Google просить підтвердити скачування «{file.Name}». Воно відкрито в новій вкладці.", Severity.Normal);
        }
        catch (JSException e)
        {
            Console.WriteLine($"[FileDownloads] {e.Message}");
            snackbar.Add($"Не вдалося скачати «{file.Name}». Спробуйте ще раз.", Severity.Warning);
        }
    }
}
