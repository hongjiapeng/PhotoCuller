using Microsoft.UI.Xaml;
using PhotoCuller.App.WinUI.Views;
using PhotoCuller.Core.Interfaces.Platform;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace PhotoCuller.App.WinUI.Services;

public sealed class WinUIFileDialogService : IFileDialogService
{
    private readonly Func<MainWindow> _getWindow;

    public WinUIFileDialogService(Func<MainWindow> getWindow) => _getWindow = getWindow;

    public async Task<string?> PickFolderAsync()
    {
        var picker = new FolderPicker();
        picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
        picker.FileTypeFilter.Add("*");

        var hwnd = WindowNative.GetWindowHandle(_getWindow());
        InitializeWithWindow.Initialize(picker, hwnd);

        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }

    public async Task<IReadOnlyList<string>> PickFilesAsync(
        string? filter = null, bool multiSelect = false)
    {
        var picker = new FileOpenPicker();
        picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;

        var exts = (filter ?? ".jpg;.jpeg;.png;.heic")
            .Split(';', StringSplitOptions.RemoveEmptyEntries);
        foreach (var ext in exts)
            picker.FileTypeFilter.Add(ext.Trim());

        var hwnd = WindowNative.GetWindowHandle(_getWindow());
        InitializeWithWindow.Initialize(picker, hwnd);

        if (multiSelect)
        {
            var files = await picker.PickMultipleFilesAsync();
            return files?.Select(f => f.Path).ToList() ?? [];
        }

        var file = await picker.PickSingleFileAsync();
        return file is null ? [] : [file.Path];
    }
}
