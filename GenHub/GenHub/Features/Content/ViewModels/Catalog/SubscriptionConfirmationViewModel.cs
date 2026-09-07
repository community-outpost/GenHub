using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Interfaces.Publishers;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results;
using GenHub.Features.Content.Services.Catalog;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.ViewModels.Catalog;

/// <summary>
/// Confirmation dialog for adding or updating a content source from a shared URL.
/// </summary>
/// <remarks>
/// <para>
/// <b>Current behavior:</b> <paramref name="catalogUrl"/> must be a GenHub-schema
/// <see cref="PublisherCatalog"/> JSON. On confirm, a <see cref="PublisherSubscription"/> is
/// written or updated in <c>subscriptions.json</c> and Downloads reloads subscribed publishers.
/// </para>
/// <para>
/// <b>Extensibility:</b> Publisher Studio will share Provider Definition URLs via the same
/// <c>genhub://subscribe?url=...</c> entry points. This ViewModel should then detect definition
/// vs catalog payloads, set <see cref="PublisherSubscription.DefinitionUrl"/>, and resolve
/// catalog endpoint(s) from the definition — without changing the OS protocol or IPC shape.
/// </para>
/// </remarks>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "ViewModel properties and methods bound to MVVM UI.")]
public partial class SubscriptionConfirmationViewModel(
    string catalogUrl,
    IPublisherSubscriptionStore subscriptionStore,
    IPublisherCatalogParser catalogParser,
    HttpClient httpClient,
    ILogger<SubscriptionConfirmationViewModel> logger,
    IPublisherDefinitionService? definitionService = null) : ObservableObject
{
    private const string DefaultCategoryKey = "All";
    private const string DefaultPublisherName = "Loading...";
    private const string FallbackPublisherInitial = "P";
    private PublisherCatalog? _parsedCatalog;

    private string? _resolvedDefinitionUrl;
    private string? _resolvedCatalogUrl;

    /// <summary>
    /// Gets or sets an action that occurs when a request is made to close the dialog.
    /// The boolean parameter indicates the result (true for Success/Subscribe, false for Cancel).
    /// </summary>
    public Action<bool>? RequestClose { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PublisherInitial))]
    private string _publisherName = DefaultPublisherName;

    [ObservableProperty]
    private string? _publisherAvatarUrl;

    [ObservableProperty]
    private string? _publisherWebsite;

    [ObservableProperty]
    private string _publisherSupportUrl = string.Empty;

    [ObservableProperty]
    private string _publisherContactEmail = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<CatalogContentItem> _contentItems = [];

    [ObservableProperty]
    private IReadOnlyList<CatalogContentItem> _filteredContentItems = [];

    [ObservableProperty]
    private IReadOnlyList<CatalogCategoryFilter> _categoryFilters = [];

    [ObservableProperty]
    private string _selectedCategoryKey = DefaultCategoryKey;

    [ObservableProperty]
    private int _contentCount;

    [ObservableProperty]
    private string _contentSummary = string.Empty;

    [ObservableProperty]
    private DateTime? _lastUpdated;

    /// <summary>
    /// Gets the subscribed URL for display (catalog JSON today; may be a definition URL later).
    /// </summary>
    public string CatalogUrlDisplay => catalogUrl;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowInitialError))]
    [NotifyPropertyChangedFor(nameof(ShowDetails))]
    private bool _isLoading = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowInitialError))]
    [NotifyPropertyChangedFor(nameof(ShowDetails))]
    [NotifyPropertyChangedFor(nameof(ShowActionError))]
    private bool _isCatalogLoaded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowActionError))]
    private string? _errorMessage;

    [ObservableProperty]
    private string _errorTitle = "Failed to Load Catalog";

    [ObservableProperty]
    private bool _canConfirm;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConfirmButtonText))]
    private bool _isAlreadySubscribed;

    /// <summary>
    /// Gets the text to display on the confirmation button.
    /// </summary>
    public string ConfirmButtonText => IsAlreadySubscribed ? "Update Subscription" : "Subscribe to Library";

    /// <summary>
    /// Gets a value indicating whether the initial catalog fetch error should be shown.
    /// </summary>
    public bool ShowInitialError => !IsLoading && !IsCatalogLoaded;

    /// <summary>
    /// Gets a value indicating whether the catalog details should be shown.
    /// </summary>
    public bool ShowDetails => !IsLoading && IsCatalogLoaded;

    /// <summary>
    /// Gets a value indicating whether an inline action error (like confirm failure) should be shown when catalog is loaded.
    /// </summary>
    public bool ShowActionError => IsCatalogLoaded && !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>
    /// Gets the single-letter initial for fallback publisher avatar display.
    /// </summary>
    public string PublisherInitial => !string.IsNullOrWhiteSpace(PublisherName) && !string.Equals(PublisherName, DefaultPublisherName, StringComparison.Ordinal)
        ? PublisherName[..1].ToUpperInvariant()
        : FallbackPublisherInitial;

    /// <summary>
    /// Fetches and validates the remote catalog so the user can confirm identity before saving.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [RelayCommand]
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            IsLoading = true;
            IsCatalogLoaded = false;
            ErrorMessage = null;
            CanConfirm = false;
            IsAlreadySubscribed = false;

            logger.LogInformation("Fetching catalog subscription");
            var response = await CatalogDocumentReader.ReadAsync(httpClient, catalogUrl, CatalogConstants.MaxCatalogSizeBytes, cancellationToken);

            var (parsedData, resolvedDefUrl, resolvedCatUrl) = await ResolveCatalogDataAsync(response, cancellationToken);
            if (parsedData == null)
            {
                return;
            }

            _resolvedDefinitionUrl = resolvedDefUrl;
            _resolvedCatalogUrl = resolvedCatUrl;
            await PopulatePublisherDetailsAsync(parsedData, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error initializing subscription confirmation");
            ErrorTitle = "Failed to Fetch Catalog";
            ErrorMessage = $"Failed to fetch catalog: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task<(PublisherCatalog? Catalog, string? DefinitionUrl, string? CatalogUrl)> ResolveCatalogDataAsync(
        string response,
        CancellationToken cancellationToken)
    {
        if (definitionService != null)
        {
            var defServiceResult = await TryFetchFromDefinitionServiceAsync(cancellationToken);
            if (defServiceResult.Catalog != null)
            {
                return defServiceResult;
            }
        }

        var defPayloadResult = await TryResolveDefinitionFromPayloadAsync(response, cancellationToken);
        if (defPayloadResult.Catalog != null)
        {
            return defPayloadResult;
        }

        var result = await catalogParser.ParseCatalogAsync(response, cancellationToken);
        if (result.Success && result.Data != null)
        {
            return (result.Data, null, null);
        }

        ErrorTitle = "Failed to Load Catalog";
        ErrorMessage = string.Join(Environment.NewLine, result.Errors);
        logger.LogWarning("Failed to parse catalog: {Errors}", ErrorMessage);
        return (null, null, null);
    }

    private async Task<(PublisherCatalog? Catalog, string? DefinitionUrl, string? CatalogUrl)> TryFetchFromDefinitionServiceAsync(
        CancellationToken cancellationToken)
    {
        if (definitionService == null)
        {
            return (null, null, null);
        }

        var defResult = await definitionService.FetchDefinitionAsync(catalogUrl, cancellationToken);
        if (!defResult.Success || defResult.Data == null)
        {
            return (null, null, null);
        }

        var definition = defResult.Data;
        var hasCatalogs = (definition.Catalogs != null && definition.Catalogs.Count > 0) || !string.IsNullOrWhiteSpace(definition.CatalogUrl);
        if (!hasCatalogs)
        {
            return (null, null, null);
        }

        var catResult = await definitionService.FetchCatalogFromDefinitionAsync(definition, cancellationToken);
        if (catResult.Success && catResult.Data != null)
        {
            var targetCatalogUrl = !string.IsNullOrWhiteSpace(definition.CatalogUrl)
                ? definition.CatalogUrl
                : definition.Catalogs?.FirstOrDefault()?.Url;
            return (catResult.Data, catalogUrl, targetCatalogUrl);
        }

        return (null, null, null);
    }

    private async Task<(PublisherCatalog? Catalog, string? DefinitionUrl, string? CatalogUrl)> TryResolveDefinitionFromPayloadAsync(
        string response,
        CancellationToken cancellationToken)
    {
        try
        {
            var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var definition = System.Text.Json.JsonSerializer.Deserialize<PublisherDefinition>(response, options);
            if (definition == null)
            {
                return (null, null, null);
            }

            var hasCatalogs = (definition.Catalogs != null && definition.Catalogs.Count > 0) || !string.IsNullOrWhiteSpace(definition.CatalogUrl);
            if (!hasCatalogs)
            {
                return (null, null, null);
            }

            var targetCatalogUrl = !string.IsNullOrWhiteSpace(definition.CatalogUrl)
                ? definition.CatalogUrl
                : definition.Catalogs?.FirstOrDefault()?.Url;

            if (string.IsNullOrWhiteSpace(targetCatalogUrl))
            {
                return (null, null, null);
            }

            logger.LogInformation("Resolved catalog URL {TargetUrl} from definition at {DefUrl}", targetCatalogUrl, catalogUrl);
            var catResponse = await CatalogDocumentReader.ReadAsync(httpClient, targetCatalogUrl, CatalogConstants.MaxCatalogSizeBytes, cancellationToken);
            var catParseResult = await catalogParser.ParseCatalogAsync(catResponse, cancellationToken);
            if (catParseResult.Success && catParseResult.Data != null)
            {
                return (catParseResult.Data, catalogUrl, targetCatalogUrl);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (System.Text.Json.JsonException jsonEx)
        {
            logger.LogDebug(jsonEx, "Payload is not a valid publisher definition; falling back to direct catalog parse");
        }
        catch (Exception defEx)
        {
            logger.LogDebug(defEx, "Failed to resolve catalog from embedded definition; falling back to direct catalog parse");
        }

        return (null, null, null);
    }

    private async Task PopulatePublisherDetailsAsync(
        PublisherCatalog catalog,
        CancellationToken cancellationToken)
    {
        _parsedCatalog = catalog;
        PublisherName = _parsedCatalog.Publisher.Name;
        PublisherAvatarUrl = _parsedCatalog.Publisher.AvatarUrl;
        PublisherWebsite = _parsedCatalog.Publisher.Website;
        PublisherSupportUrl = _parsedCatalog.Publisher.SupportUrl ?? string.Empty;
        PublisherContactEmail = _parsedCatalog.Publisher.ContactEmail ?? string.Empty;
        LastUpdated = _parsedCatalog.LastUpdated != default ? _parsedCatalog.LastUpdated : null;

        // check if this publisher is already in the subscription store
        var subCheck = await subscriptionStore.IsSubscribedAsync(_parsedCatalog.Publisher.Id, cancellationToken);
        IsAlreadySubscribed = subCheck is { Success: true, Data: true };

        if (_parsedCatalog.Content != null)
        {
            ContentItems = _parsedCatalog.Content.AsReadOnly();
            ContentCount = _parsedCatalog.Content.Count;

            var typeGroups = _parsedCatalog.Content
                .GroupBy(item => item.ContentType)
                .Select(group => $"{group.Count()} {group.Key}");
            ContentSummary = string.Join(" • ", typeGroups);

            BuildCategoryFilters(DefaultCategoryKey);
        }
        else
        {
            ContentItems = [];
            FilteredContentItems = [];
            CategoryFilters = [];
            ContentCount = 0;
            ContentSummary = string.Empty;
        }

        IsCatalogLoaded = true;
        CanConfirm = true;
        logger.LogInformation("Successfully loaded catalog for {Publisher} with {Count} items (alreadySubscribed={IsAlreadySubscribed})", PublisherName, ContentCount, IsAlreadySubscribed);
    }

    /// <summary>
    /// Selects a category filter and updates the filtered items collection.
    /// </summary>
    /// <param name="categoryKey">The category key to filter by.</param>
    [RelayCommand]
    public void SelectCategory(string? categoryKey)
    {
        var key = string.IsNullOrWhiteSpace(categoryKey) ? DefaultCategoryKey : categoryKey;
        SelectedCategoryKey = key;
        BuildCategoryFilters(key);
    }

    /// <summary>
    /// Opens the specified web URL or email link safely in the default system browser or handler.
    /// </summary>
    /// <param name="url">The URL or email address to open.</param>
    [RelayCommand]
    public void OpenUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        try
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                uri.Scheme == Uri.UriSchemeHttps)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = uri.AbsoluteUri,
                    UseShellExecute = true,
                });
            }
            else if (url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            {
                var rawAddress = url["mailto:".Length..].Split('?')[0];
                if (MailAddress.TryCreate(rawAddress, out var mailAddress))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = $"mailto:{mailAddress.Address}",
                        UseShellExecute = true,
                    });
                }
                else
                {
                    logger.LogWarning("Rejected invalid mailto address");
                }
            }
            else if (MailAddress.TryCreate(url, out var mailAddress))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = $"mailto:{mailAddress.Address}",
                    UseShellExecute = true,
                });
            }
            else
            {
                logger.LogWarning("Rejected opening unsafe or invalid URL: scheme must be HTTPS or valid email");
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to open URL in browser");
        }
    }

    /// <summary>
    /// Dismisses the active error message banner.
    /// </summary>
    [RelayCommand]
    public void DismissError()
    {
        ErrorMessage = null;
    }

    [RelayCommand]
    private async Task ConfirmAsync(CancellationToken cancellationToken = default)
    {
        if (_parsedCatalog == null) return;

        try
        {
            ErrorMessage = null;
            logger.LogInformation("Confirming subscription for {Publisher}", _parsedCatalog.Publisher.Id);

            var existingResult = await subscriptionStore.GetSubscriptionAsync(_parsedCatalog.Publisher.Id, cancellationToken);
            if (!existingResult.Success)
            {
                ErrorTitle = "Subscription Error";
                ErrorMessage = string.Join(Environment.NewLine, existingResult.Errors);
                return;
            }

            var existingSub = existingResult.Data;

            var subscription = new PublisherSubscription
            {
                PublisherId = _parsedCatalog.Publisher.Id,
                PublisherName = _parsedCatalog.Publisher.Name,
                CatalogUrl = _resolvedCatalogUrl ?? catalogUrl,
                DefinitionUrl = _resolvedDefinitionUrl ?? existingSub?.DefinitionUrl, // preserve definition URL if already set
                Added = existingSub?.Added ?? DateTime.UtcNow,
                TrustLevel = existingSub?.TrustLevel ?? TrustLevel.Untrusted, // community sources start untrusted
                AvatarUrl = _parsedCatalog.Publisher.AvatarUrl,
                AutoUpdate = existingSub?.AutoUpdate == true,
                NotifyNewReleases = existingSub?.NotifyNewReleases ?? true,
                CachedCatalogHash = existingSub?.CachedCatalogHash,
                LastFetched = existingSub?.LastFetched,
            };

            var result = (IsAlreadySubscribed || existingSub != null)
                ? await subscriptionStore.UpdateSubscriptionAsync(subscription, cancellationToken)
                : await subscriptionStore.AddSubscriptionAsync(subscription, cancellationToken);

            if (result.Success)
            {
                logger.LogInformation("Subscription saved successfully for publisher {PublisherId}", subscription.PublisherId);
                RequestClose?.Invoke(true);
            }
            else
            {
                ErrorTitle = IsAlreadySubscribed ? "Failed to Update Subscription" : "Failed to Subscribe";
                ErrorMessage = string.Join(Environment.NewLine, result.Errors);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error confirming subscription");
            ErrorTitle = "Subscription Error";
            ErrorMessage = $"Failed to save subscription: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        RequestClose?.Invoke(false);
    }

    private void BuildCategoryFilters(string activeKey)
    {
        if (_parsedCatalog?.Content == null || _parsedCatalog.Content.Count == 0)
        {
            CategoryFilters = [];
            FilteredContentItems = [];
            return;
        }

        var totalCount = _parsedCatalog.Content.Count;
        var filters = new List<CatalogCategoryFilter>
        {
            new(DefaultCategoryKey, "All Content", totalCount, string.Equals(activeKey, DefaultCategoryKey, StringComparison.OrdinalIgnoreCase)),
        };

        var groups = _parsedCatalog.Content
            .GroupBy(item => item.ContentType)
            .OrderBy(g => g.Key.ToString());

        foreach (var group in groups)
        {
            var key = group.Key.ToString();
            var label = FormatContentTypeLabel(group.Key);
            var isSelected = string.Equals(activeKey, key, StringComparison.OrdinalIgnoreCase);
            filters.Add(new CatalogCategoryFilter(key, label, group.Count(), isSelected));
        }

        CategoryFilters = filters.AsReadOnly();

        if (string.Equals(activeKey, DefaultCategoryKey, StringComparison.OrdinalIgnoreCase))
        {
            FilteredContentItems = _parsedCatalog.Content.AsReadOnly();
        }
        else if (Enum.TryParse<ContentType>(activeKey, true, out var filterType))
        {
            FilteredContentItems = _parsedCatalog.Content
                .Where(item => item.ContentType == filterType)
                .ToList()
                .AsReadOnly();
        }
        else
        {
            FilteredContentItems = _parsedCatalog.Content.AsReadOnly();
        }
    }

    private static string FormatContentTypeLabel(ContentType contentType) => contentType switch
    {
        ContentType.Mod => "Mods",
        ContentType.Map => "Maps",
        ContentType.Mission => "Missions",
        ContentType.ModdingTool => "Tools",
        ContentType.Patch => "Patches",
        ContentType.Addon => "Addons",
        _ => contentType.ToString(),
    };
}
