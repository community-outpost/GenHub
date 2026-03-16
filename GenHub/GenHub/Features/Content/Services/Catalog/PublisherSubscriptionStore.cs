using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services.Catalog;

/// <summary>
/// File-based storage for publisher subscriptions.
/// Persists to {AppData}/GenHub/subscriptions.json.
/// </summary>
public class PublisherSubscriptionStore : IPublisherSubscriptionStore
{
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
    private readonly ILogger<PublisherSubscriptionStore> _logger;
    private readonly IConfigurationProviderService _configurationProvider;
    private readonly string _subscriptionsFilePath;
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    private PublisherSubscriptionCollection? _cachedSubscriptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="PublisherSubscriptionStore"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="configurationProvider">The configuration provider service.</param>
    public PublisherSubscriptionStore(
        ILogger<PublisherSubscriptionStore> logger,
        IConfigurationProviderService configurationProvider)
    {
        _logger = logger;
        _configurationProvider = configurationProvider;

        var appDataPath = _configurationProvider.GetApplicationDataPath();
        _subscriptionsFilePath = Path.Combine(appDataPath, "subscriptions.json");
    }

    /// <inheritdoc />
    public async Task<OperationResult<IEnumerable<PublisherSubscription>>> GetSubscriptionsAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var collection = await LoadSubscriptionsAsync(cancellationToken);
            return OperationResult<IEnumerable<PublisherSubscription>>.CreateSuccess(collection.Subscriptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get subscriptions");
            return OperationResult<IEnumerable<PublisherSubscription>>.CreateFailure(
                $"Failed to load subscriptions: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<PublisherSubscription?>> GetSubscriptionAsync(
        string publisherId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var collection = await LoadSubscriptionsAsync(cancellationToken);
            var subscription = collection.Subscriptions
                .FirstOrDefault(s => s.PublisherId.Equals(publisherId, StringComparison.OrdinalIgnoreCase));

            return OperationResult<PublisherSubscription?>.CreateSuccess(subscription);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get subscription for {PublisherId}", publisherId);
            return OperationResult<PublisherSubscription?>.CreateFailure(
                $"Failed to load subscription: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<bool>> AddSubscriptionAsync(
        PublisherSubscription subscription,
        CancellationToken cancellationToken = default)
    {
        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            var collection = await LoadSubscriptionsAsync(cancellationToken);

            // Check for duplicates
            if (collection.Subscriptions.Any(s => s.PublisherId.Equals(subscription.PublisherId, StringComparison.OrdinalIgnoreCase)))
            {
                return OperationResult<bool>.CreateFailure($"Subscription for '{subscription.PublisherId}' already exists");
            }

            collection.Subscriptions.Add(subscription);
            await SaveSubscriptionsAsync(collection, cancellationToken);

            _logger.LogInformation("Added subscription for publisher: {PublisherId}", subscription.PublisherId);
            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add subscription for {PublisherId}", subscription.PublisherId);
            return OperationResult<bool>.CreateFailure($"Failed to add subscription: {ex.Message}");
        }
        finally
        {
            _fileLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<bool>> RemoveSubscriptionAsync(
        string publisherId,
        CancellationToken cancellationToken = default)
    {
        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            var collection = await LoadSubscriptionsAsync(cancellationToken);
            var removed = collection.Subscriptions.RemoveAll(s =>
                s.PublisherId.Equals(publisherId, StringComparison.OrdinalIgnoreCase));

            if (removed == 0)
            {
                return OperationResult<bool>.CreateFailure($"Subscription for '{publisherId}' not found");
            }

            await SaveSubscriptionsAsync(collection, cancellationToken);

            _logger.LogInformation("Removed subscription for publisher: {PublisherId}", publisherId);
            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove subscription for {PublisherId}", publisherId);
            return OperationResult<bool>.CreateFailure($"Failed to remove subscription: {ex.Message}");
        }
        finally
        {
            _fileLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<bool>> UpdateSubscriptionAsync(
        PublisherSubscription subscription,
        CancellationToken cancellationToken = default)
    {
        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            var collection = await LoadSubscriptionsAsync(cancellationToken);
            var index = collection.Subscriptions.FindIndex(s =>
                s.PublisherId.Equals(subscription.PublisherId, StringComparison.OrdinalIgnoreCase));

            if (index == -1)
            {
                return OperationResult<bool>.CreateFailure($"Subscription for '{subscription.PublisherId}' not found");
            }

            collection.Subscriptions[index] = subscription;
            await SaveSubscriptionsAsync(collection, cancellationToken);

            _logger.LogInformation("Updated subscription for publisher: {PublisherId}", subscription.PublisherId);
            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update subscription for {PublisherId}", subscription.PublisherId);
            return OperationResult<bool>.CreateFailure($"Failed to update subscription: {ex.Message}");
        }
        finally
        {
            _fileLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<bool>> IsSubscribedAsync(
        string publisherId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var collection = await LoadSubscriptionsAsync(cancellationToken);
            var exists = collection.Subscriptions.Any(s =>
                s.PublisherId.Equals(publisherId, StringComparison.OrdinalIgnoreCase));

            return OperationResult<bool>.CreateSuccess(exists);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check subscription for {PublisherId}", publisherId);
            return OperationResult<bool>.CreateFailure($"Failed to check subscription: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<bool>> UpdateTrustLevelAsync(
        string publisherId,
        TrustLevel trustLevel,
        CancellationToken cancellationToken = default)
    {
        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            var collection = await LoadSubscriptionsAsync(cancellationToken);
            var subscription = collection.Subscriptions.FirstOrDefault(s =>
                s.PublisherId.Equals(publisherId, StringComparison.OrdinalIgnoreCase));

            if (subscription == null)
            {
                return OperationResult<bool>.CreateFailure($"Subscription for '{publisherId}' not found");
            }

            subscription.TrustLevel = trustLevel;
            await SaveSubscriptionsAsync(collection, cancellationToken);

            _logger.LogInformation("Updated trust level for publisher {PublisherId} to {TrustLevel}", publisherId, trustLevel);
            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update trust level for {PublisherId}", publisherId);
            return OperationResult<bool>.CreateFailure($"Failed to update trust level: {ex.Message}");
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private async Task<PublisherSubscriptionCollection> LoadSubscriptionsAsync(CancellationToken cancellationToken)
    {
        // Return cached if available
        if (_cachedSubscriptions != null)
        {
            return _cachedSubscriptions;
        }

        if (!File.Exists(_subscriptionsFilePath))
        {
            _logger.LogInformation("Subscriptions file not found, creating new collection");
            _cachedSubscriptions = new PublisherSubscriptionCollection();
            return _cachedSubscriptions;
        }

        var json = await File.ReadAllTextAsync(_subscriptionsFilePath, cancellationToken);
        _cachedSubscriptions = JsonSerializer.Deserialize<PublisherSubscriptionCollection>(json)
            ?? new PublisherSubscriptionCollection();

        _logger.LogDebug("Loaded {Count} subscriptions from file", _cachedSubscriptions.Subscriptions.Count);
        return _cachedSubscriptions;
    }

    private async Task SaveSubscriptionsAsync(
        PublisherSubscriptionCollection collection,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_subscriptionsFilePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(collection, _jsonOptions);
        await File.WriteAllTextAsync(_subscriptionsFilePath, json, cancellationToken);

        _cachedSubscriptions = collection;
        _logger.LogDebug("Saved {Count} subscriptions to file", collection.Subscriptions.Count);
    }
}
