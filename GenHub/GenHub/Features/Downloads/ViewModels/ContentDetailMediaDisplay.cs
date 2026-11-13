using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Models.Parsers;
using System;

namespace GenHub.Features.Downloads.ViewModels;

/// <summary>
/// Resolves how gallery media items are presented without side effects.
/// Direct videos stream in the in-app player; embed pages open in the system browser.
/// A known file type always wins: image files never go to the video player and
/// direct video files never go to the image viewer. Extensionless hosted files
/// carry no file type, so they are only treated as videos in a video context;
/// in an image context (summary screenshots, preview images) they show as images.
/// </summary>
public static class ContentDetailMediaDisplay
{
    /// <summary>
    /// Resolves how a gallery media item should be presented without side effects.
    /// Bare string URLs are treated as images unless they name a direct video file,
    /// because the summary gallery and preview-image lists pass screenshots as strings.
    /// </summary>
    /// <param name="item">The image, video, or URL to resolve.</param>
    /// <returns>The presentation decision for the item.</returns>
    public static MediaDisplayDecision Resolve(object? item)
    {
        return Resolve(item, inVideoContext: false);
    }

    /// <summary>
    /// Resolves how a gallery media item should be presented without side effects.
    /// </summary>
    /// <param name="item">The image, video, or URL to resolve.</param>
    /// <param name="inVideoContext">
    /// True when the caller presents the item as a video (for example a preview-video
    /// list). Only then may an extensionless hosted file be routed to the video player.
    /// </param>
    /// <returns>The presentation decision for the item.</returns>
    public static MediaDisplayDecision Resolve(object? item, bool inVideoContext)
    {
        if (item is Image img)
        {
            var imageUrl = img.FullSizeUrl ?? img.ThumbnailUrl;
            if (!MediaFileHelper.IsRemoteHttpUrl(imageUrl))
            {
                return new MediaDisplayDecision(MediaDisplayAction.None, null);
            }

            if (MediaFileHelper.IsEmbedVideoPageUrl(imageUrl))
            {
                return new MediaDisplayDecision(MediaDisplayAction.OpenExternally, imageUrl);
            }

            return MediaFileHelper.IsDirectVideoFileUrl(imageUrl)
                ? new MediaDisplayDecision(MediaDisplayAction.PlayVideo, imageUrl)
                : new MediaDisplayDecision(MediaDisplayAction.ShowImage, imageUrl);
        }

        if (item is Video vid)
        {
            return ResolveVideoDisplay(vid.EmbedUrl, vid.ThumbnailUrl);
        }

        if (item is string url && MediaFileHelper.IsRemoteHttpUrl(url))
        {
            return ResolveUrl(url, inVideoContext);
        }

        return new MediaDisplayDecision(MediaDisplayAction.None, null);
    }

    private static MediaDisplayDecision ResolveUrl(string url, bool inVideoContext)
    {
        if (MediaFileHelper.IsEmbedVideoPageUrl(url))
        {
            return new MediaDisplayDecision(MediaDisplayAction.OpenExternally, url);
        }

        if (MediaFileHelper.IsDirectVideoFileUrl(url))
        {
            return new MediaDisplayDecision(MediaDisplayAction.PlayVideo, url);
        }

        if (MediaFileHelper.IsImageFile(url))
        {
            return new MediaDisplayDecision(MediaDisplayAction.ShowImage, url);
        }

        if (MediaFileHelper.IsExtensionlessHostedFileUrl(url))
        {
            return inVideoContext
                ? new MediaDisplayDecision(MediaDisplayAction.PlayVideo, url)
                : new MediaDisplayDecision(MediaDisplayAction.ShowImage, url);
        }

        return inVideoContext
            ? new MediaDisplayDecision(MediaDisplayAction.OpenExternally, url)
            : new MediaDisplayDecision(MediaDisplayAction.ShowImage, url);
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

            if (MediaFileHelper.IsImageFile(targetUrl))
            {
                return new MediaDisplayDecision(MediaDisplayAction.ShowImage, targetUrl);
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
