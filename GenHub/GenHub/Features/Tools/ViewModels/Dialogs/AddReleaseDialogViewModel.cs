using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Publishers;
using GenHub.Features.Tools.Interfaces;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.ViewModels.Dialogs;

/// <summary>
/// ViewModel for the Add/Edit Release dialog.
/// </summary>
public partial class AddReleaseDialogViewModel(
    CatalogContentItem contentItem,
    PublisherCatalog catalog,
    Action<ContentRelease> onReleaseCreated,
    IPublisherStudioDialogService dialogService,
    GenHub.Core.Interfaces.Common.ILocalizationService? localizationService = null) : ObservableValidator
{
    private readonly string? _originalVersion;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required(ErrorMessage = "Version is required")]
    [RegularExpression(@"^\d+\.\d+(\.\d+)?(-[a-zA-Z0-9.]+)?$", ErrorMessage = "Version format: X.Y or X.Y.Z or X.Y.Z-tag (e.g. 1.0, 2.1.0, 1.0.0-beta)")]
    private string _version = GetNextVersion(contentItem?.Releases ?? []);

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
    private string? _validationError;

    [ObservableProperty]
    private bool _isValid;

    /// <summary>
    /// Gets a value indicating whether the dialog is in edit mode.
    /// </summary>
    [ObservableProperty]
    private bool _isEditMode;

    /// <summary>
    /// Initializes a new instance of the <see cref="AddReleaseDialogViewModel"/> class in edit mode,
    /// pre-populated with an existing release's data.
    /// </summary>
    /// <param name="existing">The existing release to edit.</param>
    /// <param name="contentItem">The content item the release belongs to.</param>
    /// <param name="catalog">The publisher catalog.</param>
    /// <param name="onReleaseCreated">Callback invoked when release is successfully saved.</param>
    /// <param name="dialogService">The dialog service.</param>
    /// <param name="localizationService">Optional localization service.</param>
    public AddReleaseDialogViewModel(
        ContentRelease existing,
        CatalogContentItem contentItem,
        PublisherCatalog catalog,
        Action<ContentRelease> onReleaseCreated,
        IPublisherStudioDialogService dialogService,
        GenHub.Core.Interfaces.Common.ILocalizationService? localizationService = null)
        : this(contentItem, catalog, onReleaseCreated, dialogService, localizationService)
    {
        ArgumentNullException.ThrowIfNull(existing);

        IsEditMode = true;
        _originalVersion = existing.Version;
        Version = existing.Version;
        ReleaseDate = existing.ReleaseDate.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(existing.ReleaseDate.Value, DateTimeKind.Utc)) : DateTimeOffset.UtcNow;
        IsLatest = existing.IsLatest;
        IsPrerelease = existing.IsPrerelease;
        IsFeatured = existing.IsFeatured;
        Changelog = existing.Changelog ?? string.Empty;

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
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S2325:Make member static", Justification = "ViewModel property bound to XAML view")]
    public string DialogTitle => IsEditMode
        ? GetLocalizedString("Tools.PublisherStudio.Release.EditTitle", "Edit Release")
        : GetLocalizedString("Tools.PublisherStudio.Release.AddTitle", "Add New Release");

    /// <summary>
    /// Gets the submit button text based on the current mode.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S2325:Make member static", Justification = "ViewModel property bound to XAML view")]
    public string SubmitButtonText => IsEditMode
        ? GetLocalizedString("Tools.PublisherStudio.Common.SaveChanges", "Save Changes")
        : GetLocalizedString("Tools.PublisherStudio.Release.CreateRelease", "Create Release");

    /// <summary>
    /// Gets the content item name for display in the dialog title.
    /// </summary>
    public string ContentName => contentItem?.Name ?? string.Empty;

    /// <summary>
    /// Gets the suggested next version based on existing releases.
    /// </summary>
    public string SuggestedVersion => GetNextVersion(contentItem?.Releases ?? []);

    [GeneratedRegex(@"^(\d+)\.(\d+)\.(\d+)")]
    private static partial Regex VersionRegex();

    private static string GetNextVersion(IReadOnlyList<ContentRelease> existingReleases)
    {
        if (existingReleases == null || existingReleases.Count == 0)
        {
            return "1.0.0";
        }

        // Find the highest version
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

        // Increment patch version
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

    partial void OnVersionChanged(string value) => Validate();

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
            // If this is marked as primary, unmark others
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
            // Avoid duplicate dependencies for the same content
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
    /// Cancels the dialog without saving.
    /// </summary>
    [RelayCommand]
    private void Cancel() => Close();

    /// <summary>
    /// Creates the release if validation passes.
    /// </summary>
    [RelayCommand]
    private void CreateRelease()
    {
        Validate();

        if (!IsValid)
        {
            return;
        }

        // Check for artifacts
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

        // Check for duplicate version (skip check if version hasn't changed in edit mode)
        var isDuplicateVersion = contentItem.Releases.Any(r => r.Version.Equals(Version, StringComparison.OrdinalIgnoreCase));
        var isOriginalVersion = IsEditMode && _originalVersion != null && _originalVersion.Equals(Version, StringComparison.OrdinalIgnoreCase);
        if (isDuplicateVersion && !isOriginalVersion)
        {
            ValidationError = localizationService?.GetString(
                "Tools.PublisherStudio.Release.DuplicateVersion",
                Version) ?? $"Version {Version} already exists for this content";
            IsValid = false;
            return;
        }

        var release = new ContentRelease
        {
            Version = Version.Trim(),
            ReleaseDate = ReleaseDate.UtcDateTime,
            IsLatest = IsLatest,
            IsPrerelease = IsPrerelease,
            IsFeatured = IsFeatured,
            Changelog = Changelog.Trim(),
            Artifacts = [.. Artifacts],
            Dependencies = [.. Dependencies],
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
            errors.AddRange(GetErrors().Select(e => e.ErrorMessage ?? "Validation error"));
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
        return localizationService?.GetString(key) ?? fallback;
    }
}
