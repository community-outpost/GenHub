using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using GenHub.Core.Constants;
using GenHub.Core.Extensions;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Messages;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Downloads.Services;

/// <summary>
/// Implementation of the content download coordinator.
/// </summary>
public sealed class ContentDownloadCoordinator(
    IContentOrchestrator contentOrchestrator,
    IContentStateService contentStateService,
    INotificationService notificationService,
    ILogger<ContentDownloadCoordinator> logger) : IContentDownloadCoordinator
{
    private sealed class InFlightDownload : IDisposable
    {
        public TaskCompletionSource<OperationResult<ContentManifest>> Tcs { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<OperationResult<ContentManifest>> Task => Tcs.Task;

        public CancellationTokenSource InternalCts { get; } = new();

        public int WaiterCount { get; set; }

        public Action<ContentAcquisitionProgress>? ProgressCallbacks { get; set; }

        public double LastProgressPercentage { get; set; }

        public string LastStatusMessage { get; set; } = string.Empty;

        public ContentSearchResult? SearchResult { get; set; }

        public string? ParentContentId { get; set; }

        public object Lock { get; } = new();

        public void Dispose()
        {
            InternalCts.Dispose();
        }
    }

    private readonly ConcurrentDictionary<string, InFlightDownload> _inFlightDownloads = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Computes the download key for a content item.
    /// </summary>
    /// <param name="searchResult">The search result.</param>
    /// <returns>A string key uniquely identifying the content download.</returns>
    public static string GetDownloadKey(ContentSearchResult searchResult)
    {
        ArgumentNullException.ThrowIfNull(searchResult);

        return !string.IsNullOrWhiteSpace(searchResult.Id)
            ? $"{searchResult.ProviderName}::{searchResult.Id}"
            : $"{searchResult.ProviderName}::{searchResult.Name}";
    }

    /// <inheritdoc />
    public bool HasActiveDownloads => !_inFlightDownloads.IsEmpty;

    /// <inheritdoc />
    public bool IsDownloading(ContentSearchResult searchResult)
    {
        if (searchResult == null)
        {
            return false;
        }

        return FindInFlightDownload(searchResult) != null;
    }

    /// <inheritdoc />
    public bool TryGetDownloadProgress(ContentSearchResult searchResult, out double progressPercentage, out string statusMessage)
    {
        progressPercentage = 0;
        statusMessage = string.Empty;

        if (searchResult == null)
        {
            return false;
        }

        var inFlight = FindInFlightDownload(searchResult);
        if (inFlight == null)
        {
            return false;
        }

        lock (inFlight.Lock)
        {
            progressPercentage = inFlight.LastProgressPercentage;
            statusMessage = inFlight.LastStatusMessage;
        }

        return true;
    }

    /// <inheritdoc />
    public async Task<OperationResult<ContentManifest>> DownloadContentAsync(
        ContentSearchResult searchResult,
        IProgress<ContentAcquisitionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(searchResult);

        var key = GetDownloadKey(searchResult);
        Action<ContentAcquisitionProgress>? callback = progress != null ? progress.Report : null;

        var (inFlight, isInitiator) = await GetOrCreateInFlightDownloadAsync(key, searchResult, cancellationToken);

        AttachProgressCallback(inFlight, callback);

        if (isInitiator)
        {
            var multiplexedProgress = CreateMultiplexedProgress(inFlight, searchResult, key);
            _ = StartDownloadTaskAsync(inFlight, searchResult, key, multiplexedProgress);
        }

        var unregistered = 0;
        var reg = default(CancellationTokenRegistration);

        try
        {
            if (cancellationToken.CanBeCanceled)
            {
                reg = cancellationToken.Register(() => DecrementWaiterAndCancelIfEmpty(inFlight, ref unregistered));
            }

            return await inFlight.Task.WaitAsync(cancellationToken);
        }
        finally
        {
            await reg.DisposeAsync();
            DecrementWaiterAndCancelIfEmpty(inFlight, ref unregistered);
            DetachProgressCallback(inFlight, callback);
        }
    }

    private static bool IsInFlightActive(InFlightDownload inFlight) =>
        !inFlight.InternalCts.IsCancellationRequested && !inFlight.Task.IsCompleted;

    private static bool MatchesInFlightDownload(InFlightDownload download, ContentSearchResult searchResult)
    {
        if (!IsInFlightActive(download))
        {
            return false;
        }

        return download.SearchResult != null &&
            DownloadMessageMatchHelper.Matches(
                null,
                download.SearchResult.Id,
                download.SearchResult.ProviderName,
                download.SearchResult.Name,
                searchResult);
    }

    private static void IncrementWaiterCount(InFlightDownload inFlight)
    {
        lock (inFlight.Lock)
        {
            inFlight.WaiterCount++;
        }
    }

    private static async Task AwaitPreviousCancelledTaskAsync(Task task, CancellationToken cancellationToken)
    {
        try
        {
            await task.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Previous task cancelled; proceed to retry in the loop to start a fresh acquire.
        }
        catch (Exception)
        {
            // Previous task failed; proceed to retry in the loop to start a fresh acquire.
        }
    }

    private static void DecrementWaiterAndCancelIfEmpty(InFlightDownload inFlight, ref int unregistered)
    {
        if (Interlocked.Exchange(ref unregistered, 1) == 0)
        {
            lock (inFlight.Lock)
            {
                inFlight.WaiterCount--;
                if (inFlight.WaiterCount <= 0)
                {
                    try
                    {
                        inFlight.InternalCts.Cancel();
                    }
                    catch (ObjectDisposedException)
                    {
                        // Ignored if already disposed
                    }
                }
            }
        }
    }

    private static void AttachProgressCallback(InFlightDownload inFlight, Action<ContentAcquisitionProgress>? callback)
    {
        if (callback == null)
        {
            return;
        }

        lock (inFlight.Lock)
        {
            inFlight.ProgressCallbacks += callback;
        }
    }

    private static void DetachProgressCallback(InFlightDownload inFlight, Action<ContentAcquisitionProgress>? callback)
    {
        if (callback == null)
        {
            return;
        }

        lock (inFlight.Lock)
        {
            inFlight.ProgressCallbacks -= callback;
        }
    }

    private InFlightDownload? FindInFlightDownload(ContentSearchResult searchResult)
    {
        var key = GetDownloadKey(searchResult);
        if (_inFlightDownloads.TryGetValue(key, out var inFlight) && IsInFlightActive(inFlight))
        {
            return inFlight;
        }

        if (!string.IsNullOrWhiteSpace(searchResult.Name))
        {
            var nameKey = $"{searchResult.ProviderName}::{searchResult.Name}";
            if (_inFlightDownloads.TryGetValue(nameKey, out inFlight) && IsInFlightActive(inFlight))
            {
                return inFlight;
            }
        }

        return _inFlightDownloads.Values.FirstOrDefault(download => MatchesInFlightDownload(download, searchResult));
    }

    private async Task<(InFlightDownload InFlight, bool IsInitiator)> GetOrCreateInFlightDownloadAsync(
        string key,
        ContentSearchResult searchResult,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Task? previousCancelledTask = null;

            lock (_inFlightDownloads)
            {
                if (_inFlightDownloads.TryGetValue(key, out var existing) && !existing.Task.IsCompleted)
                {
                    if (existing.InternalCts.IsCancellationRequested)
                    {
                        // The previous download was cancelled by its waiter(s) but is still unwinding.
                        // Wait for it to complete outside the lock before launching a fresh acquire.
                        previousCancelledTask = existing.Task;
                    }
                    else
                    {
                        // Existing active download: join it as a waiter
                        IncrementWaiterCount(existing);
                        return (existing, false);
                    }
                }
                else
                {
                    searchResult.ResolverMetadata.TryGetValue(ContentConstants.ParentContentIdMetadataKey, out var parentContentId);
                    var inFlight = new InFlightDownload
                    {
                        SearchResult = searchResult,
                        ParentContentId = parentContentId,
                    };
                    _inFlightDownloads[key] = inFlight;
                    IncrementWaiterCount(inFlight);
                    return (inFlight, true);
                }
            }

            if (previousCancelledTask != null)
            {
                await AwaitPreviousCancelledTaskAsync(previousCancelledTask, cancellationToken);
            }
        }
    }

    private Progress<ContentAcquisitionProgress> CreateMultiplexedProgress(
        InFlightDownload inFlight,
        ContentSearchResult searchResult,
        string key)
    {
        return new Progress<ContentAcquisitionProgress>(p =>
        {
            var status = p.FormatProgressStatus();
            Action<ContentAcquisitionProgress>? callbacks;
            lock (inFlight.Lock)
            {
                inFlight.LastProgressPercentage = p.ProgressPercentage;
                inFlight.LastStatusMessage = status;
                callbacks = inFlight.ProgressCallbacks;
            }

            callbacks?.Invoke(p);

            BroadcastDownloadProgress(key, searchResult, p.ProgressPercentage, status);
        });
    }

    private void BroadcastDownloadProgress(string key, ContentSearchResult searchResult, double progressPercentage, string status)
    {
        searchResult.ResolverMetadata.TryGetValue(ContentConstants.ParentContentIdMetadataKey, out var parentContentId);
        try
        {
            WeakReferenceMessenger.Default.Send(new ContentDownloadProgressMessage(
                key,
                searchResult.Id,
                searchResult.ProviderName,
                searchResult.Name,
                progressPercentage,
                status,
                parentContentId));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to broadcast ContentDownloadProgressMessage for {Key}", key);
        }
    }

    private async Task StartDownloadTaskAsync(
        InFlightDownload inFlight,
        ContentSearchResult searchResult,
        string key,
        IProgress<ContentAcquisitionProgress> progress)
    {
        searchResult.ResolverMetadata.TryGetValue(ContentConstants.ParentContentIdMetadataKey, out var parentContentId);

        try
        {
            WeakReferenceMessenger.Default.Send(new ContentDownloadStartedMessage(
                key,
                searchResult.Id,
                searchResult.ProviderName,
                searchResult.Name,
                parentContentId));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to broadcast ContentDownloadStartedMessage for {Key}", key);
        }

        var success = false;
        string? errorMessage = null;

        try
        {
            var result = await ExecuteDownloadAsync(searchResult, progress, inFlight.InternalCts.Token);
            success = result.Success;
            errorMessage = result.FirstError;
            inFlight.Tcs.TrySetResult(result);
        }
        catch (OperationCanceledException oce)
        {
            errorMessage = "Download cancelled";
            inFlight.Tcs.TrySetCanceled(oce.CancellationToken);
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            inFlight.Tcs.TrySetException(ex);
        }
        finally
        {
            lock (_inFlightDownloads)
            {
                if (_inFlightDownloads.TryGetValue(key, out var current) && ReferenceEquals(current, inFlight))
                {
                    _inFlightDownloads.TryRemove(key, out _);
                }
            }

            try
            {
                WeakReferenceMessenger.Default.Send(new ContentDownloadCompletedMessage(
                    key,
                    searchResult.Id,
                    searchResult.ProviderName,
                    searchResult.Name,
                    success,
                    errorMessage,
                    parentContentId));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to broadcast ContentDownloadCompletedMessage for {Key}", key);
            }

            inFlight.Dispose();
        }
    }

    private async Task<OperationResult<ContentManifest>> ExecuteDownloadAsync(
        ContentSearchResult searchResult,
        IProgress<ContentAcquisitionProgress>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Starting download for content: {Name} ({Provider})", searchResult.Name, searchResult.ProviderName);

            var result = await contentOrchestrator.AcquireContentAsync(searchResult, progress, cancellationToken);

            if (result.Success && result.Data != null)
            {
                var manifest = result.Data;
                logger.LogInformation("Successfully downloaded and stored content: {ManifestId}", manifest.Id.Value);

                // Remember the pre-download catalog ID, then point the search result at the stored
                // manifest so Add to Profile and later state lookups use the real manifest ID
                // (parity with the grid download path in DownloadsBrowserViewModel).
                var originalContentId = searchResult.Id ?? string.Empty;
                searchResult.UpdateId(manifest.Id.Value);

                // Update state. The event carries both the original catalog ID and the manifest ID
                // so every subscriber can match regardless of which ID it currently holds.
                contentStateService.NotifyStateChanged(originalContentId, ContentState.Downloaded, manifest.Id.Value);

                string? parentContentId = null;
                if (searchResult.ResolverMetadata?.TryGetValue(ContentConstants.ParentContentIdMetadataKey, out var pid) == true)
                {
                    parentContentId = pid;
                }

                if (!string.IsNullOrWhiteSpace(parentContentId) &&
                    !string.Equals(parentContentId, originalContentId, StringComparison.OrdinalIgnoreCase))
                {
                    contentStateService.NotifyStateChanged(parentContentId, ContentState.Downloaded, manifest.Id.Value);
                }

                // Notify other components
                try
                {
                    WeakReferenceMessenger.Default.Send(new ContentAcquiredMessage(manifest));
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to send ContentAcquiredMessage for {ManifestId}", manifest.Id.Value);
                }

                notificationService.ShowSuccess("Download Complete", $"Downloaded {searchResult.Name}");

                return OperationResult<ContentManifest>.CreateSuccess(manifest);
            }

            var errorMsg = result.FirstError ?? "Unknown error";
            logger.LogError("Failed to download {ItemName}: {Error}", searchResult.Name, errorMsg);

            return OperationResult<ContentManifest>.CreateFailure(errorMsg);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            logger.LogInformation(ex, "Download timed out or cancelled internally for: {Name}", searchResult.Name);
            return OperationResult<ContentManifest>.CreateFailure("Download cancelled");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error downloading content: {Name}", searchResult.Name);
            return OperationResult<ContentManifest>.CreateFailure($"An unexpected error occurred: {ex.Message}");
        }
    }
}
