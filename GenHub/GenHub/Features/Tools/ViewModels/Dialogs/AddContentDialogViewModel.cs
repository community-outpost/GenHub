using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Providers;
using GenHub.Features.Tools.Interfaces;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.ViewModels.Dialogs;

/// <summary>
/// ViewModel for adding or editing a content item in the catalog.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "ViewModel properties and methods bound to MVVM UI and CommunityToolkit ObservableProperty generated properties.")]
public partial class AddContentDialogViewModel(
    Action<CatalogContentItem?> onContentCreated,
    IPublisherStudioDialogService? dialogService = null,
    GenHub.Core.Interfaces.Common.ILocalizationService? localizationService = null) : ObservableValidator, IDisposable
{
    private readonly CatalogContentItem? _existingItem;
    private CancellationTokenSource? _computationCts;

    [ObservableProperty]
    private bool _isEditMode;

    [ObservableProperty]
    [Required(ErrorMessage = "Content ID is required")]
    [RegularExpression(@"^[a-z0-9-]+$", ErrorMessage = "ID must be lowercase alphanumeric with hyphens only")]
    [MinLength(3, ErrorMessage = "ID must be at least 3 characters")]
    [MaxLength(64, ErrorMessage = "ID cannot exceed 64 characters")]
    private string _contentId = string.Empty;

    [ObservableProperty]
    [Required(ErrorMessage = "Content name is required")]
    [MinLength(2, ErrorMessage = "Name must be at least 2 characters")]
    [MaxLength(100, ErrorMessage = "Name cannot exceed 100 characters")]
    private string _contentName = string.Empty;

    [ObservableProperty]
    [Required(ErrorMessage = "Description is required")]
    [MinLength(10, ErrorMessage = "Description must be at least 10 characters")]
    [MaxLength(2000, ErrorMessage = "Description cannot exceed 2000 characters")]
    private string _description = string.Empty;

    [ObservableProperty]
    private ContentType _selectedContentType = ContentType.Mod;

    [ObservableProperty]
    private GameType _selectedTargetGame = GameType.ZeroHour;

    [ObservableProperty]
    private string _tagsInput = string.Empty;

    [ObservableProperty]
    private string? _extendsContentId;

    [ObservableProperty]
    private bool _isValid;

    [ObservableProperty]
    private string? _validationError;

    // Release & Artifact Direct Download / File support
    [ObservableProperty]
    private bool _includeInitialRelease = true;

    [ObservableProperty]
    private string _initialVersion = "1.0.0";

    [ObservableProperty]
    private bool _useDirectUrl = true;

    [ObservableProperty]
    private string? _downloadUrl;

    [ObservableProperty]
    private string? _localFilePath;

    [ObservableProperty]
    private string? _packageFilename;

    [ObservableProperty]
    private long _fileSize;

    [ObservableProperty]
    private string _fileSizeDisplay = string.Empty;

    [ObservableProperty]
    private string? _sha256Hash;

    [ObservableProperty]
    private bool _isComputingHash;

    /// <summary>
    /// Initializes a new instance of the <see cref="AddContentDialogViewModel"/> class in edit mode.
    /// </summary>
    /// <param name="existing">The existing content item to edit.</param>
    /// <param name="onContentSaved">Callback invoked when content is successfully saved.</param>
    /// <param name="dialogService">Optional dialog service for browsing files.</param>
    /// <param name="localizationService">Optional localization service.</param>
    public AddContentDialogViewModel(
        CatalogContentItem existing,
        Action<CatalogContentItem?> onContentSaved,
        IPublisherStudioDialogService? dialogService = null,
        GenHub.Core.Interfaces.Common.ILocalizationService? localizationService = null)
        : this(onContentSaved, dialogService, localizationService)
    {
        ArgumentNullException.ThrowIfNull(existing);

        _existingItem = existing;
        IsEditMode = true;
        ContentId = existing.Id;
        ContentName = existing.Name;
        Description = existing.Description;
        SelectedContentType = existing.ContentType;
        SelectedTargetGame = existing.TargetGame;
        TagsInput = string.Join(", ", existing.Tags);
        ExtendsContentId = existing.ExtendsContentId;
    }

    /// <summary>
    /// Gets available content types for selection.
    /// </summary>
    public static ContentType[] AvailableContentTypes =>
    [
        ContentType.Mod,
        ContentType.Patch,
        ContentType.Addon,
        ContentType.Map,
        ContentType.MapPack,
        ContentType.LanguagePack,
        ContentType.ContentBundle,
        ContentType.Mission,
        ContentType.Skin,
        ContentType.GameClient,
    ];

    /// <summary>
    /// Gets available target games for selection.
    /// </summary>
    public static GameType[] AvailableTargetGames =>
    [
        GameType.Generals,
        GameType.ZeroHour,
    ];

    /// <summary>
    /// Gets a value indicating whether the selected content type is a GameClient.
    /// </summary>
    public bool IsGameClientType => SelectedContentType == ContentType.GameClient;

    /// <summary>
    /// Gets the dialog title based on mode.
    /// </summary>
    public string DialogTitle => IsEditMode
        ? GetLocalizedString("Tools.PublisherStudio.Content.EditTitle", "Edit Content Item")
        : GetLocalizedString("Tools.PublisherStudio.Content.AddTitle", "Add Content Item");

    /// <summary>
    /// Gets the primary action button text based on mode.
    /// </summary>
    public string ActionButtonText => IsEditMode
        ? GetLocalizedString("Tools.PublisherStudio.Common.SaveChanges", "Save Changes")
        : GetLocalizedString("Tools.PublisherStudio.Content.CreateContent", "Create Content");

    /// <summary>
    /// Gets the submit button text for view binding.
    /// </summary>
    public string SubmitButtonText => ActionButtonText;

    /// <summary>
    /// Gets a suggested content ID based on the entered name.
    /// </summary>
    public string SuggestedContentId => GenerateContentId(ContentName);

    /// <summary>
    /// Gets a value indicating whether the content type can extend another.
    /// </summary>
    public bool CanExtend => SelectedContentType == ContentType.Addon;

    /// <summary>
    /// Gets a value indicating whether the addon parent selection field should be shown.
    /// </summary>
    public bool ShowAddonParentSelection => CanExtend;

    /// <summary>
    /// Populates content item fields from a local directory or file path.
    /// If ContentName or ContentId are empty, auto-fills them.
    /// </summary>
    /// <param name="path">Path to the folder or archive file.</param>
    public void PopulateFromPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        path = path.Trim('"', '\'', ' ');

        LocalFilePath = path;
        UseDirectUrl = false;
        IncludeInitialRelease = true;

        if (!PopulateFileSystemInfo(path, out var baseName))
        {
            return;
        }

        // Auto-fill ContentName if empty
        if (string.IsNullOrWhiteSpace(ContentName))
        {
            ContentName = FormatContentNameFromBaseName(baseName);
        }

        // Auto-fill ContentId if empty
        if (string.IsNullOrWhiteSpace(ContentId))
        {
            ContentId = GenerateContentId(ContentName);
        }

        // Auto-fill Description if empty
        if (string.IsNullOrWhiteSpace(Description))
        {
            Description = $"{ContentName} package for {SelectedTargetGame}.";
        }

        // Intelligently infer ContentType from extension or name
        var inferredType = InferContentType(path, baseName);
        if (inferredType.HasValue)
        {
            SelectedContentType = inferredType.Value;
        }

        Validate();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases unmanaged and - optionally - managed resources.
    /// </summary>
    /// <param name="disposing"><c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _computationCts?.Cancel();
            _computationCts?.Dispose();
            _computationCts = null;
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        int counter = 0;
        decimal number = bytes;
        while (Math.Round(number / 1024) >= 1 && counter < suffixes.Length - 1)
        {
            number /= 1024;
            counter++;
        }

        return $"{number:n1} {suffixes[counter]}";
    }

    /// <summary>
    /// Generates a content ID from the given name.
    /// </summary>
    /// <param name="name">The content name.</param>
    /// <returns>A URL-friendly content ID.</returns>
    private static string GenerateContentId(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        // Convert to lowercase, replace spaces with hyphens, remove invalid chars
        var id = name.ToLowerInvariant().Trim();
        id = Regex.Replace(id, @"\s+", "-", RegexOptions.None, TimeSpan.FromSeconds(1));
        id = Regex.Replace(id, @"[^a-z0-9-]", string.Empty, RegexOptions.None, TimeSpan.FromSeconds(1));
        id = Regex.Replace(id, @"-+", "-", RegexOptions.None, TimeSpan.FromSeconds(1));
        id = id.Trim('-');

        return id;
    }

    /// <summary>
    /// Parses a comma or semicolon separated string of tags.
    /// </summary>
    /// <param name="input">The input string.</param>
    /// <returns>A list of tags.</returns>
    private static List<string> ParseTags(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return [];
        }

        return input
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim().ToLowerInvariant())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct()
            .ToList();
    }

    private static string FormatContentNameFromBaseName(string baseName)
    {
        var humanized = Regex.Replace(baseName, @"[-_]+", " ", RegexOptions.None, TimeSpan.FromSeconds(1)).Trim();
        if (string.IsNullOrWhiteSpace(humanized))
        {
            return baseName;
        }

        var words = humanized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", words.Select(w => char.ToUpperInvariant(w[0]) + (w.Length > 1 ? w[1..] : string.Empty)));
    }

    private static ContentType? InferContentType(string path, string baseName)
    {
        var ext = Path.GetExtension(path);
        if (ext.Equals(".map", StringComparison.OrdinalIgnoreCase) || baseName.Contains("map", StringComparison.OrdinalIgnoreCase))
        {
            return ContentType.Map;
        }

        if (baseName.Contains("patch", StringComparison.OrdinalIgnoreCase))
        {
            return ContentType.Patch;
        }

        if (baseName.Contains("addon", StringComparison.OrdinalIgnoreCase) || baseName.Contains("tool", StringComparison.OrdinalIgnoreCase))
        {
            return ContentType.Addon;
        }

        return null;
    }

    partial void OnSelectedContentTypeChanged(ContentType value)
    {
        OnPropertyChanged(nameof(CanExtend));
        OnPropertyChanged(nameof(ShowAddonParentSelection));
        OnPropertyChanged(nameof(IsGameClientType));
        if (value == ContentType.GameClient)
        {
            UseDirectUrl = true;
            LocalFilePath = null;
            FileSize = 0;
            FileSizeDisplay = string.Empty;
            Sha256Hash = null;
        }
    }

    partial void OnUseDirectUrlChanged(bool value)
    {
        if (value)
        {
            LocalFilePath = null;
            FileSize = 0;
            FileSizeDisplay = string.Empty;
            Sha256Hash = null;
        }
        else
        {
            DownloadUrl = null;
        }
    }

    partial void OnDownloadUrlChanged(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        if (Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri))
        {
            var name = Path.GetFileName(uri.LocalPath);
            if (!string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(PackageFilename))
            {
                PackageFilename = name;
            }
        }
    }

    private bool PopulateFileSystemInfo(string path, out string baseName)
    {
        _computationCts?.Cancel();
        _computationCts?.Dispose();
        _computationCts = new CancellationTokenSource();
        var ct = _computationCts.Token;

        if (Directory.Exists(path))
        {
            var dirInfo = new DirectoryInfo(path);
            baseName = dirInfo.Name;
            PackageFilename = $"{baseName}.zip";

            FileSize = 0;
            FileSizeDisplay = "Folder (calculating size...)";
            Sha256Hash = string.Empty;

            _ = ComputeFolderSizeAsync(path, ct);
            return true;
        }

        if (File.Exists(path))
        {
            var fileInfo = new FileInfo(path);
            baseName = Path.GetFileNameWithoutExtension(path);
            PackageFilename = fileInfo.Name;
            FileSize = fileInfo.Length;
            FileSizeDisplay = FormatBytes(fileInfo.Length);
            _ = ComputeSha256Async(path, ct);
            return true;
        }

        baseName = string.Empty;
        return false;
    }

    /// <summary>
    /// Browses for a local archive file (.zip, .big, .7z, etc.).
    /// </summary>
    [RelayCommand]
    private async Task BrowseLocalFileAsync()
    {
        if (dialogService == null) return;

        var filePath = await dialogService.ShowFilePickerAsync(
            GetLocalizedString("Tools.PublisherStudio.Content.SelectArchiveTitle", "Select Content Archive File"));
        if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
        {
            PopulateFromPath(filePath);
        }
    }

    /// <summary>
    /// Browses for a local content folder.
    /// </summary>
    [RelayCommand]
    private async Task BrowseLocalFolderAsync()
    {
        if (dialogService == null) return;

        var folderPath = await dialogService.ShowFolderPickerAsync(
            GetLocalizedString("Tools.PublisherStudio.Content.SelectFolderTitle", "Select Content Folder"));
        if (!string.IsNullOrEmpty(folderPath) && Directory.Exists(folderPath))
        {
            PopulateFromPath(folderPath);
        }
    }

    private async Task ComputeFolderSizeAsync(string folderPath, CancellationToken ct)
    {
        try
        {
            var totalBytes = await Task.Run(
                () =>
                {
                    var dirInfo = new DirectoryInfo(folderPath);
                    long sum = 0;
                    foreach (var file in dirInfo.EnumerateFiles("*", SearchOption.AllDirectories))
                    {
                        ct.ThrowIfCancellationRequested();
                        sum += file.Length;
                    }

                    return sum;
                },
                ct);

            if (!ct.IsCancellationRequested && string.Equals(LocalFilePath, folderPath, StringComparison.OrdinalIgnoreCase))
            {
                FileSize = totalBytes;
                FileSizeDisplay = $"{FormatBytes(totalBytes)} (folder)";
            }
        }
        catch (OperationCanceledException)
        {
            // Calculation canceled
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (!ct.IsCancellationRequested && string.Equals(LocalFilePath, folderPath, StringComparison.OrdinalIgnoreCase))
            {
                FileSize = 0;
                FileSizeDisplay = "Folder (size unavailable)";
            }
        }
    }

    private async Task ComputeSha256Async(string filePath, CancellationToken ct)
    {
        try
        {
            IsComputingHash = true;
            using var stream = File.OpenRead(filePath);
            using var sha256 = SHA256.Create();
            var hashBytes = await sha256.ComputeHashAsync(stream, ct);
            if (!ct.IsCancellationRequested)
            {
                Sha256Hash = Convert.ToHexString(hashBytes).ToLowerInvariant();
            }
        }
        catch (OperationCanceledException)
        {
            // Calculation canceled
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (!ct.IsCancellationRequested)
            {
                Sha256Hash = string.Empty;
            }
        }
        finally
        {
            IsComputingHash = false;
        }
    }

    /// <summary>
    /// Applies the suggested content ID.
    /// </summary>
    [RelayCommand]
    private void ApplySuggestedId()
    {
        if (!string.IsNullOrWhiteSpace(ContentName))
        {
            ContentId = SuggestedContentId;
        }
    }

    /// <summary>
    /// Closes the dialog without saving.
    /// </summary>
    [RelayCommand]
    private void Close()
    {
        onContentCreated(null);
    }

    /// <summary>
    /// Creates the content item if validation passes.
    /// </summary>
    [RelayCommand]
    private void CreateContent()
    {
        ValidateAllProperties();

        if (HasErrors)
        {
            ValidationError = string.Join(Environment.NewLine, GetErrors().Select(e => e.ErrorMessage));
            IsValid = false;
            return;
        }

        var tags = ParseTags(TagsInput);

        var contentItem = new CatalogContentItem
        {
            Id = ContentId.ToLowerInvariant().Trim(),
            Name = ContentName.Trim(),
            Description = Description.Trim(),
            ContentType = SelectedContentType,
            TargetGame = SelectedTargetGame,
            Tags = [.. tags],
            ExtendsContentId = SelectedContentType == ContentType.Addon ? ExtendsContentId : null,
        };

        if (!IsEditMode && IncludeInitialRelease)
        {
            if (!ValidateInitialRelease())
            {
                return;
            }

            AttachInitialRelease(contentItem);
        }
        else if (IsEditMode)
        {
            CopyFromExistingItem(contentItem);
        }

        onContentCreated(contentItem);
    }

    private string DetermineArtifactName(string contentId, string version)
    {
        if (!string.IsNullOrWhiteSpace(PackageFilename))
        {
            return PackageFilename.Trim();
        }

        if (!string.IsNullOrWhiteSpace(LocalFilePath))
        {
            return Path.GetFileName(LocalFilePath);
        }

        return $"{contentId}-{version}.zip";
    }

    private bool ValidateInitialRelease()
    {
        if (!string.IsNullOrWhiteSpace(DownloadUrl))
        {
            if (!Uri.TryCreate(DownloadUrl.Trim(), UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                ValidationError = "Download URL must be a valid HTTP or HTTPS link.";
                IsValid = false;
                return false;
            }
        }
        else if (!string.IsNullOrWhiteSpace(LocalFilePath) &&
                 !System.IO.File.Exists(LocalFilePath) &&
                 !System.IO.Directory.Exists(LocalFilePath))
        {
            ValidationError = $"Local path does not exist: {LocalFilePath}";
            IsValid = false;
            return false;
        }

        return true;
    }

    private void AttachInitialRelease(CatalogContentItem contentItem)
    {
        var version = string.IsNullOrWhiteSpace(InitialVersion) ? "1.0.0" : InitialVersion.Trim();
        var release = new ContentRelease
        {
            Version = version,
            ReleaseDate = DateTime.UtcNow,
            IsLatest = true,
            Artifacts = [],
        };

        var artifactName = DetermineArtifactName(contentItem.Id, version);

        var artifact = new ReleaseArtifact
        {
            Filename = artifactName,
            DownloadUrl = UseDirectUrl ? (DownloadUrl?.Trim() ?? string.Empty) : string.Empty,
            LocalFilePath = UseDirectUrl ? null : LocalFilePath,
            Size = UseDirectUrl ? 0 : FileSize,
            Sha256 = UseDirectUrl ? string.Empty : (Sha256Hash?.Trim() ?? string.Empty),
            IsPrimary = true,
        };

        release.Artifacts.Add(artifact);
        contentItem.Releases.Add(release);
    }

    private void CopyFromExistingItem(CatalogContentItem contentItem)
    {
        if (_existingItem == null) return;

        // Preserve existing releases & dependencies
        foreach (var rel in _existingItem.Releases)
        {
            contentItem.Releases.Add(rel);
        }

        foreach (var dep in _existingItem.BundledItems)
        {
            contentItem.BundledItems.Add(dep);
        }
    }

    partial void OnContentIdChanged(string value) => Validate();

    partial void OnContentNameChanged(string value) => Validate();

    partial void OnDescriptionChanged(string value) => Validate();

    private void Validate()
    {
        ValidateAllProperties();
        IsValid = !HasErrors;
        ValidationError = HasErrors
            ? string.Join(Environment.NewLine, GetErrors().Select(e => e.ErrorMessage))
            : null;
    }

    private string GetLocalizedString(string key, string fallback)
    {
        return localizationService?.GetString(key) ?? fallback;
    }
}
