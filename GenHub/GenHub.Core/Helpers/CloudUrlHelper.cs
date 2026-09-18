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

        if (TryNormalizeGoogleDriveUrl(trimmed, out var googleDriveUrl))
        {
            return googleDriveUrl;
        }

        if (TryNormalizeDropboxUrl(trimmed, out var dropboxUrl))
        {
            return dropboxUrl;
        }

        if (TryNormalizeGitHubBlobUrl(trimmed, out var gitHubBlobUrl))
        {
            return gitHubBlobUrl;
        }

        return trimmed;
    }

    private static bool TryNormalizeGoogleDriveUrl(string url, out string normalizedUrl)
    {
        if (url.Contains("drive.google.com", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("docs.google.com", StringComparison.OrdinalIgnoreCase))
        {
            var match = GoogleDriveRegex.Match(url);
            if (match.Success)
            {
                var fileId = match.Groups[1].Value;
                normalizedUrl = string.Format(CultureInfo.InvariantCulture, HostingConstants.GoogleDriveDownloadUrlTemplate, fileId);
                return true;
            }
        }

        normalizedUrl = url;
        return false;
    }

    private static bool TryNormalizeDropboxUrl(string url, out string normalizedUrl)
    {
        if (url.Contains("dropbox.com", StringComparison.OrdinalIgnoreCase))
        {
            if (DropboxDlRegex.IsMatch(url))
            {
                normalizedUrl = DropboxDlRegex.Replace(url, "dl=1");
                return true;
            }

            if (!DropboxDl1Regex.IsMatch(url))
            {
                var separator = url.Contains('?') ? "&dl=1" : "?dl=1";
                var fragmentIndex = url.IndexOf('#');
                normalizedUrl = fragmentIndex == -1 ? url + separator : url.Insert(fragmentIndex, separator);
                return true;
            }
        }

        normalizedUrl = url;
        return false;
    }

    private static bool TryNormalizeGitHubBlobUrl(string url, out string normalizedUrl)
    {
        var ghMatch = GitHubBlobRegex.Match(url);
        if (ghMatch.Success)
        {
            var owner = ghMatch.Groups[1].Value;
            var repo = ghMatch.Groups[2].Value;
            var branch = ghMatch.Groups[3].Value;
            var path = ghMatch.Groups[4].Value;
            normalizedUrl = $"https://raw.githubusercontent.com/{owner}/{repo}/{branch}/{path}";
            return true;
        }

        normalizedUrl = url;
        return false;
    }
}
