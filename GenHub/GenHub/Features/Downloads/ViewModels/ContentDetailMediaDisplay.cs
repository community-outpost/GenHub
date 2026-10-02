using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Models.Parsers;
using System;

namespace GenHub.Features.Downloads.ViewModels;

/// <summary>
/// Resolves how gallery media items are presented without side effects.
/// Direct videos stream in the in-app player; embed pages open in the system browser.
/// </summary>
public static class ContentDetailMediaDisplay
{
    /// <summary>
    /// Resolves how a gallery media item should be presented without side effects.
    /// </summary>
    /// <param name="item">The image, video, or URL to resolve.</param>
    /// <returns>The presentation decision for the item.</returns>
    public static MediaDisplayDecision Resolve(object? item)
    {
        if (item is Image img)
        {
            var imageUrl = img.FullSizeUrl ?? img.ThumbnailUrl;
            return MediaFileHelper.IsRemoteHttpUrl(imageUrl)
                ? new MediaDisplayDecision(MediaDisplayAction.ShowImage, imageUrl)
                : new MediaDisplayDecision(MediaDisplayAction.None, null);
        }

        if (item is Video vid)
        {
            return ResolveVideoDisplay(vid.EmbedUrl, vid.ThumbnailUrl);
        }

        if (item is string url && MediaFileHelper.IsRemoteHttpUrl(url))
        {
            if (MediaFileHelper.IsEmbedVideoPageUrl(url))
            {
                return new MediaDisplayDecision(MediaDisplayAction.OpenExternally, url);
            }

            if (MediaFileHelper.IsDirectVideoFileUrl(url) || MediaFileHelper.IsExtensionlessHostedFileUrl(url))
            {
                return new MediaDisplayDecision(MediaDisplayAction.PlayVideo, url);
            }

            return new MediaDisplayDecision(MediaDisplayAction.ShowImage, url);
        }

        return new MediaDisplayDecision(MediaDisplayAction.None, null);
    }

    private static MediaDisplayDecision ResolveVideoDisplay(string? embedUrl, string? thumbnailUrl)
    {
        if (!string.IsNullOrWhiteSpace(embedUrl) && MediaFileHelper.IsRemoteHttpUrl(embedUrl))
        {
            var targetUrl = NormalizeEmbedUrl(embedUrl);
            if (MediaFileHelper.IsEmbedVideoPageUrl(targetUrl))
            {
                return new MediaDisplayDecision(MediaDisplayAction.OpenExternally, targetUrl);
            }

            if (MediaFileHelper.IsDirectVideoFileUrl(targetUrl) || MediaFileHelper.IsExtensionlessHostedFileUrl(targetUrl))
            {
                return new MediaDisplayDecision(MediaDisplayAction.PlayVideo, targetUrl);
            }

            // Watch pages and other non-media URLs open in the system browser so the
            // player is never handed an HTML page.
            return new MediaDisplayDecision(MediaDisplayAction.OpenExternally, targetUrl);
        }

        return MediaFileHelper.IsRemoteHttpUrl(thumbnailUrl)
            ? new MediaDisplayDecision(MediaDisplayAction.ShowImage, thumbnailUrl)
            : new MediaDisplayDecision(MediaDisplayAction.None, null);
    }

    private static string NormalizeEmbedUrl(string embedUrl)
    {
        if (embedUrl.Contains("/embed/", StringComparison.OrdinalIgnoreCase) &&
            embedUrl.Contains("youtube", StringComparison.OrdinalIgnoreCase))
        {
            var embedParts = embedUrl.Split("/embed/", StringSplitOptions.RemoveEmptyEntries);
            if (embedParts.Length > 1)
            {
                var id = embedParts[1].Split('?')[0];
                if (!string.IsNullOrWhiteSpace(id))
                {
                    return $"{ApiConstants.YouTubeWatchUrlPrefix}{id}";
                }
            }
        }

        return embedUrl;
    }
}
