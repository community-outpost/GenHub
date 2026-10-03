using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using GenHub.Features.GameProfiles.ViewModels;
using GenHub.Infrastructure.Converters;
using System;
using System.Linq;

namespace GenHub.Features.GameProfiles.Views;

/// <summary>
/// View for adding local game content (mods, maps, tools).
/// </summary>
public partial class AddLocalContentView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AddLocalContentView"/> class.
    /// </summary>
    public AddLocalContentView()
    {
        InitializeComponent();
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
    }

    /// <summary>
    /// Called when the view is attached to the visual tree.
    /// </summary>
    /// <param name="e">The event arguments.</param>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        InitializeBrowseActions();
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        (DataContext as IDisposable)?.Dispose();
    }

    /// <summary>
    /// Called when the data context changes.
    /// </summary>
    /// <param name="e">The event arguments.</param>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        InitializeBrowseActions();
    }

    private static string ResolvePickerString(string key, string fallback)
    {
        var localizationService = LocalizationConverterHelper.ResolveLocalizationService();
        return LocalizationConverterHelper.GetLocalizedOrDefault(localizationService, key, fallback);
    }

    private void InitializeBrowseActions()
    {
        if (DataContext is AddLocalContentViewModel vm)
        {
            // Wire up the browse delegates
            vm.BrowseFolderAction = async () =>
            {
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel?.StorageProvider == null)
                {
                    return null;
                }

                var result = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = ResolvePickerString("Profiles.AddLocalContent.Picker.SelectFolderTitle", "Select Content Folder"),
                    AllowMultiple = false,
                });
                return result.Count > 0 ? result[0].Path.LocalPath : null;
            };

            vm.BrowseFileAction = async () =>
            {
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel?.StorageProvider == null)
                {
                    return null;
                }

                var result = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = ResolvePickerString("Profiles.AddLocalContent.Picker.SelectFilesTitle", "Select Files"),
                    AllowMultiple = true,
                    FileTypeFilter =
                    [
                        new(ResolvePickerString("Profiles.AddLocalContent.Picker.SupportedFilesFilter", "Supported Content Files (*.zip, *.7z, *.rar, *.tar, *.gz, *.big)"))
                        {
                            Patterns = ["*.zip", "*.7z", "*.rar", "*.tar", "*.gz", "*.big"],
                        },
                        new(ResolvePickerString("Profiles.AddLocalContent.Picker.ZipArchivesFilter", "Zip Archives (*.zip)")) { Patterns = ["*.zip"] },
                        new(ResolvePickerString("Profiles.AddLocalContent.Picker.BigFilesFilter", "BIG Files (*.big)")) { Patterns = ["*.big"] },
                        FilePickerFileTypes.All,
                    ],
                });
                return result.Count > 0 ? result.Select(f => f.Path.LocalPath).ToList() : null;
            };
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    // Drag & Drop handlers
    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.Data.Contains(DataFormats.Files) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not AddLocalContentViewModel vm) return;

        var files = e.Data.GetFiles();
        if (files != null)
        {
            foreach (var file in files)
            {
                if (file?.Path?.LocalPath is { } path)
                {
                    await vm.ImportContentAsync(path);
                }
            }
        }
    }
}
