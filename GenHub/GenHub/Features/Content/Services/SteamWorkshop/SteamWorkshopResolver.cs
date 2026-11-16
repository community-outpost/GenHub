using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ParsedContentDetails = GenHub.Core.Models.Content.ParsedContentDetails;

namespace GenHub.Features.Content.Services.SteamWorkshop;

/// <summary>
/// Resolves Steam Workshop items into content manifests by parsing the public file details page anonymously.
/// </summary>
/// <param name="httpClientFactory">Factory for creating HTTP clients.</param>
/// <param name="manifestFactory">The Steam Workshop manifest factory.</param>
/// <param name="logger">The logger.</param>
public class SteamWorkshopResolver(
    IHttpClientFactory httpClientFactory,
    SteamWorkshopManifestFactory manifestFactory,
    ILogger<SteamWorkshopResolver> logger) : IContentResolver
{
    /// <inheritdoc />
    public string ResolverId => SteamWorkshopConstants.ResolverId;

    /// <inheritdoc />
    public async Task<OperationResult<ContentManifest>> ResolveAsync(
        ContentSearchResult discoveredItem,
        CancellationToken cancellationToken = default)
    {
        if (discoveredItem == null)
        {
            return OperationResult<ContentManifest>.CreateFailure("Discovered item cannot be null.");
        }

        var publishedFileId = ExtractPublishedFileId(discoveredItem);
        if (string.IsNullOrWhiteSpace(publishedFileId))
        {
            return OperationResult<ContentManifest>.CreateFailure("The workshop item ID is missing.");
        }

        try
        {
            logger.LogInformation("Resolving Steam Workshop content {PublishedFileId}", publishedFileId);

            var detailsResult = await FetchDetailsAsync(discoveredItem, publishedFileId, cancellationToken).ConfigureAwait(false);
            if (!detailsResult.Success || detailsResult.Data == null)
            {
                return OperationResult<ContentManifest>.CreateFailure(detailsResult.Errors);
            }

            var manifest = await manifestFactory.CreateManifestAsync(detailsResult.Data, cancellationToken).ConfigureAwait(false);
            ApplyResolutionMetadata(manifest, discoveredItem, publishedFileId);
            ApplyResolvedDisplayData(discoveredItem, detailsResult.Data);

            logger.LogInformation(
                "Successfully resolved Steam Workshop content: {ManifestId} - {Name}",
                manifest.Id.Value,
                manifest.Name);

            return OperationResult<ContentManifest>.CreateSuccess(manifest);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "HTTP error while resolving Steam Workshop item {PublishedFileId}", publishedFileId);
            return OperationResult<ContentManifest>.CreateFailure($"Failed to fetch content: {ex.Message}");
        }
        catch (IOException ex)
        {
            logger.LogError(ex, "Network error while resolving Steam Workshop item {PublishedFileId}", publishedFileId);
            return OperationResult<ContentManifest>.CreateFailure($"Failed to fetch content: {ex.Message}");
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Timeout while resolving Steam Workshop item {PublishedFileId}", publishedFileId);
            return OperationResult<ContentManifest>.CreateFailure("Resolution timed out. Check your connection and try again.");
        }
    }

    private static string? ExtractPublishedFileId(ContentSearchResult discoveredItem)
    {
        if (discoveredItem.ResolverMetadata.TryGetValue(SteamWorkshopConstants.PublishedFileIdMetadataKey, out var stored)
            && !string.IsNullOrWhiteSpace(stored))
        {
            return stored;
        }

        if (SteamWorkshopHelper.TryExtractPublishedFileId(discoveredItem.SourceUrl, out var fromUrl))
        {
            return fromUrl;
        }

        return SteamWorkshopHelper.TryExtractPublishedFileIdFromContentId(discoveredItem.Id, out var fromId)
            ? fromId
            : null;
    }

    private static GameType ResolveGame(ContentSearchResult discoveredItem, int? reportedAppId = null)
    {
        if (reportedAppId is SteamWorkshopConstants.GeneralsAppId or SteamWorkshopConstants.ZeroHourAppId)
        {
            return SteamWorkshopHelper.GameForAppId(reportedAppId.Value);
        }

        if (discoveredItem.ResolverMetadata.TryGetValue(SteamWorkshopConstants.AppIdMetadataKey, out var appIdText)
            && int.TryParse(appIdText, out var appId))
        {
            return SteamWorkshopHelper.GameForAppId(appId);
        }

        if (reportedAppId is > 0)
        {
            return SteamWorkshopHelper.GameForAppId(reportedAppId.Value);
        }

        return discoveredItem.TargetGame != GameType.Unknown ? discoveredItem.TargetGame : GameType.ZeroHour;
    }

    /// <summary>
    /// Determines whether the item currently carries a placeholder description.
    /// </summary>
    /// <param name="item">The content search result.</param>
    /// <returns>True if the description is missing, template-based, or flagged as placeholder.</returns>
    private static bool IsPlaceholderDescription(ContentSearchResult item)
    {
        return string.IsNullOrWhiteSpace(item.Description) ||
            string.Equals(item.Description, SteamWorkshopConstants.MapDescriptionTemplate, StringComparison.Ordinal) ||
            (item.ResolverMetadata.TryGetValue(SteamWorkshopConstants.PlaceholderDescriptionMetadataKey, out var placeholderFlag) &&
             string.Equals(placeholderFlag, "true", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Echoes resolved display data back onto the discovered item without clobbering
    /// real discovery data. Lets detail views hydrate from the passed item after
    /// resolution instead of re-parsing the workshop page.
    /// </summary>
    /// <param name="discoveredItem">The item that was resolved.</param>
    /// <param name="details">The resolved details.</param>
    private static void ApplyResolvedDisplayData(ContentSearchResult discoveredItem, ParsedContentDetails details)
    {
        ApplyResolvedTextAndMedia(discoveredItem, details);
        ApplyResolvedScreenshots(discoveredItem, details);
        ApplyResolvedTags(discoveredItem, details);
        ApplyResolvedMetrics(discoveredItem, details);
    }

    private static void ApplyResolvedTextAndMedia(ContentSearchResult discoveredItem, ParsedContentDetails details)
    {
        var shouldReplaceDescription = IsPlaceholderDescription(discoveredItem) ||
            (!string.IsNullOrWhiteSpace(details.Description) &&
             !string.IsNullOrWhiteSpace(discoveredItem.Description) &&
             details.Description.Length > discoveredItem.Description.Length);

        if (!string.IsNullOrWhiteSpace(details.Description) && shouldReplaceDescription)
        {
            discoveredItem.Description = details.Description;
            discoveredItem.ResolverMetadata.Remove(SteamWorkshopConstants.PlaceholderDescriptionMetadataKey);
        }

        if (!string.IsNullOrWhiteSpace(details.Author) &&
            (string.IsNullOrWhiteSpace(discoveredItem.AuthorName) ||
             string.Equals(discoveredItem.AuthorName, SteamWorkshopConstants.DefaultAuthorName, StringComparison.Ordinal)))
        {
            discoveredItem.AuthorName = details.Author;
        }

        if (!string.IsNullOrWhiteSpace(details.PreviewImage) && string.IsNullOrWhiteSpace(discoveredItem.IconUrl))
        {
            discoveredItem.IconUrl = details.PreviewImage;
        }
    }

    private static void ApplyResolvedScreenshots(ContentSearchResult discoveredItem, ParsedContentDetails details)
    {
        var screenshots = new List<string>();
        if (!string.IsNullOrWhiteSpace(details.PreviewImage))
        {
            screenshots.Add(details.PreviewImage);
        }

        if (details.Screenshots != null)
        {
            screenshots.AddRange(details.Screenshots.Where(url => !string.IsNullOrWhiteSpace(url)));
        }

        foreach (var screenshot in screenshots
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(s => !discoveredItem.ScreenshotUrls.Contains(s, StringComparer.OrdinalIgnoreCase)))
        {
            discoveredItem.ScreenshotUrls.Add(screenshot);
        }
    }

    private static void ApplyResolvedTags(ContentSearchResult discoveredItem, ParsedContentDetails details)
    {
        if (details.Tags == null)
        {
            return;
        }

        foreach (var tag in details.Tags.Where(tag => !string.IsNullOrWhiteSpace(tag)))
        {
            var normalized = SteamWorkshopHelper.NormalizeWorkshopTag(tag);
            if (!discoveredItem.Tags.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                discoveredItem.Tags.Add(normalized);
            }
        }

        SteamWorkshopHelper.ApplyWorkshopTagBadges(discoveredItem, details.Tags);
        ContentCardBadgeHelper.PromoteFromTags(discoveredItem);
    }

    private static void ApplyResolvedMetrics(ContentSearchResult discoveredItem, ParsedContentDetails details)
    {
        if (details.FileSize > 0 && discoveredItem.DownloadSize <= 0)
        {
            discoveredItem.DownloadSize = details.FileSize;
        }

        if (details.DownloadCount > 0 && discoveredItem.DownloadCount <= 0)
        {
            discoveredItem.DownloadCount = details.DownloadCount;
        }

        var effectiveLastUpdated = details.LastUpdated
            ?? (details.SubmissionDate > DateTime.MinValue ? details.SubmissionDate : (DateTime?)null);
        if (effectiveLastUpdated.HasValue)
        {
            discoveredItem.LastUpdated = effectiveLastUpdated.Value;
        }

        if (!string.IsNullOrWhiteSpace(details.CreatorAvatarUrl))
        {
            discoveredItem.Metadata[SteamWorkshopConstants.CreatorAvatarUrlMetadataKey] = details.CreatorAvatarUrl;
        }
    }

    private static void ApplyResolutionMetadata(ContentManifest manifest, ContentSearchResult discoveredItem, string publishedFileId)
    {
        if (string.IsNullOrEmpty(manifest.OriginalProviderName))
        {
            manifest.OriginalProviderName = SteamWorkshopConstants.PublisherPrefix;
        }

        if (string.IsNullOrEmpty(manifest.OriginalContentId))
        {
            string? parentId = null;
            if (discoveredItem.ResolverMetadata.TryGetValue(ContentConstants.ParentContentIdMetadataKey, out var storedParentId)
                && SteamWorkshopHelper.TryExtractPublishedFileIdFromContentId(storedParentId, out _))
            {
                parentId = storedParentId;
            }

            manifest.OriginalContentId = parentId
                ?? string.Format(SteamWorkshopConstants.ContentIdFormat, publishedFileId);
        }

        if (!string.IsNullOrWhiteSpace(discoveredItem.SourceUrl) && manifest.Publisher != null)
        {
            manifest.Publisher.SupportUrl = discoveredItem.SourceUrl;
        }
    }

    private static int ParseSubscriberCount(IReadOnlyDictionary<string, string>? stats)
    {
        if (stats == null)
        {
            return 0;
        }

        foreach (var (label, value) in stats)
        {
            if (!label.Contains(SteamWorkshopConstants.DetailsStatSubscriberFragment, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var digits = new string(value.Where(char.IsDigit).ToArray());
            if (int.TryParse(digits, out var count))
            {
                return count;
            }
        }

        return 0;
    }

    private async Task<OperationResult<ParsedContentDetails>> FetchDetailsAsync(
        ContentSearchResult discoveredItem,
        string publishedFileId,
        CancellationToken cancellationToken)
    {
        var sourceUrl = !string.IsNullOrWhiteSpace(discoveredItem.SourceUrl)
            ? discoveredItem.SourceUrl
            : SteamWorkshopHelper.BuildFileDetailsUrl(publishedFileId);
        var client = httpClientFactory.CreateClient(SteamWorkshopConstants.PublisherType);
        var html = await client.GetStringAsync(sourceUrl, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        var parsed = SteamWorkshopDetailsParser.Parse(html);
        var title = parsed.Title ?? discoveredItem.Name;
        var description = SteamWorkshopHelper.NormalizeHtmlDescription(parsed.DescriptionHtml);
        if (string.IsNullOrWhiteSpace(description) && !string.IsNullOrWhiteSpace(discoveredItem.Description)
            && !IsPlaceholderDescription(discoveredItem))
        {
            description = discoveredItem.Description;
        }

        var game = ResolveGame(discoveredItem, parsed.AppId);
        var tags = parsed.Tags
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(SteamWorkshopHelper.NormalizeWorkshopTag)
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var (contentType, _) = SteamWorkshopHelper.InferContentType(title, description, tags);
        var screenshots = (parsed.Screenshots ?? [])
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return OperationResult<ParsedContentDetails>.CreateSuccess(new ParsedContentDetails(
            Name: title,
            Description: description,
            Author: parsed.Author ?? discoveredItem.AuthorName ?? SteamWorkshopConstants.DefaultAuthorName,
            PreviewImage: parsed.PreviewUrl ?? discoveredItem.IconUrl ?? string.Empty,
            Screenshots: screenshots,
            FileSize: FileSizeFormatter.ParseToBytes(parsed.FileSizeText),
            DownloadCount: ParseSubscriberCount(parsed.Stats),
            SubmissionDate: parsed.Posted ?? DateTime.MinValue,
            DownloadUrl: SteamWorkshopHelper.BuildFileDetailsUrl(publishedFileId),
            TargetGame: game,
            ContentType: contentType,
            FileType: SteamWorkshopConstants.MapFileExtension,
            Rating: 0f,
            RefererUrl: SteamWorkshopHelper.BuildFileDetailsUrl(publishedFileId),
            AdditionalFiles: null,
            Tags: tags,
            CreatorAvatarUrl: parsed.CreatorAvatarUrl,
            LastUpdated: parsed.Updated ?? parsed.Posted));
    }
}
