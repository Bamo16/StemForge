using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace StemForge.Extensions;

/// <summary>
/// Centralised wrappers around <see cref="IStorageProvider"/>'s folder and file pickers.
/// Every caller goes through these so the "open from a sensible starting location" behaviour
/// is uniform — pass the path currently shown in the field, and the picker opens there.
/// When the path can't be resolved, the picker falls through to the OS default.
/// </summary>
public static class StorageProviderExtensions
{
    /// <summary>
    /// Opens a single-folder picker. Returns the picked folder's local path, or null if the user
    /// cancels or no <see cref="TopLevel"/> can be resolved from <paramref name="visual"/>.
    /// </summary>
    public static async Task<string?> PickFolderAsync(
        this Visual visual,
        string? suggestedStartPath = null
    )
    {
        if (TopLevel.GetTopLevel(visual) is not { StorageProvider: { } provider })
            return null;

        var start = await TryGetStartFolderAsync(provider, suggestedStartPath);
        var folders = await provider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { AllowMultiple = false, SuggestedStartLocation = start }
        );

        return folders is [{ Path.LocalPath: { } path }, ..] ? path : null;
    }

    /// <summary>
    /// Opens a multi-select file picker. Returns the picked files' local paths (possibly empty
    /// when cancelled). When <paramref name="fileTypes"/> is null, no extension filter is applied.
    /// </summary>
    public static async Task<IReadOnlyList<string>> PickFilesAsync(
        this Visual visual,
        IReadOnlyList<FilePickerFileType>? fileTypes = null,
        string? suggestedStartPath = null
    )
    {
        if (TopLevel.GetTopLevel(visual) is not { StorageProvider: { } provider })
            return [];

        var start = await TryGetStartFolderAsync(provider, suggestedStartPath);
        var files = await provider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                AllowMultiple = true,
                FileTypeFilter = fileTypes,
                SuggestedStartLocation = start,
            }
        );

        return [.. files.Select(f => f.Path.LocalPath)];
    }

    private static async Task<IStorageFolder?> TryGetStartFolderAsync(
        IStorageProvider provider,
        string? suggestedStartPath
    )
    {
        if (string.IsNullOrWhiteSpace(suggestedStartPath))
            return null;
        try
        {
            return await provider.TryGetFolderFromPathAsync(suggestedStartPath);
        }
        catch
        {
            return null;
        }
    }
}
