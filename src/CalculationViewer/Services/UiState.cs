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
    public async Task AddFileAsync(DriveFile file)
    {
        await ShowAsync(new CollectionItem
        {
            Kind = BookmarkKind.File,
            DriveId = file.Id,
            FolderId = file.FolderId,
            Title = file.Name,
            OriginalName = file.Name,
            FileKind = file.Kind,
            Path = string.Join(" › ", (await drive.GetPathAsync(file.FolderId)).Select(s => s.Name)),
        });
    }

    public async Task AddFolderAsync(string folderId, string name)
    {
        var path = await drive.GetPathAsync(folderId);
        await ShowAsync(new CollectionItem
        {
            Kind = BookmarkKind.Folder,
            DriveId = folderId,
            FolderId = folderId,
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
