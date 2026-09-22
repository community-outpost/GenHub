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
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
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
    GenHub.Core.Interfaces.Common.ILocalizationService? localizationService = null,
    PublisherCatalog? catalog = null) : ObservableValidator, IDisposable
{
    /// <summary>
    /// A local file or folder staged for the initial release.
    /// Each staged entry becomes one release artifact; multiple entries are
    /// installed together as one package.
    /// </summary>
    public partial class StagedContentFile : ObservableObject
    {
        [ObservableProperty]
        private string _localPath = string.Empty;

        [ObservableProperty]
        private string _displayName = string.Empty;

        [ObservableProperty]
        private bool _isFolder;

        [ObservableProperty]
        private bool _isArchive;

        [ObservableProperty]
        private long _fileSizeBytes;

        [ObservableProperty]
        private string _fileSizeDisplay = string.Empty;

        [ObservableProperty]
        private string? _sha256Hash;

        [ObservableProperty]
        private bool _isComputingHash;

        [ObservableProperty]
        private string? _archiveNoteText;

        /// <summary>
        /// Gets or sets the cancellation source for the entry's background computation.
        /// </summary>
        internal CancellationTokenSource? ComputeCts { get; set; }
    }

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
    private bool _syncingPrimary;
    private bool _disposed;

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
    private string? _videoUrl;

    [ObservableProperty]
    private string _screenshotUrlsInput = string.Empty;

    /// <summary>
    /// Gets the list of screenshot URLs for the content item.
    /// </summary>
    public ObservableCollection<string> Screenshots { get; } = [];

    /// <summary>
    /// Gets the list of video URLs for the content item.
    /// </summary>
    public ObservableCollection<string> Videos { get; } = [];

    [ObservableProperty]
    private string? _newScreenshotUrl;

    [ObservableProperty]
    private string? _newVideoUrl;

    // Rich Initial Release properties
    [ObservableProperty]
    private string? _releaseChangelog;

    [ObservableProperty]
    private bool _bundleArtifacts = true;

    [ObservableProperty]
    private bool _isVariantsMode;

    /// <summary>
    /// Gets explicitly added release artifacts.
    /// </summary>
    public ObservableCollection<ReleaseArtifact> ReleaseArtifacts { get; } = [];

    /// <summary>
    /// Gets release dependencies.
    /// </summary>
    public ObservableCollection<CatalogDependency> ReleaseDependencies { get; } = [];

    [ObservableProperty]
    private string? _releaseImageUrlsInput;

    [ObservableProperty]
    private string? _releaseVideoUrlsInput;

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
    /// <param name="catalog">Optional parent catalog.</param>
    public AddContentDialogViewModel(
        CatalogContentItem existing,
        Action<CatalogContentItem?> onContentSaved,
        IPublisherStudioDialogService? dialogService = null,
        GenHub.Core.Interfaces.Common.ILocalizationService? localizationService = null,
        PublisherCatalog? catalog = null)
        : this(onContentSaved, dialogService, localizationService, catalog)
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
        VideoUrl = existing.Metadata?.VideoUrl;
        ScreenshotUrlsInput = existing.Metadata?.ScreenshotUrls is { Count: > 0 }
            ? string.Join(Environment.NewLine, existing.Metadata.ScreenshotUrls)
            : string.Empty;

        if (existing.Metadata?.ScreenshotUrls != null)
        {
            foreach (var shot in existing.Metadata.ScreenshotUrls)
            {
                if (!string.IsNullOrWhiteSpace(shot))
                {
                    Screenshots.Add(shot);
                }
            }
        }

        if (existing.Metadata?.VideoUrls != null)
        {
            foreach (var vid in existing.Metadata.VideoUrls)
            {
                if (!string.IsNullOrWhiteSpace(vid))
                {
                    Videos.Add(vid);
                }
            }
        }
        else if (!string.IsNullOrWhiteSpace(existing.Metadata?.VideoUrl))
        {
            Videos.Add(existing.Metadata.VideoUrl);
        }
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
    /// Gets curated accent color presets for the color picker.
    /// </summary>
    public static IReadOnlyList<string> AccentColorPresets =>
    [
        "#7C3AED",
        "#4F46E5",
        "#2563EB",
        "#0891B2",
        "#0D9488",
        "#059669",
        "#65A30D",
        "#CA8A04",
        "#EA580C",
        "#DC2626",
        "#DB2777",
        "#64748B",
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
    /// Gets the local files and folders staged for the initial release.
    /// Each entry becomes one release artifact uploaded separately.
    /// </summary>
    public ObservableCollection<StagedContentFile> StagedFiles { get; } = [];

    /// <summary>
    /// Gets a value indicating whether any local files are staged.
    /// </summary>
    public bool HasStagedFiles => StagedFiles.Count > 0;

    /// <summary>
    /// Gets a value indicating whether multiple local files are staged for upload.
    /// </summary>
    public bool IsMultiFileStaging => !UseDirectUrl && StagedFiles.Count > 1;

    /// <summary>
    /// Gets the localized note explaining multi-file installs, or null for single files.
    /// </summary>
    public string? MultiFileBundleNote => IsMultiFileStaging
        ? string.Format(
            GetLocalizedString(
                "Tools.PublisherStudio.Content.MultiFileBundleNoteFormat",
                "{0} files will be uploaded separately and installed together as one package."),
            StagedFiles.Count)
        : null;

    /// <summary>
    /// Populates content item fields from a local directory or file path.
    /// If ContentName or ContentId are empty, auto-fills them.
    /// </summary>
    /// <param name="path">Path to the folder or archive file.</param>
    public void PopulateFromPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        StagePaths([path], autofill: true);
    }

    /// <summary>
    /// Stages multiple local directories or file paths for the initial release.
    /// Empty content fields are auto-filled from the first staged entry.
    /// </summary>
    /// <param name="paths">Paths to stage.</param>
    public void PopulateFromPaths(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        StagePaths(paths, autofill: true);
    }

    /// <summary>
    /// Adds screenshots from dropped file paths with duplicate detection.
    /// </summary>
    /// <param name="paths">The dropped file or directory paths.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task AddScreenshotsFromPathsAsync(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            var cleanPath = path.Trim();
            if (File.Exists(cleanPath))
            {
                string sha256 = string.Empty;
                try
                {
                    using var stream = File.OpenRead(cleanPath);
                    using var sha = SHA256.Create();
                    var hashBytes = await sha.ComputeHashAsync(stream);
                    sha256 = Convert.ToHexString(hashBytes).ToLowerInvariant();
                }
                catch
                {
                    // ignore
                }

                var targetUrl = cleanPath;
                if (!string.IsNullOrEmpty(sha256) && dialogService?.DuplicateAssetLookup != null)
                {
                    var match = dialogService.DuplicateAssetLookup(sha256);
                    if (match.HasValue)
                    {
                        var title = GetLocalizedString("Tools.PublisherStudio.Duplicate.Title", "Duplicate File Detected");
                        var prompt = string.Format(
                            GetLocalizedString(
                                "Tools.PublisherStudio.Duplicate.MessageFormat",
                                "We found an identical file already hosted on your provider:\n• Name: {0}\n• URL: {1}\n\nWould you like to use this existing hosted file instead of uploading a new copy?"),
                            match.Value.Name,
                            match.Value.Url);
                        var confirmText = GetLocalizedString("Tools.PublisherStudio.Duplicate.UseExisting", "Use Existing File");
                        var cancelText = GetLocalizedString("Tools.PublisherStudio.Duplicate.UploadNew", "Upload New Copy");

                        var useExisting = await dialogService.ShowConfirmationAsync(title, prompt, confirmText, cancelText);
                        if (useExisting)
                        {
                            targetUrl = match.Value.Url;
                        }
                    }
                }

                if (!Screenshots.Contains(targetUrl, StringComparer.OrdinalIgnoreCase))
                {
                    Screenshots.Add(targetUrl);
                }
            }
            else if (Uri.TryCreate(cleanPath, UriKind.Absolute, out _))
            {
                if (!Screenshots.Contains(cleanPath, StringComparer.OrdinalIgnoreCase))
                {
                    Screenshots.Add(cleanPath);
                }
            }
        }
    }

    /// <summary>
    /// Adds release images from dropped paths with duplicate detection.
    /// </summary>
    /// <param name="paths">The dropped file or directory paths.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task AddReleaseImagesFromPathsAsync(IEnumerable<string> paths)
    {
        var existing = ParseUrls(ReleaseImageUrlsInput);
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            var cleanPath = path.Trim();
            if (File.Exists(cleanPath))
            {
                string sha256 = string.Empty;
                try
                {
                    using var stream = File.OpenRead(cleanPath);
                    using var sha = SHA256.Create();
                    var hashBytes = await sha.ComputeHashAsync(stream);
                    sha256 = Convert.ToHexString(hashBytes).ToLowerInvariant();
                }
                catch
                {
                    // ignore
                }

                var targetUrl = cleanPath;
                if (!string.IsNullOrEmpty(sha256) && dialogService?.DuplicateAssetLookup != null)
                {
                    var match = dialogService.DuplicateAssetLookup(sha256);
                    if (match.HasValue)
                    {
                        var title = GetLocalizedString("Tools.PublisherStudio.Duplicate.Title", "Duplicate File Detected");
                        var prompt = string.Format(
                            GetLocalizedString(
                                "Tools.PublisherStudio.Duplicate.MessageFormat",
                                "We found an identical file already hosted on your provider:\n• Name: {0}\n• URL: {1}\n\nWould you like to use this existing hosted file instead of uploading a new copy?"),
                            match.Value.Name,
                            match.Value.Url);
                        var confirmText = GetLocalizedString("Tools.PublisherStudio.Duplicate.UseExisting", "Use Existing File");
                        var cancelText = GetLocalizedString("Tools.PublisherStudio.Duplicate.UploadNew", "Upload New Copy");

                        var useExisting = await dialogService.ShowConfirmationAsync(title, prompt, confirmText, cancelText);
                        if (useExisting)
                        {
                            targetUrl = match.Value.Url;
                        }
                    }
                }

                if (!existing.Contains(targetUrl, StringComparer.OrdinalIgnoreCase))
                {
                    existing.Add(targetUrl);
                }
            }
            else if (Uri.TryCreate(cleanPath, UriKind.Absolute, out _))
            {
                if (!existing.Contains(cleanPath, StringComparer.OrdinalIgnoreCase))
                {
                    existing.Add(cleanPath);
                }
            }
        }

        ReleaseImageUrlsInput = string.Join(Environment.NewLine, existing);
    }

    /// <summary>
    /// Adds release artifacts from dropped paths with duplicate detection.
    /// </summary>
    /// <param name="paths">The dropped file or directory paths.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task AddReleaseArtifactsFromPathsAsync(IEnumerable<string> paths)
    {
        foreach (var rawPath in paths)
        {
            if (string.IsNullOrWhiteSpace(rawPath)) continue;
            var path = rawPath.Trim('"', '\x27', ' ');
            if (File.Exists(path))
            {
                string sha256 = string.Empty;
                try
                {
                    using var stream = File.OpenRead(path);
                    using var sha = SHA256.Create();
                    var hashBytes = await sha.ComputeHashAsync(stream);
                    sha256 = Convert.ToHexString(hashBytes).ToLowerInvariant();
                }
                catch
                {
                    // ignore
                }

                if (!string.IsNullOrEmpty(sha256) && dialogService?.DuplicateAssetLookup != null)
                {
                    var match = dialogService.DuplicateAssetLookup(sha256);
                    if (match.HasValue)
                    {
                        var title = GetLocalizedString("Tools.PublisherStudio.Duplicate.Title", "Duplicate File Detected");
                        var prompt = string.Format(
                            GetLocalizedString(
                                "Tools.PublisherStudio.Duplicate.MessageFormat",
                                "We found an identical file already hosted on your provider:\n• Name: {0}\n• URL: {1}\n\nWould you like to use this existing hosted file instead of uploading a new copy?"),
                            match.Value.Name,
                            match.Value.Url);
                        var confirmText = GetLocalizedString("Tools.PublisherStudio.Duplicate.UseExisting", "Use Existing File");
                        var cancelText = GetLocalizedString("Tools.PublisherStudio.Duplicate.UploadNew", "Upload New Copy");

                        var useExisting = await dialogService.ShowConfirmationAsync(title, prompt, confirmText, cancelText);
                        if (useExisting)
                        {
                            var art = new ReleaseArtifact
                            {
                                Filename = Path.GetFileName(path),
                                DownloadUrl = match.Value.Url,
                                LocalFilePath = null,
                                Size = match.Value.Size > 0 ? match.Value.Size : new FileInfo(path).Length,
                                Sha256 = sha256,
                                ContentType = MimeTypeHelper.FromFileName(path),
                                IsPrimary = ReleaseArtifacts.Count == 0 && StagedFiles.Count == 0,
                            };
                            ReleaseArtifacts.Add(art);
                            IncludeInitialRelease = true;
                            continue;
                        }
                    }
                }
            }

            TryStagePath(path, autofill: false);
        }

        IncludeInitialRelease = true;
        SyncPrimaryFromStaged();
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
        if (disposing && !_disposed)
        {
            _disposed = true;
            CancelAllStagedCompute();
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
    private static List<string> ParseUrls(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return [];
        }

        return input
            .Split(["\r\n", "\r", "\n", ","], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

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
            BundleArtifacts = source.BundleArtifacts,
            Artifacts = source.Artifacts.Select(CloneArtifact).ToList(),
            Dependencies = source.Dependencies.Select(CloneDependency).ToList(),
            ImageUrls = [.. source.ImageUrls],
            VideoUrls = [.. source.VideoUrls],
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

    partial void OnIsVariantsModeChanged(bool value)
    {
        if (value)
        {
            BundleArtifacts = false;
        }
    }

    partial void OnBundleArtifactsChanged(bool value)
    {
        if (value)
        {
            IsVariantsMode = false;
        }
    }

    partial void OnUseDirectUrlChanged(bool value)
    {
        if (value)
        {
            CancelAllStagedCompute();
            StagedFiles.Clear();
            SyncPrimaryFromStaged();
        }
        else
        {
            DownloadUrl = null;
        }

        OnPropertyChanged(nameof(IsMultiFileStaging));
        OnPropertyChanged(nameof(MultiFileBundleNote));
    }

    partial void OnLocalFilePathChanged(string? value)
    {
        if (_syncingPrimary || string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        // External assignment (tests, legacy callers): mirror an existing path
        // into the staged files so creation flows stay consistent.
        if (StagedFiles.Count == 1 && string.Equals(StagedFiles[0].LocalPath, value, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!File.Exists(value) && !Directory.Exists(value))
        {
            return;
        }

        StagePaths([value], autofill: false);
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

    private void StagePaths(IEnumerable<string> paths, bool autofill)
    {
        var stagedAny = false;
        foreach (var rawPath in paths)
        {
            stagedAny |= TryStagePath(rawPath, autofill);
        }

        if (!stagedAny)
        {
            return;
        }

        UseDirectUrl = false;
        IncludeInitialRelease = true;
        SyncPrimaryFromStaged();
        Validate();
    }

    private bool TryStagePath(string? rawPath, bool autofill)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return false;
        }

        var path = rawPath.Trim('"', '\'', ' ');
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return false;
        }

        if (StagedFiles.Any(e => string.Equals(e.LocalPath, path, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var isFirst = StagedFiles.Count == 0;
        var entry = CreateStagedEntry(path);
        StagedFiles.Add(entry);
        StartStagedCompute(entry);

        if (isFirst)
        {
            ApplyFirstStagedEntry(path, entry, autofill);
        }

        return true;
    }

    private void ApplyFirstStagedEntry(string path, StagedContentFile entry, bool autofill)
    {
        PackageFilename = entry.IsFolder ? $"{entry.DisplayName}.zip" : entry.DisplayName;
        if (autofill)
        {
            AutoFillFromEntry(path, entry);
        }
    }

    private void AutoFillFromEntry(string path, StagedContentFile entry)
    {
        var baseName = entry.IsFolder ? entry.DisplayName : Path.GetFileNameWithoutExtension(entry.DisplayName);

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
            Description = string.Format(
                GetLocalizedString("Tools.PublisherStudio.Content.AutoDescriptionFormat", "{0} package for {1}."),
                ContentName,
                GetLocalizedGameName(SelectedTargetGame));
        }

        // Intelligently infer ContentType from extension or name
        var inferredType = InferContentType(path, baseName);
        if (inferredType.HasValue)
        {
            SelectedContentType = inferredType.Value;
        }
    }

    private StagedContentFile CreateStagedEntry(string path)
    {
        if (Directory.Exists(path))
        {
            var dirInfo = new DirectoryInfo(path);
            return new StagedContentFile
            {
                LocalPath = path,
                DisplayName = dirInfo.Name,
                IsFolder = true,
                FileSizeDisplay = GetLocalizedString(
                    "Tools.PublisherStudio.Content.StagedFolderCalculating",
                    "Folder (calculating size...)"),
                Sha256Hash = string.Empty,
            };
        }

        var fileInfo = new FileInfo(path);
        var entry = new StagedContentFile
        {
            LocalPath = path,
            DisplayName = fileInfo.Name,
            FileSizeBytes = fileInfo.Length,
            FileSizeDisplay = FormatBytes(fileInfo.Length),
            IsArchive = IsArchivePath(path),
        };

        if (entry.IsArchive)
        {
            entry.ArchiveNoteText = DescribeArchive(path);
        }

        return entry;
    }

    private bool IsArchivePath(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".zip", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".7z", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".rar", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".tar", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".gz", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".tgz", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".bz2", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".xz", StringComparison.OrdinalIgnoreCase);
    }

    private string DescribeArchive(string path)
    {
        var entryCount = TryGetZipEntryCount(path);
        if (entryCount >= 0)
        {
            return string.Format(
                GetLocalizedString(
                    "Tools.PublisherStudio.Content.StagedArchiveFormat",
                    "Archive with {0} files. Contents are extracted automatically when players install this content."),
                entryCount);
        }

        return GetLocalizedString(
            "Tools.PublisherStudio.Content.StagedArchiveUnknownFormat",
            "Archive. Contents are extracted automatically when players install this content.");
    }

    private int TryGetZipEntryCount(string path)
    {
        if (!Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            return -1;
        }

        try
        {
            using var archive = ZipFile.OpenRead(path);
            return archive.Entries.Count(e => !string.IsNullOrEmpty(e.Name));
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return -1;
        }
    }

    private void StartStagedCompute(StagedContentFile entry)
    {
        CancelStagedCompute(entry);
        var cts = new CancellationTokenSource();
        entry.ComputeCts = cts;
        entry.IsComputingHash = true;
        UpdateAggregateHashState();
        _ = ComputeStagedEntryAsync(entry, cts.Token);
    }

    private void CancelStagedCompute(StagedContentFile entry)
    {
        entry.ComputeCts?.Cancel();
        entry.ComputeCts?.Dispose();
        entry.ComputeCts = null;
    }

    private void CancelAllStagedCompute()
    {
        foreach (var entry in StagedFiles)
        {
            CancelStagedCompute(entry);
        }
    }

    private void SyncPrimaryFromStaged()
    {
        _syncingPrimary = true;
        try
        {
            var first = StagedFiles.FirstOrDefault();
            if (first == null)
            {
                LocalFilePath = null;
                FileSize = 0;
                FileSizeDisplay = string.Empty;
                Sha256Hash = null;
            }
            else
            {
                LocalFilePath = first.LocalPath;
                FileSize = first.FileSizeBytes;
                FileSizeDisplay = first.FileSizeDisplay;
                Sha256Hash = first.Sha256Hash;
            }

            UpdateAggregateHashState();
            OnPropertyChanged(nameof(HasStagedFiles));
            OnPropertyChanged(nameof(IsMultiFileStaging));
            OnPropertyChanged(nameof(MultiFileBundleNote));
        }
        finally
        {
            _syncingPrimary = false;
        }
    }

    private void UpdateAggregateHashState()
    {
        IsComputingHash = StagedFiles.Any(e => e.IsComputingHash);
    }

    /// <summary>
    /// Removes a staged file or folder from the initial release.
    /// </summary>
    /// <param name="entry">The staged entry to remove.</param>
    [RelayCommand]
    private void RemoveStagedFile(StagedContentFile? entry)
    {
        if (entry == null || !StagedFiles.Contains(entry))
        {
            return;
        }

        CancelStagedCompute(entry);
        StagedFiles.Remove(entry);
        SyncPrimaryFromStaged();
    }

    /// <summary>
    /// Browses for local content files (.zip, .big, .7z, etc.).
    /// Multiple files can be selected; each becomes one release artifact.
    /// </summary>
    [RelayCommand]
    private async Task BrowseLocalFileAsync()
    {
        if (dialogService == null) return;

        var filePaths = await dialogService.ShowFilesPickerAsync(
            GetLocalizedString("Tools.PublisherStudio.Content.SelectArchiveTitle", "Select Content Archive File"));
        var existing = filePaths.Where(File.Exists).ToList();
        if (existing.Count > 0)
        {
            StagePaths(existing, autofill: true);
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

        var filePath = await dialogService.ShowImagePickerAsync(
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

    private async Task ComputeStagedEntryAsync(StagedContentFile entry, CancellationToken ct)
    {
        try
        {
            if (entry.IsFolder)
            {
                await ComputeStagedFolderSizeAsync(entry, ct);
            }
            else
            {
                await ComputeStagedFileHashAsync(entry, ct);
            }
        }
        finally
        {
            entry.IsComputingHash = false;
            SyncPrimaryFromStaged();
        }
    }

    private async Task ComputeStagedFolderSizeAsync(StagedContentFile entry, CancellationToken ct)
    {
        try
        {
            var totalBytes = await Task.Run(
                () =>
                {
                    long sum = 0;
                    foreach (var file in new DirectoryInfo(entry.LocalPath).EnumerateFiles("*", SearchOption.AllDirectories))
                    {
                        ct.ThrowIfCancellationRequested();
                        sum += file.Length;
                    }

                    return sum;
                },
                ct);

            if (!ct.IsCancellationRequested)
            {
                entry.FileSizeBytes = totalBytes;
                entry.FileSizeDisplay = string.Format(
                    GetLocalizedString("Tools.PublisherStudio.Content.StagedFolderSizeFormat", "{0} (folder)"),
                    FormatBytes(totalBytes));
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
                entry.FileSizeBytes = 0;
                entry.FileSizeDisplay = GetLocalizedString(
                    "Tools.PublisherStudio.Content.StagedFolderUnavailable",
                    "Folder (size unavailable)");
            }
        }
    }

    private async Task ComputeStagedFileHashAsync(StagedContentFile entry, CancellationToken ct)
    {
        try
        {
            using var stream = File.OpenRead(entry.LocalPath);
            using var sha256 = SHA256.Create();
            var hashBytes = await sha256.ComputeHashAsync(stream, ct);
            if (!ct.IsCancellationRequested)
            {
                entry.Sha256Hash = Convert.ToHexString(hashBytes).ToLowerInvariant();
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
                entry.Sha256Hash = string.Empty;
            }
        }
    }

    /// <summary>
    /// Applies a preset accent color from the picker.
    /// </summary>
    /// <param name="hex">The preset hex color.</param>
    [RelayCommand]
    private void SelectAccentColor(string? hex)
    {
        if (!string.IsNullOrWhiteSpace(hex))
        {
            AccentColor = hex;
        }
    }

    /// <summary>
    /// Adds a screenshot URL to the content item.
    /// </summary>
    [RelayCommand]
    private void AddScreenshot()
    {
        if (!string.IsNullOrWhiteSpace(NewScreenshotUrl))
        {
            var url = NewScreenshotUrl.Trim();
            if (!Screenshots.Contains(url))
            {
                Screenshots.Add(url);
            }

            NewScreenshotUrl = string.Empty;
        }
    }

    /// <summary>
    /// Removes a screenshot URL from the content item.
    /// </summary>
    [RelayCommand]
    private void RemoveScreenshot(string? url)
    {
        if (!string.IsNullOrWhiteSpace(url))
        {
            Screenshots.Remove(url);
        }
    }

    /// <summary>
    /// Adds a video URL to the content item.
    /// </summary>
    [RelayCommand]
    private void AddVideo()
    {
        if (!string.IsNullOrWhiteSpace(NewVideoUrl))
        {
            var url = NewVideoUrl.Trim();
            if (!Videos.Contains(url))
            {
                Videos.Add(url);
            }

            NewVideoUrl = string.Empty;
        }
    }

    /// <summary>
    /// Removes a video URL from the content item.
    /// </summary>
    [RelayCommand]
    private void RemoveVideo(string? url)
    {
        if (!string.IsNullOrWhiteSpace(url))
        {
            Videos.Remove(url);
        }
    }

    /// <summary>
    /// Opens the artifact creation dialog to add an artifact to the initial release.
    /// </summary>
    [RelayCommand]
    private async Task AddArtifactAsync()
    {
        if (dialogService == null) return;
        var artifact = await dialogService.ShowAddArtifactDialogAsync();
        if (artifact != null)
        {
            if (artifact.IsPrimary)
            {
                foreach (var a in ReleaseArtifacts) a.IsPrimary = false;
            }

            ReleaseArtifacts.Add(artifact);
        }
    }

    /// <summary>
    /// Removes an artifact from the initial release.
    /// </summary>
    [RelayCommand]
    private void RemoveArtifact(ReleaseArtifact? artifact)
    {
        if (artifact != null)
        {
            ReleaseArtifacts.Remove(artifact);
        }
    }

    /// <summary>
    /// Opens the dependency selection dialog to add a dependency to the initial release.
    /// </summary>
    [RelayCommand]
    private async Task AddReleaseDependencyAsync()
    {
        if (dialogService == null || catalog == null) return;
        var tempItem = new CatalogContentItem { Id = ContentId ?? "new-item", Name = ContentName ?? "New Item" };
        var dep = await dialogService.ShowAddDependencyDialogAsync(catalog, tempItem);
        if (dep != null)
        {
            var existing = ReleaseDependencies.FirstOrDefault(d =>
                string.Equals(d.PublisherId, dep.PublisherId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(d.ContentId, dep.ContentId, StringComparison.OrdinalIgnoreCase));
            if (existing != null) ReleaseDependencies.Remove(existing);
            ReleaseDependencies.Add(dep);
        }
    }

    /// <summary>
    /// Removes a dependency from the initial release.
    /// </summary>
    [RelayCommand]
    private void RemoveReleaseDependency(CatalogDependency? dep)
    {
        if (dep != null)
        {
            ReleaseDependencies.Remove(dep);
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
                ValidationError = GetLocalizedString(
                    "Tools.PublisherStudio.Validation.DownloadUrlInvalid",
                    "Download URL must be a valid HTTP or HTTPS link.");
                IsValid = false;
                return false;
            }
        }
        else if (StagedFiles.Count > 0)
        {
            var missing = StagedFiles.FirstOrDefault(e => !File.Exists(e.LocalPath) && !Directory.Exists(e.LocalPath));
            if (missing != null)
            {
                ValidationError = string.Format(
                    GetLocalizedString(
                        "Tools.PublisherStudio.Validation.LocalPathMissingFormat",
                        "Local path does not exist: {0}"),
                    missing.LocalPath);
                IsValid = false;
                return false;
            }
        }
        else if (!string.IsNullOrWhiteSpace(LocalFilePath) &&
                 !System.IO.File.Exists(LocalFilePath) &&
                 !System.IO.Directory.Exists(LocalFilePath))
        {
            ValidationError = string.Format(
                GetLocalizedString(
                    "Tools.PublisherStudio.Validation.LocalPathMissingFormat",
                    "Local path does not exist: {0}"),
                LocalFilePath);
            IsValid = false;
            return false;
        }

        return true;
    }

    private string GetLocalizedGameName(GameType game)
    {
        return game switch
        {
            GameType.Generals => GetLocalizedString("GameProfiles.GameType.Generals", "C&C Generals"),
            GameType.ZeroHour => GetLocalizedString("GameProfiles.GameType.ZeroHour", "C&C Generals: Zero Hour"),
            _ => game.ToString(),
        };
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
        var singleVideo = NormalizeArtworkValue(VideoUrl);
        var source = _existingItem?.Metadata;

        var allScreenshots = new List<string>(Screenshots);
        foreach (var s in ParseUrls(ScreenshotUrlsInput))
        {
            if (!allScreenshots.Contains(s, StringComparer.OrdinalIgnoreCase))
            {
                allScreenshots.Add(s);
            }
        }

        var allVideos = new List<string>(Videos);
        if (!string.IsNullOrWhiteSpace(singleVideo) && !allVideos.Contains(singleVideo, StringComparer.OrdinalIgnoreCase))
        {
            allVideos.Add(singleVideo);
        }

        if (icon == null && banner == null && backdrop == null && accent == null && allVideos.Count == 0 && allScreenshots.Count == 0 && source == null)
        {
            return null;
        }

        List<string> resolvedScreenshots;
        if (allScreenshots.Count > 0)
        {
            resolvedScreenshots = allScreenshots;
        }
        else if (source?.ScreenshotUrls is { } sUrls)
        {
            resolvedScreenshots = [.. sUrls];
        }
        else
        {
            resolvedScreenshots = [];
        }

        List<string> resolvedVideos;
        if (allVideos.Count > 0)
        {
            resolvedVideos = allVideos;
        }
        else if (source?.VideoUrls is { } vUrls)
        {
            resolvedVideos = [.. vUrls];
        }
        else
        {
            resolvedVideos = [];
        }

        return new ContentRichMetadata
        {
            IconUrl = icon ?? source?.IconUrl,
            BannerUrl = banner ?? source?.BannerUrl,
            BackdropUrl = backdrop ?? source?.BackdropUrl,
            AccentColor = accent ?? source?.AccentColor,
            ScreenshotUrls = resolvedScreenshots,
            VideoUrl = allVideos.FirstOrDefault() ?? source?.VideoUrl,
            VideoUrls = resolvedVideos,
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
            Changelog = string.IsNullOrWhiteSpace(ReleaseChangelog) ? null : ReleaseChangelog.Trim(),
            BundleArtifacts = BundleArtifacts,
            Artifacts = [],
            Dependencies = [.. ReleaseDependencies],
            ImageUrls = ParseUrls(ReleaseImageUrlsInput),
            VideoUrls = ParseUrls(ReleaseVideoUrlsInput),
        };

        if (ReleaseArtifacts.Count > 0)
        {
            foreach (var art in ReleaseArtifacts)
            {
                release.Artifacts.Add(art);
            }
        }
        else if (UseDirectUrl || StagedFiles.Count == 0)
        {
            AttachSingleInitialArtifact(contentItem, release, version);
        }
        else
        {
            AttachStagedInitialArtifacts(release);
        }

        contentItem.Releases.Add(release);
    }

    private void AttachSingleInitialArtifact(CatalogContentItem contentItem, ContentRelease release, string version)
    {
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
    }

    private void AttachStagedInitialArtifacts(ContentRelease release)
    {
        var isSingle = StagedFiles.Count == 1;
        for (var index = 0; index < StagedFiles.Count; index++)
        {
            var entry = StagedFiles[index];
            var artifactName = isSingle && !string.IsNullOrWhiteSpace(PackageFilename)
                ? PackageFilename.Trim()
                : StagedArtifactFilename(entry);

            release.Artifacts.Add(new ReleaseArtifact
            {
                Filename = artifactName,
                DownloadUrl = string.Empty,
                LocalFilePath = entry.LocalPath,
                Size = entry.FileSizeBytes,
                Sha256 = entry.Sha256Hash?.Trim() ?? string.Empty,
                ContentType = MimeTypeHelper.FromFileName(artifactName),
                IsPrimary = index == 0,
            });
        }

        // Multiple staged files are parts of one payload; bundle them so the
        // downloads browser installs all of them instead of offering a picker.
        release.BundleArtifacts = StagedFiles.Count > 1;
    }

    private string StagedArtifactFilename(StagedContentFile entry)
    {
        if (!entry.IsFolder)
        {
            return entry.DisplayName;
        }

        return entry.DisplayName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
            ? entry.DisplayName
            : entry.DisplayName + ".zip";
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

        foreach (var addon in _existingItem.Addons)
        {
            contentItem.Addons.Add(CloneDependency(addon));
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
