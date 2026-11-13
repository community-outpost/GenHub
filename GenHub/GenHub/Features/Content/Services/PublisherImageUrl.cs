using GenHub.Core.Constants;
using System;
using System.Text.RegularExpressions;
using System.Web;

namespace GenHub.Features.Content.Services;

/// <summary>
/// Recognizes and repairs image links that publishers paste into custom tab cards.
/// </summary>
public static partial class PublisherImageUrl
{
    /// <summary>
    /// Rewrites a Google Drive file viewer link to the direct download form, which serves the image bytes.
    /// Other links are returned unchanged.
    /// </summary>
    /// <remarks>
    /// A Drive share link such as <c>drive.google.com/file/d/ID/view</c> returns an HTML viewer page, which the
    /// image loader rejects because it is not an image.
    /// </remarks>
    /// <param name="url">The image URL from the catalog.</param>
    /// <returns>The direct image URL.</returns>
    public static string? Normalize(string? url)
    {
        if (!TryGetGoogleDriveFileId(url, out var fileId))
        {
            return url;
        }

        var direct = string.Format(CustomTabConstants.GoogleDriveDirectUrlFormat, Uri.EscapeDataString(fileId));
        var resourceKey = HttpUtility.ParseQueryString(new Uri(url!).Query)[CustomTabConstants.GoogleDriveResourceKeyQueryKey];
        return resourceKey != null && ResourceKeyRegex().IsMatch(resourceKey)
            ? $"{direct}&{CustomTabConstants.GoogleDriveResourceKeyQueryKey}={Uri.EscapeDataString(resourceKey)}"
            : direct;
    }

    /// <summary>
    /// Determines whether a URL is a Google Drive file viewer or open link that <see cref="Normalize"/> rewrites.
    /// </summary>
    /// <param name="url">The URL to check.</param>
    /// <param name="fileId">The Drive file ID when the URL is a viewer link.</param>
    /// <returns><c>true</c> for a Drive viewer link.</returns>
    public static bool TryGetGoogleDriveFileId(string? url, out string fileId)
    {
        fileId = string.Empty;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !uri.IsDefaultPort ||
            uri.UserInfo.Length > 0 ||
            !uri.Host.Equals(CustomTabConstants.GoogleDriveHost, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var match = DriveFilePathRegex().Match(uri.AbsolutePath);
        if (match.Success)
        {
            fileId = match.Groups["id"].Value;
            return true;
        }

        if (uri.AbsolutePath.Equals(CustomTabConstants.GoogleDriveOpenPath, StringComparison.OrdinalIgnoreCase) ||
            uri.AbsolutePath.Equals(CustomTabConstants.GoogleDriveUcPath, StringComparison.OrdinalIgnoreCase))
        {
            var id = HttpUtility.ParseQueryString(uri.Query)[CustomTabConstants.GoogleDriveIdQueryKey];
            if (id != null && DriveFileIdRegex().IsMatch(id))
            {
                fileId = id;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Determines whether a URL is a signed Discord attachment link. Discord signs these links with
    /// <c>ex</c>, <c>is</c> and <c>hm</c> parameters, and they stop working when the signature expires.
    /// </summary>
    /// <param name="url">The URL to check.</param>
    /// <returns><c>true</c> for a Discord attachment link that will expire.</returns>
    public static bool IsExpiringDiscordAttachment(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            !(uri.Host.Equals(CustomTabConstants.DiscordCdnHost, StringComparison.OrdinalIgnoreCase) ||
              uri.Host.Equals(CustomTabConstants.DiscordMediaHost, StringComparison.OrdinalIgnoreCase)) ||
            !(uri.AbsolutePath.StartsWith(CustomTabConstants.DiscordAttachmentsPathPrefix, StringComparison.OrdinalIgnoreCase) ||
              uri.AbsolutePath.StartsWith(CustomTabConstants.DiscordEphemeralAttachmentsPathPrefix, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var query = HttpUtility.ParseQueryString(uri.Query);
        return query[CustomTabConstants.DiscordExpiryQueryKey] != null || query[CustomTabConstants.DiscordSignatureQueryKey] != null;
    }

    [GeneratedRegex(CustomTabConstants.GoogleDriveFilePathPattern, RegexOptions.IgnoreCase)]
    private static partial Regex DriveFilePathRegex();

    [GeneratedRegex(CustomTabConstants.GoogleDriveFileIdPattern)]
    private static partial Regex DriveFileIdRegex();

    [GeneratedRegex(CustomTabConstants.GoogleDriveResourceKeyPattern)]
    private static partial Regex ResourceKeyRegex();
}
