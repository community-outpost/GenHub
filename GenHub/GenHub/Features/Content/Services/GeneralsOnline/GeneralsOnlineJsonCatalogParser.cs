using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GeneralsOnline;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Core.Services.Providers.VersionSchemes;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services.GeneralsOnline;

/// <summary>
/// Parses Generals Online JSON catalog data into content search results.
/// Accepts pre-fetched JSON from the Discoverer in wrapper format containing source type and data.
/// </summary>
public class GeneralsOnlineJsonCatalogParser(
    ILogger<GeneralsOnlineJsonCatalogParser> logger
) : ICatalogParser
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <inheritdoc/>
    public string CatalogFormat => "generalsonline-json-api";

    /// <inheritdoc/>
    public Task<OperationResult<IEnumerable<ContentSearchResult>>> ParseAsync(
        string catalogContent,
        ProviderDefinition provider,
        CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogInformation("Parsing Generals Online catalog data");

            if (string.IsNullOrWhiteSpace(catalogContent))
            {
                logger.LogWarning("Catalog content is empty");
                return Task.FromResult(
                    OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess(
                        []));
            }

            // Parse the wrapper to determine source type
            using var document = JsonDocument.Parse(catalogContent);
            var root = document.RootElement;

            if (!root.TryGetProperty("source", out var sourceElement))
            {
                logger.LogError("Invalid catalog format: missing 'source' property");
                return Task.FromResult(
                    OperationResult<IEnumerable<ContentSearchResult>>.CreateFailure(
                        "Invalid catalog format"));
            }

            var source = sourceElement.GetString();
            GeneralsOnlineRelease? release = null;

            if (source == "manifest")
            {
                // Parse full manifest.json response
                if (root.TryGetProperty("data", out var dataElement))
                {
                    var apiResponse = JsonSerializer.Deserialize<GeneralsOnlineApiResponse>(
                        dataElement.GetRawText(),
                        _jsonOptions);

                    if (apiResponse != null && (!string.IsNullOrEmpty(apiResponse.Version) || !string.IsNullOrEmpty(apiResponse.DownloadUrl)))
                    {
                        release = CreateReleaseFromApiResponse(apiResponse);
                        logger.LogInformation(
                            "Parsed release from manifest.json: {Version}",
                            release.Version);
                    }
                }
            }
            else if (source == "latest")
            {
                // Parse simple version from latest.txt
                if (root.TryGetProperty("version", out var versionElement))
                {
                    var version = versionElement.GetString();
                    if (!string.IsNullOrEmpty(version))
                    {
                        release = CreateReleaseFromVersion(version, provider);
                        logger.LogInformation(
                            "Parsed release from latest.txt: {Version}",
                            release.Version);
                    }
                }
            }
            else
            {
                logger.LogWarning("Unknown catalog source: {Source}", source);
            }

            if (release == null)
            {
                logger.LogInformation("No Generals Online releases found in catalog");
                return Task.FromResult(
                    OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess(
                        []));
            }

            // Create search result from release
            var searchResult = CreateSearchResult(release, provider);

            return Task.FromResult(
                OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess(
                    [searchResult]));
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed to parse Generals Online catalog JSON");
            return Task.FromResult(
                OperationResult<IEnumerable<ContentSearchResult>>.CreateFailure(
                    $"JSON parsing failed: {ex.Message}"));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to parse Generals Online catalog");
            return Task.FromResult(
                OperationResult<IEnumerable<ContentSearchResult>>.CreateFailure(
                    $"Catalog parsing failed: {ex.Message}"));
        }
    }

    /// <summary>
    /// Resolves the canonical release version, extracting the full version (including QFE suffix)
    /// from the download URL if the API response version lacks it.
    /// </summary>
    /// <param name="apiVersion">The version string from the API JSON response.</param>
    /// <param name="downloadUrl">The download URL for the portable package.</param>
    /// <returns>The resolved canonical version string.</returns>
    internal static string ResolveReleaseVersion(string? apiVersion, string? downloadUrl)
    {
        var urlVersion = ExtractVersionFromUrl(downloadUrl);
        if (string.IsNullOrWhiteSpace(urlVersion))
        {
            return !string.IsNullOrWhiteSpace(apiVersion) ? apiVersion : GeneralsOnlineConstants.UnknownVersion;
        }

        if (string.IsNullOrWhiteSpace(apiVersion))
        {
            return urlVersion;
        }

        var scheme = new MmddyyQfeVersionScheme();
        var urlParsed = scheme.TryParse(urlVersion, out var urlContentVersion);
        var apiParsed = scheme.TryParse(apiVersion, out var apiContentVersion);

        if (urlParsed && apiParsed)
        {
            return urlContentVersion > apiContentVersion ? urlVersion : apiVersion;
        }

        if (urlParsed)
        {
            return urlVersion;
        }

        if (apiParsed)
        {
            return apiVersion;
        }

        var urlHasQfe = urlVersion.Contains(GeneralsOnlineConstants.QfeMarkerPrefix, StringComparison.OrdinalIgnoreCase);
        var apiHasQfe = apiVersion.Contains(GeneralsOnlineConstants.QfeMarkerPrefix, StringComparison.OrdinalIgnoreCase);

        if (urlHasQfe && !apiHasQfe)
        {
            return urlVersion;
        }

        return apiVersion;
    }

    /// <summary>
    /// Extracts a version string from a Generals Online download URL or package filename.
    /// e.g. "https://cdn.playgenerals.online/GeneralsOnline_portable_082826_QFE1.zip" -> "082826_QFE1".
    /// </summary>
    /// <param name="downloadUrl">The download URL or archive filename.</param>
    /// <returns>The extracted version string, or null if not found.</returns>
    internal static string? ExtractVersionFromUrl(string? downloadUrl)
    {
        if (string.IsNullOrWhiteSpace(downloadUrl))
        {
            return null;
        }

        string fileName;
        try
        {
            if (Uri.TryCreate(downloadUrl, UriKind.Absolute, out var uri))
            {
                fileName = Path.GetFileNameWithoutExtension(uri.AbsolutePath);
            }
            else
            {
                var urlWithoutQuery = downloadUrl.Split('?')[0];
                fileName = Path.GetFileNameWithoutExtension(urlWithoutQuery);
            }
        }
        catch (ArgumentException)
        {
            return null;
        }

        const string prefix = "GeneralsOnline_portable_";
        var prefixIndex = fileName.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (prefixIndex >= 0)
        {
            var candidate = fileName[(prefixIndex + prefix.Length)..].Trim();
            if (!string.IsNullOrEmpty(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// Creates a GeneralsOnlineRelease from a full API response (manifest.json).
    /// </summary>
    private static GeneralsOnlineRelease CreateReleaseFromApiResponse(GeneralsOnlineApiResponse apiResponse)
    {
        var version = ResolveReleaseVersion(apiResponse.Version, apiResponse.DownloadUrl);
        var versionDate = ParseVersionDate(version) ?? DateTime.UtcNow;

        return new GeneralsOnlineRelease
        {
            Version = version,
            VersionDate = versionDate,
            ReleaseDate = versionDate,
            PortableUrl = apiResponse.DownloadUrl,
            PortableSize = apiResponse.Size,
            Sha256 = apiResponse.Sha256,
            Changelog = apiResponse.ReleaseNotes ?? $"Generals Online {version}",
        };
    }

    /// <summary>
    /// Creates a GeneralsOnlineRelease from a version string (latest.txt fallback).
    /// Constructs download URL using provider configuration.
    /// </summary>
    private static GeneralsOnlineRelease CreateReleaseFromVersion(string version, ProviderDefinition provider)
    {
        var versionDate = ParseVersionDate(version) ?? DateTime.UtcNow;
        var releasesUrl = provider.Endpoints.GetEndpoint("releasesUrl");

        return new GeneralsOnlineRelease
        {
            Version = version,
            VersionDate = versionDate,
            ReleaseDate = versionDate,
            PortableUrl = $"{releasesUrl}/GeneralsOnline_portable_{version}{GeneralsOnlineConstants.PortableExtension}",
            PortableSize = null, // Size unknown when using latest.txt fallback
            Changelog = $"Generals Online {version}",
        };
    }

    /// <summary>
    /// Parses a version string (MMDDYY_QFE#) to extract the date.
    /// </summary>
    private static DateTime? ParseVersionDate(string version)
    {
        try
        {
            var parts = version.Split(
                [GeneralsOnlineConstants.QfeSeparator],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (parts.Length < 1)
            {
                return null;
            }

            var datePart = parts[0];
            if (datePart.Length != 6)
            {
                return null;
            }

            if (!int.TryParse(datePart[..2], out var month) ||
                !int.TryParse(datePart.Substring(2, 2), out var day) ||
                !int.TryParse(datePart[4..], out var yearSuffix))
            {
                return null;
            }

            var year = 2000 + yearSuffix;
            return new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Creates a ContentSearchResult from a release and provider configuration.
    /// </summary>
    private static ContentSearchResult CreateSearchResult(
        GeneralsOnlineRelease release,
        ProviderDefinition provider)
    {
        var downloadPageUrl = provider.Endpoints.GetEndpoint("downloadPageUrl");
        var iconUrl = provider.Endpoints.GetEndpoint("iconUrl");

        var searchResult = new ContentSearchResult
        {
            Id = $"GeneralsOnline_{release.Version}",
            Name = GeneralsOnlineConstants.ContentName,
            Description = release.Changelog ?? provider.Description,
            Version = release.Version,
            ContentType = ContentType.GameClient,
            TargetGame = provider.TargetGame ?? GameType.ZeroHour,
            ProviderName = provider.PublisherType,
            AuthorName = GeneralsOnlineConstants.PublisherName,
            IconUrl = iconUrl ?? string.Empty,
            LastUpdated = release.ReleaseDate,
            DownloadSize = release.PortableSize ?? 0,
            RequiresResolution = true,
            ResolverId = GeneralsOnlineConstants.ResolverId,
            SourceUrl = downloadPageUrl ?? provider.Endpoints.WebsiteUrl ?? string.Empty,
        };

        // Add default tags from provider
        foreach (var tag in provider.DefaultTags)
        {
            if (!searchResult.Tags.Contains(tag))
            {
                searchResult.Tags.Add(tag);
            }
        }

        // Store release data for the Resolver
        searchResult.SetData(release);

        return searchResult;
    }
}
