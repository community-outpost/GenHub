using GenHub.Core.Constants;
using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace GenHub.Core.Helpers;

/// <summary>
/// Helper for detecting and normalizing cloud storage URLs (Google Drive, Dropbox, GitHub) into direct download links.
/// </summary>
public static class CloudUrlHelper
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    private static readonly Regex GoogleDriveRegex = new(
        @"(?:\/file\/d\/|[?&]id=)([a-zA-Z0-9_-]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        RegexTimeout);

    private static readonly Regex GitHubBlobRegex = new(
        @"^https?:\/\/github\.com\/([^\/]+)\/([^\/]+)\/blob\/([^\/]+)\/(.+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        RegexTimeout);

    private static readonly Regex DropboxDlRegex = new(
        @"(?<=[?&])dl=0(?=[&#]|$)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        RegexTimeout);

    private static readonly Regex DropboxDl1Regex = new(
        @"(?<=[?&])dl=1(?=[&#]|$)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        RegexTimeout);

    /// <summary>
    /// Normalizes a given URL so that it points directly to the raw content stream rather than an interactive HTML viewer page.
    /// </summary>
    /// <param name="url">The URL to normalize.</param>
    /// <returns>The normalized direct download URL, or the original URL if no normalization applies.</returns>
    public static string NormalizeDirectDownloadUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return url ?? string.Empty;
        }

        var trimmed = url.Trim();

        // 1. Google Drive (file view links, open?id links, uc?id links)
        if (trimmed.Contains("drive.google.com", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("docs.google.com", StringComparison.OrdinalIgnoreCase))
        {
            var match = GoogleDriveRegex.Match(trimmed);
            if (match.Success)
            {
                var fileId = match.Groups[1].Value;
                return string.Format(CultureInfo.InvariantCulture, HostingConstants.GoogleDriveDownloadUrlTemplate, fileId);
            }
        }

        // 2. Dropbox share links (force dl=1)
        if (trimmed.Contains("dropbox.com", StringComparison.OrdinalIgnoreCase))
        {
            if (DropboxDlRegex.IsMatch(trimmed))
            {
                return DropboxDlRegex.Replace(trimmed, "dl=1");
            }

            if (!DropboxDl1Regex.IsMatch(trimmed))
            {
                var separator = trimmed.Contains('?') ? "&dl=1" : "?dl=1";
                var fragmentIndex = trimmed.IndexOf('#');
                return fragmentIndex == -1 ? trimmed + separator : trimmed.Insert(fragmentIndex, separator);
            }
        }

        // 3. GitHub blob URLs to raw user content
        var ghMatch = GitHubBlobRegex.Match(trimmed);
        if (ghMatch.Success)
        {
            var owner = ghMatch.Groups[1].Value;
            var repo = ghMatch.Groups[2].Value;
            var branch = ghMatch.Groups[3].Value;
            var path = ghMatch.Groups[4].Value;
            return $"https://raw.githubusercontent.com/{owner}/{repo}/{branch}/{path}";
        }

        return trimmed;
    }
}
