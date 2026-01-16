using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Interfaces.Publishers;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Providers;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace GenHub.Features.Content.ViewModels.Catalog;

/// <summary>
/// ViewModel for the subscription confirmation dialog.
/// Handles fetching and validating a catalog before subscription.
/// </summary>
public partial class SubscriptionConfirmationViewModel(
    string url, // Renamed from catalogUrl to generic url
    IPublisherSubscriptionStore subscriptionStore,
    IPublisherCatalogParser catalogParser,
    IPublisherDefinitionService publisherDefinitionService,
    HttpClient httpClient,
    ILogger<SubscriptionConfirmationViewModel> logger) : ObservableObject
{
    /// <summary>
    /// Gets or sets an action that occurs when a request is made to close the dialog.
    /// The boolean parameter indicates the result (true for Success/Subscribe, false for Cancel).
    /// </summary>
    public Action<bool>? RequestClose { get; set; }

    [ObservableProperty]
    private string _publisherName = "Loading...";

    [ObservableProperty]
    private string? _publisherAvatarUrl;

    [ObservableProperty]
    private string? _publisherWebsite;

    [ObservableProperty]
    private string? _publisherDescription; // Added description

    /// <summary>
    /// Gets the URL for display.
    /// </summary>
    public string UrlDisplay => url;

    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _canConfirm;

    /// <summary>
    /// Gets the list of available catalogs in a V2 definition.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<SelectableCatalogEntry> _availableCatalogs = new();

    private PublisherCatalog? _parsedCatalog;
    private PublisherDefinition? _parsedDefinition;
    private bool _isProviderDefinition;

    /// <summary>
    /// Gets whether this is a multi-catalog definition.
    /// </summary>
    public bool HasMultipleCatalogs => AvailableCatalogs.Count > 1;

    /// <summary>
    /// Initializes the ViewModel by fetching the metadata.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task InitializeAsync()
    {
        try
        {
            IsLoading = true;
            ErrorMessage = null;
            CanConfirm = false;
            _isProviderDefinition = false;

            logger.LogInformation("Fetching metadata from {Url}", url);

            // 1. Attempt to fetch as Provider Definition first
            // We check extension or just try parsing.
            // .json could be either, but .provider.json is definitely definition.
            // Let's try fetching as definition first.
            var definitionResult = await publisherDefinitionService.FetchDefinitionAsync(url);

            // Check if it looks like a valid definition (schema version, catalogUrl present)
            if (definitionResult.Success && definitionResult.Data != null && !string.IsNullOrEmpty(definitionResult.Data.CatalogUrl))
            {
                _parsedDefinition = definitionResult.Data;
                _isProviderDefinition = true;

                // Populate catalog selection for V2 definitions
                if (_parsedDefinition.Catalogs.Count > 0)
                {
                    foreach (var catalog in _parsedDefinition.Catalogs)
                    {
                        AvailableCatalogs.Add(new SelectableCatalogEntry { Entry = catalog });
                    }
                }

                PublisherName = _parsedDefinition.Publisher.Name;
                PublisherDescription = _parsedDefinition.Publisher.Description;
                PublisherWebsite = _parsedDefinition.Publisher.WebsiteUrl;

                // ProviderDefinition might not have AvatarUrl directly at top level generally,
                // but let's assume implementation details or fallback.
                // If the definition doesn't carry avatar, we might fetch it from catalog later, but for specific "Subscribe" dialog,
                // we show what we have.
                CanConfirm = true;
                logger.LogInformation("Successfully loaded provider definition for {Publisher}", PublisherName);
                return;
            }

            // 2. Fallback: Attempt to fetch as pure Catalog
            // If definition fetch failed (e.g. 404 or invalid format), try catalog parser.
            // Note: existing simple HttpClient string fetch.
            var response = await httpClient.GetStringAsync(url);
            var catalogResult = await catalogParser.ParseCatalogAsync(response);

            if (catalogResult.Success && catalogResult.Data != null)
            {
                _parsedCatalog = catalogResult.Data;
                PublisherName = _parsedCatalog.Publisher.Name;
                PublisherAvatarUrl = _parsedCatalog.Publisher.AvatarUrl;
                PublisherWebsite = _parsedCatalog.Publisher.WebsiteUrl; // Fixed property name
                CanConfirm = true;
                logger.LogInformation("Successfully loaded catalog for {Publisher}", PublisherName);
            }
            else
            {
                ErrorMessage = "Failed to load subscription information. Reference could not be parsed as a Provider Definition or Catalog.";
                logger.LogWarning("Failed to parse as definition or catalog from {Url}", url);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error initializing subscription confirmation");
            ErrorMessage = $"Failed to fetch information: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task ConfirmAsync()
    {
        if (!CanConfirm) return;

        try
        {
            PublisherSubscription subscription;

            if (_isProviderDefinition && _parsedDefinition != null)
            {
                logger.LogInformation("Confirming provider definition subscription for {Publisher}", _parsedDefinition.Publisher.Id);
                subscription = new PublisherSubscription
                {
                    PublisherId = _parsedDefinition.Publisher.Id,
                    PublisherName = _parsedDefinition.Publisher.Name,
                    CatalogUrl = _parsedDefinition.CatalogUrl,
                    DefinitionUrl = url,
                    SubscriptionType = SubscriptionType.ProviderDefinition,
                    Added = DateTime.UtcNow,
                    TrustLevel = TrustLevel.Untrusted,

                    // AvatarUrl from definition if available, otherwise might be updated later
                };

                // Add selected catalog entries for V2 definitions
                foreach (var selectable in AvailableCatalogs.Where(c => c.IsSelected))
                {
                    subscription.CatalogEntries.Add(new SubscribedCatalogEntry
                    {
                        CatalogId = selectable.Entry.Id,
                        CatalogName = selectable.Entry.Name,
                        CatalogUrl = selectable.Entry.Url,
                        IsEnabled = true
                    });
                }
            }
            else if (_parsedCatalog != null)
            {
                logger.LogInformation("Confirming catalog subscription for {Publisher}", _parsedCatalog.Publisher.Id);
                subscription = new PublisherSubscription
                {
                    PublisherId = _parsedCatalog.Publisher.Id,
                    PublisherName = _parsedCatalog.Publisher.Name,
                    CatalogUrl = url,
                    DefinitionUrl = null,
                    SubscriptionType = SubscriptionType.CatalogOnly,
                    Added = DateTime.UtcNow,
                    TrustLevel = TrustLevel.Untrusted,
                    AvatarUrl = _parsedCatalog.Publisher.AvatarUrl,
                };
            }
            else
            {
                return;
            }

            var result = await subscriptionStore.AddSubscriptionAsync(subscription);
            if (result.Success)
            {
                logger.LogInformation("Subscription added successfully");
                RequestClose?.Invoke(true);
            }
            else
            {
                ErrorMessage = string.Join(Environment.NewLine, result.Errors);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error confirming subscription");
            ErrorMessage = $"Failed to save subscription: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        RequestClose?.Invoke(false);
    }
}

/// <summary>
/// Wrapper for CatalogEntry with selection state.
/// </summary>
public partial class SelectableCatalogEntry : ObservableObject
{
    /// <summary>
    /// Gets the catalog entry.
    /// </summary>
    public CatalogEntry Entry { get; init; } = new();

    [ObservableProperty]
    private bool _isSelected = true;

    /// <summary>
    /// Gets the ID of the catalog.
    /// </summary>
    public string Id => Entry.Id;

    /// <summary>
    /// Gets the name of the catalog.
    /// </summary>
    public string Name => Entry.Name;

    /// <summary>
    /// Gets the description of the catalog.
    /// </summary>
    public string? Description => Entry.Description;
}
