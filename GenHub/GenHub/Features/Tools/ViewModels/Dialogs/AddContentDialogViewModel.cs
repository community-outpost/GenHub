using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Common.Validation;
using GenHub.Core.Helpers;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Providers;
using GenHub.Features.Tools.Interfaces;
using System;
using System.Collections.Generic;
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
    /// <summary>
    /// Artwork browse target for the content icon.
    /// </summary>
    public const string ArtworkTargetIcon = "Icon";

    /// <summary>
    /// Artwork browse target for the content banner.
    /// </summary>
    public const string ArtworkTargetBanner = "Banner";

    /// <summary>
    /// Artwork browse target for the content backdrop cover.
    /// </summary>
    public const string ArtworkTargetBackdrop = "Backdrop";

    private readonly CatalogContentItem? _existingItem;
    private CancellationTokenSource? _computationCts;

    private int _hashGeneration;

    [ObservableProperty]
    private bool _isEditMode;

    [ObservableProperty]
    [LocalizedRequired("Tools.PublisherStudio.Validation.ContentIdRequired", "Content ID is required")]
    [LocalizedRegularExpression(@"^[a-z0-9-]+$", "Tools.PublisherStudio.Validation.ContentIdPattern", "ID must be lowercase alphanumeric with hyphens only")]
    [LocalizedMinLength(3, "Tools.PublisherStudio.Validation.ContentIdMinLength", "ID must be at least 3 characters")]
    [LocalizedMaxLength(64, "Tools.PublisherStudio.Validation.ContentIdMaxLength", "ID cannot exceed 64 characters")]
    private string _contentId = string.Empty;

    [ObservableProperty]
    [LocalizedRequired("Tools.PublisherStudio.Validation.ContentNameRequired", "Content name is required")]
    [LocalizedMinLength(2, "Tools.PublisherStudio.Validation.ContentNameMinLength", "Name must be at least 2 characters")]
    [LocalizedMaxLength(100, "Tools.PublisherStudio.Validation.ContentNameMaxLength", "Name cannot exceed 100 characters")]
    private string _contentName = string.Empty;

    [ObservableProperty]
    [LocalizedRequired("Tools.PublisherStudio.Validation.DescriptionRequired", "Description is required")]
    [LocalizedMinLength(10, "Tools.PublisherStudio.Validation.DescriptionMinLength", "Description must be at least 10 characters")]
    [LocalizedMaxLength(2000, "Tools.PublisherStudio.Validation.DescriptionMaxLength", "Description cannot exceed 2000 characters")]
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
    private string? _iconArtwork;

    [ObservableProperty]
    private string? _bannerArtwork;

    [ObservableProperty]
    private string? _backdropArtwork;

    [ObservableProperty]
    private string? _accentColor;

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
        IconArtwork = existing.Metadata?.IconUrl;
        BannerArtwork = existing.Metadata?.BannerUrl;
        BackdropArtwork = existing.Metadata?.BackdropUrl;
        AccentColor = existing.Metadata?.AccentColor;
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
        if (IsGameClientPath(path, baseName))
        {
            return ContentType.GameClient;
        }

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

    private static bool IsGameClientPath(string path, string baseName)
    {
        var ext = Path.GetExtension(path);
        if (ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".msi", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string[] clientKeywords =
        [
            "gameclient",
            "game client",
            "generalsonline",
            "generals online",
            "genlauncher",
            "cnconline",
            "cnc online",
        ];

        return clientKeywords.Any(keyword =>
            baseName.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
            path.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private static ContentRelease CloneRelease(ContentRelease source)
    {
        return new ContentRelease
        {
            Version = source.Version,
            ReleaseDate = source.ReleaseDate,
            IsPrerelease = source.IsPrerelease,
            IsLatest = source.IsLatest,
            IsFeatured = source.IsFeatured,
            Changelog = source.Changelog,
            Artifacts = source.Artifacts.Select(CloneArtifact).ToList(),
            Dependencies = source.Dependencies.Select(CloneDependency).ToList(),
        };
    }

    private static ReleaseArtifact CloneArtifact(ReleaseArtifact source)
    {
        return new ReleaseArtifact
        {
            Filename = source.Filename,
            DownloadUrl = source.DownloadUrl,
            Size = source.Size,
            Sha256 = source.Sha256,
            ContentType = source.ContentType,
            IsPrimary = source.IsPrimary,
            VariantAxis = source.VariantAxis,
            Variant = source.Variant,
            IsDefaultVariant = source.IsDefaultVariant,
            LocalFilePath = source.LocalFilePath,
        };
    }

    private static CatalogDependency CloneDependency(CatalogDependency source)
    {
        return new CatalogDependency
        {
            PublisherId = source.PublisherId,
            ContentId = source.ContentId,
            VersionConstraint = source.VersionConstraint,
            IsOptional = source.IsOptional,
            ContentType = source.ContentType,
            CatalogUrl = source.CatalogUrl,
            DependencyType = source.DependencyType,
            DefinitionUrl = source.DefinitionUrl,
            ConflictsWith = [.. source.ConflictsWith],
        };
    }

    partial void OnSelectedContentTypeChanged(ContentType value)
    {
        _ = value;
        OnPropertyChanged(nameof(CanExtend));
        OnPropertyChanged(nameof(ShowAddonParentSelection));
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
        _hashGeneration++;

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

    /// <summary>
    /// Browses for a local artwork image and assigns it to the requested artwork slot.
    /// Local files are uploaded to the hosting provider during catalog publish.
    /// </summary>
    /// <param name="target">One of the <c>ArtworkTarget*</c> slot names.</param>
    [RelayCommand]
    private async Task BrowseArtworkAsync(string? target)
    {
        if (dialogService == null || string.IsNullOrEmpty(target))
        {
            return;
        }

        var filePath = await dialogService.ShowFilePickerAsync(
            GetLocalizedString("Tools.PublisherStudio.Content.SelectArtworkTitle", "Select Artwork Image"));
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            return;
        }

        switch (target)
        {
            case ArtworkTargetIcon:
                IconArtwork = filePath;
                break;
            case ArtworkTargetBanner:
                BannerArtwork = filePath;
                break;
            case ArtworkTargetBackdrop:
                BackdropArtwork = filePath;
                break;
            default:
                break;
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
        var generation = _hashGeneration;
        try
        {
            IsComputingHash = true;
            using var stream = File.OpenRead(filePath);
            using var sha256 = SHA256.Create();
            var hashBytes = await sha256.ComputeHashAsync(stream, ct);
            if (!ct.IsCancellationRequested && generation == _hashGeneration)
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
            if (!ct.IsCancellationRequested && generation == _hashGeneration)
            {
                Sha256Hash = string.Empty;
            }
        }
        finally
        {
            if (generation == _hashGeneration)
            {
                IsComputingHash = false;
            }
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

        if (IsComputingHash)
        {
            ValidationError = GetLocalizedString(
                "Tools.PublisherStudio.Artifact.HashInProgress",
                "Hash computation is still in progress. Please wait.");
            IsValid = false;
            return;
        }

        if (!ValidateArtwork())
        {
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
            Metadata = MergeArtworkMetadata(),
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

    private bool IsRemoteArtworkUrl(string value)
    {
        return Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private string? NormalizeArtworkValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private bool ValidateArtwork()
    {
        var slots = new (string? Value, string Name)[]
        {
            (IconArtwork, GetLocalizedString("Tools.PublisherStudio.Content.IconArtwork", "Icon")),
            (BannerArtwork, GetLocalizedString("Tools.PublisherStudio.Content.BannerArtwork", "Banner")),
            (BackdropArtwork, GetLocalizedString("Tools.PublisherStudio.Content.BackdropArtwork", "Backdrop cover")),
        };

        foreach (var (value, name) in slots)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (!IsRemoteArtworkUrl(value) && !File.Exists(value))
            {
                ValidationError = string.Format(
                    GetLocalizedString(
                        "Tools.PublisherStudio.Validation.ArtworkPathInvalidFormat",
                        "{0} must be an HTTPS URL or an existing local image file."),
                    name);
                IsValid = false;
                return false;
            }
        }

        if (!string.IsNullOrWhiteSpace(AccentColor) && !ContentCardBadgeHelper.IsValidAccentColor(AccentColor))
        {
            ValidationError = GetLocalizedString(
                "Tools.PublisherStudio.Validation.AccentColorInvalid",
                "Accent color must be a hex color like #7C3AED.");
            IsValid = false;
            return false;
        }

        return true;
    }

    private ContentRichMetadata? MergeArtworkMetadata()
    {
        var icon = NormalizeArtworkValue(IconArtwork);
        var banner = NormalizeArtworkValue(BannerArtwork);
        var backdrop = NormalizeArtworkValue(BackdropArtwork);
        var accent = NormalizeArtworkValue(AccentColor);
        var source = _existingItem?.Metadata;

        if (icon == null && banner == null && backdrop == null && accent == null && source == null)
        {
            return null;
        }

        return new ContentRichMetadata
        {
            IconUrl = icon,
            BannerUrl = banner,
            BackdropUrl = backdrop,
            AccentColor = accent,
            ScreenshotUrls = source != null ? [.. source.ScreenshotUrls] : [],
            VideoUrl = source?.VideoUrl,
            DocumentationUrl = source?.DocumentationUrl,
            Author = source?.Author,
            License = source?.License,
            Category = source?.Category,
            PlayerCount = source?.PlayerCount,
        };
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
            ContentType = MimeTypeHelper.FromFileName(artifactName),
            IsPrimary = true,
        };

        release.Artifacts.Add(artifact);
        contentItem.Releases.Add(release);
    }

    private void CopyFromExistingItem(CatalogContentItem contentItem)
    {
        if (_existingItem == null)
        {
            return;
        }

        // Preserve existing releases & dependencies as deep copies so the edited
        // item never aliases the source item's mutable lists.
        foreach (var release in _existingItem.Releases)
        {
            contentItem.Releases.Add(CloneRelease(release));
        }

        foreach (var dependency in _existingItem.BundledItems)
        {
            contentItem.BundledItems.Add(CloneDependency(dependency));
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
