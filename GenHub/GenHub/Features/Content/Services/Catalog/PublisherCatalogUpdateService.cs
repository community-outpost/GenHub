using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Notifications;
using GenHub.Core.Models.Results.Content;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PublisherSubscription = GenHub.Core.Models.Providers.PublisherSubscription;

namespace GenHub.Features.Content.Services.Catalog;

/// <summary>
/// Background update service for periodically polling subscribed publisher catalogs,
/// detecting updates to downloaded content, and notifying the user persistently until dismissed.
/// </summary>
public sealed class PublisherCatalogUpdateService : ContentUpdateServiceBase, IPublisherCatalogUpdateService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly INotificationService _notificationService;
    private readonly ILocalizationService _localizationService;
    private readonly ILogger<PublisherCatalogUpdateService> _logger;

    private readonly ConcurrentDictionary<string, byte> _dismissedUpdates = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _activeNotifications = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of the <see cref="PublisherCatalogUpdateService"/> class.
    /// </summary>
    /// <param name="scopeFactory">The service scope factory.</param>
    /// <param name="notificationService">The notification service.</param>
    /// <param name="localizationService">The localization service.</param>
    /// <param name="logger">The logger instance.</param>
    public PublisherCatalogUpdateService(
        IServiceScopeFactory scopeFactory,
        INotificationService notificationService,
        ILocalizationService localizationService,
        ILogger<PublisherCatalogUpdateService> logger)
        : base(logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    protected override string ServiceName => "PublisherCatalog";

    /// <inheritdoc />
    protected override TimeSpan UpdateCheckInterval => TimeSpan.FromMinutes(30);

    /// <inheritdoc />
    public bool IsUpdateDismissed(string publisherId, string contentId, string version)
    {
        if (string.IsNullOrWhiteSpace(publisherId) || string.IsNullOrWhiteSpace(contentId) || string.IsNullOrWhiteSpace(version))
        {
            return false;
        }

        var key = $"{publisherId}:{contentId}:{version}";
        return _dismissedUpdates.ContainsKey(key);
    }

    /// <inheritdoc />
    public void DismissUpdate(string publisherId, string contentId, string version)
    {
        if (string.IsNullOrWhiteSpace(publisherId) || string.IsNullOrWhiteSpace(contentId) || string.IsNullOrWhiteSpace(version))
        {
            return;
        }

        var key = $"{publisherId}:{contentId}:{version}";
        _dismissedUpdates.TryAdd(key, 0);
        _activeNotifications.TryRemove(key, out _);
        _logger.LogInformation("Update dismissed by user for {PublisherId}/{ContentId} v{Version}", publisherId, contentId, version);
    }

    /// <inheritdoc />
    public void ClearDismissedUpdates()
    {
        _dismissedUpdates.Clear();
        _activeNotifications.Clear();
    }

    /// <inheritdoc />
    public override async Task<ContentUpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var subscriptionStore = scope.ServiceProvider.GetService<IPublisherSubscriptionStore>();
            var stateService = scope.ServiceProvider.GetService<IContentStateService>();
            var discoverer = scope.ServiceProvider.GetService<GenericCatalogDiscoverer>();

            if (subscriptionStore == null || stateService == null || discoverer == null)
            {
                return ContentUpdateCheckResult.CreateNoUpdateAvailable();
            }

            var subResult = await subscriptionStore.GetSubscriptionsAsync(cancellationToken);
            if (!subResult.Success || subResult.Data == null || subResult.Data.Count == 0)
            {
                return ContentUpdateCheckResult.CreateNoUpdateAvailable();
            }

            var anyUpdateFound = false;

            foreach (var subscription in subResult.Data)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                if (await ProcessSubscriptionUpdatesAsync(subscription, discoverer, stateService, cancellationToken))
                {
                    anyUpdateFound = true;
                }
            }

            return anyUpdateFound
                ? ContentUpdateCheckResult.CreateUpdateAvailable(string.Empty, "latest")
                : ContentUpdateCheckResult.CreateNoUpdateAvailable();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error occurred while polling publisher catalog updates");
            return ContentUpdateCheckResult.CreateFailure(ex.Message);
        }
    }

    private async Task<bool> ProcessSubscriptionUpdatesAsync(
        PublisherSubscription subscription,
        GenericCatalogDiscoverer discoverer,
        IContentStateService stateService,
        CancellationToken cancellationToken)
    {
        try
        {
            discoverer.Configure(subscription);
            var discoveryResult = await discoverer.DiscoverAsync(new ContentSearchQuery(), cancellationToken);
            if (!discoveryResult.Success || discoveryResult.Data?.Items == null)
            {
                return false;
            }

            var anyUpdate = false;
            foreach (var contentItem in discoveryResult.Data.Items)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                var state = await stateService.GetStateAsync(contentItem, cancellationToken);
                if (state == ContentState.UpdateAvailable)
                {
                    anyUpdate = true;
                    NotifyUpdateAvailable(subscription, contentItem);
                }
            }

            return anyUpdate;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to check update state for subscription {PublisherId}", subscription.PublisherId);
            return false;
        }
    }

    private void NotifyUpdateAvailable(PublisherSubscription subscription, ContentSearchResult contentItem)
    {
        var version = contentItem.Version ?? "latest";

        if (IsUpdateDismissed(subscription.PublisherId, contentItem.Id, version))
        {
            return;
        }

        var dismissalKey = $"{subscription.PublisherId}:{contentItem.Id}:{version}";
        if (_activeNotifications.ContainsKey(dismissalKey))
        {
            return;
        }

        _activeNotifications.TryAdd(dismissalKey, 0);

        var title = _localizationService.GetString("Downloads.ContentUpdateNotificationTitle", contentItem.Name);
        if (string.IsNullOrWhiteSpace(title))
        {
            title = $"Update Available: {contentItem.Name}";
        }

        var message = _localizationService.GetString("Downloads.ContentUpdateNotificationMessage", contentItem.Name, version);
        if (string.IsNullOrWhiteSpace(message))
        {
            message = $"Version {version} of {contentItem.Name} is available for download.";
        }

        var dismissText = _localizationService.GetString("Common.Dismiss") ?? "Dismiss";

        var notification = new NotificationMessage(
            NotificationType.Info,
            title,
            message,
            autoDismissMilliseconds: null,
            actionText: dismissText,
            action: () => DismissUpdate(subscription.PublisherId, contentItem.Id, version),
            isPersistent: true);

        _notificationService.Show(notification);
    }
}
