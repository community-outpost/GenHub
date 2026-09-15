using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Results.Content;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services.CommunityOutpost;

/// <summary>
/// Background service that periodically checks for new Community Outpost patches.
/// </summary>
/// <param name="discoverer">Content discoverer.</param>
/// <param name="resolver">Content resolver.</param>
/// <param name="manifestPool">Manifest pool.</param>
/// <param name="versionComparer">Publisher-aware version comparer.</param>
/// <param name="logger">Logger instance.</param>
public class CommunityOutpostUpdateService(
    CommunityOutpostDiscoverer discoverer,
    CommunityOutpostResolver resolver,
    IContentManifestPool manifestPool,
    IContentVersionComparer versionComparer,
    ILogger<CommunityOutpostUpdateService> logger)
    : ContentUpdateServiceBase(logger),
      ICommunityOutpostUpdateService,
      IRecipient<ContentAcquiredMessage>,
      IRecipient<ReconciliationCompletedEvent>,
      IRecipient<ContentRemovingEvent>
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan FailureCooldown = TimeSpan.FromMinutes(5);

    private readonly SemaphoreSlim _checkLock = new(1, 1);
    private readonly object _cacheLock = new();
    private readonly object _messengerLock = new();
    private ContentUpdateCheckResult? _cachedResult;
    private DateTimeOffset _lastCheckTime = DateTimeOffset.MinValue;
    private int _cacheGeneration;
    private bool _messengerRegistered;
    private bool _isDisposed;

    /// <inheritdoc/>
    public void InvalidateCache()
    {
        if (_isDisposed)
        {
            return;
        }

        lock (_cacheLock)
        {
            _cachedResult = null;
            _lastCheckTime = DateTimeOffset.MinValue;
            Interlocked.Increment(ref _cacheGeneration);
        }

        logger.LogDebug("[CO UpdateService] Update check cache invalidated");
    }

    /// <summary>
    /// Handles content acquisition messages to invalidate stale update caches.
    /// </summary>
    /// <param name="message">The content acquired message.</param>
    public void Receive(ContentAcquiredMessage message)
    {
        InvalidateCache();
    }

    /// <summary>
    /// Handles reconciliation completion events to invalidate stale update caches.
    /// </summary>
    /// <param name="message">The reconciliation completed event.</param>
    public void Receive(ReconciliationCompletedEvent message)
    {
        InvalidateCache();
    }

    /// <summary>
    /// Handles content removal events to invalidate stale update caches.
    /// </summary>
    /// <param name="message">The content removing event.</param>
    public void Receive(ContentRemovingEvent message)
    {
        InvalidateCache();
    }

    /// <inheritdoc/>
    public override async Task<ContentUpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken)
    {
        if (_isDisposed)
        {
            return ContentUpdateCheckResult.CreateNoUpdateAvailable();
        }

        EnsureMessengerRegistered();

        if (_isDisposed)
        {
            return ContentUpdateCheckResult.CreateNoUpdateAvailable();
        }

        try
        {
            await _checkLock.WaitAsync(cancellationToken);
        }
        catch (ObjectDisposedException)
        {
            return ContentUpdateCheckResult.CreateNoUpdateAvailable();
        }

        try
        {
            if (_isDisposed)
            {
                return ContentUpdateCheckResult.CreateNoUpdateAvailable();
            }

            var startGeneration = Volatile.Read(ref _cacheGeneration);
            var now = DateTimeOffset.UtcNow;
            lock (_cacheLock)
            {
                var validDuration = _cachedResult?.Success == true ? CacheDuration : FailureCooldown;
                if (_cachedResult != null && (now - _lastCheckTime) < validDuration)
                {
                    logger.LogDebug("[CO UpdateService] Returning cached update check result (age: {Age})", now - _lastCheckTime);
                    return _cachedResult;
                }
            }

            var result = await CheckForUpdatesInternalAsync(cancellationToken);

            lock (_cacheLock)
            {
                if (startGeneration == Volatile.Read(ref _cacheGeneration))
                {
                    _cachedResult = result;
                    _lastCheckTime = DateTimeOffset.UtcNow;
                }
            }

            return result;
        }
        finally
        {
            try
            {
                _checkLock.Release();
            }
            catch (ObjectDisposedException)
            {
                // Disposed concurrently
            }
        }
    }

    /// <inheritdoc/>
    public override void Dispose()
    {
        lock (_messengerLock)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            WeakReferenceMessenger.Default.UnregisterAll(this);
            _messengerRegistered = false;
        }

        _checkLock.Dispose();
        base.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <inheritdoc/>
    protected override string ServiceName => CommunityOutpostConstants.PublisherName;

    /// <inheritdoc/>
    protected override TimeSpan UpdateCheckInterval => TimeSpan.FromDays(1);

    private void EnsureMessengerRegistered()
    {
        if (_messengerRegistered || _isDisposed)
        {
            return;
        }

        lock (_messengerLock)
        {
            if (_messengerRegistered || _isDisposed)
            {
                return;
            }

            WeakReferenceMessenger.Default.RegisterAll(this);
            _messengerRegistered = true;
        }
    }

    private async Task<ContentUpdateCheckResult> CheckForUpdatesInternalAsync(CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Checking for Community Outpost patch updates...");

            // Discover latest content
            var discoveryResult = await discoverer.DiscoverAsync(new ContentSearchQuery(), cancellationToken);
            if (!discoveryResult.Success || discoveryResult.Data?.Items == null || !discoveryResult.Data.Items.Any())
            {
                logger.LogWarning("No Community Outpost content discovered");
                return ContentUpdateCheckResult.CreateNoUpdateAvailable();
            }

            // Get currently installed manifests from this publisher
            var manifestsResult = await manifestPool.GetAllManifestsAsync(cancellationToken);
            var installedManifests = (manifestsResult.Data ?? [])
                .Where(m => m.Publisher?.PublisherType == CommunityOutpostConstants.PublisherType)
                .ToList();

            if (installedManifests.Count == 0)
            {
                logger.LogInformation("No Community Outpost content installed. No updates possible.");
                return ContentUpdateCheckResult.CreateNoUpdateAvailable();
            }

            // Check if any installed manifest has a newer version in the catalog
            ContentSearchResult? latestToResolve = null;
            string? currentVersionAtLatest = null;

            foreach (var discovered in discoveryResult.Data.Items)
            {
                var installed = installedManifests.FirstOrDefault(m =>
                    m.Id.Value.Equals(discovered.Id, StringComparison.OrdinalIgnoreCase));

                if (installed != null &&
                    versionComparer.IsNewer(discovered.Version, installed.Version, CommunityOutpostConstants.PublisherType) &&
                    (latestToResolve == null || versionComparer.IsNewer(discovered.Version, latestToResolve.Version, CommunityOutpostConstants.PublisherType)))
                {
                    logger.LogInformation("Newer update candidate found for {Id}: {OldVersion} -> {NewVersion}", discovered.Id, installed.Version, discovered.Version);
                    latestToResolve = discovered;
                    currentVersionAtLatest = installed.Version;
                }
            }

            if (latestToResolve == null)
            {
                logger.LogInformation("All installed Community Outpost content is up to date");
                return ContentUpdateCheckResult.CreateNoUpdateAvailable(installedManifests.FirstOrDefault()?.Version);
            }

            var latestVersion = latestToResolve.Version;
            logger.LogInformation("Update available: {Id} version {Version}", latestToResolve.Id, latestVersion);

            // Resolve new content to manifest for verification
            var resolveResult = await resolver.ResolveAsync(latestToResolve, cancellationToken);

            if (!resolveResult.Success || resolveResult.Data == null)
            {
                logger.LogError("Failed to resolve Community Outpost content: {Error}", resolveResult.FirstError);
                return ContentUpdateCheckResult.CreateFailure($"Failed to resolve: {resolveResult.FirstError}", currentVersionAtLatest);
            }

            return ContentUpdateCheckResult.CreateUpdateAvailable(
                latestVersion,
                currentVersionAtLatest);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error checking for Community Outpost updates");
            return ContentUpdateCheckResult.CreateFailure(ex.Message);
        }
    }
}
