using System;
using System.Text.RegularExpressions;
using System.Web;

namespace GenHub.Features.Content.Services;

/// <summary>
/// Recognizes and repairs image links that publishers paste into custom tab cards.
/// </summary>
public static partial class PublisherImageUrl
{
    private const string GoogleDriveHost = "drive.google.com";
    private const string GoogleDriveDirectFormat = "https://drive.usercontent.google.com/download?id={0}&export=view";

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
        return TryGetGoogleDriveFileId(url, out var fileId)
            ? string.Format(GoogleDriveDirectFormat, Uri.EscapeDataString(fileId))
            : url;
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
            !uri.Host.Equals(GoogleDriveHost, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var match = DriveFilePathRegex().Match(uri.AbsolutePath);
        if (match.Success)
        {
            fileId = match.Groups["id"].Value;
            return true;
        }

        if (uri.AbsolutePath.Equals("/open", StringComparison.OrdinalIgnoreCase) ||
            uri.AbsolutePath.Equals("/uc", StringComparison.OrdinalIgnoreCase))
        {
            var id = HttpUtility.ParseQueryString(uri.Query)["id"];
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
            !(uri.Host.Equals("cdn.discordapp.com", StringComparison.OrdinalIgnoreCase) ||
              uri.Host.Equals("media.discordapp.net", StringComparison.OrdinalIgnoreCase)) ||
            !(uri.AbsolutePath.StartsWith("/attachments/", StringComparison.OrdinalIgnoreCase) ||
              uri.AbsolutePath.StartsWith("/ephemeral-attachments/", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var query = HttpUtility.ParseQueryString(uri.Query);
        return query["ex"] != null || query["hm"] != null;
    }

    [GeneratedRegex(@"^/file/d/(?<id>[A-Za-z0-9_-]{10,})(?:/[^/]*)?$")]
    private static partial Regex DriveFilePathRegex();

    [GeneratedRegex(@"^[A-Za-z0-9_-]{10,}$")]
    private static partial Regex DriveFileIdRegex();
}
