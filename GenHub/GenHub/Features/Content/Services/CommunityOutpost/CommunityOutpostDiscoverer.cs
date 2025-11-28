using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services.CommunityOutpost;

/// <summary>
/// Discovers content from Community Outpost (legi.cc).
/// </summary>
/// <param name="httpClientFactory">HTTP client factory.</param>
/// <param name="logger">Logger instance.</param>
public class CommunityOutpostDiscoverer(
    IHttpClientFactory httpClientFactory,
    ILogger<CommunityOutpostDiscoverer> logger) : IContentDiscoverer
{
    /// <inheritdoc/>
    public string SourceName => CommunityOutpostConstants.PublisherType;

    /// <inheritdoc/>
    public string Description => CommunityOutpostConstants.DiscovererDescription;

    /// <inheritdoc/>
    public bool IsEnabled => true;

    /// <inheritdoc/>
    public ContentSourceCapabilities Capabilities =>
        ContentSourceCapabilities.RequiresDiscovery |
        ContentSourceCapabilities.SupportsPackageAcquisition;

    /// <summary>
    /// Gets the provider ID for registration.
    /// </summary>
    public string ProviderId => CommunityOutpostConstants.PublisherId;

    /// <inheritdoc/>
    public async Task<OperationResult<IEnumerable<ContentSearchResult>>> DiscoverAsync(
        ContentSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogInformation("Discovering content from Community Outpost...");

            using var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(30);
            var response = await client.GetStringAsync(CommunityOutpostConstants.PatchPageUrl, cancellationToken);

            // Find the weekly zip link
            var match = Regex.Match(response, CommunityOutpostConstants.PatchZipLinkPattern);

            if (!match.Success)
            {
                logger.LogWarning("No weekly patch link found on Community Outpost page");
                return OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess(Enumerable.Empty<ContentSearchResult>());
            }

            var hrefValue = match.Groups[1].Value;

            var downloadUrl = hrefValue.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? hrefValue
                : $"{CommunityOutpostConstants.PatchPageUrl.TrimEnd('/')}/{hrefValue.TrimStart('/')}";

            var fileName = System.IO.Path.GetFileName(hrefValue);

            // Extract date from filename (e.g., "generalszh-weekly-2025-11-07-with-camera-fix.zip" -> "2025-11-07")
            var dateMatch = Regex.Match(fileName, @"(\d{4}-\d{2}-\d{2})");
            var dateString = dateMatch.Success
                ? dateMatch.Groups[1].Value
                : DateTime.Now.ToString("yyyy-MM-dd"); // Fallback only if no date found

            logger.LogInformation("Found Community Outpost patch: {Filename} (Date: {Date})", fileName, dateString);

            var result = new ContentSearchResult
            {
                Id = $"{CommunityOutpostConstants.PublisherId}.{dateString}",
                Name = CommunityOutpostConstants.ContentName,
                Description = string.Format(CommunityOutpostConstants.DescriptionTemplate, dateString),
                Version = dateString,
                ContentType = ContentType.Patch,
                TargetGame = GameType.ZeroHour,
                ProviderName = SourceName,
                AuthorName = CommunityOutpostConstants.PublisherName,
                SourceUrl = downloadUrl,
                RequiresResolution = true,
                ResolverId = CommunityOutpostConstants.PublisherId,
                LastUpdated = dateMatch.Success
                    ? DateTime.Parse(dateString)
                    : DateTime.Now,
            };

            foreach (var tag in CommunityOutpostConstants.PatchTags)
            {
                result.Tags.Add(tag);
            }

            result.ResolverMetadata.Add("filename", fileName);
            result.ResolverMetadata.Add("publishDate", dateString);

            return OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess(new[] { result });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to discover Community Outpost content");
            return OperationResult<IEnumerable<ContentSearchResult>>.CreateFailure($"Discovery failed: {ex.Message}");
        }
    }
}
