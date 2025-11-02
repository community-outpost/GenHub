using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Content;
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
/// Discovers Generals Online releases by querying the CDN API or falling back to mock data.
/// Supports both manifest.json API and latest.txt polling for release discovery.
/// </summary>
public class GeneralsOnlineDiscoverer : IContentDiscoverer
{
    private readonly ILogger _logger;
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="GeneralsOnlineDiscoverer"/> class.
    /// </summary>
    /// <param name="logger">The logger for diagnostic information.</param>
    public GeneralsOnlineDiscoverer(ILogger logger)
    {
        _logger = logger;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
    }

    /// <inheritdoc />
    public string SourceName => "Generals Online";

    /// <inheritdoc />
    public string Description => "Discovers Generals Online releases from official CDN";

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public ContentSourceCapabilities Capabilities =>
        ContentSourceCapabilities.RequiresDiscovery |
        ContentSourceCapabilities.SupportsPackageAcquisition;

    /// <summary>
    /// Discovers Generals Online releases from CDN API or mock data.
    /// Tries manifest.json first, then latest.txt, then falls back to mock release.
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
            // Try to get release from API, fall back to mock
            var release = await TryGetReleaseFromApiAsync(cancellationToken)
                          ?? await GetMockReleaseAsync();

            // Filter by search query if provided
            if (!string.IsNullOrWhiteSpace(query.SearchTerm) &&
                !release.Version.Contains(query.SearchTerm, StringComparison.OrdinalIgnoreCase) &&
                !"generals online".Contains(query.SearchTerm, StringComparison.OrdinalIgnoreCase))
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
    /// Disposes resources used by the discoverer.
    /// </summary>
    public void Dispose()
    {
        _httpClient?.Dispose();
    }

    /// <summary>
    /// Attempts to get release information from the Generals Online CDN API.
    /// Tries manifest.json first for full release details, then latest.txt for version-only.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Release information if API is available, null otherwise.</returns>
    private async Task<GeneralsOnlineRelease?> TryGetReleaseFromApiAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("Attempting to query Generals Online CDN API");

            // Try manifest.json first (full API response)
            var manifestResponse = await _httpClient.GetAsync(GeneralsOnlineConstants.ManifestApiUrl, cancellationToken);
            if (manifestResponse.IsSuccessStatusCode)
            {
                var json = await manifestResponse.Content.ReadAsStringAsync(cancellationToken);
                var apiResponse = JsonSerializer.Deserialize<GeneralsOnlineApiResponse>(json);

                if (apiResponse != null && !string.IsNullOrEmpty(apiResponse.Version))
                {
                    _logger.LogInformation("Retrieved release from manifest.json API: {Version}", apiResponse.Version);
                    return CreateReleaseFromApiResponse(apiResponse);
                }
            }

            // Fall back to latest.txt (simple version polling)
            var versionResponse = await _httpClient.GetAsync(GeneralsOnlineConstants.LatestVersionUrl, cancellationToken);
            if (versionResponse.IsSuccessStatusCode)
            {
                var version = await versionResponse.Content.ReadAsStringAsync(cancellationToken);
                version = version?.Trim();

                if (!string.IsNullOrEmpty(version))
                {
                    _logger.LogInformation("Retrieved version from latest.txt: {Version}", version);
                    return CreateReleaseFromVersion(version);
                }
            }

            _logger.LogDebug("CDN API endpoints not available, will use mock data");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query Generals Online CDN, will use mock data");
            return null;
        }
    }

    /// <summary>
    /// Creates a GeneralsOnlineRelease from a full API response (manifest.json).
    /// </summary>
    /// <param name="apiResponse">The API response.</param>
    /// <returns>A fully populated GeneralsOnlineRelease.</returns>
    private GeneralsOnlineRelease CreateReleaseFromApiResponse(GeneralsOnlineApiResponse apiResponse)
    {
        var versionDate = ParseVersionDate(apiResponse.Version);

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
        var versionDate = ParseVersionDate(version);

        return new GeneralsOnlineRelease
        {
            Version = version,
            VersionDate = versionDate,
            ReleaseDate = versionDate,
            PortableUrl = $"{GeneralsOnlineConstants.ReleasesUrl}/GeneralsOnline_portable_{version}{GeneralsOnlineConstants.PortableExtension}",
            PortableSize = GeneralsOnlineConstants.DefaultPortableSize,
            Changelog = $"Generals Online {version}",
        };
    }

    /// <summary>
    /// Parses a version string (MMDDYY_QFE#) to extract the date.
    /// </summary>
    /// <param name="version">The version string.</param>
    /// <returns>The parsed date, or current date if parsing fails.</returns>
    private DateTime ParseVersionDate(string version)
    {
        try
        {
            var parts = version.Split('_');
            if (parts.Length < 1)
            {
                return DateTime.Now;
            }

            var datePart = parts[0];
            if (datePart.Length != 6)
            {
                return DateTime.Now;
            }

            var month = int.Parse(datePart.Substring(0, 2));
            var day = int.Parse(datePart.Substring(2, 2));
            var year = 2000 + int.Parse(datePart.Substring(4, 2));

            return new DateTime(year, month, day);
        }
        catch
        {
            return DateTime.Now;
        }
    }

    private async Task<GeneralsOnlineRelease> GetMockReleaseAsync()
    {
        _logger.LogInformation("Using mock Generals Online release data (CDN not yet available)");

        return await Task.FromResult(new GeneralsOnlineRelease
        {
            Version = "101525_QFE5",
            VersionDate = new DateTime(2025, 10, 15),
            ReleaseDate = new DateTime(2025, 10, 15),
            PortableUrl = $"{GeneralsOnlineConstants.CdnBaseUrl}/GeneralsOnline_portable_101525_QFE5{GeneralsOnlineConstants.PortableExtension}",
            PortableSize = GeneralsOnlineConstants.DefaultPortableSize,
            Changelog = "QFE5 Release - Improved stability and networking performance",
        });
    }

    private ContentSearchResult CreateSearchResult(GeneralsOnlineRelease release)
    {
        var searchResult = new ContentSearchResult
        {
            Id = $"GeneralsOnline_{release.Version}",
            Name = GeneralsOnlineConstants.ContentName,
            Description = GeneralsOnlineConstants.Description,
            Version = release.Version,
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            ProviderName = SourceName,
            AuthorName = GeneralsOnlineConstants.PublisherName,
            IconUrl = GeneralsOnlineConstants.IconUrl,
            LastUpdated = release.ReleaseDate,
            DownloadSize = release.PortableSize,
            RequiresResolution = true,
            ResolverId = "GeneralsOnline",
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
