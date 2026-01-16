using Avalonia;
using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using GenHub.Features.GameProfiles.ViewModels;

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

    /// <summary>
    /// Called when the data context changes.
    /// </summary>
    /// <param name="e">The event arguments.</param>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        InitializeBrowseActions();
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
                    Title = "Select Content Folder",
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
                    Title = "Select Files",
                    AllowMultiple = true,
                    FileTypeFilter = [FilePickerFileTypes.All, new("Zip Archives") { Patterns = ["*.zip"] }],
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
        if (e.Data.Contains(DataFormats.Files))
        {
            e.DragEffects = DragDropEffects.Copy;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
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
