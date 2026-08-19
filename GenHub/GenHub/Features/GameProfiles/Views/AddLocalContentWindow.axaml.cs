using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using GenHub.Features.GameProfiles.ViewModels;

namespace GenHub.Features.GameProfiles.Views;

/// <summary>
/// Window for adding local content to game profiles.
/// </summary>
public partial class AddLocalContentWindow : Window
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AddLocalContentWindow"/> class.
    /// </summary>
    public AddLocalContentWindow()
    {
        InitializeComponent();
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
    }

    /// <inheritdoc />
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        GenHub.Infrastructure.Interop.AdminDragDropFix.Apply(this, OnAdminDrop);
    }

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is AddLocalContentViewModel vm)
        {
            vm.RequestClose += (s, result) => Close(result);

            // Wire up the browse delegates
            vm.BrowseFolderAction = async () =>
            {
                if (StorageProvider == null)
                {
                    return null;
                }

                var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = "Select Content Folder",
                    AllowMultiple = false,
                });

                return folders.Count > 0 ? folders[0].Path.LocalPath : null;
            };

            vm.BrowseFileAction = async () =>
            {
                if (StorageProvider == null)
                {
                    return null;
                }

                var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Select Archive File",
                    AllowMultiple = true,
                    FileTypeFilter =
                    [
                        new FilePickerFileType("Archive Files")
                        {
                            Patterns = ["*.zip", "*.7z", "*.rar", "*.tar", "*.gz", "*.big"],
                        },
                        new FilePickerFileType("All Files")
                        {
                            Patterns = ["*.*"],
                        },
                    ],
                });

                return files.Count > 0 ? files.Select(f => f.Path.LocalPath).ToList() : null;
            };
        }
    }

    /// <summary>
    /// Handles pointer pressed on the title bar for dragging and maximizing.
    /// </summary>
    /// <param name="sender">The sender.</param>
    /// <param name="e">The event arguments.</param>
    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            if (e.ClickCount == 2 && CanResize)
            {
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            }
            else
            {
                BeginMoveDrag(e);
            }
        }
    }

    private void OnAdminDrop(string[] files)
    {
        _ = ProcessAdminDropAsync(files);
    }

    private async Task ProcessAdminDropAsync(string[] files)
    {
        if (DataContext is not AddLocalContentViewModel vm)
        {
            return;
        }

        try
        {
            foreach (var file in files)
            {
                await vm.ImportContentAsync(file);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error during admin drop import: {ex.Message}");
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
