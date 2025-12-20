using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GeneralsOnline;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Content.Services.GeneralsOnline;

/// <summary>
/// Discovers Generals Online releases by querying the CDN API.
/// Supports both manifest.json API and latest.txt polling for release discovery.
/// </summary>
public class GeneralsOnlineDiscoverer : IContentDiscoverer
{
    private readonly ILogger<GeneralsOnlineDiscoverer> _logger;
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="GeneralsOnlineDiscoverer"/> class.
    /// </summary>
    /// <param name="logger">The logger for diagnostic information.</param>
    /// <param name="httpClientFactory">Factory for creating HTTP clients.</param>
    public GeneralsOnlineDiscoverer(
        ILogger<GeneralsOnlineDiscoverer> logger,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient(GeneralsOnlineConstants.PublisherType);
    }

    /// <inheritdoc />
    public string SourceName => GeneralsOnlineConstants.PublisherType;

    /// <inheritdoc />
    public string Description => "Discovers Generals Online releases from official CDN";

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public ContentSourceCapabilities Capabilities =>
        ContentSourceCapabilities.RequiresDiscovery |
        ContentSourceCapabilities.SupportsPackageAcquisition;

    /// <summary>
    /// Disposes resources used by the discoverer.
    /// </summary>
    public void Dispose()
    {
        _httpClient?.Dispose();
    }

    /// <summary>
    /// Discovers Generals Online releases from CDN API.
    /// Tries manifest.json first, then latest.txt. Returns error if CDN is unreachable.
    /// </summary>
    /// <param name="query">The search query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Operation result containing discovered content.</returns>
    public async Task<OperationResult<IEnumerable<ContentSearchResult>>> DiscoverAsync(
        ContentSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Discovering Generals Online releases");

        try
        {
            // Try to get release from API
            var (cdnAvailable, release) = await TryGetReleaseFromApiAsync(cancellationToken);

            // If CDN is unreachable, return failure
            if (!cdnAvailable)
            {
                _logger.LogWarning("Generals Online CDN is currently unreachable");
                return OperationResult<IEnumerable<ContentSearchResult>>.CreateFailure(
                    "Generals Online CDN is currently unavailable. Please try again later.");
            }

            // CDN is reachable but has no releases
            if (release == null)
            {
                _logger.LogInformation("No Generals Online releases available");
                return OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess(
                    Enumerable.Empty<ContentSearchResult>());
            }

            // Filter by search query if provided
            if (!string.IsNullOrWhiteSpace(query.SearchTerm) &&
                !release.Version.Contains(query.SearchTerm, StringComparison.OrdinalIgnoreCase) &&
                !GeneralsOnlineConstants.ContentName.Contains(query.SearchTerm, StringComparison.OrdinalIgnoreCase))
            {
                return OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess(
                    Enumerable.Empty<ContentSearchResult>());
            }

            var searchResult = CreateSearchResult(release);

            return OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess(
                new[] { searchResult });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to discover Generals Online releases");
            return OperationResult<IEnumerable<ContentSearchResult>>.CreateFailure(
                $"Discovery failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Attempts to get release information from the Generals Online CDN API.
    /// </summary>
    /// <returns>Tuple of (cdnAvailable, release). cdnAvailable is false if CDN is unreachable, release is null if none found.</returns>
    private async Task<(bool CdnAvailable, GeneralsOnlineRelease? Release)> TryGetReleaseFromApiAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("Attempting to query Generals Online CDN API");

            // Try manifest.json first (full API response)
            var manifestResponse = await _httpClient.GetAsync(
                GeneralsOnlineConstants.ManifestApiUrl,
                cancellationToken);

            if (manifestResponse.IsSuccessStatusCode)
            {
                var json = await manifestResponse.Content.ReadAsStringAsync(cancellationToken);
                var apiResponse = JsonSerializer.Deserialize<GeneralsOnlineApiResponse>(json);

                if (apiResponse != null && !string.IsNullOrEmpty(apiResponse.Version))
                {
                    _logger.LogInformation("Retrieved release from manifest.json API: {Version}", apiResponse.Version);
                    return (true, CreateReleaseFromApiResponse(apiResponse));
                }
            }

            // Fall back to latest.txt (simple version polling)
            var versionResponse = await _httpClient.GetAsync(
                GeneralsOnlineConstants.LatestVersionUrl,
                cancellationToken);

            if (versionResponse.IsSuccessStatusCode)
            {
                var version = await versionResponse.Content.ReadAsStringAsync(cancellationToken);
                version = version?.Trim();

                if (!string.IsNullOrEmpty(version))
                {
                    _logger.LogInformation("Retrieved version from latest.txt: {Version}", version);
                    return (true, CreateReleaseFromVersion(version));
                }
            }

            // CDN responded but had no valid data (unlikely)
            _logger.LogDebug("CDN responded but contains no release data");
            return (true, null);
        }
        catch (HttpRequestException ex)
        {
            // Network error - CDN is unreachable
            _logger.LogWarning(ex, "Generals Online CDN is unreachable");
            return (false, null);
        }
        catch (Exception ex)
        {
            // Other errors (parsing, etc.) - treat as CDN issue
            _logger.LogWarning(ex, "Failed to query Generals Online CDN");
            return (false, null);
        }
    }

    /// <summary>
    /// Creates a GeneralsOnlineRelease from a full API response (manifest.json).
    /// </summary>
    /// <param name="apiResponse">The API response.</param>
    /// <returns>A fully populated GeneralsOnlineRelease.</returns>
    private GeneralsOnlineRelease CreateReleaseFromApiResponse(GeneralsOnlineApiResponse apiResponse)
    {
        var versionDate = ParseVersionDate(apiResponse.Version) ?? DateTime.Now;

        return new GeneralsOnlineRelease
        {
            Version = apiResponse.Version,
            VersionDate = versionDate,
            ReleaseDate = versionDate,
            PortableUrl = apiResponse.DownloadUrl,
            PortableSize = apiResponse.Size,
            Changelog = apiResponse.ReleaseNotes ?? $"Generals Online {apiResponse.Version}",
        };
    }

    /// <summary>
    /// Creates a GeneralsOnlineRelease from a version string (latest.txt fallback).
    /// Constructs URLs and uses default sizes since full API data is unavailable.
    /// </summary>
    /// <param name="version">The version string (e.g., "101525_QFE5").</param>
    /// <returns>A GeneralsOnlineRelease with constructed URLs.</returns>
    private GeneralsOnlineRelease CreateReleaseFromVersion(string version)
    {
        var versionDate = ParseVersionDate(version) ?? DateTime.Now;

        return new GeneralsOnlineRelease
        {
            Version = version,
            VersionDate = versionDate,
            ReleaseDate = versionDate,
            PortableUrl = $"{GeneralsOnlineConstants.ReleasesUrl}/GeneralsOnline_portable_{version}{GeneralsOnlineConstants.PortableExtension}",
            PortableSize = null, // Size unknown when using latest.txt fallback
            Changelog = $"Generals Online {version}",
        };
    }

    /// <summary>
    /// Parses a version string (MMDDYY_QFE#) to extract the date.
    /// </summary>
    /// <param name="version">The version string.</param>
    /// <returns>The parsed date, or null if parsing fails.</returns>
    private DateTime? ParseVersionDate(string version)
    {
        try
        {
            // Split on underscore to separate date from QFE portion
            var parts = version.Split(new[] { GeneralsOnlineConstants.QfeSeparator }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 1)
            {
                _logger.LogWarning("Failed to parse version date from: {Version} - invalid format", version);
                return null;
            }

            var datePart = parts[0];
            if (datePart.Length != 6)
            {
                _logger.LogWarning("Failed to parse version date from: {Version} - invalid date length", version);
                return null;
            }

            var month = int.Parse(datePart.Substring(0, 2));
            var day = int.Parse(datePart.Substring(2, 2));
            var year = 2000 + int.Parse(datePart.Substring(4, 2));

            return new DateTime(year, month, day);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse version date from: {Version}", version);
            return null;
        }
    }

    private ContentSearchResult CreateSearchResult(GeneralsOnlineRelease release)
    {
        var searchResult = new ContentSearchResult
        {
            Id = $"GeneralsOnline_{release.Version}",
            Name = GeneralsOnlineConstants.ContentName,
            Description = release.Changelog ?? GeneralsOnlineConstants.Description,
            Version = release.Version,
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
            ProviderName = SourceName,
            AuthorName = GeneralsOnlineConstants.PublisherName,
            IconUrl = GeneralsOnlineConstants.IconUrl,
            LastUpdated = release.ReleaseDate,
            DownloadSize = release.PortableSize ?? 0,
            RequiresResolution = true,
            ResolverId = GeneralsOnlineConstants.ResolverId,
            SourceUrl = GeneralsOnlineConstants.DownloadPageUrl,
        };

        foreach (var tag in GeneralsOnlineConstants.Tags)
        {
            searchResult.Tags.Add(tag);
        }

        searchResult.SetData(release);

        return searchResult;
    }
}
