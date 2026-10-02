using GenHub.Core.Constants;
using System;
using System.Globalization;
using System.Linq;
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

    private static readonly Regex GoogleDriveConfirmHrefRegex = new(
        @"href=""([^""]*confirm=[^""]+)""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        RegexTimeout);

    private static readonly Regex GoogleDriveFormActionRegex = new(
        @"action=""(https://drive\.usercontent\.google\.com/download[^""]*)""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        RegexTimeout);

    private static readonly Regex GoogleDriveFormInputRegex = new(
        @"<input[^>]+type=""hidden""[^>]+name=""([^""]+)""[^>]+value=""([^""]*)""",
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

    /// <summary>
    /// Attempts to extract a confirmed direct download URL from Google Drive HTML warning/confirmation page.
    /// </summary>
    /// <param name="html">The HTML content returned by Google Drive.</param>
    /// <param name="requestUri">The request URI that returned the HTML.</param>
    /// <returns>The confirmed direct download URL, or null if not found.</returns>
    public static string? TryExtractGoogleDriveConfirmationUrl(string? html, Uri? requestUri)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        var confirmMatch = GoogleDriveConfirmHrefRegex.Match(html);
        if (confirmMatch.Success)
        {
            var rawUrl = confirmMatch.Groups[1].Value.Replace("&amp;", "&");
            if (Uri.TryCreate(rawUrl, UriKind.Absolute, out var absUri))
            {
                return absUri.ToString();
            }

            var baseUri = requestUri ?? new Uri(Uri.UriSchemeHttps + "://drive.google.com");
            return new Uri(baseUri, rawUrl).ToString();
        }

        var actionMatch = GoogleDriveFormActionRegex.Match(html);
        if (actionMatch.Success)
        {
            var action = actionMatch.Groups[1].Value.Replace("&amp;", "&");
            var inputMatches = GoogleDriveFormInputRegex.Matches(html);
            var queryParams = inputMatches
                .Select(m => $"{Uri.EscapeDataString(m.Groups[1].Value)}={Uri.EscapeDataString(m.Groups[2].Value)}")
                .ToList();

            if (queryParams.Count > 0)
            {
                var separator = action.Contains('?') ? "&" : "?";
                return $"{action}{separator}{string.Join("&", queryParams)}";
            }

            return action;
        }

        return null;
    }

    /// <summary>
    /// Attempts to extract a confirmed direct download URL from Google Drive HTML warning/confirmation page.
    /// </summary>
    /// <param name="html">The HTML content returned by Google Drive.</param>
    /// <param name="requestUri">The request URI that returned the HTML.</param>
    /// <param name="confirmedUrl">When successful, contains the confirmed download URL.</param>
    /// <returns>True if a confirmation URL was extracted; otherwise false.</returns>
    public static bool TryExtractGoogleDriveConfirmationUrl(string? html, Uri? requestUri, out string? confirmedUrl)
    {
        confirmedUrl = TryExtractGoogleDriveConfirmationUrl(html, requestUri);
        return !string.IsNullOrEmpty(confirmedUrl);
    }

    private static bool TryNormalizeGoogleDriveUrl(string url, out string normalizedUrl)
    {
        if (IsMatchingHost(url, "drive.google.com", "docs.google.com"))
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
        if (IsMatchingHost(url, "dropbox.com"))
        {
            if (DropboxDlRegex.IsMatch(url))
            {
                normalizedUrl = DropboxDlRegex.Replace(url, "dl=1");
                return true;
            }

            if (!DropboxDl1Regex.IsMatch(url))
            {
                var separator = url.Contains('?') ? "&" : "?";
                var fragmentIndex = url.IndexOf('#');
                normalizedUrl = fragmentIndex == -1 ? url + separator : url.Insert(fragmentIndex, separator);
                return true;
            }
        }

        normalizedUrl = url;
        return false;
    }

    private static bool IsMatchingHost(string url, params string[] hosts)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return hosts.Any(host =>
            uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase) ||
            uri.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase));
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
