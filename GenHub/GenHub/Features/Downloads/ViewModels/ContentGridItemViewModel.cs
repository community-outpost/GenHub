using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using GenHub.Core.Constants;
using GenHub.Core.Extensions;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Messages;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Downloads.Services;
using GenHub.Infrastructure.Services;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Downloads.ViewModels;

/// <summary>
/// Initializes a new instance of the <see cref="ContentGridItemViewModel"/> class.
/// </summary>
/// <param name="searchResult">The content search result to display.</param>
/// <param name="contentStateService">The content state service.</param>
/// <param name="logger">The logger.</param>
public partial class ContentGridItemViewModel(
    ContentSearchResult searchResult,
    IContentStateService contentStateService,
    ILogger<ContentGridItemViewModel> logger) : ObservableObject, IDisposable
{
    private bool _disposed;

    /// <summary>
    /// Gets the underlying content search result.
    /// </summary>
    public ContentSearchResult SearchResult { get; } = searchResult ?? throw new ArgumentNullException(nameof(searchResult));

    [ObservableProperty]
    private bool _isDownloading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDownloadButton))]
    [NotifyPropertyChangedFor(nameof(ShowAddToProfileButton))]
    private bool _isDownloaded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDownloadButton))]
    [NotifyPropertyChangedFor(nameof(ShowUpdateButton))]
    [NotifyPropertyChangedFor(nameof(ShowAddToProfileButton))]
    private ContentState _currentState = ContentState.NotDownloaded;

    [ObservableProperty]
    private int _downloadProgress;

    [ObservableProperty]
    private string _downloadStatus = string.Empty;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private Bitmap? _iconBitmap;

    [ObservableProperty]
    private Bitmap? _publisherLogoBitmap;

    /// <summary>
    /// Performs initialization.
    /// </summary>
    public void Initialize()
    {
        // Subscribe to content state changes
        contentStateService.ContentStateChanged += OnContentStateChanged;
        WeakReferenceMessenger.Default.Register<ContentLibraryClearedMessage>(
            this,
            static (recipient, _) => ((ContentGridItemViewModel)recipient).ResetDownloadState());

        LoadBundleComponents();
        _ = LoadIconAsync();
        _ = RefreshBundleComponentStatesAsync();
    }

    /// <summary>
    /// Gets the content ID.
    /// </summary>
    public string Id => SearchResult.Id ?? string.Empty;

    /// <summary>
    /// Gets the display name of the content.
    /// </summary>
    public string Name
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(SelectedVariant?.Name))
            {
                return SelectedVariant.Name;
            }

            if (!string.IsNullOrWhiteSpace(SearchResult.Name))
            {
                return SearchResult.Name;
            }

            return SearchResult.VariantFamilyName ?? "Unknown";
        }
    }

    /// <summary>
    /// Gets the content description.
    /// </summary>
    public string Description => HtmlTextHelper.NormalizeHtml(SearchResult.Description);

    /// <summary>
    /// Gets the truncated description for card display.
    /// </summary>
    public string ShortDescription => HtmlTextHelper.CleanToSingleLine(Description, 90);

    /// <summary>
    /// Gets a value indicating whether a short description should be shown on the card.
    /// </summary>
    public bool HasShortDescription => !HasBundleComponents && !string.IsNullOrWhiteSpace(ShortDescription);

    /// <summary>
    /// Gets the content version.
    /// </summary>
    public string Version => SearchResult.Version ?? string.Empty;

    /// <summary>
    /// Gets a concise, labelled version badge for display on cards.
    /// </summary>
    public string VersionBadge
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Version))
            {
                return string.Empty;
            }

            // GitHub release tags such as "weekly-2026-07-03" are build stamps, not semantic
            // versions. Surface the date alone so the badge reads as a clean build identifier.
            var dateSuffix = ContentCardBadgeHelper.ExtractDateFromTag(Version);
            if (dateSuffix != null)
            {
                return dateSuffix;
            }

            return char.IsDigit(Version[0]) ? $"v{Version}" : Version;
        }
    }

    /// <summary>
    /// Gets a value indicating whether the version is meaningful enough to show on a card.
    /// </summary>
    public bool HasDisplayVersion => !string.IsNullOrWhiteSpace(Version) &&
                                     !string.Equals(Version, "0", StringComparison.Ordinal);

    /// <summary>
    /// Gets the map player-count badge when a publisher exposes one.
    /// </summary>
    public string PlayerCountBadge => ContentCardBadgeHelper.GetPlayerCountBadge(SearchResult);

    /// <summary>
    /// Gets a value indicating whether a player-count badge is available.
    /// </summary>
    public bool HasPlayerCountBadge => !string.IsNullOrEmpty(PlayerCountBadge);

    /// <summary>
    /// Gets the category badge when a publisher exposes one.
    /// </summary>
    public string CategoryBadge => ContentCardBadgeHelper.GetCategoryBadge(SearchResult);

    /// <summary>
    /// Gets a value indicating whether a category badge is available.
    /// </summary>
    public bool HasCategoryBadge => !string.IsNullOrEmpty(CategoryBadge) &&
                                     !ContentCardBadgeHelper.IsCategoryDuplicateOfContentType(CategoryBadge, ContentType);

    /// <summary>
    /// Gets the tags to render as chips, excluding any already surfaced by the
    /// player-count or category badges to avoid duplicate display.
    /// </summary>
    public IReadOnlyList<string> CardTags => ContentCardBadgeHelper.GetCardTags(SearchResult);

    /// <summary>
    /// Gets a capped tag list for glanceable card chips (avoids wrapping a full tag dump).
    /// </summary>
    public IReadOnlyList<string> DisplayCardTags => [.. CardTags.Take(3)];

    /// <summary>
    /// Gets a value indicating whether there are tag chips to display.
    /// </summary>
    public bool HasCardTags => DisplayCardTags.Count > 0;

    /// <summary>
    /// Gets a comma-separated includes summary for bundles / multi-content packages.
    /// </summary>
    public string IncludesSummary => ContentCardBadgeHelper.GetIncludesSummary(SearchResult);

    /// <summary>
    /// Gets a value indicating whether an includes summary is available.
    /// </summary>
    public bool HasIncludesSummary => !HasBundleComponents && !string.IsNullOrWhiteSpace(IncludesSummary);

    /// <summary>
    /// Gets a compact badge when this card collapses multiple installable variants.
    /// </summary>
    public string VariantCountBadge => HasVariants ? $"{Variants.Count} variants" : string.Empty;

    /// <summary>
    /// Gets a value indicating whether the variant-count badge should be shown.
    /// </summary>
    public bool HasVariantCountBadge => HasVariants;

    /// <summary>
    /// Gets the author name.
    /// </summary>
    public string AuthorName
    {
        get
        {
            var author = SearchResult.AuthorName;
            if (string.IsNullOrWhiteSpace(author) || string.Equals(author, "Unknown", StringComparison.OrdinalIgnoreCase))
            {
                return !string.IsNullOrWhiteSpace(ProviderName) ? ProviderName : "Unknown";
            }

            if (string.Equals(author, "GenHub Test Publishers", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(ProviderName))
            {
                return ProviderName;
            }

            return author;
        }
    }

    /// <summary>
    /// Gets a value indicating whether the author is known (not null/empty/Unknown).
    /// </summary>
    public bool HasAuthor => !string.IsNullOrEmpty(AuthorName) &&
                             !string.Equals(AuthorName, "Unknown", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the content type.
    /// </summary>
    public ContentType ContentType => SearchResult.ContentType;

    /// <summary>
    /// Gets the content type display name.
    /// </summary>
    public string ContentTypeDisplay => ContentType.GetDisplayName();

    /// <summary>
    /// Gets the target game.
    /// </summary>
    public GameType TargetGame => SearchResult.TargetGame;

    /// <summary>
    /// Gets the provider name.
    /// </summary>
    public string ProviderName => SearchResult.ProviderName ?? string.Empty;

    /// <summary>
    /// Gets the icon URL for the content.
    /// </summary>
    public string? IconUrl => SearchResult.IconUrl;

    /// <summary>
    /// Gets the preferred card thumbnail URL (banner / screenshot / icon).
    /// </summary>
    public string? ThumbnailUrl => ContentCardBadgeHelper.GetThumbnailUrl(SearchResult);

    /// <summary>
    /// Gets the source URL for viewing more details.
    /// </summary>
    public string? SourceUrl => SearchResult.SourceUrl;

    /// <summary>
    /// Gets the last updated date (optional).
    /// </summary>
    public DateTime? LastUpdated => SearchResult.LastUpdated;

    /// <summary>
    /// Gets the formatted last updated string.
    /// </summary>
    public string LastUpdatedDisplay => LastUpdated?.ToString("MMM dd, yyyy") ?? "Unknown Date";

    /// <summary>
    /// Gets a value indicating whether the last updated date is visible.
    /// </summary>
    public bool IsLastUpdatedVisible => LastUpdated.HasValue;

    /// <summary>
    /// Gets a value indicating whether both author and date are visible (for separator).
    /// </summary>
    public bool HasAuthorAndDate => HasAuthor && IsLastUpdatedVisible;

    /// <summary>
    /// Gets the download size in bytes.
    /// </summary>
    public long DownloadSize => SearchResult.DownloadSize;

    /// <summary>
    /// Gets a value indicating whether the download size should be displayed (non-zero).
    /// </summary>
    public bool IsDownloadSizeVisible => DownloadSize > 0;

    /// <summary>
    /// Gets a value indicating whether the Download button should be shown. Reflects the
    /// currently selected variant when the card represents a variant group.
    /// </summary>
    public bool ShowDownloadButton => HasBundleComponents
        ? !AreBundleComponentsReadyForProfile && !IsDownloading
        : EffectiveCurrentState == ContentState.NotDownloaded && !EffectiveIsDownloaded;

    /// <summary>
    /// Gets a value indicating whether the Update button should be shown. Reflects the
    /// currently selected variant when the card represents a variant group.
    /// </summary>
    public bool ShowUpdateButton => !HasBundleComponents && EffectiveCurrentState == ContentState.UpdateAvailable;

    /// <summary>
    /// Gets a value indicating whether the Add to Profile button should be shown.
    /// Bundles require every selected required member to be acquired — the empty bundle
    /// recipe itself is never enough.
    /// </summary>
    public bool ShowAddToProfileButton => HasBundleComponents
        ? AreBundleComponentsReadyForProfile
        : EffectiveCurrentState == ContentState.Downloaded || EffectiveIsDownloaded;

    /// <summary>
    /// Gets the tags associated with this content.
    /// </summary>
    public IList<string> Tags => SearchResult.Tags;

    /// <summary>
    /// Gets or sets the command to view details.
    /// </summary>
    public System.Windows.Input.ICommand? ViewCommand { get; set; }

    /// <summary>
    /// Gets or sets the command to open the source URL.
    /// </summary>
    public System.Windows.Input.ICommand? OpenUrlCommand { get; set; }

    /// <summary>
    /// Gets or sets the command to download the content.
    /// </summary>
    public System.Windows.Input.ICommand? DownloadCommand { get; set; }

    /// <summary>
    /// Gets or sets the command to add content to a profile.
    /// </summary>
    public System.Windows.Input.ICommand? AddToProfileCommand { get; set; }

    /// <summary>
    /// Gets or sets the command to update the content (download newer version).
    /// </summary>
    public System.Windows.Input.ICommand? UpdateCommand { get; set; }

    /// <summary>
    /// Gets a value indicating whether this content has multiple variants.
    /// </summary>
    public bool HasVariants => Variants.Count > 0;

    /// <summary>
    /// Gets variant options grouped by <see cref="InstallableVariant.VariantType"/> for multi-axis ComboBoxes.
    /// Single-axis content (e.g. lemon controlbar resolution) yields one group — identical UX to a lone ComboBox.
    /// Multi-axis is rendering infrastructure only; choosing an option sets <see cref="SelectedVariant"/>
    /// with no cross-product filtering between axes.
    /// </summary>
    public ObservableCollection<VariantAxisGroup> VariantAxes { get; } = [];

    /// <summary>
    /// Gets a value indicating whether more than one variant axis is present (show per-axis labels).
    /// </summary>
    public bool HasMultipleVariantAxes => VariantAxes.Count > 1;

    /// <summary>
    /// Gets downloadable members of a ContentBundle, each with its own identity and variant pickers.
    /// </summary>
    public ObservableCollection<BundleComponentViewModel> BundleComponents { get; } = [];

    /// <summary>
    /// Gets a value indicating whether this card is a multi-content bundle with selectable members.
    /// </summary>
    public bool HasBundleComponents => BundleComponents.Count > 0;

    /// <summary>
    /// Gets a value indicating whether every required selected bundle member is acquired.
    /// </summary>
    public bool AreBundleComponentsReadyForProfile =>
        HasBundleComponents && BundleComponentViewModel.AreRequiredSelectionsDownloaded(BundleComponents);

    private Action? _unsubscribeAxisHandlers;

    /// <summary>
    /// Disposes resources used by the view model.
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            // Unsubscribe from state changes
            contentStateService.ContentStateChanged -= OnContentStateChanged;
            WeakReferenceMessenger.Default.Unregister<ContentLibraryClearedMessage>(this);
            _unsubscribeAxisHandlers?.Invoke();
            _unsubscribeAxisHandlers = null;
            foreach (var component in BundleComponents)
            {
                component.PropertyChanged -= OnBundleComponentPropertyChanged;
            }

            IconBitmap = null;
            PublisherLogoBitmap = null;

            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// Clears a completed UI-only operation message before a cached card is restored.
    /// </summary>
    public void ClearInactiveDownloadStatus()
    {
        if (!IsDownloading)
        {
            DownloadStatus = string.Empty;
        }
    }

    private void ResetDownloadState()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            CurrentState = ContentState.NotDownloaded;
            IsDownloaded = false;
            IsDownloading = false;
            DownloadStatus = string.Empty;
        });
    }

    /// <summary>
    /// Handles content state changes from the ContentStateService.
    /// </summary>
    private void OnContentStateChanged(object? sender, ContentStateChangedEventArgs e)
    {
        // Match on either the catalog ID or the manifest ID: after a download the shared
        // ContentSearchResult's ID is rewritten to the manifest ID, so a single key is not enough.
        var isForThisContent = e.ContentId == Id ||
                               (!string.IsNullOrEmpty(e.ManifestId) && e.ManifestId == Id);

        // A variant matches the changed content when its catalog key equals the event's
        // content ID, or when the stored sibling snapshot's Id was rewritten to the
        // on-disk manifest ID after a prior download of that same variant.
        var variantIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { e.ContentId };
        if (!string.IsNullOrEmpty(e.ManifestId))
        {
            variantIds.Add(e.ManifestId);
        }

        var variantMatched = false;
        var ownsVariant = Variants.Any(v =>
            !string.IsNullOrEmpty(v.ManifestId) && variantIds.Contains(v.ManifestId));
        if (!ownsVariant)
        {
            ownsVariant = _variantSearchResults.Any(kvp =>
                variantIds.Contains(kvp.Key) ||
                (!string.IsNullOrEmpty(kvp.Value.Id) && variantIds.Contains(kvp.Value.Id)));
        }

        var ownsBundleComponent = HasBundleComponents && BundleComponents.Any(component =>
            component.Variants.Any(v =>
                !string.IsNullOrEmpty(v.ManifestId) && variantIds.Contains(v.ManifestId)));

        var publisherMatches = false;
        if (!string.IsNullOrEmpty(e.ManifestId))
        {
            var segments = e.ManifestId.Split('.');
            if (segments.Length == 5 &&
                !string.IsNullOrEmpty(SearchResult.ProviderName) &&
                (string.Equals(segments[2], SearchResult.ProviderName, StringComparison.OrdinalIgnoreCase) ||
                 ContentStateService.IsCompatiblePublisherAlias(segments[2], SearchResult.ProviderName)) &&
                string.Equals(segments[3], SearchResult.ContentType.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                publisherMatches = true;
            }
        }

        // Only dispatch when this card owns the changed content (directly or via a variant or publisher family),
        // so unrelated state changes don't wake every card's UI thread on each event.
        if (!isForThisContent && !ownsVariant && !ownsBundleComponent && !publisherMatches)
        {
            return;
        }

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _ = RefreshVariantStatesAsync();

            var selectedVariantMatched = false;

            foreach (var variant in Variants)
            {
                var matched = !string.IsNullOrEmpty(variant.ManifestId) && variantIds.Contains(variant.ManifestId);
                if (!matched &&
                    !string.IsNullOrEmpty(variant.ManifestId) &&
                    _variantSearchResults.TryGetValue(variant.ManifestId, out var sibling) &&
                    !string.IsNullOrEmpty(sibling.Id) &&
                    variantIds.Contains(sibling.Id))
                {
                    matched = true;
                }

                if (matched)
                {
                    variant.CurrentState = e.NewState;
                    variantMatched = true;

                    if (ReferenceEquals(variant, SelectedVariant))
                    {
                        selectedVariantMatched = true;
                    }

                    if (e.NewState == ContentState.Downloaded &&
                        !string.IsNullOrEmpty(e.ManifestId) &&
                        !string.IsNullOrEmpty(variant.ManifestId) &&
                        _variantSearchResults.TryGetValue(variant.ManifestId, out var stored))
                    {
                        stored.UpdateId(e.ManifestId);
                    }
                }
            }

            if (variantMatched)
            {
                if (selectedVariantMatched)
                {
                    CurrentState = e.NewState;
                    IsDownloaded = e.NewState is ContentState.Downloaded or ContentState.UpdateAvailable;
                }

                OnPropertyChanged(nameof(EffectiveCurrentState));
                OnPropertyChanged(nameof(EffectiveIsDownloaded));
                OnPropertyChanged(nameof(ShowDownloadButton));
                OnPropertyChanged(nameof(ShowUpdateButton));
                OnPropertyChanged(nameof(ShowAddToProfileButton));
            }

            if (HasBundleComponents)
            {
                foreach (var component in BundleComponents)
                {
                    foreach (var variant in component.Variants)
                    {
                        if (!string.IsNullOrEmpty(variant.ManifestId) && variantIds.Contains(variant.ManifestId))
                        {
                            component.MarkDownloaded(e.ContentId, e.ManifestId ?? variant.ManifestId);
                        }
                    }
                }

                OnPropertyChanged(nameof(AreBundleComponentsReadyForProfile));
                OnPropertyChanged(nameof(ShowDownloadButton));
                OnPropertyChanged(nameof(ShowAddToProfileButton));
            }

            if (isForThisContent && !HasBundleComponents)
            {
                CurrentState = e.NewState;

                switch (e.NewState)
                {
                    case ContentState.Downloaded:
                        IsDownloaded = true;
                        IsDownloading = false;
                        break;
                    case ContentState.NotDownloaded:
                        IsDownloaded = false;
                        IsDownloading = false;
                        break;
                    case ContentState.UpdateAvailable:
                        IsDownloaded = true;
                        IsDownloading = false;
                        break;
                    default:
                        // State unchanged
                        break;
                }

                logger.LogDebug("Content state updated for {ContentId}: {State}", e.ContentId, e.NewState);
            }
        });
    }

    private int _iconLoadVersion;

    private async Task LoadIconAsync()
    {
        if (_disposed)
        {
            return;
        }

        var currentVersion = ++_iconLoadVersion;

        // 1. Load publisher logo if available
        var publisherLogoUrl = ContentCardBadgeHelper.GetPublisherLogoUrl(SearchResult);
        if (!string.IsNullOrEmpty(publisherLogoUrl) && PublisherLogoBitmap == null)
        {
            try
            {
                PublisherLogoBitmap = await ImageCacheService.Instance.GetBitmapAsync(publisherLogoUrl);
            }
            catch
            {
                // ignore load failure for publisher logo
            }
        }

        // 2. Load primary thumbnail bitmap
        var thumbnailUrl = ThumbnailUrl;
        if (string.IsNullOrEmpty(thumbnailUrl))
        {
            if (currentVersion == _iconLoadVersion)
            {
                IconBitmap = null;
            }

            return;
        }

        try
        {
            var loadedBitmap = await ImageCacheService.Instance.GetBitmapAsync(thumbnailUrl);
            if (currentVersion == _iconLoadVersion)
            {
                IconBitmap = loadedBitmap;
            }
        }
        catch
        {
            if (currentVersion == _iconLoadVersion)
            {
                IconBitmap = null;
            }
        }
    }

    /// <summary>
    /// Command to view content details.
    /// </summary>
    [RelayCommand]
    private void ViewDetails()
    {
        ViewCommand?.Execute(this);
    }

    /// <summary>
    /// Command to open source URL in browser.
    /// </summary>
    [RelayCommand]
    private void OpenSourceUrl()
    {
        if (!string.IsNullOrEmpty(SourceUrl))
        {
            OpenUrlCommand?.Execute(SourceUrl);
        }
    }

    /// <summary>
    /// Maps variant manifest IDs (or content IDs when manifest ID is empty) back to the
    /// original <see cref="ContentSearchResult"/> so download/add-to-profile can target
    /// the correct sibling card.
    /// </summary>
    private readonly Dictionary<string, ContentSearchResult> _variantSearchResults = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the map of manifest ID to search result for sibling variants.
    /// </summary>
    public IReadOnlyDictionary<string, ContentSearchResult> VariantSearchResults => _variantSearchResults;

    [ObservableProperty]
    private System.Collections.ObjectModel.ObservableCollection<InstallableVariant> _variants = [];

    /// <summary>
    /// Gets or sets the currently selected variant in the dropdown.
    /// When set, the card's effective state reflects this variant's install status.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveCurrentState))]
    [NotifyPropertyChangedFor(nameof(EffectiveIsDownloaded))]
    [NotifyPropertyChangedFor(nameof(ShowDownloadButton))]
    [NotifyPropertyChangedFor(nameof(ShowUpdateButton))]
    [NotifyPropertyChangedFor(nameof(ShowAddToProfileButton))]
    private InstallableVariant? _selectedVariant;

    /// <summary>
    /// Gets the effective <see cref="ContentState"/> for the card. When a variant is
    /// selected, returns that variant's state; otherwise returns the card's own state.
    /// </summary>
    public ContentState EffectiveCurrentState => SelectedVariant?.CurrentState ?? CurrentState;

    /// <summary>
    /// Gets a value indicating whether the effective selection is downloaded. When a variant
    /// is selected, reflects <em>only</em> that variant's state — never the card-level
    /// <see cref="IsDownloaded"/> flag, which would keep "Add to Profile" visible after
    /// downloading a sibling and switching to an undownloaded variant.
    /// </summary>
    public bool EffectiveIsDownloaded => SelectedVariant != null
        ? SelectedVariant.CurrentState is ContentState.Downloaded or ContentState.UpdateAvailable
        : IsDownloaded;

    /// <summary>
    /// Adds a variant and optionally maps its <see cref="ContentSearchResult"/> for
    /// download/add-to-profile target swapping.
    /// </summary>
    /// <param name="variant">The variant to add.</param>
    /// <param name="searchResult">The sibling search result for this variant.</param>
    public void AddVariant(InstallableVariant variant, ContentSearchResult? searchResult = null)
    {
        Variants.Add(variant);
        if (searchResult != null)
        {
            var key = !string.IsNullOrEmpty(variant.ManifestId) ? variant.ManifestId : (searchResult.Id ?? string.Empty);
            if (!string.IsNullOrEmpty(key))
            {
                // Always store a clone. The card's SearchResult is often the default sibling
                // by reference; in-place VariantSwap would otherwise overwrite that entry.
                _variantSearchResults[key] = VariantSwap.Clone(searchResult);
            }
        }

        RebuildVariantAxes();
    }

    /// <summary>
    /// Rebuilds <see cref="VariantAxes"/> from the flat <see cref="Variants"/> list.
    /// </summary>
    public void RebuildVariantAxes()
    {
        _unsubscribeAxisHandlers = VariantAxisGrouping.Rebuild(
            Variants,
            VariantAxes,
            SelectedVariant,
            OnAxisSelectionCommitted,
            _unsubscribeAxisHandlers);
        OnPropertyChanged(nameof(HasMultipleVariantAxes));
        OnPropertyChanged(nameof(HasVariants));
    }

    /// <summary>
    /// Hydrates <see cref="BundleComponents"/> from catalog metadata on the search result.
    /// </summary>
    public void LoadBundleComponents()
    {
        if (BundleComponents.Count > 0)
        {
            return;
        }

        foreach (var component in BundleComponentViewModel.CreateFromSearchResult(SearchResult))
        {
            component.PropertyChanged += OnBundleComponentPropertyChanged;
            BundleComponents.Add(component);
        }

        OnPropertyChanged(nameof(HasBundleComponents));
        OnPropertyChanged(nameof(AreBundleComponentsReadyForProfile));
        OnPropertyChanged(nameof(ShowDownloadButton));
        OnPropertyChanged(nameof(ShowAddToProfileButton));
        OnPropertyChanged(nameof(HasIncludesSummary));
    }

    /// <summary>
    /// Refreshes each bundle member's install state from the manifest pool.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task RefreshBundleComponentStatesAsync()
    {
        if (BundleComponents.Count == 0)
        {
            return;
        }

        foreach (var component in BundleComponents)
        {
            await component.RefreshStateAsync(contentStateService);
        }

        OnPropertyChanged(nameof(AreBundleComponentsReadyForProfile));
        OnPropertyChanged(nameof(ShowDownloadButton));
        OnPropertyChanged(nameof(ShowAddToProfileButton));
    }

    /// <summary>
    /// Refreshes each variant's <see cref="InstallableVariant.CurrentState"/> by checking
    /// the manifest pool. Call during initialization or after a download completes.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task RefreshVariantStatesAsync()
    {
        try
        {
            var mainState = await contentStateService.GetStateAsync(SearchResult);
            CurrentState = mainState;
            IsDownloaded = mainState is ContentState.Downloaded or ContentState.UpdateAvailable;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to resolve state for main content {Id}", Id);
        }

        foreach (var variant in Variants)
        {
            // Resolve install state through the provenance-aware detection path using the sibling
            // ContentSearchResult. A variant's ManifestId is frequently the catalog card ID (e.g.
            // GitHub multi-asset releases), not the on-disk manifest ID, so GetStateByManifestIdAsync
            // would misreport. GetStateAsync maps the card to the stored manifest via OriginalContentId.
            var key = !string.IsNullOrEmpty(variant.ManifestId) ? variant.ManifestId : variant.Name;
            if (key != null && _variantSearchResults.TryGetValue(key, out var sibling))
            {
                try
                {
                    variant.CurrentState = await contentStateService.GetStateAsync(sibling);
                    continue;
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Failed to resolve state for variant {Key}", key);
                }
            }

            // Fallback: direct manifest-ID lookup when no sibling search result is mapped.
            if (!string.IsNullOrEmpty(variant.ManifestId))
            {
                try
                {
                    variant.CurrentState = await contentStateService.GetStateByManifestIdAsync(variant.ManifestId);
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Failed to resolve state for variant {ManifestId}", variant.ManifestId);
                }
            }
        }

        OnPropertyChanged(nameof(EffectiveCurrentState));
        OnPropertyChanged(nameof(EffectiveIsDownloaded));
        OnPropertyChanged(nameof(ShowDownloadButton));
        OnPropertyChanged(nameof(ShowUpdateButton));
        OnPropertyChanged(nameof(ShowAddToProfileButton));
    }

    /// <summary>
    /// Selects a variant matching the specified manifest ID.
    /// </summary>
    /// <param name="manifestId">The manifest ID of the variant to select.</param>
    public void SelectVariantByManifestId(string manifestId)
    {
        if (string.IsNullOrWhiteSpace(manifestId) || Variants == null)
        {
            return;
        }

        var match = Variants.FirstOrDefault(v => string.Equals(v.ManifestId, manifestId, StringComparison.OrdinalIgnoreCase));
        if (match != null)
        {
            SelectedVariant = match;
        }
    }

    /// <summary>
    /// Marks the matching variant as downloaded after a successful acquisition, and rewrites
    /// the stored sibling snapshot's ID to the on-disk manifest ID without losing the catalog
    /// key used for dropdown matching.
    /// </summary>
    /// <param name="catalogContentId">The pre-download catalog ID used as the variant key.</param>
    /// <param name="manifestId">The stored manifest ID.</param>
    public void MarkVariantDownloaded(string catalogContentId, string manifestId)
    {
        if (string.IsNullOrEmpty(catalogContentId) && string.IsNullOrEmpty(manifestId))
        {
            return;
        }

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

        if (SelectedVariant != null &&
            ((!string.IsNullOrEmpty(catalogContentId) &&
              string.Equals(SelectedVariant.ManifestId, catalogContentId, StringComparison.OrdinalIgnoreCase)) ||
             SelectedVariant.CurrentState == ContentState.Downloaded))
        {
            CurrentState = ContentState.Downloaded;
            IsDownloaded = true;
        }

        OnPropertyChanged(nameof(EffectiveCurrentState));
        OnPropertyChanged(nameof(EffectiveIsDownloaded));
        OnPropertyChanged(nameof(ShowDownloadButton));
        OnPropertyChanged(nameof(ShowUpdateButton));
        OnPropertyChanged(nameof(ShowAddToProfileButton));
    }

    /// <summary>
    /// Loads icon and logo bitmaps if not already loaded.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task EnsureIconsLoadedAsync()
    {
        if (_disposed)
        {
            return;
        }

        if (PublisherLogoBitmap == null || (IconBitmap == null && !string.IsNullOrEmpty(ThumbnailUrl)))
        {
            await LoadIconAsync();
        }
    }

    /// <summary>
    /// Command to download content.
    /// </summary>
    [RelayCommand]
    private void DownloadContent()
    {
        DownloadCommand?.Execute(this);
    }

    /// <summary>
    /// Command to update content to newer version.
    /// </summary>
    [RelayCommand]
    private void UpdateContent()
    {
        UpdateCommand?.Execute(this);
    }

    private void OnAxisSelectionCommitted(InstallableVariant? value)
    {
        if (value == null || ReferenceEquals(SelectedVariant, value))
        {
            return;
        }

        SelectedVariant = value;
    }

    private void SyncAxisSelectionsFromSelectedVariant()
    {
        VariantAxisGrouping.SyncSelections(VariantAxes, SelectedVariant);
    }

    partial void OnSelectedVariantChanged(InstallableVariant? value)
    {
        SyncAxisSelectionsFromSelectedVariant();

        if (value == null || string.IsNullOrEmpty(value.ManifestId) ||
            !_variantSearchResults.TryGetValue(value.ManifestId, out var sr))
        {
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(ShowDownloadButton));
            OnPropertyChanged(nameof(ShowUpdateButton));
            OnPropertyChanged(nameof(ShowAddToProfileButton));
            OnPropertyChanged(nameof(EffectiveCurrentState));
            OnPropertyChanged(nameof(EffectiveIsDownloaded));
            return;
        }

        // Swap the underlying SearchResult so download/add-to-profile targets the selected variant.
        VariantSwap.Apply(SearchResult, sr);

        // Keep the card's own state in sync with the selected variant so non-variant-aware
        // bindings (and Add to Profile's ID resolution) reflect the active selection.
        CurrentState = value.CurrentState;
        IsDownloaded = value.CurrentState is ContentState.Downloaded or ContentState.UpdateAvailable;

        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(DownloadSize));
        OnPropertyChanged(nameof(LastUpdatedDisplay));
        OnPropertyChanged(nameof(IsDownloadSizeVisible));
        OnPropertyChanged(nameof(SourceUrl));
        OnPropertyChanged(nameof(TargetGame));
        OnPropertyChanged(nameof(Id));
        OnPropertyChanged(nameof(IconUrl));
        OnPropertyChanged(nameof(ThumbnailUrl));
        OnPropertyChanged(nameof(IncludesSummary));
        OnPropertyChanged(nameof(HasIncludesSummary));
        OnPropertyChanged(nameof(ShortDescription));
        OnPropertyChanged(nameof(HasShortDescription));
        OnPropertyChanged(nameof(ShowDownloadButton));
        OnPropertyChanged(nameof(ShowUpdateButton));
        OnPropertyChanged(nameof(ShowAddToProfileButton));
        OnPropertyChanged(nameof(EffectiveCurrentState));
        OnPropertyChanged(nameof(EffectiveIsDownloaded));

        _ = LoadIconAsync();
    }

    private void OnBundleComponentPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(BundleComponentViewModel.SelectedVariant)
            or nameof(BundleComponentViewModel.IsSelectedDownloaded)
            or nameof(BundleComponentViewModel.CurrentState)
            or nameof(BundleComponentViewModel.RequiresDownload))
        {
            OnPropertyChanged(nameof(AreBundleComponentsReadyForProfile));
            OnPropertyChanged(nameof(ShowDownloadButton));
            OnPropertyChanged(nameof(ShowAddToProfileButton));
        }
    }
}
