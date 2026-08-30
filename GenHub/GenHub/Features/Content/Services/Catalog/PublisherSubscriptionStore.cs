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
/// File-based storage for user content subscriptions under
/// <c>{AppData}/GenHub/subscriptions.json</c> (<see cref="CatalogConstants.SubscriptionFileName"/>).
/// </summary>
/// <remarks>
/// Stores <see cref="PublisherSubscription"/> entries for catalog-direct follows today.
/// When Publisher Studio adds definition-based subscribe, the same file/schema gains
/// <see cref="PublisherSubscription.DefinitionUrl"/> usage; Downloads continues to bind
/// sidebar entries from this store.
/// </remarks>
public class PublisherSubscriptionStore(
    ILogger<PublisherSubscriptionStore> logger,
    IConfigurationProviderService configurationProvider) : IPublisherSubscriptionStore
{
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    private readonly string _subscriptionsFilePath = Path.Combine(
        configurationProvider.GetApplicationDataPath(),
        CatalogConstants.SubscriptionFileName);

    private readonly SemaphoreSlim _fileLock = new(1, 1);

    private PublisherSubscriptionContainer? _cachedSubscriptions;

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<PublisherSubscription>>> GetSubscriptionsAsync(
        CancellationToken cancellationToken = default)
    {
        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            var collection = await LoadSubscriptionsCoreAsync(cancellationToken);
            IReadOnlyList<PublisherSubscription> clones = collection.Subscriptions.Select(CloneSubscription).ToList();
            return OperationResult<IReadOnlyList<PublisherSubscription>>.CreateSuccess(clones);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get subscriptions");
            return OperationResult<IReadOnlyList<PublisherSubscription>>.CreateFailure(
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
            var collection = await LoadSubscriptionsCoreAsync(cancellationToken);
            var subscription = collection.Subscriptions
                .FirstOrDefault(s => s.PublisherId.Equals(publisherId, StringComparison.OrdinalIgnoreCase));

            return OperationResult<PublisherSubscription?>.CreateSuccess(
                subscription != null ? CloneSubscription(subscription) : null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get subscription for {PublisherId}", publisherId);
            return OperationResult<PublisherSubscription?>.CreateFailure(
                $"Failed to load subscription: {ex.Message}");
        }
        finally
        {
            _fileLock.Release();
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
            var collection = await LoadSubscriptionsCoreAsync(cancellationToken);

            // Check for duplicates
            if (collection.Subscriptions.Any(s => s.PublisherId.Equals(subscription.PublisherId, StringComparison.OrdinalIgnoreCase)))
            {
                return OperationResult<bool>.CreateFailure($"Subscription for '{subscription.PublisherId}' already exists");
            }

            collection.Subscriptions.Add(CloneSubscription(subscription));
            await SaveSubscriptionsAsync(collection, cancellationToken);

            logger.LogInformation("Added subscription for publisher: {PublisherId}", subscription.PublisherId);
            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to add subscription for {PublisherId}", subscription.PublisherId);
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
            var collection = await LoadSubscriptionsCoreAsync(cancellationToken);
            var removed = collection.Subscriptions.RemoveAll(s =>
                s.PublisherId.Equals(publisherId, StringComparison.OrdinalIgnoreCase));

            if (removed == 0)
            {
                return OperationResult<bool>.CreateFailure($"Subscription for '{publisherId}' not found");
            }

            await SaveSubscriptionsAsync(collection, cancellationToken);

            logger.LogInformation("Removed subscription for publisher: {PublisherId}", publisherId);
            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to remove subscription for {PublisherId}", publisherId);
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
            var collection = await LoadSubscriptionsCoreAsync(cancellationToken);
            var index = collection.Subscriptions.FindIndex(s =>
                s.PublisherId.Equals(subscription.PublisherId, StringComparison.OrdinalIgnoreCase));

            if (index == -1)
            {
                return OperationResult<bool>.CreateFailure($"Subscription for '{subscription.PublisherId}' not found");
            }

            collection.Subscriptions[index] = CloneSubscription(subscription);
            await SaveSubscriptionsAsync(collection, cancellationToken);

            logger.LogInformation("Updated subscription for publisher: {PublisherId}", subscription.PublisherId);
            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update subscription for {PublisherId}", subscription.PublisherId);
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
        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            var collection = await LoadSubscriptionsCoreAsync(cancellationToken);
            var exists = collection.Subscriptions.Any(s =>
                s.PublisherId.Equals(publisherId, StringComparison.OrdinalIgnoreCase));

            return OperationResult<bool>.CreateSuccess(exists);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to check subscription for {PublisherId}", publisherId);
            return OperationResult<bool>.CreateFailure($"Failed to check subscription: {ex.Message}");
        }
        finally
        {
            _fileLock.Release();
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
            var collection = await LoadSubscriptionsCoreAsync(cancellationToken);
            var subscription = collection.Subscriptions.FirstOrDefault(s =>
                s.PublisherId.Equals(publisherId, StringComparison.OrdinalIgnoreCase));

            if (subscription == null)
            {
                return OperationResult<bool>.CreateFailure($"Subscription for '{publisherId}' not found");
            }

            subscription.TrustLevel = trustLevel;
            await SaveSubscriptionsAsync(collection, cancellationToken);

            logger.LogInformation("Updated trust level for publisher {PublisherId} to {TrustLevel}", publisherId, trustLevel);
            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update trust level for {PublisherId}", publisherId);
            return OperationResult<bool>.CreateFailure($"Failed to update trust level: {ex.Message}");
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private static PublisherSubscription CloneSubscription(PublisherSubscription source)
    {
        return new PublisherSubscription
        {
            PublisherId = source.PublisherId,
            PublisherName = source.PublisherName,
            CatalogUrl = source.CatalogUrl,
            DefinitionUrl = source.DefinitionUrl,
            Added = source.Added,
            TrustLevel = source.TrustLevel,
            AutoUpdate = source.AutoUpdate,
            NotifyNewReleases = source.NotifyNewReleases,
            CachedCatalogHash = source.CachedCatalogHash,
            LastFetched = source.LastFetched,
            AvatarUrl = source.AvatarUrl,
        };
    }

    private async Task<PublisherSubscriptionContainer> LoadSubscriptionsCoreAsync(CancellationToken cancellationToken)
    {
        // Return cached if available
        if (_cachedSubscriptions != null)
        {
            return _cachedSubscriptions;
        }

        if (!File.Exists(_subscriptionsFilePath))
        {
            logger.LogInformation("Subscriptions file not found, creating new collection");
            _cachedSubscriptions = new PublisherSubscriptionContainer();
            return _cachedSubscriptions;
        }

        var json = await File.ReadAllTextAsync(_subscriptionsFilePath, cancellationToken);
        _cachedSubscriptions = JsonSerializer.Deserialize<PublisherSubscriptionContainer>(json)
            ?? new PublisherSubscriptionContainer();

        logger.LogDebug("Loaded {Count} subscriptions from file", _cachedSubscriptions.Subscriptions.Count);
        return _cachedSubscriptions;
    }

    private async Task SaveSubscriptionsAsync(
        PublisherSubscriptionContainer collection,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_subscriptionsFilePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(collection, _jsonOptions);

        try
        {
            // Atomic write via temp file + replace, so a failed write cannot leave a truncated
            // subscriptions.json on disk.
            var tempPath = _subscriptionsFilePath + ".tmp";
            await File.WriteAllTextAsync(tempPath, json, cancellationToken);
            File.Move(tempPath, _subscriptionsFilePath, overwrite: true);
        }
        catch
        {
            // The caller already mutated the cached container (Add/Remove/Update/TrustLevel).
            // Since the write failed, drop the cache so later reads re-load the true disk state
            // instead of returning state that was never persisted.
            _cachedSubscriptions = null;
            throw;
        }

        // Only commit the cache after the write succeeds.
        _cachedSubscriptions = collection;
        logger.LogDebug("Saved {Count} subscriptions to file", collection.Subscriptions.Count);
    }
}
