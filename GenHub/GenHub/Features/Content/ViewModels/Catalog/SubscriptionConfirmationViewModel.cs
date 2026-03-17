using System;
using System.Net.Http;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Providers;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.ViewModels.Catalog;

/// <summary>
/// ViewModel for the subscription confirmation dialog.
/// Handles fetching and validating a catalog before subscription.
/// </summary>
public partial class SubscriptionConfirmationViewModel(
    string catalogUrl,
    IPublisherSubscriptionStore subscriptionStore,
    IPublisherCatalogParser catalogParser,
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

    /// <summary>
    /// Gets the catalog URL for display.
    /// </summary>
    public string CatalogUrlDisplay => catalogUrl;

    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _canConfirm;

    private PublisherCatalog? _parsedCatalog;

    /// <summary>
    /// Initializes the ViewModel by fetching the catalog metadata.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task InitializeAsync()
    {
        try
        {
            IsLoading = true;
            ErrorMessage = null;
            CanConfirm = false;

            logger.LogInformation("Fetching catalog from {Url}", catalogUrl);
            var response = await httpClient.GetStringAsync(catalogUrl);

            var result = await catalogParser.ParseCatalogAsync(response);
            if (result.Success && result.Data != null)
            {
                _parsedCatalog = result.Data;
                PublisherName = _parsedCatalog.Publisher.Name;
                PublisherAvatarUrl = _parsedCatalog.Publisher.AvatarUrl;
                PublisherWebsite = _parsedCatalog.Publisher.Website;
                CanConfirm = true;
                logger.LogInformation("Successfully loaded catalog for {Publisher}", PublisherName);
            }
            else
            {
                ErrorMessage = string.Join(Environment.NewLine, result.Errors);
                logger.LogWarning("Failed to parse catalog: {Errors}", ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error initializing subscription confirmation");
            ErrorMessage = $"Failed to fetch catalog: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task ConfirmAsync()
    {
        if (_parsedCatalog == null) return;

        try
        {
            logger.LogInformation("Confirming subscription for {Publisher}", _parsedCatalog.Publisher.Id);

            var subscription = new PublisherSubscription
            {
                PublisherId = _parsedCatalog.Publisher.Id,
                PublisherName = _parsedCatalog.Publisher.Name,
                CatalogUrl = catalogUrl,
                Added = DateTime.UtcNow,
                TrustLevel = TrustLevel.Untrusted, // Default for new unverified subscriptions
                AvatarUrl = _parsedCatalog.Publisher.AvatarUrl,
            };

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
