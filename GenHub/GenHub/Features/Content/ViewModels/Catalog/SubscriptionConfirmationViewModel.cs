using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using GenHub.Core.Constants;
using GenHub.Core.Extensions;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Interfaces.Publishers;
using GenHub.Core.Messages;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results;
using GenHub.Features.Content.Services.Catalog;
using GenHub.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.Mail;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

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
    IPublisherDefinitionService? definitionService = null,
    ILocalizationService? localizationService = null) : ObservableObject
{
    private const string DefaultCategoryKey = "All";
    private const string DefaultPublisherName = "Loading...";
    private const string FallbackPublisherInitial = "P";

    private static readonly JsonSerializerOptions DefinitionJsonOptions = PublisherJsonOptions.Definition;

    private readonly List<(string Id, string Name, string Url, PublisherCatalog Catalog)> _definitionCatalogs = [];

    private PublisherCatalog? _parsedCatalog;
    private PublisherDefinition? _resolvedDefinition;

    private string? _resolvedDefinitionUrl;
    private string? _resolvedCatalogUrl;
    private string? _selectedCatalogUrl;

    /// <summary>
    /// Gets or sets an action that occurs when a request is made to close the dialog.
    /// The boolean parameter indicates the result (true for Success/Subscribe, false for Cancel).
    /// </summary>
    public Action<bool>? RequestClose { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PublisherInitial))]
    private string _publisherName = localizationService?.GetString("Downloads.Subscription.LoadingPublisher") ?? DefaultPublisherName;

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
    private string _errorTitle = localizationService?.GetString("Downloads.Subscription.ErrorTitle.FailedToLoad") ?? "Failed to Load Catalog";

    [ObservableProperty]
    private bool _canConfirm;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConfirmButtonText))]
    private bool _isAlreadySubscribed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCatalogSelector))]
    [NotifyPropertyChangedFor(nameof(NewSourceBadgeText))]
    private bool _isDefinitionSubscription;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCatalogSelector))]
    [NotifyPropertyChangedFor(nameof(NewSourceBadgeText))]
    private IReadOnlyList<CatalogCategoryFilter> _definitionCatalogOptions = [];

    /// <summary>
    /// Gets the text to display on the confirmation button.
    /// </summary>
    public string ConfirmButtonText => IsAlreadySubscribed ? GetLocalizedString("Downloads.Subscription.UpdateSubscription", "Update Subscription") : GetLocalizedString("Downloads.Subscription.SubscribeToLibrary", "Subscribe to Library");

    /// <summary>
    /// Gets a value indicating whether the definition catalog selector should be shown.
    /// </summary>
    public bool ShowCatalogSelector => IsDefinitionSubscription && DefinitionCatalogOptions.Count > 1;

    /// <summary>
    /// Gets the "new source" badge text, reflecting a publisher definition when subscribed via one.
    /// </summary>
    public string NewSourceBadgeText
    {
        get
        {
            if (!IsDefinitionSubscription)
            {
                return GetLocalizedString("Downloads.Subscription.NewCatalogBadge", "+ New Catalog");
            }

            if (DefinitionCatalogOptions.Count > 1)
            {
                return GetLocalizedString("Downloads.Subscription.NewPublisherBadgeFormat", "+ New Publisher • {0} catalogs", DefinitionCatalogOptions.Count);
            }

            return GetLocalizedString("Downloads.Subscription.NewPublisherBadge", "+ New Publisher");
        }
    }

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

            var (parsedData, resolvedDefUrl, resolvedCatUrl, definition) = await ResolveCatalogDataAsync(response, cancellationToken);
            if (parsedData == null)
            {
                return;
            }

            _resolvedDefinitionUrl = resolvedDefUrl;
            _resolvedCatalogUrl = resolvedCatUrl;
            _resolvedDefinition = definition;
            if (definition != null && !string.IsNullOrWhiteSpace(resolvedCatUrl))
            {
                await BuildDefinitionCatalogListAsync(definition, parsedData, resolvedCatUrl, cancellationToken);
            }

            await PopulatePublisherDetailsAsync(parsedData, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error initializing subscription confirmation");
            ErrorTitle = GetLocalizedString("Downloads.Subscription.ErrorTitle.FailedToFetch", "Failed to Fetch Catalog");
            ErrorMessage = GetLocalizedString("Downloads.Subscription.ErrorMessage.FailedToFetchFormat", "Failed to fetch catalog: {0}", ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
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
    /// Selects a definition catalog and shows its content items.
    /// </summary>
    /// <param name="catalogId">The definition catalog entry ID to show.</param>
    [RelayCommand]
    public void SelectDefinitionCatalog(string? catalogId)
    {
        if (_definitionCatalogs.Count == 0)
        {
            return;
        }

        var index = _definitionCatalogs.FindIndex(c => string.Equals(c.Id, catalogId, StringComparison.Ordinal));
        if (index < 0)
        {
            index = 0;
        }

        var selected = _definitionCatalogs[index];
        if (string.Equals(selected.Url, _selectedCatalogUrl, StringComparison.Ordinal))
        {
            return;
        }

        logger.LogDebug("Showing definition catalog {CatalogId} in subscription confirmation", selected.Id);
        ApplyCatalogSelection(selected.Catalog, selected.Url);
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
                ErrorMessage = GetLocalizedString("Downloads.Subscription.ErrorMessage.HttpsOrEmailOnly", "Only secure HTTPS links or valid email addresses can be opened.");
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

    private static bool HasCatalogReference(PublisherDefinition? definition) =>
        (definition?.Catalogs?.Count > 0) || !string.IsNullOrWhiteSpace(definition?.CatalogUrl);

    private static string ResolveCatalogDisplayName(string? name, string fallback)
    {
        return string.IsNullOrWhiteSpace(name) ? fallback : name;
    }

    private static string? ResolveTargetCatalogUrl(PublisherDefinition? definition)
    {
        if (definition == null)
        {
            return null;
        }

        return !string.IsNullOrWhiteSpace(definition.CatalogUrl)
            ? definition.CatalogUrl
            : definition.Catalogs?.FirstOrDefault()?.Url;
    }

    [RelayCommand]
    private async Task ConfirmAsync(CancellationToken cancellationToken = default)
    {
        if (_parsedCatalog == null) return;

        try
        {
            ErrorMessage = null;
            var effectivePublisher = ResolveEffectivePublisher(_parsedCatalog);
            var publisherId = effectivePublisher.Id;
            var publisherName = effectivePublisher.Name;
            var publisherAvatar = effectivePublisher.AvatarUrl;

            logger.LogInformation("Confirming subscription for {Publisher}", publisherId);

            var existingResult = await subscriptionStore.GetSubscriptionAsync(publisherId, cancellationToken);
            if (!existingResult.Success)
            {
                ErrorTitle = GetLocalizedString("Downloads.Subscription.ErrorTitle.SubscriptionError", "Subscription Error");
                ErrorMessage = string.Join(Environment.NewLine, existingResult.Errors);
                return;
            }

            var existingSub = existingResult.Data;

            var subscription = new PublisherSubscription
            {
                PublisherId = publisherId,
                PublisherName = publisherName,
                CatalogUrl = _selectedCatalogUrl ?? _resolvedCatalogUrl ?? catalogUrl,
                DefinitionUrl = _resolvedDefinitionUrl ?? existingSub?.DefinitionUrl, // preserve definition URL if already set
                Added = existingSub?.Added ?? DateTime.UtcNow,
                TrustLevel = existingSub?.TrustLevel ?? TrustLevel.Untrusted, // community sources start untrusted
                AvatarUrl = ImageCacheService.SanitizeRemoteImageUrl(publisherAvatar),
                AutoUpdate = existingSub?.AutoUpdate ?? true,
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
                WeakReferenceMessenger.Default.Send(new PublisherSubscriptionsChangedMessage(subscription.PublisherId));
                RequestClose?.Invoke(true);
            }
            else
            {
                ErrorTitle = IsAlreadySubscribed ? GetLocalizedString("Downloads.Subscription.ErrorTitle.FailedToUpdate", "Failed to Update Subscription") : GetLocalizedString("Downloads.Subscription.ErrorTitle.FailedToSubscribe", "Failed to Subscribe");
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
            ErrorTitle = GetLocalizedString("Downloads.Subscription.ErrorTitle.SubscriptionError", "Subscription Error");
            ErrorMessage = GetLocalizedString("Downloads.Subscription.ErrorMessage.FailedToSaveFormat", "Failed to save subscription: {0}", ex.Message);
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        RequestClose?.Invoke(false);
    }

    private string GetLocalizedString(string key, string fallback) =>
        localizationService?.GetString(key) ?? fallback;

    private string GetLocalizedString(string key, string fallback, params object[] args)
    {
        var format = localizationService?.GetString(key);
        return string.IsNullOrEmpty(format) || string.Equals(format, key, StringComparison.Ordinal)
            ? string.Format(System.Globalization.CultureInfo.InvariantCulture, fallback, args)
            : string.Format(System.Globalization.CultureInfo.InvariantCulture, format, args);
    }

    private async Task<(PublisherCatalog? Catalog, string? DefinitionUrl, string? CatalogUrl, PublisherDefinition? Definition)> ResolveCatalogDataAsync(
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
            return (result.Data, null, null, null);
        }

        ErrorTitle = GetLocalizedString("Downloads.Subscription.ErrorTitle.FailedToLoad", "Failed to Load Catalog");
        ErrorMessage = string.Join(Environment.NewLine, result.Errors);
        logger.LogWarning("Failed to parse catalog: {Errors}", ErrorMessage);
        return (null, null, null, null);
    }

    private string ResolveContentTypeDisplay(ContentType contentType)
    {
        var typeKey = $"ContentType.{contentType}";
        var localizedType = localizationService?.GetString(typeKey);
        return !string.IsNullOrEmpty(localizedType) && !string.Equals(localizedType, typeKey, StringComparison.Ordinal)
            ? localizedType
            : contentType.GetDisplayName();
    }

    private async Task<(PublisherCatalog? Catalog, string? DefinitionUrl, string? CatalogUrl, PublisherDefinition? Definition)> TryFetchFromDefinitionServiceAsync(
        CancellationToken cancellationToken)
    {
        if (definitionService == null)
        {
            return (null, null, null, null);
        }

        var defResult = await definitionService.FetchDefinitionAsync(catalogUrl, cancellationToken);
        if (!defResult.Success || defResult.Data == null)
        {
            return (null, null, null, null);
        }

        var definition = defResult.Data;
        if (!HasCatalogReference(definition))
        {
            return (null, null, null, null);
        }

        var catResult = await definitionService.FetchCatalogFromDefinitionAsync(definition, cancellationToken);
        if (catResult.Success && catResult.Data != null)
        {
            var targetCatalogUrl = ResolveTargetCatalogUrl(definition);

            if (string.IsNullOrWhiteSpace(targetCatalogUrl))
            {
                return (null, null, null, null);
            }

            var (targetSafe, ssrfReason) = await NetworkSecurityHelper.IsSafeUrlAsync(targetCatalogUrl, cancellationToken);
            if (!targetSafe)
            {
                if (!string.IsNullOrEmpty(ssrfReason))
                {
                    logger.LogWarning("Blocked unsafe catalog URL in definition payload: {Reason}", ssrfReason);
                }

                return (null, null, null, null);
            }

            return (catResult.Data, catalogUrl, targetCatalogUrl, definition);
        }

        return (null, null, null, null);
    }

    private async Task<(PublisherCatalog? Catalog, string? DefinitionUrl, string? CatalogUrl, PublisherDefinition? Definition)> TryResolveDefinitionFromPayloadAsync(
        string response,
        CancellationToken cancellationToken)
    {
        try
        {
            var definition = JsonSerializer.Deserialize<PublisherDefinition>(response, DefinitionJsonOptions);
            if (definition == null)
            {
                return (null, null, null, null);
            }

            if (!HasCatalogReference(definition))
            {
                return (null, null, null, null);
            }

            var targetCatalogUrl = ResolveTargetCatalogUrl(definition);

            if (string.IsNullOrWhiteSpace(targetCatalogUrl))
            {
                return (null, null, null, null);
            }

            var (targetSafe, ssrfReason) = await NetworkSecurityHelper.IsSafeUrlAsync(targetCatalogUrl, cancellationToken);
            if (!targetSafe)
            {
                if (!string.IsNullOrEmpty(ssrfReason))
                {
                    logger.LogWarning("Blocked unsafe catalog URL in definition payload: {Reason}", ssrfReason);
                }

                return (null, null, null, null);
            }

            logger.LogInformation("Resolved catalog URL {TargetUrl} from definition at {DefUrl}", targetCatalogUrl, catalogUrl);
            var catResponse = await CatalogDocumentReader.ReadAsync(httpClient, targetCatalogUrl, CatalogConstants.MaxCatalogSizeBytes, cancellationToken);
            var catParseResult = await catalogParser.ParseCatalogAsync(catResponse, cancellationToken);
            if (catParseResult.Success && catParseResult.Data != null)
            {
                return (catParseResult.Data, catalogUrl, targetCatalogUrl, definition);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException httpEx)
        {
            logger.LogWarning(httpEx, "Transient error resolving catalog from embedded definition at {Url}; falling back to direct catalog parse", catalogUrl);
            return (null, null, null, null);
        }
        catch (OperationCanceledException timeoutEx)
        {
            logger.LogWarning(timeoutEx, "Timeout fetching target catalog from definition at {Url}", catalogUrl);
            return (null, null, null, null);
        }
        catch (JsonException jsonEx)
        {
            logger.LogDebug(jsonEx, "Payload is not a valid publisher definition; falling back to direct catalog parse");
        }
        catch (Exception defEx)
        {
            logger.LogDebug(defEx, "Failed to resolve catalog from embedded definition; falling back to direct catalog parse");
        }

        return (null, null, null, null);
    }

    private PublisherProfile ResolveEffectivePublisher(PublisherCatalog catalog)
    {
        var def = _resolvedDefinition?.Publisher;
        var cat = catalog.Publisher;
        return new PublisherProfile
        {
            Id = !string.IsNullOrWhiteSpace(def?.Id) ? def.Id : cat.Id,
            Name = !string.IsNullOrWhiteSpace(def?.Name) ? def.Name : cat.Name,
            Description = !string.IsNullOrWhiteSpace(def?.Description) ? def.Description : cat.Description,
            AvatarUrl = !string.IsNullOrWhiteSpace(def?.AvatarUrl) ? def.AvatarUrl : cat.AvatarUrl,
            Website = !string.IsNullOrWhiteSpace(def?.Website) ? def.Website : cat.Website,
            SupportUrl = !string.IsNullOrWhiteSpace(def?.SupportUrl) ? def.SupportUrl : cat.SupportUrl,
            ContactEmail = !string.IsNullOrWhiteSpace(def?.ContactEmail) ? def.ContactEmail : cat.ContactEmail,
        };
    }

    private async Task PopulatePublisherDetailsAsync(
        PublisherCatalog catalog,
        CancellationToken cancellationToken)
    {
        var pubProfile = ResolveEffectivePublisher(catalog);

        PublisherName = pubProfile.Name;
        PublisherAvatarUrl = ImageCacheService.SanitizeRemoteImageUrl(pubProfile.AvatarUrl) ?? PublisherInfoConstants.GetPublisherLogo(pubProfile.Name, pubProfile.Id);
        PublisherWebsite = pubProfile.Website;
        PublisherSupportUrl = pubProfile.SupportUrl ?? string.Empty;
        PublisherContactEmail = pubProfile.ContactEmail ?? string.Empty;

        var publisherId = pubProfile.Id;

        // check if this publisher is already in the subscription store
        var subCheck = await subscriptionStore.IsSubscribedAsync(publisherId, cancellationToken);
        IsAlreadySubscribed = subCheck is { Success: true, Data: true };
        IsDefinitionSubscription = _resolvedDefinitionUrl != null;

        ApplyCatalogSelection(catalog, _resolvedCatalogUrl);
        logger.LogInformation("Successfully loaded catalog for {Publisher} with {Count} items (alreadySubscribed={IsAlreadySubscribed})", PublisherName, ContentCount, IsAlreadySubscribed);
    }

    private async Task BuildDefinitionCatalogListAsync(
        PublisherDefinition definition,
        PublisherCatalog firstCatalog,
        string firstCatalogUrl,
        CancellationToken cancellationToken)
    {
        _definitionCatalogs.Clear();
        var firstEntry = definition.Catalogs.FirstOrDefault(e => string.Equals(e.Url, firstCatalogUrl, StringComparison.OrdinalIgnoreCase))
            ?? definition.Catalogs.FirstOrDefault();
        _definitionCatalogs.Add((
            firstEntry?.Id ?? "primary",
            ResolveCatalogDisplayName(firstEntry?.Name, firstEntry?.Id ?? "primary"),
            firstCatalogUrl,
            firstCatalog));

        foreach (var entry in definition.Catalogs)
        {
            if (string.IsNullOrWhiteSpace(entry.Url) ||
                string.Equals(entry.Url, firstCatalogUrl, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parsed = await TryFetchDefinitionCatalogEntryAsync(entry, cancellationToken);
            if (parsed != null)
            {
                _definitionCatalogs.Add((entry.Id, ResolveCatalogDisplayName(entry.Name, entry.Id), entry.Url, parsed));
            }
        }
    }

    private async Task<PublisherCatalog?> TryFetchDefinitionCatalogEntryAsync(CatalogEntry entry, CancellationToken cancellationToken)
    {
        try
        {
            var (targetSafe, ssrfReason) = await NetworkSecurityHelper.IsSafeUrlAsync(entry.Url, cancellationToken);
            if (!targetSafe)
            {
                logger.LogWarning("Blocked unsafe catalog URL {Url} in definition: {Reason}", entry.Url, ssrfReason);
                return null;
            }

            var catResponse = await CatalogDocumentReader.ReadAsync(httpClient, entry.Url, CatalogConstants.MaxCatalogSizeBytes, cancellationToken);
            var catParseResult = await catalogParser.ParseCatalogAsync(catResponse, cancellationToken);
            if (catParseResult.Success && catParseResult.Data != null)
            {
                return catParseResult.Data;
            }

            logger.LogWarning("Failed to parse definition catalog {CatalogId}: {Errors}", entry.Id, string.Join("; ", catParseResult.Errors));
            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch definition catalog {CatalogId}; showing remaining catalogs", entry.Id);
            return null;
        }
    }

    private void ApplyCatalogSelection(PublisherCatalog catalog, string? catalogUrl)
    {
        _parsedCatalog = catalog;
        _selectedCatalogUrl = catalogUrl;
        LastUpdated = catalog.LastUpdated != default ? catalog.LastUpdated : null;

        if (catalog.Content != null)
        {
            ContentItems = catalog.Content.AsReadOnly();
            ContentCount = catalog.Content.Count;

            var typeGroups = catalog.Content
                .GroupBy(item => item.ContentType)
                .Select(group => $"{group.Count()} {ResolveContentTypeDisplay(group.Key)}");
            ContentSummary = string.Join(" • ", typeGroups);

            SelectCategory(null);
        }
        else
        {
            ContentItems = [];
            FilteredContentItems = [];
            CategoryFilters = [];
            ContentCount = 0;
            ContentSummary = string.Empty;
        }

        BuildDefinitionCatalogOptions();
        IsCatalogLoaded = true;
        CanConfirm = true;
    }

    private void BuildDefinitionCatalogOptions()
    {
        if (!IsDefinitionSubscription || _definitionCatalogs.Count == 0)
        {
            DefinitionCatalogOptions = [];
            return;
        }

        var selectedId = _definitionCatalogs.FirstOrDefault(c => string.Equals(c.Url, _selectedCatalogUrl, StringComparison.Ordinal)).Id
            ?? _definitionCatalogs[0].Id;
        DefinitionCatalogOptions = _definitionCatalogs
            .Select(c => new CatalogCategoryFilter(c.Id, c.Name, c.Catalog.Content?.Count ?? 0, string.Equals(c.Id, selectedId, StringComparison.Ordinal)))
            .ToList()
            .AsReadOnly();
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
            new(DefaultCategoryKey, GetLocalizedString("Downloads.Subscription.Category.All", "All"), totalCount, string.Equals(activeKey, DefaultCategoryKey, StringComparison.OrdinalIgnoreCase)),
        };

        var groups = _parsedCatalog.Content
            .GroupBy(item => item.ContentType)
            .OrderBy(g => g.Key.ToString());

        foreach (var group in groups)
        {
            var key = group.Key.ToString();
            var typeDisplay = ResolveContentTypeDisplay(group.Key);
            var isSelected = string.Equals(activeKey, key, StringComparison.OrdinalIgnoreCase);
            filters.Add(new CatalogCategoryFilter(key, typeDisplay, group.Count(), isSelected));
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
}
