using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Features.Tools.ModBuilder.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.ModBuilder.ViewModels;

/// <summary>
/// ViewModel for bundle pack editor dialog.
/// </summary>
public partial class BundlePackEditorViewModel(
    INotificationService notificationService,
    ILogger<BundlePackEditorViewModel> logger,
    ILocalizationService? localizationService = null) : ObservableObject
{
    /// <summary>
    /// Gets or sets the bundle pack name.
    /// </summary>
    [ObservableProperty]
    private string _bundlePackName = string.Empty;

    /// <summary>
    /// Gets or sets the bundle pack description.
    /// </summary>
    [ObservableProperty]
    private string _bundlePackDescription = string.Empty;

    /// <summary>
    /// Gets or sets the output file name.
    /// </summary>
    [ObservableProperty]
    private string _outputFileName = string.Empty;

    /// <summary>
    /// Gets the collection of files in the bundle.
    /// </summary>
    public ObservableCollection<BundleFileInfo> Files { get; } = [];

    /// <summary>
    /// Gets the collection of selected files.
    /// </summary>
    public ObservableCollection<BundleFileInfo> SelectedFiles { get; } = [];

    /// <summary>
    /// Gets or sets the selected file for preview.
    /// </summary>
    [ObservableProperty]
    private BundleFileInfo? _selectedFile;

    /// <summary>
    /// Gets or sets the search filter text.
    /// </summary>
    [ObservableProperty]
    private string _searchFilter = string.Empty;

    /// <summary>
    /// Gets or sets the total file count.
    /// </summary>
    [ObservableProperty]
    private int _totalFileCount;

    /// <summary>
    /// Gets or sets the total size formatted.
    /// </summary>
    [ObservableProperty]
    private string _totalSizeFormatted = "0 B";

    /// <summary>
    /// Gets or sets a value indicating whether changes have been made.
    /// </summary>
    [ObservableProperty]
    private bool _hasChanges;

    /// <summary>
    /// Loads the bundle pack data.
    /// </summary>
    /// <param name="bundlePackName">The bundle pack name.</param>
    /// <param name="files">The files in the bundle.</param>
    public void LoadBundlePack(string bundlePackName, ObservableCollection<BundleFileInfo> files)
    {
        BundlePackName = bundlePackName;
        Files.Clear();

        foreach (var file in files)
        {
            Files.Add(file);
        }

        UpdateStatistics();
        HasChanges = false;
    }

    /// <summary>
    /// Adds files to the bundle.
    /// </summary>
    /// <param name="owner">The owner window.</param>
    [RelayCommand]
    private async Task AddFilesAsync(Window? owner = null)
    {
        try
        {
            if (owner == null)
            {
                logger.LogWarning("No owner window provided for file picker");
                return;
            }

            var pickerTitle = localizationService?.GetString("Tools.ModBuilder.BundlePack.PickerTitle") ?? "Add Files to Bundle";
            var allFilesLabel = localizationService?.GetString("Tools.ModBuilder.Filter.AllFiles") ?? "All Files";
            var imageFilesLabel = localizationService?.GetString("Tools.ModBuilder.Filter.ImageFiles") ?? "Image Files";
            var textFilesLabel = localizationService?.GetString("Tools.ModBuilder.Filter.TextFiles") ?? "Text Files";

            var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = pickerTitle,
                AllowMultiple = true,
                FileTypeFilter =
                [
                    new FilePickerFileType(allFilesLabel) { Patterns = ["*.*"] },
                    new FilePickerFileType(imageFilesLabel) { Patterns = ["*.tga", "*.dds", "*.psd", "*.png", "*.jpg"] },
                    new FilePickerFileType(textFilesLabel) { Patterns = ["*.csf", "*.ini", "*.txt"] }
                ]
            });

            if (files.Count > 0)
            {
                var newFiles = new List<BundleFileInfo>();
                foreach (var file in files)
                {
                    var fileInfo = new FileInfo(file.Path.LocalPath);
                    var bundleFile = new BundleFileInfo
                    {
                        FileName = fileInfo.Name,
                        SourcePath = fileInfo.FullName,
                        DestinationPath = fileInfo.Name,
                        FileType = fileInfo.Extension.TrimStart('.').ToUpperInvariant(),
                        FileSize = fileInfo.Length,
                        FileSizeFormatted = FormatFileSize(fileInfo.Length),
                        LastModified = fileInfo.LastWriteTime,
                        IconKey = GetIconKeyForFileType(fileInfo.Extension),
                        Order = Files.Count + newFiles.Count
                    };
                    newFiles.Add(bundleFile);
                }

                Dispatcher.UIThread.Post(() =>
                {
                    foreach (var bf in newFiles)
                    {
                        Files.Add(bf);
                    }

                    UpdateStatistics();
                    HasChanges = true;
                });

                var title = localizationService?.GetString("Tools.ModBuilder.Notification.FilesAdded.Title") ?? "Files Added";
                var msg = localizationService?.GetString("Tools.ModBuilder.Notification.FilesAdded.Message", files.Count) ?? $"Added {files.Count} file(s) to bundle pack";
                notificationService.ShowSuccess(title, msg);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to add files to bundle pack");
            var title = localizationService?.GetString("Tools.ModBuilder.Notification.AddFilesFailed.Title") ?? "Add Files Failed";
            var msg = localizationService?.GetString("Tools.ModBuilder.Notification.AddFilesFailed.Message", ex.Message) ?? $"Failed to add files: {ex.Message}";
            notificationService.ShowError(title, msg);
        }
    }

    /// <summary>
    /// Removes selected files from the bundle.
    /// </summary>
    [RelayCommand]
    private void RemoveFiles()
    {
        List<BundleFileInfo> filesToRemove;
        if (SelectedFiles.Count > 0)
        {
            filesToRemove = SelectedFiles.ToList();
        }
        else if (SelectedFile != null)
        {
            filesToRemove = [SelectedFile];
        }
        else
        {
            return;
        }

        foreach (var file in filesToRemove)
        {
            Files.Remove(file);
        }

        SelectedFiles.Clear();
        SelectedFile = null;
        UpdateStatistics();
        HasChanges = true;

        var title = localizationService?.GetString("Tools.ModBuilder.Notification.FilesRemoved.Title") ?? "Files Removed";
        var msg = localizationService?.GetString("Tools.ModBuilder.Notification.FilesRemoved.Message", filesToRemove.Count) ?? $"Removed {filesToRemove.Count} file(s) from bundle pack";
        notificationService.ShowSuccess(title, msg);
    }

    /// <summary>
    /// Moves selected files up in the order.
    /// </summary>
    [RelayCommand]
    private void MoveUp()
    {
        if (SelectedFile == null || Files.Count < 2)
        {
            return;
        }

        var index = Files.IndexOf(SelectedFile);
        if (index > 0)
        {
            Files.Move(index, index - 1);
            UpdateOrder();
            HasChanges = true;
        }
    }

    /// <summary>
    /// Moves selected files down in the order.
    /// </summary>
    [RelayCommand]
    private void MoveDown()
    {
        if (SelectedFile == null || Files.Count < 2)
        {
            return;
        }

        var index = Files.IndexOf(SelectedFile);
        if (index < Files.Count - 1)
        {
            Files.Move(index, index + 1);
            UpdateOrder();
            HasChanges = true;
        }
    }

    /// <summary>
    /// Converts all TGA files to DDS.
    /// </summary>
    [RelayCommand]
    private void ConvertAllToDds()
    {
        var tgaFiles = Files.Where(f => f.FileType.Equals("TGA", StringComparison.OrdinalIgnoreCase)).ToList();
        if (tgaFiles.Count == 0)
        {
            var title = localizationService?.GetString("Tools.ModBuilder.Notification.NoTgaFiles.Title") ?? "No TGA Files";
            var msg = localizationService?.GetString("Tools.ModBuilder.Notification.NoTgaFiles.Message") ?? "No TGA files found to convert";
            notificationService.ShowInfo(title, msg);
            return;
        }

        // This would trigger the actual conversion in the build engine
        var queuedTitle = localizationService?.GetString("Tools.ModBuilder.Notification.ConversionQueued.Title") ?? "Conversion Queued";
        var queuedMsg = localizationService?.GetString("Tools.ModBuilder.Notification.ConversionQueued.Message", tgaFiles.Count) ?? $"{tgaFiles.Count} TGA file(s) will be converted to DDS during build";
        notificationService.ShowInfo(queuedTitle, queuedMsg);
    }

    /// <summary>
    /// Saves the bundle pack changes.
    /// </summary>
    [RelayCommand]
    private void Save()
    {
        HasChanges = false;
        var title = localizationService?.GetString("Tools.ModBuilder.Notification.BundlePackSaved.Title") ?? "Bundle Pack Saved";
        var msg = localizationService?.GetString("Tools.ModBuilder.Notification.BundlePackSaved.Message", BundlePackName) ?? $"Changes to '{BundlePackName}' have been saved";
        notificationService.ShowSuccess(title, msg);
    }

    /// <summary>
    /// Cancels the editing and closes the dialog.
    /// </summary>
    [RelayCommand]
    private void Cancel()
    {
        // Dialog will be closed by the view
    }

    /// <summary>
    /// Updates the file statistics.
    /// </summary>
    private void UpdateStatistics()
    {
        TotalFileCount = Files.Count;
        var totalSize = Files.Sum(f => f.FileSize);
        TotalSizeFormatted = FormatFileSize(totalSize);
    }

    /// <summary>
    /// Updates the order property of all files.
    /// </summary>
    private void UpdateOrder()
    {
        for (var i = 0; i < Files.Count; i++)
        {
            Files[i].Order = i;
        }
    }

    /// <summary>
    /// Formats a file size in bytes to a human-readable string.
    /// </summary>
    private static string FormatFileSize(long bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB"];
        var order = 0;
        var size = (double)bytes;

        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }

        return $"{size:F2} {sizes[order]}";
    }

    /// <summary>
    /// Gets the icon key for a file type.
    /// </summary>
    private static string GetIconKeyForFileType(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".tga" or ".dds" or ".psd" or ".png" or ".jpg" => "IconImageFile",
            ".csf" or ".ini" or ".txt" => "IconTextFile",
            ".big" or ".zip" => "IconArchiveFile",
            _ => "IconTextFile"
        };
    }
}
