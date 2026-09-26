using CalculationViewer.Models;
using MudBlazor;

namespace CalculationViewer.Services;

public enum ViewMode { Grid, List }

/// <summary>Налаштування вигляду, які користувач очікує побачити збереженими між папками.</summary>
public sealed class UiPrefs
{
    public ViewMode View { get; set; } = ViewMode.Grid;
}

public static class DialogExtensions
{
    /// <summary>Питає одне текстове значення. Повертає null, якщо користувач скасував.</summary>
    public static async Task<string?> PromptAsync(this IDialogService dialogs, string title, string label, string value = "", string confirm = "Зберегти")
    {
        var parameters = new DialogParameters<Components.PromptDialog>
        {
            { x => x.Title, title }, { x => x.Label, label }, { x => x.Value, value }, { x => x.ConfirmText, confirm },
        };
        var dialog = await dialogs.ShowAsync<Components.PromptDialog>(title, parameters, new DialogOptions { MaxWidth = MaxWidth.Small, FullWidth = true });
        var result = await dialog.Result;
        return result is { Canceled: false, Data: string text } ? text : null;
    }

    public static async Task<bool> ConfirmAsync(this IDialogService dialogs, string title, string message, string confirm)
        => await dialogs.ShowMessageBoxAsync(title, message, yesText: confirm, cancelText: "Скасувати") == true;
}

/// <summary>Відкриває вікно «Додати в добірку» для файлу або папки.</summary>
public sealed class BookmarkActions(IDialogService dialogs, IDriveBrowser drive)
{
    static string[] Chain(string folderPath) => folderPath.Split('/', StringSplitOptions.RemoveEmptyEntries);

    /// <param name="folderPath">Шлях до папки файлу з id через «/», як в адресі сторінки.</param>
    public async Task AddFileAsync(DriveFile file, string folderPath)
    {
        await ShowAsync(new CollectionItem
        {
            Kind = BookmarkKind.File,
            DriveId = file.Id,
            FolderId = file.FolderId,
            FolderPath = folderPath,
            Title = file.Name,
            OriginalName = file.Name,
            FileKind = file.Kind,
            Path = string.Join(" › ", (await drive.GetPathAsync(Chain(folderPath))).Select(s => s.Name)),
        });
    }

    /// <param name="folderPath">Шлях до самої папки з id через «/», як в адресі сторінки.</param>
    public async Task AddFolderAsync(string folderPath, string name)
    {
        var chain = Chain(folderPath);
        var path = await drive.GetPathAsync(chain);
        await ShowAsync(new CollectionItem
        {
            Kind = BookmarkKind.Folder,
            DriveId = chain[^1],
            FolderId = chain[^1],
            FolderPath = folderPath,
            Title = name,
            OriginalName = name,
            Path = string.Join(" › ", path.Take(Math.Max(0, path.Count - 1)).Select(s => s.Name)),
        });
    }

    async Task ShowAsync(CollectionItem draft)
    {
        var parameters = new DialogParameters<Components.AddToCollectionDialog> { { x => x.Draft, draft } };
        var options = new DialogOptions { MaxWidth = MaxWidth.Small, FullWidth = true, CloseOnEscapeKey = true };
        await dialogs.ShowAsync<Components.AddToCollectionDialog>("Додати в добірку", parameters, options);
    }
}
