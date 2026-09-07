using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Models.Providers;
using GenHub.Features.Tools.Interfaces;
using GenHub.Features.Tools.Services;

namespace GenHub.Features.Tools.ViewModels.Dialogs;

/// <summary>
/// ViewModel for the Add New Release dialog.
/// Provides validation and creation of new ContentRelease entries.
/// </summary>
public partial class AddReleaseDialogViewModel : ObservableValidator
{
    private readonly CatalogContentItem _contentItem;
    private readonly PublisherCatalog _catalog;
    private readonly Action<ContentRelease> _onReleaseCreated;
    private readonly IPublisherStudioDialogService _dialogService;
    private readonly string? _originalVersion;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required(ErrorMessage = "Version is required")]
    [RegularExpression(@"^\d+\.\d+\.\d+(-[a-zA-Z0-9]+)?$", ErrorMessage = "Use semantic versioning (e.g., 1.0.0, 2.1.0-beta)")]
    private string _version = string.Empty;

    [ObservableProperty]
    private DateTimeOffset _releaseDate = DateTimeOffset.Now;

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
    /// Gets the artifacts currently added to this release.
    /// </summary>
    public ObservableCollection<ReleaseArtifact> Artifacts { get; } = [];

    /// <summary>
    /// Gets the dependencies currently added to this release.
    /// </summary>
    public ObservableCollection<CatalogDependency> Dependencies { get; } = [];

    /// <summary>
    /// Gets a value indicating whether the dialog is in edit mode.
    /// </summary>
    [ObservableProperty]
    private bool _isEditMode;

    /// <summary>
    /// Gets the dialog title based on the current mode.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S2325:Make member static", Justification = "ViewModel property bound to XAML view")]
    public string DialogTitle => IsEditMode ? "Edit Release" : "Add New Release";

    /// <summary>
    /// Gets the submit button text based on the current mode.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S2325:Make member static", Justification = "ViewModel property bound to XAML view")]
    public string SubmitButtonText => IsEditMode ? "Save Changes" : "Create Release";

    /// <summary>
    /// Gets the content item name for display in the dialog title.
    /// </summary>
    public string ContentName => _contentItem.Name;

    /// <summary>
    /// Gets the suggested next version based on existing releases.
    /// </summary>
    public string SuggestedVersion => GetNextVersion(_contentItem.Releases);

    [GeneratedRegex("^(\\d+)\\.(\\d+)\\.(\\d+)")]
    private static partial Regex VersionRegex();

    private static string GetNextVersion(IReadOnlyList<ContentRelease> existingReleases)
    {
        if (existingReleases.Count == 0)
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

    /// <summary>
    /// Initializes a new instance of the <see cref="AddReleaseDialogViewModel"/> class.
    /// </summary>
    /// <param name="contentItem">The content item to add a release to.</param>
    /// <param name="catalog">The publisher catalog.</param>
    /// <param name="onReleaseCreated">Callback invoked when release is successfully created.</param>
    /// <param name="dialogService">The dialog service.</param>
    public AddReleaseDialogViewModel(
        CatalogContentItem contentItem,
        PublisherCatalog catalog,
        Action<ContentRelease> onReleaseCreated,
        IPublisherStudioDialogService dialogService)
    {
        _contentItem = contentItem ?? throw new ArgumentNullException(nameof(contentItem));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _onReleaseCreated = onReleaseCreated ?? throw new ArgumentNullException(nameof(onReleaseCreated));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));

        // Set suggested version as default
        Version = SuggestedVersion;

        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Version))
            {
                Validate();
            }
        };
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AddReleaseDialogViewModel"/> class in edit mode,
    /// pre-populated with an existing release's data.
    /// </summary>
    /// <param name="existing">The existing release to edit.</param>
    /// <param name="contentItem">The content item the release belongs to.</param>
    /// <param name="catalog">The publisher catalog.</param>
    /// <param name="onReleaseCreated">Callback invoked when release is successfully saved.</param>
    /// <param name="dialogService">The dialog service.</param>
    public AddReleaseDialogViewModel(
        ContentRelease existing,
        CatalogContentItem contentItem,
        PublisherCatalog catalog,
        Action<ContentRelease> onReleaseCreated,
        IPublisherStudioDialogService dialogService)
        : this(contentItem, catalog, onReleaseCreated, dialogService)
    {
        ArgumentNullException.ThrowIfNull(existing);

        IsEditMode = true;
        _originalVersion = existing.Version;
        Version = existing.Version;
        ReleaseDate = existing.ReleaseDate.HasValue ? new DateTimeOffset(existing.ReleaseDate.Value) : DateTimeOffset.Now;
        IsLatest = existing.IsLatest;
        IsPrerelease = existing.IsPrerelease;
        IsFeatured = existing.IsFeatured;
        Changelog = existing.Changelog ?? string.Empty;

        foreach (var artifact in existing.Artifacts)
        {
            Artifacts.Add(artifact);
        }

        foreach (var dependency in existing.Dependencies)
        {
            Dependencies.Add(dependency);
        }
    }

    /// <summary>
    /// Applies the suggested version.
    /// </summary>
    [RelayCommand]
    private void ApplySuggestedVersion()
    {
        Version = SuggestedVersion;
    }

    /// <summary>
    /// Closes the dialog without saving.
    /// </summary>
    [RelayCommand]
    private void Close()
    {
        _onReleaseCreated(null!);
    }

    /// <summary>
    /// Adds an artifact to the release.
    /// </summary>
    [RelayCommand]
    private async Task AddArtifactAsync()
    {
        var artifact = await _dialogService.ShowAddArtifactDialogAsync();
        if (artifact != null)
        {
            Artifacts.Add(artifact);
            Validate();
        }
    }

    /// <summary>
    /// Removes an artifact from the release.
    /// </summary>
    /// <param name="artifact">The artifact to remove.</param>
    [RelayCommand]
    private void RemoveArtifact(ReleaseArtifact artifact)
    {
        Artifacts.Remove(artifact);
        Validate();
    }

    /// <summary>
    /// Adds a dependency to the release.
    /// </summary>
    [RelayCommand]
    private async Task AddDependencyAsync()
    {
        var dependency = await _dialogService.ShowAddDependencyDialogAsync(_catalog, _contentItem);
        if (dependency != null)
        {
            Dependencies.Add(dependency);
            Validate();
        }
    }

    /// <summary>
    /// Removes a dependency from the release.
    /// </summary>
    /// <param name="dependency">The dependency to remove.</param>
    [RelayCommand]
    private void RemoveDependency(CatalogDependency dependency)
    {
        Dependencies.Remove(dependency);
    }

    /// <summary>
    /// Creates the release if validation passes.
    /// </summary>
    [RelayCommand]
    private void CreateRelease()
    {
        ValidateAllProperties();

        // Check for artifacts
        if (Artifacts.Count == 0)
        {
            ValidationError = "At least one artifact is required";
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
        var isDuplicateVersion = _contentItem.Releases.Any(r => r.Version.Equals(Version, StringComparison.OrdinalIgnoreCase));
        var isOriginalVersion = IsEditMode && _originalVersion != null && _originalVersion.Equals(Version, StringComparison.OrdinalIgnoreCase);
        if (isDuplicateVersion && !isOriginalVersion)
        {
            ValidationError = $"Version {Version} already exists for this content";
            IsValid = false;
            return;
        }

        var release = new ContentRelease
        {
            Version = Version.Trim(),
            ReleaseDate = ReleaseDate.DateTime,
            IsLatest = IsLatest,
            IsPrerelease = IsPrerelease,
            IsFeatured = IsFeatured,
            Changelog = Changelog.Trim(),
            Artifacts = [.. Artifacts],
            Dependencies = [.. Dependencies],
        };

        _onReleaseCreated(release);
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
            errors.Add("At least one artifact is required");
        }

        IsValid = errors.Count == 0;
        ValidationError = errors.Count > 0 ? string.Join(Environment.NewLine, errors) : null;
    }
}
