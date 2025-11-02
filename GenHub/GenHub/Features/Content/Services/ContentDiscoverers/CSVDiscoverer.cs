using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Features.Manifest;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Content.Services.ContentDiscoverers;

/// <summary>
/// CSV content discoverer that discovers content from CSV files.
/// Implements <see cref="IContentDiscoverer"/> for CSV-based content discovery.
/// </summary>
/// <remarks>
/// This class provides discovery capabilities for CSV-based content sources.
/// It creates <see cref="ContentSearchResult"/> objects with metadata pointing to CSV files
/// that can be resolved by the corresponding CSV resolver.
/// The discoverer supports different game types and versions, providing appropriate
/// CSV URLs and metadata for each supported content type.
/// </remarks>
public class CSVDiscoverer(
    ILogger<CSVDiscoverer> logger,
    IConfigurationProviderService configurationProvider,
    HttpClient httpClient) : IContentDiscoverer
{
    
    private readonly IConfigurationProviderService _configurationProvider = configurationProvider;
    private readonly HttpClient _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

    /// <summary>
    /// Gets the resolver identifier for CSV content.
    /// </summary>
    /// <returns>A string identifier used to match this discoverer with appropriate resolvers.</returns>
    /// <remarks>
    /// This identifier is used by the content resolution system to associate discovered
    /// <see cref="ContentSearchResult"/> items with the appropriate resolver implementation.
    /// The value "CSVResolver" indicates that discovered items should be resolved using CSV-specific logic.
    /// </remarks>
    public static string ResolverId => CsvConstants.CsvResolverId;

    /// <inheritdoc />
    public string SourceName => CsvConstants.CsvSourceName;

    /// <inheritdoc />
    public string Description => "Discovers content from CSV files.";

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public ContentSourceCapabilities Capabilities =>
        ContentSourceCapabilities.SupportsManifestGeneration |
        ContentSourceCapabilities.DirectSearch |
        ContentSourceCapabilities.SupportsPackageAcquisition;

    /// <summary>
    /// Discovers content from CSV files based on the search query.
    /// </summary>
    /// <param name="query">The search criteria containing game type and other filters.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A <see cref="OperationResult{T}"/> containing discovered <see cref="ContentSearchResult"/> objects.</returns>
    /// <remarks>
    /// This method examines the <paramref name="query"/> to determine what type of content to discover.
    /// Currently supports discovery of:
    /// <list type="bullet">
    /// <item><see cref="GameType.Generals"/> version 1.08 content</item>
    /// <item><see cref="GameType.ZeroHour"/> version 1.04 content</item>
    /// </list>
    /// For each supported game type, it creates a <see cref="ContentSearchResult"/> with appropriate
    /// metadata including CSV URLs and resolver information. The results can then be resolved
    /// by the corresponding CSV resolver to obtain full <see cref="ContentManifest"/> objects.
    /// Language parameters are normalized to uppercase and included in the manifest ID generation.
    /// </remarks>
    public async Task<OperationResult<IEnumerable<ContentSearchResult>>> DiscoverAsync(
        ContentSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Normalize language to uppercase
            var normalizedLanguage = string.IsNullOrWhiteSpace(query.Language)
                ? "All"
                : query.Language.ToUpperInvariant();

            // Try to load from index.json first
            var indexUrl = CsvConstants.DefaultCsvIndexUrl; // Use default for now
            CsvRegistryIndex? registryIndex = null;

            try
            {
                logger.LogDebug("Attempting to load CSV registry from index.json: {Url}", indexUrl);
                var indexJson = await _httpClient.GetStringAsync(indexUrl, cancellationToken);
                registryIndex = JsonSerializer.Deserialize<CsvRegistryIndex>(indexJson, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                });

                if (registryIndex?.Registries == null || !registryIndex.Registries.Any())
                {
                    logger.LogWarning("Index.json loaded but contains no valid registries");
                    registryIndex = null;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to load index.json from {Url}, falling back to configuration", indexUrl);
            }

            var results = new List<ContentSearchResult>();

            // Generals 1.08
            if (query.TargetGame == GameType.Generals)
            {
                var searchResult = CreateSearchResult("Generals", "1.08", normalizedLanguage, registryIndex);
                if (searchResult != null)
                {
                    results.Add(searchResult);
                }
            }

            // Zero Hour 1.04
            if (query.TargetGame == GameType.ZeroHour)
            {
                var searchResult = CreateSearchResult("ZeroHour", "1.04", normalizedLanguage, registryIndex);
                if (searchResult != null)
                {
                    results.Add(searchResult);
                }
            }

            return OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess(results);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("CSV discovery was cancelled");
            return OperationResult<IEnumerable<ContentSearchResult>>.CreateFailure("Operation cancelled");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "CSV discovery failed");
            return OperationResult<IEnumerable<ContentSearchResult>>.CreateFailure($"Discovery failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Generates a deterministic manifest ID for CSV content.
    /// </summary>
    /// <param name="gameType">The game type (Generals or ZeroHour).</param>
    /// <param name="version">The game version.</param>
    /// <param name="language">The language code (normalized to uppercase).</param>
    /// <returns>A deterministic manifest ID in the format "GameType-Version-Language".</returns>
    private static string GenerateManifestId(string gameType, string version, string language)
    {
        return $"{gameType}-{version}-{language}";
    }

    /// <summary>
    /// Creates a ContentSearchResult for the specified game, using registry data if available.
    /// </summary>
    /// <param name="gameType">The game type (Generals or ZeroHour).</param>
    /// <param name="version">The game version.</param>
    /// <param name="language">The normalized language code.</param>
    /// <param name="registryIndex">The loaded registry index, or null if not available.</param>
    /// <returns>A ContentSearchResult, or null if no suitable registry found.</returns>
    private ContentSearchResult? CreateSearchResult(
        string gameType,
        string version,
        string language,
        CsvRegistryIndex? registryIndex)
    {
        string csvUrl;
        List<string>? supportedLanguages = null;

        // Try to get data from registry index
        if (registryIndex != null)
        {
            var registry = registryIndex.Registries
                .FirstOrDefault(r => r.GameType == gameType && r.Version == version && r.IsActive);

            if (registry != null)
            {
                csvUrl = registry.Url;
                supportedLanguages = registry.Languages;
            }
            else
            {
                logger.LogWarning("No active registry found for {GameType} {Version} in index.json", gameType, version);
                return null;
            }
        }
        else
        {
            // Fallback to default URLs
            csvUrl = gameType == "Generals" ? CsvConstants.DefaultGeneralsCsvUrl : CsvConstants.DefaultZeroHourCsvUrl;
        }

        var manifestId = GenerateManifestId(gameType, version, language);
        var searchResult = new ContentSearchResult
        {
            Id = manifestId,
            Name = gameType == "Generals"
                ? $"Command & Conquer Generals {version} ({language})"
                : $"Command & Conquer Generals: Zero Hour {version} ({language})",
            ResolverId = ResolverId,
        };

        searchResult.ResolverMetadata.Add("game", gameType);
        searchResult.ResolverMetadata.Add("version", version);
        searchResult.ResolverMetadata.Add("language", language);
        searchResult.ResolverMetadata.Add("csvUrl", csvUrl);

        if (supportedLanguages != null)
        {
            searchResult.ResolverMetadata.Add("supportedLanguages", string.Join(",", supportedLanguages));
        }

        return searchResult;
    }
}
