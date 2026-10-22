using CommunityToolkit.Mvvm.ComponentModel;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Content.Services.Catalog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Downloads.ViewModels;

/// <summary>
/// One member of a ContentBundle card: identity, optional variant pickers, and download state
/// for the currently selected option.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "ViewModel properties and methods bound to MVVM UI.")]
public sealed partial class BundleComponentViewModel : ObservableObject
{
    private readonly Dictionary<string, ContentSearchResult> _variantSearchResults = new(StringComparer.OrdinalIgnoreCase);
    private Action? _unsubscribeAxisHandlers;

    /// <summary>Gets the catalog content id of this component.</summary>
    public string CatalogContentId { get; init; } = string.Empty;

    /// <summary>Gets the display name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Gets a short content-type label.</summary>
    public string ContentTypeDisplay { get; init; } = string.Empty;

    /// <summary>Gets a value indicating whether this component is optional.</summary>
    public bool IsOptional { get; init; }

    /// <summary>
    /// Gets a value indicating whether this is a base-game installation constraint
    /// (not downloadable catalog content).
    /// </summary>
    public bool IsBaseGame { get; init; }

    /// <summary>Gets the flat variant list (empty or a single unnamed entry when there is no picker).</summary>
    public ObservableCollection<InstallableVariant> Variants { get; } = [];

    /// <summary>Gets variant options grouped by axis for ComboBox rendering.</summary>
    public ObservableCollection<VariantAxisGroup> VariantAxes { get; } = [];

    /// <summary>Gets a value indicating whether a variant dropdown should be shown.</summary>
    public bool HasVariants => Variants.Count > 1;

    /// <summary>Gets a value indicating whether more than one axis is present.</summary>
    public bool HasMultipleVariantAxes => VariantAxes.Count > 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSelectedDownloaded))]
    [NotifyPropertyChangedFor(nameof(RequiresUpdate))]
    [NotifyPropertyChangedFor(nameof(EffectiveState))]
    [NotifyPropertyChangedFor(nameof(SelectedDisplayName))]
    private InstallableVariant? _selectedVariant;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSelectedDownloaded))]
    [NotifyPropertyChangedFor(nameof(RequiresUpdate))]
    [NotifyPropertyChangedFor(nameof(EffectiveState))]
    private ContentState _currentState = ContentState.NotDownloaded;

    /// <summary>Gets the effective install state of the selected option.</summary>
    public ContentState EffectiveState => SelectedVariant?.CurrentState ?? CurrentState;

    /// <summary>Gets a value indicating whether the selected option is acquired.</summary>
    public bool IsSelectedDownloaded =>
        EffectiveState is ContentState.Downloaded or ContentState.UpdateAvailable;

    /// <summary>
    /// Gets a value indicating whether this required component still needs to be downloaded.
    /// </summary>
    public bool RequiresDownload => !IsBaseGame && !IsOptional && !IsSelectedDownloaded;

    /// <summary>
    /// Gets a value indicating whether this required component is acquired but a newer
    /// version is available. Unlike <see cref="RequiresDownload"/>, these members count
    /// as ready for profiles but should surface a bundle-level update action.
    /// </summary>
    public bool RequiresUpdate => !IsBaseGame && !IsOptional && EffectiveState == ContentState.UpdateAvailable;

    /// <summary>Gets the name shown on the component row (variant label when present).</summary>
    public string SelectedDisplayName =>
        HasVariants && SelectedVariant != null && !string.IsNullOrWhiteSpace(SelectedVariant.Name)
            ? $"{Name} — {SelectedVariant.Name}"
            : Name;

    /// <summary>
    /// Builds view-models from bundle component JSON stored on a search result.
    /// </summary>
    /// <param name="bundleResult">The bundle search result.</param>
    /// <returns>Downloadable (non-base-game) component view-models.</returns>
    public static IReadOnlyList<BundleComponentViewModel> CreateFromSearchResult(ContentSearchResult bundleResult)
    {
        ArgumentNullException.ThrowIfNull(bundleResult);

        if (bundleResult.ContentType != ContentType.ContentBundle)
        {
            return [];
        }

        if (!bundleResult.ResolverMetadata.TryGetValue(
                CatalogConstants.BundleComponentsJsonMetadataKey,
                out var json) ||
            string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        List<CatalogBundleComponentDescriptor>? descriptors;
        try
        {
            descriptors = JsonSerializer.Deserialize<List<CatalogBundleComponentDescriptor>>(json);
        }
        catch (JsonException)
        {
            return [];
        }

        if (descriptors == null || descriptors.Count == 0)
        {
            return [];
        }

        bundleResult.ResolverMetadata.TryGetValue(
            CatalogConstants.PublisherProfileJsonMetadataKey,
            out var publisherJson);

        var components = new List<BundleComponentViewModel>();
        foreach (var descriptor in descriptors)
        {
            var component = CreateComponentFromDescriptor(bundleResult, descriptor, publisherJson);
            if (component != null)
            {
                components.Add(component);
            }
        }

        return components;
    }

    /// <summary>
    /// Returns whether every required component's selected variant is downloaded.
    /// </summary>
    /// <param name="components">Bundle components.</param>
    /// <returns><see langword="true"/> when the bundle is ready to add to a profile.</returns>
    public static bool AreRequiredSelectionsDownloaded(IEnumerable<BundleComponentViewModel> components) =>
        components.Where(c => !c.IsOptional && !c.IsBaseGame).All(c => c.IsSelectedDownloaded);

    /// <summary>
    /// Returns search results that still need to be acquired for the current selections.
    /// </summary>
    /// <param name="components">Bundle components.</param>
    /// <returns>Missing download targets.</returns>
    public static IReadOnlyList<ContentSearchResult> GetRequiredDownloadTargets(
        IEnumerable<BundleComponentViewModel> components)
    {
        var targets = new List<ContentSearchResult>();
        foreach (var component in components.Where(c => c.RequiresDownload))
        {
            var searchResult = component.GetSelectedSearchResult();
            if (searchResult != null)
            {
                targets.Add(searchResult);
            }
        }

        return targets;
    }

    /// <summary>
    /// Returns search results for acquired members that have a newer version available.
    /// </summary>
    /// <param name="components">Bundle components.</param>
    /// <returns>Update targets for the current selections.</returns>
    public static IReadOnlyList<ContentSearchResult> GetRequiredUpdateTargets(
        IEnumerable<BundleComponentViewModel> components)
    {
        var targets = new List<ContentSearchResult>();
        foreach (var component in components.Where(c => c.RequiresUpdate))
        {
            var searchResult = component.GetSelectedSearchResult();
            if (searchResult != null)
            {
                targets.Add(searchResult);
            }
        }

        return targets;
    }

    /// <summary>
    /// Resolves acquired manifest IDs for every required selected component.
    /// </summary>
    /// <param name="components">Bundle components.</param>
    /// <param name="contentStateService">The content state service.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Acquired manifest IDs, or an empty list when any required item is missing.</returns>
    public static async Task<IReadOnlyList<string>> GetRequiredProfileManifestIdsAsync(
        IEnumerable<BundleComponentViewModel> components,
        IContentStateService contentStateService,
        CancellationToken cancellationToken = default)
    {
        var ids = new List<string>();
        foreach (var component in components.Where(c => !c.IsOptional && !c.IsBaseGame))
        {
            var manifestId = await component.GetAcquiredManifestIdAsync(contentStateService, cancellationToken);
            if (string.IsNullOrEmpty(manifestId))
            {
                return [];
            }

            ids.Add(manifestId);
        }

        return ids;
    }

    /// <summary>
    /// Adds a variant option and its acquire-able search result.
    /// </summary>
    /// <param name="variant">The installable option.</param>
    /// <param name="searchResult">The sibling search result used for acquisition.</param>
    public void AddVariant(InstallableVariant variant, ContentSearchResult searchResult)
    {
        Variants.Add(variant);
        var key = !string.IsNullOrEmpty(variant.ManifestId) ? variant.ManifestId : searchResult.Id;
        if (!string.IsNullOrEmpty(key))
        {
            _variantSearchResults[key] = VariantSwap.Clone(searchResult);
        }

        RebuildVariantAxes();
        OnPropertyChanged(nameof(HasVariants));
        OnPropertyChanged(nameof(HasMultipleVariantAxes));
    }

    /// <summary>
    /// Gets the search result that should be acquired for the current selection.
    /// </summary>
    /// <returns>The selected search result, or <see langword="null"/> for base-game rows.</returns>
    public ContentSearchResult? GetSelectedSearchResult()
    {
        if (IsBaseGame)
        {
            return null;
        }

        if (SelectedVariant != null &&
            !string.IsNullOrEmpty(SelectedVariant.ManifestId) &&
            _variantSearchResults.TryGetValue(SelectedVariant.ManifestId, out var selected))
        {
            return selected;
        }

        return _variantSearchResults.Values.FirstOrDefault();
    }

    /// <summary>
    /// Resolves the acquired manifest ID for the current selection.
    /// </summary>
    /// <param name="contentStateService">The content state service.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The acquired manifest ID, or <see langword="null"/> if not downloaded.</returns>
    public async Task<string?> GetAcquiredManifestIdAsync(
        IContentStateService contentStateService,
        CancellationToken cancellationToken = default)
    {
        var searchResult = GetSelectedSearchResult();
        if (searchResult == null)
        {
            return null;
        }

        if (!string.IsNullOrEmpty(searchResult.Id) &&
            ManifestIdValidator.IsValid(searchResult.Id, out _) &&
            await contentStateService.GetStateByManifestIdAsync(searchResult.Id, cancellationToken) == ContentState.Downloaded)
        {
            return searchResult.Id;
        }

        return await contentStateService.GetLocalManifestIdAsync(searchResult, cancellationToken);
    }

    /// <summary>
    /// Refreshes install state from the content state service.
    /// </summary>
    /// <param name="contentStateService">The content state service.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task RefreshStateAsync(
        IContentStateService contentStateService,
        CancellationToken cancellationToken = default)
    {
        if (IsBaseGame)
        {
            CurrentState = ContentState.Downloaded;
            return;
        }

        foreach (var variant in Variants)
        {
            var key = !string.IsNullOrEmpty(variant.ManifestId) ? variant.ManifestId : null;
            if (key != null && _variantSearchResults.TryGetValue(key, out var sibling))
            {
                variant.CurrentState = await contentStateService.GetStateAsync(sibling, cancellationToken);
                continue;
            }

            if (!string.IsNullOrEmpty(variant.ManifestId))
            {
                variant.CurrentState = await contentStateService.GetStateByManifestIdAsync(
                    variant.ManifestId,
                    cancellationToken);
            }
        }

        if (SelectedVariant != null)
        {
            CurrentState = SelectedVariant.CurrentState;
        }
        else if (Variants.Count == 1)
        {
            CurrentState = Variants[0].CurrentState;
        }

        OnPropertyChanged(nameof(IsSelectedDownloaded));
        OnPropertyChanged(nameof(EffectiveState));
        OnPropertyChanged(nameof(RequiresDownload));
        OnPropertyChanged(nameof(RequiresUpdate));
    }

    /// <summary>
    /// Resets all variant states to NotDownloaded.
    /// </summary>
    public void ResetState()
    {
        CurrentState = ContentState.NotDownloaded;
        foreach (var variant in Variants)
        {
            variant.CurrentState = ContentState.NotDownloaded;
        }

        OnPropertyChanged(nameof(IsSelectedDownloaded));
        OnPropertyChanged(nameof(EffectiveState));
        OnPropertyChanged(nameof(RequiresDownload));
        OnPropertyChanged(nameof(RequiresUpdate));
    }

    /// <summary>
    /// Marks the matching variant as downloaded after a successful acquisition.
    /// </summary>
    /// <param name="catalogContentId">The pre-download catalog ID.</param>
    /// <param name="manifestId">The stored manifest ID.</param>
    public void MarkDownloaded(string catalogContentId, string manifestId)
    {
        foreach (var variant in Variants)
        {
            if ((!string.IsNullOrEmpty(catalogContentId) &&
                 string.Equals(variant.ManifestId, catalogContentId, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(manifestId) &&
                 string.Equals(variant.ManifestId, manifestId, StringComparison.OrdinalIgnoreCase)))
            {
                variant.CurrentState = ContentState.Downloaded;
            }
        }

        if (!string.IsNullOrEmpty(catalogContentId) &&
            _variantSearchResults.TryGetValue(catalogContentId, out var sibling))
        {
            sibling.UpdateId(manifestId);
        }

        if (SelectedVariant != null)
        {
            CurrentState = SelectedVariant.CurrentState;
        }
        else if (Variants.Count == 1)
        {
            CurrentState = Variants[0].CurrentState;
        }
        else if (Variants.Any(v => v.CurrentState == ContentState.Downloaded))
        {
            CurrentState = ContentState.Downloaded;
        }

        OnPropertyChanged(nameof(IsSelectedDownloaded));
        OnPropertyChanged(nameof(EffectiveState));
        OnPropertyChanged(nameof(RequiresDownload));
        OnPropertyChanged(nameof(RequiresUpdate));
    }

    /// <summary>
    /// Sets the download URL from the artifact matching the selected variant,
    /// falling back to the release primary and then the first downloadable artifact.
    /// </summary>
    /// <param name="searchResult">The component search result to update.</param>
    /// <param name="variant">The selected component variant.</param>
    /// <param name="releaseJson">The serialized release holding every artifact.</param>
    internal static void ApplyReleaseDownloadUrl(
        ContentSearchResult searchResult,
        CatalogBundleComponentVariantDescriptor variant,
        string? releaseJson)
    {
        if (string.IsNullOrWhiteSpace(releaseJson) || !string.IsNullOrWhiteSpace(searchResult.SelectedDownloadUrl))
        {
            return;
        }

        try
        {
            var release = JsonSerializer.Deserialize<ContentRelease>(releaseJson);
            var downloadable = release?.Artifacts?.Where(a => !string.IsNullOrWhiteSpace(a.DownloadUrl)).ToList();
            var artifact = (!string.IsNullOrWhiteSpace(variant.Label)
                ? downloadable?.FirstOrDefault(a => string.Equals(a.Variant, variant.Label, StringComparison.OrdinalIgnoreCase) &&
                    (string.IsNullOrWhiteSpace(variant.Axis) || string.Equals(a.VariantAxis, variant.Axis, StringComparison.OrdinalIgnoreCase))) ??
                    downloadable?.FirstOrDefault(a => string.Equals(a.Variant, variant.Label, StringComparison.OrdinalIgnoreCase))
                : null) ??
                release?.Artifacts?.FirstOrDefault(a => a.IsPrimary && !string.IsNullOrWhiteSpace(a.DownloadUrl)) ??
                downloadable?.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(artifact?.DownloadUrl))
            {
                searchResult.SelectedDownloadUrl = artifact.DownloadUrl;
            }
        }
        catch (JsonException)
        {
            // Fall back to catalog IDs when the embedded release cannot be parsed.
        }
    }

    private static BundleComponentViewModel? CreateComponentFromDescriptor(
        ContentSearchResult bundleResult,
        CatalogBundleComponentDescriptor descriptor,
        string? publisherJson)
    {
        if (descriptor.IsBaseGame || descriptor.Variants.Count == 0)
        {
            return null;
        }

        var component = new BundleComponentViewModel
        {
            CatalogContentId = descriptor.ContentId,
            Name = descriptor.Name,
            ContentTypeDisplay = descriptor.ContentType,
            IsOptional = descriptor.IsOptional,
            IsBaseGame = false,
        };

        InstallableVariant? defaultVariant = null;
        foreach (var variant in descriptor.Variants)
        {
            var searchResult = CreateComponentSearchResult(bundleResult, descriptor, variant, publisherJson);
            var installable = new InstallableVariant
            {
                Name = string.IsNullOrWhiteSpace(variant.Label) ? descriptor.Name : variant.Label,
                ManifestId = variant.CatalogId,
                VariantType = variant.Axis ?? string.Empty,
            };

            component.AddVariant(installable, searchResult);
            if (variant.IsDefault)
            {
                defaultVariant = installable;
            }
        }

        component.SelectedVariant = defaultVariant ?? component.Variants.FirstOrDefault();
        return component;
    }

    private static ContentSearchResult CreateComponentSearchResult(
        ContentSearchResult bundleResult,
        CatalogBundleComponentDescriptor descriptor,
        CatalogBundleComponentVariantDescriptor variant,
        string? publisherJson)
    {
        if (!Enum.TryParse<ContentType>(descriptor.ContentType, ignoreCase: true, out var contentType))
        {
            contentType = ContentType.UnknownContentType;
        }

        var (version, lastUpdated) = ResolveReleaseTiming(bundleResult, variant.ReleaseJson);
        var targetGame = ResolveVariantGameType(bundleResult, variant);

        var searchResult = new ContentSearchResult
        {
            Id = variant.CatalogId,
            Name = string.IsNullOrWhiteSpace(variant.Label)
                ? descriptor.Name
                : $"{descriptor.Name} ({variant.Label})",
            Description = bundleResult.Description,
            Version = version,
            ContentType = contentType,
            TargetGame = targetGame,
            ProviderName = !string.IsNullOrWhiteSpace(descriptor.PublisherId)
                ? descriptor.PublisherId
                : bundleResult.ProviderName,
            AuthorName = bundleResult.AuthorName,
            ResolverId = bundleResult.ResolverId,
            IconUrl = bundleResult.IconUrl,
            BannerUrl = bundleResult.BannerUrl,
            LastUpdated = lastUpdated,
            RequiresResolution = true,
            DownloadSize = variant.DownloadSize,
        };

        if (!string.IsNullOrWhiteSpace(descriptor.CatalogItemJson))
        {
            searchResult.ResolverMetadata[CatalogConstants.CatalogItemJsonMetadataKey] = descriptor.CatalogItemJson;
        }

        if (!string.IsNullOrWhiteSpace(variant.ReleaseJson))
        {
            searchResult.ResolverMetadata[CatalogConstants.ReleaseJsonMetadataKey] = variant.ReleaseJson;
        }

        if (!string.IsNullOrWhiteSpace(publisherJson))
        {
            searchResult.ResolverMetadata[CatalogConstants.PublisherProfileJsonMetadataKey] = publisherJson;
        }

        searchResult.ResolverMetadata[CatalogConstants.CatalogContentIdMetadataKey] = descriptor.ContentId;

        ApplyUpstreamIdentity(searchResult, descriptor.CatalogItemJson, variant);
        return searchResult;
    }

    /// <summary>
    /// Propagates upstream GitHub identity and the selected artifact URL from the
    /// referenced catalog item so install-state detection can match the same files
    /// acquired through another publisher.
    /// </summary>
    private static void ApplyUpstreamIdentity(
        ContentSearchResult searchResult,
        string? catalogItemJson,
        CatalogBundleComponentVariantDescriptor variant)
    {
        ApplyCatalogItemUpstreamIdentity(searchResult, catalogItemJson);
        ApplyReleaseDownloadUrl(searchResult, variant, variant.ReleaseJson);
    }

    private static void ApplyCatalogItemUpstreamIdentity(ContentSearchResult searchResult, string? catalogItemJson)
    {
        if (string.IsNullOrWhiteSpace(catalogItemJson))
        {
            return;
        }

        CatalogContentItem? sibling;
        try
        {
            sibling = JsonSerializer.Deserialize<CatalogContentItem>(catalogItemJson, PublisherJsonOptions.CatalogImport);
        }
        catch (JsonException)
        {
            // Fall back to catalog IDs when the embedded item cannot be parsed.
            return;
        }

        if (sibling == null)
        {
            return;
        }

        var repository = ResolveSiblingRepository(sibling);
        if (repository != null)
        {
            ApplyGitHubCoordinates(searchResult, repository[0], repository[1]);
        }

        GenericCatalogDiscoverer.ApplyUpstreamCommunityOutpostIdentity(searchResult, sibling);
    }

    private static string[]? ResolveSiblingRepository(CatalogContentItem sibling)
    {
        var declaredProvider = CatalogConstants.UpstreamProviders.DeclaredProvider(sibling.UpstreamSync?.Provider, sibling.PublisherType);
        if (!CatalogConstants.UpstreamProviders.TryResolveGitHubRepository(
            declaredProvider,
            sibling.UpstreamSync?.Repository,
            out var owner,
            out var repo))
        {
            return null;
        }

        return [owner, repo];
    }

    private static void ApplyGitHubCoordinates(ContentSearchResult searchResult, string owner, string repo)
    {
        if (!searchResult.ResolverMetadata.ContainsKey(GitHubConstants.OwnerMetadataKey))
        {
            searchResult.ResolverMetadata[GitHubConstants.OwnerMetadataKey] = owner.Trim();
        }

        if (!searchResult.ResolverMetadata.ContainsKey(GitHubConstants.RepoMetadataKey))
        {
            searchResult.ResolverMetadata[GitHubConstants.RepoMetadataKey] = repo.Trim();
        }

        if (string.IsNullOrWhiteSpace(searchResult.SourceUrl))
        {
            searchResult.SourceUrl = $"https://github.com/{owner.Trim()}/{repo.Trim()}";
        }
    }

    private static (string Version, DateTime? LastUpdated) ResolveReleaseTiming(ContentSearchResult bundleResult, string? releaseJson)
    {
        var version = bundleResult.Version;
        var lastUpdated = bundleResult.LastUpdated;

        if (!string.IsNullOrWhiteSpace(releaseJson))
        {
            try
            {
                var releaseObj = JsonSerializer.Deserialize<ContentRelease>(releaseJson);
                if (releaseObj != null)
                {
                    if (!string.IsNullOrWhiteSpace(releaseObj.Version))
                    {
                        version = releaseObj.Version;
                    }

                    if (releaseObj.ReleaseDate != default)
                    {
                        lastUpdated = releaseObj.ReleaseDate;
                    }
                }
            }
            catch
            {
                // Fallback to bundle metadata
            }
        }

        return (version, lastUpdated);
    }

    private static GameType ResolveVariantGameType(ContentSearchResult bundleResult, CatalogBundleComponentVariantDescriptor variant)
    {
        if (string.Equals(variant.Axis, "game-type", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(variant.Label, "Generals", StringComparison.OrdinalIgnoreCase))
            {
                return GameType.Generals;
            }

            if (string.Equals(variant.Label, "Zero Hour", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(variant.Label, "ZeroHour", StringComparison.OrdinalIgnoreCase))
            {
                return GameType.ZeroHour;
            }
        }

        return bundleResult.TargetGame;
    }

    partial void OnSelectedVariantChanged(InstallableVariant? value)
    {
        VariantAxisGrouping.SyncSelections(VariantAxes, value);
        if (value != null)
        {
            CurrentState = value.CurrentState;
        }

        OnPropertyChanged(nameof(SelectedDisplayName));
        OnPropertyChanged(nameof(IsSelectedDownloaded));
        OnPropertyChanged(nameof(EffectiveState));
        OnPropertyChanged(nameof(RequiresDownload));
        OnPropertyChanged(nameof(RequiresUpdate));
    }

    private void RebuildVariantAxes()
    {
        _unsubscribeAxisHandlers = VariantAxisGrouping.Rebuild(
            Variants,
            VariantAxes,
            SelectedVariant,
            OnAxisSelectionCommitted,
            _unsubscribeAxisHandlers);
        OnPropertyChanged(nameof(HasMultipleVariantAxes));
    }

    private void OnAxisSelectionCommitted(InstallableVariant? value)
    {
        if (value == null || ReferenceEquals(SelectedVariant, value))
        {
            return;
        }

        SelectedVariant = value;
    }
}
