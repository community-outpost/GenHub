using System;
using System.Collections.Generic;
using System.IO;
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
    public string Publisher
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(dependency.Publisher))
            {
                return dependency.Publisher;
            }

            if (!string.IsNullOrWhiteSpace(dependency.PublisherType))
            {
                return dependency.PublisherType;
            }

            return "Community";
        }
    }

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
    public string? FormattedHash => Hash is { Length: > 16 }
        ? $"{Hash[..8]}...{Hash[^8..]}"
        : Hash;

    /// <summary>
    /// Gets the primary download URL if available.
    /// </summary>
    public string? DownloadUrl => !string.IsNullOrWhiteSpace(dependency.PackageUrl)
        ? dependency.PackageUrl
        : dependency.Files.FirstOrDefault(f => !string.IsNullOrWhiteSpace(f.DownloadUrl))?.DownloadUrl;

    /// <summary>
    /// Gets the list of file entries for this content package.
    /// </summary>
    public IReadOnlyList<ManifestFile> Files => dependency.Files;

    /// <summary>
    /// Gets the number of files in the manifest.
    /// </summary>
    public int FilesCount => dependency.Files.Count;

    /// <summary>
    /// Gets a value indicating whether this component package contains executable binaries (.exe, .dll, .asi, .bat, .cmd).
    /// </summary>
    public bool ContainsExecutables => dependency.Files.Any(IsExecutableFile);

    /// <summary>
    /// Gets the count of executable files contained in this component.
    /// </summary>
    public int ExecutableFilesCount => dependency.Files.Count(IsExecutableFile);

    /// <summary>
    /// Gets a value indicating whether this package originates from a temporary cloud upload.
    /// </summary>
    public bool IsCloudPackage => !string.IsNullOrWhiteSpace(DownloadUrl) &&
        (DownloadUrl.Contains(ApiConstants.UploadThingUrlFragment, StringComparison.OrdinalIgnoreCase) ||
         DownloadUrl.Contains(ApiConstants.UploadThingUfsUrlFragment, StringComparison.OrdinalIgnoreCase) ||
         DownloadUrl.Contains(ApiConstants.UploadThingUfsShortUrlFragment, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Gets the provenance description of the content source.
    /// </summary>
    public string Provenance => GetProvenance();

    /// <summary>
    /// Gets the detailed file view models for inspector display.
    /// </summary>
    public IReadOnlyList<ManifestFileItemViewModel> FileItems { get; } =
        dependency.Files.Select(f => new ManifestFileItemViewModel(f)).ToList();

    /// <summary>
    /// Gets a value indicating whether additional inspection details are available.
    /// </summary>
    public bool HasDetails => !string.IsNullOrWhiteSpace(DownloadUrl) || !string.IsNullOrWhiteSpace(Hash) || FilesCount > 0;

    private static bool IsExecutableFile(ManifestFile file)
    {
        if (string.IsNullOrWhiteSpace(file.RelativePath))
        {
            return false;
        }

        var ext = Path.GetExtension(file.RelativePath);
        return ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".dll", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".asi", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".bat", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".cmd", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".ps1", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".vbs", StringComparison.OrdinalIgnoreCase);
    }

    private string GetProvenance()
    {
        if (IsCloudPackage)
        {
            return "GenHub Cloud (UploadThing)";
        }

        return !string.IsNullOrWhiteSpace(dependency.Publisher)
            ? dependency.Publisher
            : "Community";
    }

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
