using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
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
    /// Initializes a new instance of the <see cref="PublisherSubscriptionStore"/> class and configures the path to the subscriptions.json file inside the application's data directory.
    /// </summary>
    /// <param name="logger">Logger for publisher subscription store operations.</param>
    /// <param name="configurationProvider">Service that provides application configuration values, including the application data path used to locate the subscriptions file.</param>
    public PublisherSubscriptionStore(
        ILogger<PublisherSubscriptionStore> logger,
        IConfigurationProviderService configurationProvider)
    {
        _logger = logger;
        _configurationProvider = configurationProvider;

        var appDataPath = _configurationProvider.GetApplicationDataPath();
        _subscriptionsFilePath = Path.Combine(appDataPath, FileTypes.SubscriptionsFileName);
    }

    /// <summary>
    /// Retrieve all stored publisher subscriptions.
    /// </summary>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    /// <returns>An OperationResult containing the stored <see cref="PublisherSubscription"/> sequence on success; on failure the result indicates failure and includes an error message.</returns>
    public async Task<OperationResult<IEnumerable<PublisherSubscription>>> GetSubscriptionsAsync(
        CancellationToken cancellationToken = default)
    {
        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            var collection = await LoadSubscriptionsAsync(cancellationToken);

            // Return defensive copies to prevent cached object mutation
            return OperationResult<IEnumerable<PublisherSubscription>>.CreateSuccess(collection.Subscriptions.Select(s => s.Clone()));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get subscriptions");
            return OperationResult<IEnumerable<PublisherSubscription>>.CreateFailure(
                $"Failed to load subscriptions: {ex.Message}");
        }
        finally
        {
            _fileLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<PublisherSubscription?>> GetSubscriptionAsync(
        string publisherId,
        CancellationToken cancellationToken = default)
    {
        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            var collection = await LoadSubscriptionsAsync(cancellationToken);
            var subscription = collection.Subscriptions
                .FirstOrDefault(s => s.PublisherId.Equals(publisherId, StringComparison.OrdinalIgnoreCase));

            return OperationResult<PublisherSubscription?>.CreateSuccess(subscription?.Clone());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get subscription for {PublisherId}", publisherId);
            return OperationResult<PublisherSubscription?>.CreateFailure(
                $"Failed to load subscription: {ex.Message}");
        }
        finally
        {
            _fileLock.Release();
        }
    }

    /// <summary>
    /// Adds a new publisher subscription to the persistent subscriptions store and prevents duplicates.
    /// </summary>
    /// <param name="subscription">The subscription to add.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>An <see cref="OperationResult{T}"/> containing `true` if the subscription was added; a failure result if a subscription with the same publisher ID already exists or an error occurs.</returns>
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

            collection.Subscriptions.Add(subscription.Clone());
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

            collection.Subscriptions[index] = subscription.Clone();
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

    /// <summary>
    /// Determines whether a subscription exists for the specified publisher.
    /// </summary>
    /// <param name="publisherId">The identifier of the publisher to check. Comparison is case-insensitive.</param>
    /// <param name="cancellationToken">A cancellation token to observe while checking the subscription status.</param>
    /// <returns>true if a subscription for the specified publisher exists, false otherwise.</returns>
    public async Task<OperationResult<bool>> IsSubscribedAsync(
        string publisherId,
        CancellationToken cancellationToken = default)
    {
        await _fileLock.WaitAsync(cancellationToken);
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
        finally
        {
            _fileLock.Release();
        }
    }

    /// <summary>
    /// Updates the trust level of the subscription for the specified publisher.
    /// </summary>
    /// <param name="publisherId">The identifier of the publisher whose subscription will be updated (comparison is case-insensitive).</param>
    /// <param name="trustLevel">The new trust level to assign to the subscription.</param>
    /// <param name="cancellationToken">A cancellation token to observe while updating the trust level.</param>
    /// <returns>An <see cref="OperationResult{T}"/> containing `true` if the subscription was found and updated; on failure the result will contain an error message.</returns>
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

    /// <summary>
    /// Loads and returns the publisher subscription collection, using the in-memory cache when available.
    /// If the subscriptions file is missing, initializes and returns an empty collection; otherwise deserializes the file and caches the result.
    /// </summary>
    /// <remarks>
    /// The cache is currently only invalidated when this instance performs a write operation.
    /// External changes to the subscriptions file will not be reflected until the application is restarted
    /// or a write operation triggers a cache refresh.
    /// </remarks>
    /// <returns>The loaded and cached <see cref="PublisherSubscriptionCollection"/>.</returns>
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

    /// <summary>
    /// Persists the provided subscription collection to the subscriptions file and updates the in-memory cache.
    /// </summary>
    /// <param name="collection">The subscription collection to serialize and save to disk; becomes the new in-memory cache.</param>
    /// <param name="cancellationToken">Token to observe for cancellation of the file write operation.</param>
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

        // Atomic write using a temp file to prevent data corruption if the process crashes or power is lost
        var tempFile = $"{_subscriptionsFilePath}.tmp";
        try
        {
            await File.WriteAllTextAsync(tempFile, json, cancellationToken);
            File.Move(tempFile, _subscriptionsFilePath, overwrite: true);

            _cachedSubscriptions = collection;
            _logger.LogDebug("Saved {Count} subscriptions to file", collection.Subscriptions.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to perform atomic write to {Path}", _subscriptionsFilePath);
            throw;
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                try
                {
                    File.Delete(tempFile);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete temporary file {TempFile}", tempFile);
                }
            }
        }
    }
}