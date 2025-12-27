using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GeneralsOnline;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services.GeneralsOnline;

/// <summary>
/// Parser for Generals Online JSON-based catalogs and manifest responses.
/// </summary>
public class GeneralsOnlineJsonCatalogParser(ILogger<GeneralsOnlineJsonCatalogParser> logger) : ICatalogParser
{
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
            logger.LogInformation("Parsing Generals Online catalog");

            if (string.IsNullOrWhiteSpace(catalogContent))
            {
                logger.LogWarning("Catalog content is empty");
                return Task.FromResult(OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess(
                        []));
            }

            // Parse the wrapper to determine source type
            using var doc = JsonDocument.Parse(catalogContent);
            var root = doc.RootElement;

            if (!root.TryGetProperty("source", out var sourceElement))
            {
                logger.LogError("Missing 'source' property in catalog metadata");
                return Task.FromResult(OperationResult<IEnumerable<ContentSearchResult>>.CreateFailure(
                    "Invalid catalog data format: missing source metadata"));
            }

            var source = sourceElement.GetString();
            GeneralsOnlineReleaseModel? release = null;

            if (source == "manifest")
            {
                // Parse full manifest.json response
                if (root.TryGetProperty("data", out var dataElement))
                {
                    var apiResponse = JsonSerializer.Deserialize<GeneralsOnlineApiResponse>(
                        dataElement.GetRawText());

                    if (apiResponse != null && !string.IsNullOrEmpty(apiResponse.Version))
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
                return Task.FromResult(OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess(
                        []));
            }

            // Create search result from release
            var searchResult = CreateSearchResult(release, provider);

            return Task.FromResult(OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess(
                    [searchResult]));
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed to parse Generals Online catalog JSON");
            return Task.FromResult(OperationResult<IEnumerable<ContentSearchResult>>.CreateFailure(
                    $"JSON parsing failed: {ex.Message}"));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to parse Generals Online catalog");
            return Task.FromResult(OperationResult<IEnumerable<ContentSearchResult>>.CreateFailure(
                    $"Catalog parsing failed: {ex.Message}"));
        }
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

            var month = int.Parse(datePart[..2]);
            var day = int.Parse(datePart[2..4]);
            var year = 2000 + int.Parse(datePart[4..6]);

            return new DateTime(year, month, day);
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
        GeneralsOnlineReleaseModel release,
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

    /// <summary>
    /// Creates a GeneralsOnlineReleaseModel from a full API response (manifest.json).
    /// </summary>
    private static GeneralsOnlineReleaseModel CreateReleaseFromApiResponse(GeneralsOnlineApiResponse apiResponse)
    {
        var versionDate = ParseVersionDate(apiResponse.Version) ?? DateTime.Now;

        return new GeneralsOnlineReleaseModel
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
    /// Creates a GeneralsOnlineReleaseModel from a version string (latest.txt fallback).
    /// Constructs download URL using provider configuration.
    /// </summary>
    private static GeneralsOnlineReleaseModel CreateReleaseFromVersion(string version, ProviderDefinition provider)
    {
        var versionDate = ParseVersionDate(version) ?? DateTime.Now;
        var releasesUrl = provider.Endpoints.GetEndpoint("releasesUrl");

        return new GeneralsOnlineReleaseModel
        {
            Version = version,
            VersionDate = versionDate,
            ReleaseDate = versionDate,
            PortableUrl = $"{releasesUrl}/GeneralsOnline_portable_{version}{GeneralsOnlineConstants.PortableExtension}",
            PortableSize = null, // Size unknown when using latest.txt fallback
            Changelog = $"Generals Online {version}",
        };
    }
}
