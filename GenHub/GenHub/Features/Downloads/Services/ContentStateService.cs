using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results.Content;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Downloads.Services;

/// <summary>
/// Service to determine the current state of content for UI display.
/// Checks whether content is Downloaded, UpdateAvailable, or NotDownloaded.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="ContentStateService"/> class.
/// </remarks>
/// <param name="manifestPool">The manifest pool to check for existing content.</param>
/// <param name="logger">The logger for diagnostic output.</param>
public sealed class ContentStateService(
    IContentManifestPool manifestPool,
    ILogger<ContentStateService> logger) : IContentStateService
{
    private readonly IContentManifestPool _manifestPool = manifestPool ?? throw new ArgumentNullException(nameof(manifestPool));
    private readonly ILogger<ContentStateService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// Determines the content's UI state (Downloaded, UpdateAvailable, or NotDownloaded) by comparing a prospective manifest derived from the provided content metadata against local manifests.
    /// </summary>
    /// <param name="item">Content metadata used to generate a prospective manifest identifier for matching against local manifests.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>
    /// <see cref="ContentState.Downloaded"/> if an exact manifest is acquired or a matching local manifest exists with the same or newer version;
    /// <see cref="ContentState.UpdateAvailable"/> if a matching local manifest exists but a newer version is available;
    /// <see cref="ContentState.NotDownloaded"/> if no matching manifest is found.
    /// </returns>
    public async Task<ContentState> GetStateAsync(ContentSearchResult item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        // 1. Get release date from item.LastUpdated or use DateTime.MinValue
        var releaseDate = item.LastUpdated ?? DateTime.MinValue;

        // 2. Generate prospective manifest ID (used for parsing segments)
        var prospectiveId = ManifestIdGenerator.GeneratePublisherContentId(
            item.ProviderName ?? "unknown",
            item.ContentType,
            item.Name ?? item.Id ?? "unknown",
            releaseDate);

        _logger.LogDebug(
            "Generated prospective manifest ID: {ManifestId} for content: {ContentName}",
            prospectiveId,
            item.Name);

        // 3. Check if exact match exists (fast path)
        var isAcquiredResult = await _manifestPool.IsManifestAcquiredAsync(prospectiveId, cancellationToken);
        if (isAcquiredResult.Success && isAcquiredResult.Data)
        {
            _logger.LogDebug("Content {ContentName} is downloaded (exact match found)", item.Name);
            return ContentState.Downloaded;
        }

        // 4. Find ANY matching manifest by publisher+type+name, ignoring version
        // This handles cases where factories use different versioning schemes (date vs numeric)
        var (matchingManifest, isNewerAvailable) = await FindMatchingManifestAsync(prospectiveId, releaseDate, cancellationToken);

        if (matchingManifest != null)
        {
            if (isNewerAvailable)
            {
                _logger.LogDebug(
                    "Content {ContentName} has an update available (local: {LocalId})",
                    item.Name,
                    matchingManifest.Id.Value);
                return ContentState.UpdateAvailable;
            }

            _logger.LogDebug(
                "Content {ContentName} is downloaded (matched by publisher/type/name: {LocalId})",
                item.Name,
                matchingManifest.Id.Value);
            return ContentState.Downloaded;
        }

        // 5. No versions found
        _logger.LogDebug("Content {ContentName} is not downloaded", item.Name);
        return ContentState.NotDownloaded;
    }

    /// <inheritdoc/>
    public async Task<ContentState> GetStateAsync(
        string publisher,
        ContentType contentType,
        string contentName,
        DateTime releaseDate,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publisher);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentName);

        // Create a temporary ContentSearchResult for processing
        var item = new ContentSearchResult
        {
            ProviderName = publisher,
            ContentType = contentType,
            Name = contentName,
            Id = contentName,
            LastUpdated = releaseDate,
        };

        return await GetStateAsync(item, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<string?> GetLocalManifestIdAsync(ContentSearchResult item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        // 1. Get release date from item.LastUpdated or use DateTime.MinValue
        var releaseDate = item.LastUpdated ?? DateTime.MinValue;

        // 2. Generate prospective manifest ID
        var prospectiveId = ManifestIdGenerator.GeneratePublisherContentId(
            item.ProviderName ?? "unknown",
            item.ContentType,
            item.Name ?? item.Id ?? "unknown",
            releaseDate);

        // 3. Check if exact match exists (fast path)
        var isAcquiredResult = await _manifestPool.IsManifestAcquiredAsync(prospectiveId, cancellationToken);
        if (isAcquiredResult.Success && isAcquiredResult.Data)
        {
            return prospectiveId;
        }

        // 4. Find ANY matching manifest by publisher+type+name, ignoring version
        var (matchingManifest, _) = await FindMatchingManifestAsync(prospectiveId, releaseDate, cancellationToken);
        return matchingManifest?.Id.Value;
    }

    /// <summary>
    /// Finds a matching manifest by publisher, content type, and content name (ignoring version).
    /// This handles cases where different factories use different versioning schemes.
    /// </summary>
    /// <param name="prospectiveId">The prospective manifest ID for the current version.</param>
    /// <param name="releaseDate">The release date from the content source (used for update detection).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A tuple containing:
    /// - The matching manifest (or null if not found)
    /// - Whether a newer version is available (true if local version is older than release date)
    /// <summary>
    /// Locates a local manifest that matches the prospective manifest's publisher, content type, and content name (ignoring version) and indicates if a newer version is available.
    /// </summary>
    /// <param name="prospectiveId">The prospective manifest identifier in the form <c>schemaVersion.userVersion.publisher.contentType.contentName</c>.</param>
    /// <param name="releaseDate">The prospective content release date used for version comparison.</param>
    /// <returns>
    /// A tuple where the first element is the matching local <see cref="ContentManifest"/> or <c>null</c> if none is found, and the second element is <c>true</c> if the prospective manifest's version represents a newer release than the local manifest (comparison is performed only when both versions are 8-digit numeric dates in yyyyMMdd format), <c>false</c> otherwise.
    /// </returns>
    private async Task<(ContentManifest? Manifest, bool IsNewerAvailable)> FindMatchingManifestAsync(
        string prospectiveId,
        DateTime releaseDate,
        CancellationToken cancellationToken)
    {
        // Get all manifests and filter in memory
        var allManifestsResult = await _manifestPool.GetAllManifestsAsync(cancellationToken);
        if (!allManifestsResult.Success || allManifestsResult.Data is null)
        {
            return (null, false);
        }

        // Parse the prospective ID to extract components
        var prospectiveSegments = prospectiveId.Split('.');
        if (prospectiveSegments.Length != 5)
        {
            _logger.LogWarning("Prospective manifest ID has invalid segment count: {ManifestId}", prospectiveId);
            return (null, false);
        }

        // Format: schemaVersion.userVersion.publisher.contentType.contentName
        // We want to match manifests with same publisher, contentType, and contentName
        var publisher = prospectiveSegments[2];
        var contentType = prospectiveSegments[3];
        var contentName = prospectiveSegments[4];

        ContentManifest? bestMatch = null;
        string? bestMatchVersion = null;

        foreach (var manifest in allManifestsResult.Data)
        {
            var manifestSegments = manifest.Id.Value.Split('.');
            if (manifestSegments.Length != 5)
            {
                continue;
            }

            // Check if publisher, contentType, and contentName match (case-insensitive)
            if (manifestSegments[2].Equals(publisher, StringComparison.OrdinalIgnoreCase) &&
                manifestSegments[3].Equals(contentType, StringComparison.OrdinalIgnoreCase) &&
                manifestSegments[4].Equals(contentName, StringComparison.OrdinalIgnoreCase))
            {
                var existingVersion = manifestSegments[1];

                // Keep the one with the highest version (newest)
                if (bestMatch == null || string.CompareOrdinal(existingVersion, bestMatchVersion) > 0)
                {
                    bestMatch = manifest;
                    bestMatchVersion = existingVersion;
                }
            }
        }

        if (bestMatch == null)
        {
            return (null, false);
        }

        // Determine if a newer version is available
        // Compare the local version with the prospective version (release date)
        // If prospective version (from source) is newer than local version → update available
        var prospectiveVersion = prospectiveSegments[1];
        bool isNewerAvailable = false;

        // Only consider update available if:
        // 1. Both versions are date-based (8 digits, yyyyMMdd format)
        // 2. The prospective version is greater than the local version
        if (prospectiveVersion.Length == 8 && bestMatchVersion?.Length == 8 &&
            int.TryParse(prospectiveVersion, out var prospectiveInt) &&
            int.TryParse(bestMatchVersion, out var localInt))
        {
            isNewerAvailable = prospectiveInt > localInt;
        }

        _logger.LogDebug(
            "Found matching manifest: {ManifestId}, local version: {LocalVersion}, prospective version: {ProspectiveVersion}, update available: {UpdateAvailable}",
            bestMatch.Id.Value,
            bestMatchVersion,
            prospectiveVersion,
            isNewerAvailable);

        return (bestMatch, isNewerAvailable);
    }
}