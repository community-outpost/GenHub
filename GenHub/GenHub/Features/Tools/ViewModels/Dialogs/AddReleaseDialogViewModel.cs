using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Common.Validation;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Models.Providers;
using GenHub.Features.Tools.Interfaces;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.ViewModels.Dialogs;

/// <summary>
/// ViewModel for the Add/Edit Release or Addon dialog.
/// </summary>
public partial class AddReleaseDialogViewModel(
    CatalogContentItem contentItem,
    PublisherCatalog catalog,
    Action<ContentRelease> onReleaseCreated,
    IPublisherStudioDialogService dialogService,
    GenHub.Core.Interfaces.Common.ILocalizationService? localizationService = null,
    bool isAddon = false,
    INotificationService? notificationService = null) : ObservableValidator
{
    private readonly string? _originalVersion;
    private readonly ContentRelease? _existingRelease;
    private readonly Func<ContentRelease, Task>? _onReleaseDeleted;

    [ObservableProperty]
    private bool _isAddonMode = isAddon;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _category = "Addon";

    /// <summary>
    /// Gets the localized label for the release or addon title input.
    /// </summary>
    public string TitleLabel => IsAddonMode
        ? GetLocalizedString("Tools.PublisherStudio.Release.AddonTitle", "Addon Title / Name")
        : GetLocalizedString("Tools.PublisherStudio.Release.Title", "Release Title / Name");

    /// <summary>
    /// Gets the placeholder/watermark for the title input, showing the inherited name if blank.
    /// </summary>
    public string TitlePlaceholder => IsAddonMode
        ? GetLocalizedString("Tools.PublisherStudio.Release.AddonTitlePlaceholder", "e.g., Russian Localization Patch")
        : (!string.IsNullOrWhiteSpace(contentItem?.Name)
            ? $"{contentItem.Name} Version {Version}".Trim()
            : GetLocalizedString("Tools.PublisherStudio.Release.TitlePlaceholder", "Leave empty to inherit content name and version"));

    partial void OnVersionChanged(string value)
    {
        OnPropertyChanged(nameof(TitlePlaceholder));
        Validate();
    }

    partial void OnIsAddonModeChanged(bool value)
    {
        OnPropertyChanged(nameof(TitleLabel));
        OnPropertyChanged(nameof(TitlePlaceholder));
    }

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [LocalizedRequired("Tools.PublisherStudio.Validation.VersionRequired", "Version is required")]
    [LocalizedRegularExpression(@"^\d+\.\d+(\.\d+)?(-[a-zA-Z0-9.]+)?$", "Tools.PublisherStudio.Validation.VersionPattern", "Version format: X.Y or X.Y.Z or X.Y.Z-tag (e.g. 1.0, 2.1.0, 1.0.0-beta)")]
    private string _version = GetNextVersion(isAddon ? (contentItem?.AddonReleases ?? []) : (contentItem?.Releases ?? []));

    [ObservableProperty]
    private DateTimeOffset _releaseDate = DateTimeOffset.UtcNow;

    [ObservableProperty]
    private bool _isLatest = true;

    [ObservableProperty]
    private bool _isPrerelease;

    [ObservableProperty]
    private bool _isFeatured;

    [ObservableProperty]
    private string _changelog = string.Empty;

    [ObservableProperty]
    private bool _bundleArtifacts = true;

    [ObservableProperty]
    private bool _isVariantsMode;

    [ObservableProperty]
    private string _imageUrlsInput = string.Empty;

    [ObservableProperty]
    private string _videoUrlsInput = string.Empty;

    [ObservableProperty]
    private string? _validationError;

    [ObservableProperty]
    private bool _isValid;

    /// <summary>
    /// Gets a value indicating whether the dialog is in edit mode.
    /// </summary>
    [ObservableProperty]
    private bool _isEditMode;

    /// <summary>
    /// Gets the list of available categories for addons.
    /// </summary>
    public IReadOnlyList<string> AvailableAddonCategories { get; } =
    [
        "Addon",
        "Map",
        "Patch",
        "UI",
        "AI",
        "Music",
        "Skin",
        "Tools",
        "Other",
    ];

    /// <summary>
    /// Initializes a new instance of the <see cref="AddReleaseDialogViewModel"/> class in edit mode,
    /// pre-populated with an existing release or addon's data.
    /// </summary>
    /// <param name="existing">The existing release or addon to edit.</param>
    /// <param name="contentItem">The content item the release belongs to.</param>
    /// <param name="catalog">The publisher catalog.</param>
    /// <param name="onReleaseCreated">Callback invoked when release is successfully saved.</param>
    /// <param name="dialogService">The dialog service.</param>
    /// <param name="localizationService">Optional localization service.</param>
    /// <param name="isAddon">True if editing an addon; false if editing a release.</param>
    /// <param name="notificationService">Optional notification service for user feedback.</param>
    /// <param name="onReleaseDeleted">Optional callback invoked when deleting the release.</param>
    public AddReleaseDialogViewModel(
        ContentRelease existing,
        CatalogContentItem contentItem,
        PublisherCatalog catalog,
        Action<ContentRelease> onReleaseCreated,
        IPublisherStudioDialogService dialogService,
        GenHub.Core.Interfaces.Common.ILocalizationService? localizationService = null,
        bool isAddon = false,
        INotificationService? notificationService = null,
        Func<ContentRelease, Task>? onReleaseDeleted = null)
        : this(contentItem, catalog, onReleaseCreated, dialogService, localizationService, isAddon, notificationService)
    {
        ArgumentNullException.ThrowIfNull(existing);

        _existingRelease = existing;
        _onReleaseDeleted = onReleaseDeleted;
        IsEditMode = true;
        _originalVersion = existing.Version;
        Version = existing.Version;
        Title = existing.Title ?? string.Empty;
        Category = !string.IsNullOrWhiteSpace(existing.Category) ? existing.Category : "Addon";
        ReleaseDate = existing.ReleaseDate.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(existing.ReleaseDate.Value, DateTimeKind.Utc)) : DateTimeOffset.UtcNow;
        IsLatest = existing.IsLatest;
        IsPrerelease = existing.IsPrerelease;
        IsFeatured = existing.IsFeatured;
        Changelog = existing.Changelog ?? string.Empty;
        BundleArtifacts = existing.BundleArtifacts;
        IsVariantsMode = !existing.BundleArtifacts;
        ImageUrlsInput = existing.ImageUrls is { Count: > 0 }
            ? string.Join(Environment.NewLine, existing.ImageUrls)
            : string.Empty;
        VideoUrlsInput = existing.VideoUrls is { Count: > 0 }
            ? string.Join(Environment.NewLine, existing.VideoUrls)
            : string.Empty;

        Artifacts.Clear();
        foreach (var artifact in existing.Artifacts)
        {
            Artifacts.Add(CloneArtifact(artifact));
        }

        Dependencies.Clear();
        foreach (var dep in existing.Dependencies)
        {
            Dependencies.Add(CloneDependency(dep));
        }
    }

    /// <summary>
    /// Gets the artifacts currently added to this release.
    /// </summary>
    public ObservableCollection<ReleaseArtifact> Artifacts { get; } = [];

    /// <summary>
    /// Gets the dependencies currently added to this release.
    /// </summary>
    public ObservableCollection<CatalogDependency> Dependencies { get; } = [];

    /// <summary>
    /// Gets the dialog title based on the current mode.
    /// </summary>
    public string DialogTitle
    {
        get
        {
            if (IsAddonMode)
            {
                return IsEditMode
                    ? GetLocalizedString("Tools.PublisherStudio.Addon.EditTitle", "Edit Addon")
                    : GetLocalizedString("Tools.PublisherStudio.Addon.AddTitle", "Add Addon");
            }

            return IsEditMode
                ? GetLocalizedString("Tools.PublisherStudio.Release.EditTitle", "Edit Release")
                : GetLocalizedString("Tools.PublisherStudio.Release.AddTitle", "Add New Release");
        }
    }

    /// <summary>
    /// Gets the submit button text based on the current mode.
    /// </summary>
    public string SubmitButtonText
    {
        get
        {
            if (IsEditMode)
            {
                return GetLocalizedString("Tools.PublisherStudio.Common.SaveChanges", "Save Changes");
            }

            return IsAddonMode
                ? GetLocalizedString("Tools.PublisherStudio.Addon.AddButton", "Add Addon")
                : GetLocalizedString("Tools.PublisherStudio.Release.CreateRelease", "Create Release");
        }
    }

    /// <summary>
    /// Gets the content item name for display in the dialog title.
    /// </summary>
    public string ContentName => contentItem?.Name ?? string.Empty;

    /// <summary>
    /// Gets the suggested next version based on existing releases or addons.
    /// </summary>
    public string SuggestedVersion => GetNextVersion(IsAddonMode ? (contentItem?.AddonReleases ?? []) : (contentItem?.Releases ?? []));

    /// <summary>
    /// Determines whether the specified path has an image file extension.
    /// </summary>
    /// <param name="path">The file path to check.</param>
    /// <returns>True if the specified path has an image file extension; otherwise, false.</returns>
    public static bool IsImageFile(string path) => MediaFileHelper.IsImageFile(path);

    /// <summary>
    /// Adds dropped files or folders as artifacts, checking for duplicates against hosting provider.
    /// </summary>
    /// <param name="paths">The local file or directory paths dropped.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task AddArtifactsFromPathsAsync(IEnumerable<string> paths, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var addedCount = 0;
        foreach (var rawPath in paths)
        {
            if (string.IsNullOrWhiteSpace(rawPath))
            {
                continue;
            }

            var path = rawPath.Trim('"', '\'', ' ');
            ReleaseArtifact? artifact = null;

            if (File.Exists(path))
            {
                artifact = await BuildFileArtifactAsync(path, cancellationToken);
            }
            else if (Directory.Exists(path))
            {
                artifact = await BuildFolderArtifactAsync(path, cancellationToken);
            }

            if (artifact == null)
            {
                continue;
            }

            Artifacts.Add(artifact);
            addedCount++;
        }

        // Files dropped together are parts of one payload; bundle them so the
        // downloads browser installs all of them instead of offering a picker.
        if (addedCount > 1)
        {
            BundleArtifacts = true;
        }

        Validate();
    }

    /// <summary>
    /// Adds dropped image files to the release's image URLs, with duplicate checking against hosting provider.
    /// </summary>
    /// <param name="paths">The image file paths dropped.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task AddImagesFromPathsAsync(IEnumerable<string> paths, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var existingUrls = ParseUrls(ImageUrlsInput);
        foreach (var rawPath in paths)
        {
            await ProcessImagePathAsync(rawPath, existingUrls, cancellationToken);
        }

        ImageUrlsInput = string.Join(Environment.NewLine, existingUrls);
    }

    /// <summary>
    /// Shows an error notification when drag-and-drop staging fails unexpectedly.
    /// </summary>
    public void NotifyDropFailed()
    {
        var title = GetLocalizedString("Tools.PublisherStudio.Dialogs.StageArtifactsFailedTitle", "Could Not Stage Files");
        var message = GetLocalizedString("Tools.PublisherStudio.Dialogs.StageArtifactsFailedMessage", "Some dropped files could not be staged and were skipped. See logs for details.");
        notificationService?.ShowError(title, message);
    }

    [GeneratedRegex(@"^(\d+)\.(\d+)\.(\d+)")]
    private static partial Regex VersionRegex();

    private static string GetNextVersion(IReadOnlyList<ContentRelease> existingReleases)
    {
        if (existingReleases == null || existingReleases.Count == 0)
        {
            return "1.0.0";
        }

        var versions = existingReleases
            .Select(r => ParseVersion(r.Version))
            .Where(v => v != null)
            .OrderByDescending(v => v!.Value.Major)
            .ThenByDescending(v => v!.Value.Minor)
            .ThenByDescending(v => v!.Value.Patch)
            .FirstOrDefault();

        if (versions == null)
        {
            return "1.0.0";
        }

        var (major, minor, patch) = versions.Value;
        return $"{major}.{minor}.{patch + 1}";
    }

    private static (int Major, int Minor, int Patch)? ParseVersion(string version)
    {
        var match = VersionRegex().Match(version);
        if (match.Success &&
            int.TryParse(match.Groups[1].Value, out var major) &&
            int.TryParse(match.Groups[2].Value, out var minor) &&
            int.TryParse(match.Groups[3].Value, out var patch))
        {
            return (major, minor, patch);
        }

        return null;
    }

    private static ReleaseArtifact CloneArtifact(ReleaseArtifact source)
    {
        ArgumentNullException.ThrowIfNull(source);

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
        ArgumentNullException.ThrowIfNull(source);

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

    private static List<string> ParseUrls(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return [];
        }

        return input
            .Split(new[] { "\r\n", "\r", "\n", "," }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static async Task<string> ComputeFileSha256SafeAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = File.OpenRead(path);
            using var hasher = SHA256.Create();
            var hashBytes = await hasher.ComputeHashAsync(stream, cancellationToken);
            return Convert.ToHexString(hashBytes).ToLowerInvariant();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            return string.Empty;
        }
    }

    partial void OnIsVariantsModeChanged(bool value)
    {
        if (BundleArtifacts == value)
        {
            BundleArtifacts = !value;
        }
    }

    partial void OnBundleArtifactsChanged(bool value)
    {
        if (IsVariantsMode == value)
        {
            IsVariantsMode = !value;
        }
    }

    partial void OnTitleChanged(string value) => Validate();

    /// <summary>
    /// Applies the suggested next version based on existing releases.
    /// </summary>
    [RelayCommand]
    private void ApplySuggestedVersion()
    {
        Version = SuggestedVersion;
    }

    /// <summary>
    /// Opens the Add Artifact dialog.
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
                foreach (var a in Artifacts)
                {
                    a.IsPrimary = false;
                }
            }

            Artifacts.Add(artifact);
            Validate();
        }
    }

    private async Task<string?> PromptDuplicateAssetUrlAsync(string sha256)
    {
        if (string.IsNullOrEmpty(sha256) || dialogService?.DuplicateAssetLookup == null)
        {
            return null;
        }

        var match = dialogService.DuplicateAssetLookup(sha256);
        if (!match.HasValue)
        {
            return null;
        }

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
        return useExisting ? match.Value.Url : null;
    }

    private async Task ProcessImagePathAsync(string rawPath, List<string> existingUrls, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return;
        }

        var path = rawPath.Trim('"', '\'', ' ');
        if (!File.Exists(path) || !IsImageFile(path))
        {
            return;
        }

        var sha256 = await ComputeFileSha256SafeAsync(path, cancellationToken);
        var duplicateUrl = await PromptDuplicateAssetUrlAsync(sha256);
        var targetUrl = duplicateUrl ?? path;

        if (!existingUrls.Contains(targetUrl, StringComparer.OrdinalIgnoreCase))
        {
            existingUrls.Add(targetUrl);
        }
    }

    private async Task<ReleaseArtifact> BuildFileArtifactAsync(string path, CancellationToken cancellationToken)
    {
        var fileInfo = new FileInfo(path);
        var filename = fileInfo.Name;
        var sha256 = string.Empty;

        try
        {
            await using var stream = File.OpenRead(path);
            using var hasher = SHA256.Create();
            var hashBytes = await hasher.ComputeHashAsync(stream, cancellationToken);
            sha256 = Convert.ToHexString(hashBytes).ToLowerInvariant();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            sha256 = string.Empty;
        }

        var artifact = new ReleaseArtifact
        {
            Filename = filename,
            DownloadUrl = string.Empty,
            Size = fileInfo.Length,
            Sha256 = sha256,
            ContentType = MimeTypeHelper.FromFileName(filename),
            IsPrimary = Artifacts.Count == 0,
            LocalFilePath = path,
        };

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
                    artifact.DownloadUrl = match.Value.Url;
                    artifact.LocalFilePath = null;
                    if (match.Value.Size > 0)
                    {
                        artifact.Size = match.Value.Size;
                    }
                }
            }
        }

        return artifact;
    }

    private async Task<ReleaseArtifact?> BuildFolderArtifactAsync(string path, CancellationToken cancellationToken)
    {
        var folderName = new DirectoryInfo(path).Name;
        var filename = folderName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
            ? folderName
            : folderName + ".zip";

        long totalBytes = 0;
        try
        {
            totalBytes = await Task.Run(
                () =>
                {
                    long sum = 0;
                    foreach (var file in new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        sum += file.Length;
                    }

                    return sum;
                },
                cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            totalBytes = 0;
        }

        return new ReleaseArtifact
        {
            Filename = filename,
            DownloadUrl = string.Empty,
            Size = totalBytes,
            Sha256 = string.Empty,
            ContentType = HostingConstants.ZipContentType,
            IsPrimary = Artifacts.Count == 0,
            LocalFilePath = path,
        };
    }

    /// <summary>
    /// Removes an artifact from the release.
    /// </summary>
    /// <param name="artifact">The artifact to remove.</param>
    [RelayCommand]
    private void RemoveArtifact(ReleaseArtifact? artifact)
    {
        if (artifact != null)
        {
            Artifacts.Remove(artifact);
            Validate();
        }
    }

    /// <summary>
    /// Opens the Add Dependency dialog.
    /// </summary>
    [RelayCommand]
    private async Task AddDependencyAsync()
    {
        if (dialogService == null) return;
        var dependency = await dialogService.ShowAddDependencyDialogAsync(catalog, contentItem);
        if (dependency != null)
        {
            var existing = Dependencies.FirstOrDefault(d =>
                string.Equals(d.PublisherId, dependency.PublisherId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(d.ContentId, dependency.ContentId, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                Dependencies.Remove(existing);
            }

            Dependencies.Add(dependency);
        }
    }

    /// <summary>
    /// Removes a dependency from the release.
    /// </summary>
    /// <param name="dependency">The dependency to remove.</param>
    [RelayCommand]
    private void RemoveDependency(CatalogDependency? dependency)
    {
        if (dependency != null)
        {
            Dependencies.Remove(dependency);
        }
    }

    /// <summary>
    /// Closes the dialog without saving.
    /// </summary>
    [RelayCommand]
    private void Close()
    {
        ArgumentNullException.ThrowIfNull(onReleaseCreated);
        onReleaseCreated(null!);
    }

    /// <summary>
    /// Deletes the current release or addon if in edit mode after user confirmation.
    /// </summary>
    [RelayCommand]
    private async Task DeleteReleaseAsync()
    {
        if (!IsEditMode || _existingRelease == null)
        {
            return;
        }

        string title;
        string messageFormat;
        string targetName;

        if (IsAddonMode)
        {
            title = localizationService?.GetString("Tools.PublisherStudio.Library.DeleteAddonTitle") ?? "Delete Addon";
            messageFormat = localizationService?.GetString("Tools.PublisherStudio.Library.DeleteAddonMessageFormat") ?? "Are you sure you want to delete addon '{0}'? This cannot be undone.";
            targetName = !string.IsNullOrWhiteSpace(_existingRelease.Title) ? _existingRelease.Title : _existingRelease.Version;
        }
        else
        {
            title = localizationService?.GetString("Tools.PublisherStudio.Library.DeleteReleaseTitle") ?? "Delete Release";
            messageFormat = localizationService?.GetString("Tools.PublisherStudio.Library.DeleteReleaseMessageFormat") ?? "Are you sure you want to delete release v{0}? This will also remove its artifacts and cannot be undone.";
            targetName = _existingRelease.Version;
        }

        var message = string.Format(messageFormat, targetName);

        var confirmed = await dialogService.ShowConfirmationAsync(title, message);
        if (!confirmed)
        {
            return;
        }

        if (_onReleaseDeleted != null)
        {
            await _onReleaseDeleted(_existingRelease);
        }

        Close();
    }

    /// <summary>
    /// Cancels the dialog without saving.
    /// </summary>
    [RelayCommand]
    private void Cancel() => Close();

    /// <summary>
    /// Creates the release or addon if validation passes.
    /// </summary>
    [RelayCommand]
    private void CreateRelease()
    {
        Validate();

        if (!IsValid)
        {
            return;
        }

        if (Artifacts.Count == 0)
        {
            ValidationError = GetLocalizedString(
                "Tools.PublisherStudio.Release.ArtifactRequired",
                "At least one artifact is required");
            IsValid = false;
            return;
        }

        if (HasErrors)
        {
            ValidationError = string.Join(Environment.NewLine, GetErrors().Select(e => e.ErrorMessage));
            IsValid = false;
            return;
        }

        var releasesPool = IsAddonMode ? (contentItem?.AddonReleases ?? []) : (contentItem?.Releases ?? []);
        var isDuplicateVersion = releasesPool.Any(r => r.Version.Equals(Version, StringComparison.OrdinalIgnoreCase));
        var isOriginalVersion = IsEditMode && _originalVersion != null && _originalVersion.Equals(Version, StringComparison.OrdinalIgnoreCase);
        if (isDuplicateVersion && !isOriginalVersion)
        {
            ValidationError = localizationService?.GetString(
                "Tools.PublisherStudio.Release.DuplicateVersion",
                Version) ?? $"Version {Version} already exists";
            IsValid = false;
            return;
        }

        string? effectiveTitle;
        if (!string.IsNullOrWhiteSpace(Title))
        {
            effectiveTitle = Title.Trim();
        }
        else if (!IsAddonMode)
        {
            effectiveTitle = !string.IsNullOrWhiteSpace(contentItem?.Name)
                ? $"{contentItem.Name} Version {Version.Trim()}"
                : $"Version {Version.Trim()}";
        }
        else
        {
            effectiveTitle = null;
        }

        var release = new ContentRelease
        {
            Title = effectiveTitle,
            Category = IsAddonMode && !string.IsNullOrWhiteSpace(Category) ? Category.Trim() : null,
            Version = Version.Trim(),
            ReleaseDate = ReleaseDate.UtcDateTime,
            IsLatest = IsLatest,
            IsPrerelease = IsPrerelease,
            IsFeatured = IsFeatured,
            Changelog = Changelog.Trim(),
            BundleArtifacts = BundleArtifacts,
            Artifacts = [.. Artifacts],
            Dependencies = [.. Dependencies],
            ImageUrls = ParseUrls(ImageUrlsInput),
            VideoUrls = ParseUrls(VideoUrlsInput),
        };

        ArgumentNullException.ThrowIfNull(onReleaseCreated);
        onReleaseCreated(release);
    }

    private void Validate()
    {
        ValidateAllProperties();

        var errors = new List<string>();

        if (HasErrors)
        {
            errors.AddRange(GetErrors().Select(e => e.ErrorMessage ?? ValidationResourceResolver.FormatMessage("Tools.PublisherStudio.Validation.GenericError", "Validation error")));
        }

        if (IsAddonMode && string.IsNullOrWhiteSpace(Title))
        {
            errors.Add(GetLocalizedString("Tools.PublisherStudio.Addon.TitleRequired", "Addon title / name is required"));
        }

        if (Artifacts.Count == 0)
        {
            errors.Add(GetLocalizedString(
                "Tools.PublisherStudio.Release.ArtifactRequired",
                "At least one artifact is required"));
        }

        IsValid = errors.Count == 0;
        ValidationError = errors.Count > 0 ? string.Join(Environment.NewLine, errors) : null;
    }

    private string GetLocalizedString(string key, string fallback)
    {
        if (localizationService != null && localizationService.TryGetString(key, out var localized) && !string.IsNullOrWhiteSpace(localized))
        {
            return localized;
        }

        return fallback;
    }
}
