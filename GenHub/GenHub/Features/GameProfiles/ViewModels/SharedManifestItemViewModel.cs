using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Common.ViewModels;
using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Manifest;

namespace GenHub.Features.GameProfiles.ViewModels;

/// <summary>
/// ViewModel representing a single content manifest item in the import inspection list.
/// </summary>
/// <param name="dependency">The shared manifest dependency model.</param>
public partial class SharedManifestItemViewModel(SharedManifestDependency dependency) : ViewModelBase
{
    [ObservableProperty]
    private bool _isExpanded;

    /// <summary>
    /// Gets the unique identifier of the manifest.
    /// </summary>
    public string ManifestId => dependency.ManifestId;

    /// <summary>
    /// Gets the user-friendly display name of the content.
    /// </summary>
    public string DisplayName => dependency.DisplayName;

    /// <summary>
    /// Gets the version string.
    /// </summary>
    public string Version => dependency.Version;

    /// <summary>
    /// Gets the type of content (Mod, Patch, MapPack, etc.).
    /// </summary>
    public ContentType ContentType => dependency.ContentType;

    /// <summary>
    /// Gets the publisher display name.
    /// </summary>
    public string Publisher => !string.IsNullOrWhiteSpace(dependency.Publisher)
        ? dependency.Publisher
        : !string.IsNullOrWhiteSpace(dependency.PublisherType)
            ? dependency.PublisherType
            : "Community";

    /// <summary>
    /// Gets the download size in bytes.
    /// </summary>
    public long DownloadSize => dependency.DownloadSize;

    /// <summary>
    /// Gets a value indicating whether this manifest is cached in the local CAS pool.
    /// </summary>
    public bool IsCachedLocally => dependency.IsCachedLocally;

    /// <summary>
    /// Gets the cryptographic SHA-256 hash if available.
    /// </summary>
    public string? Hash => !string.IsNullOrWhiteSpace(dependency.Hash)
        ? dependency.Hash
        : dependency.Files.FirstOrDefault(f => !string.IsNullOrWhiteSpace(f.Hash))?.Hash;

    /// <summary>
    /// Gets a truncated hash string for compact badge display.
    /// </summary>
    public string? FormattedHash => Hash != null && Hash.Length > 16
        ? $"{Hash[..8]}...{Hash[^8..]}"
        : Hash;

    /// <summary>
    /// Gets the primary download URL if available.
    /// </summary>
    public string? DownloadUrl => dependency.Files.FirstOrDefault(f => !string.IsNullOrWhiteSpace(f.DownloadUrl))?.DownloadUrl;

    /// <summary>
    /// Gets the list of file entries for this content package.
    /// </summary>
    public IReadOnlyList<ManifestFile> Files => dependency.Files;

    /// <summary>
    /// Gets the number of files in the manifest.
    /// </summary>
    public int FilesCount => dependency.Files.Count;

    /// <summary>
    /// Gets a value indicating whether additional inspection details are available.
    /// </summary>
    public bool HasDetails => !string.IsNullOrWhiteSpace(DownloadUrl) || !string.IsNullOrWhiteSpace(Hash) || FilesCount > 0;

    /// <summary>
    /// Toggles the expanded view state of the manifest card.
    /// </summary>
    [RelayCommand]
    private void ToggleExpand()
    {
        IsExpanded = !IsExpanded;
    }

    /// <summary>
    /// Copies the full SHA-256 hash to the clipboard.
    /// </summary>
    /// <returns>A task representing the clipboard operation.</returns>
    [RelayCommand]
    private async Task CopyHashAsync()
    {
        if (string.IsNullOrWhiteSpace(Hash))
        {
            return;
        }

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } mainWindow })
        {
            var topLevel = TopLevel.GetTopLevel(mainWindow);
            if (topLevel?.Clipboard != null)
            {
                await topLevel.Clipboard.SetTextAsync(Hash);
            }
        }
    }

    /// <summary>
    /// Copies the primary download URL to the clipboard.
    /// </summary>
    /// <returns>A task representing the clipboard operation.</returns>
    [RelayCommand]
    private async Task CopyDownloadUrlAsync()
    {
        if (string.IsNullOrWhiteSpace(DownloadUrl))
        {
            return;
        }

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } mainWindow })
        {
            var topLevel = TopLevel.GetTopLevel(mainWindow);
            if (topLevel?.Clipboard != null)
            {
                await topLevel.Clipboard.SetTextAsync(DownloadUrl);
            }
        }
    }
}
