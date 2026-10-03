using GenHub.Core.Constants;
using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace GenHub.Core.Helpers;

/// <summary>
/// Helper utilities for normalizing and transforming cloud storage and hosting URLs (Google Drive, Dropbox, ModDB, GitHub).
/// </summary>
public static partial class CloudUrlHelper
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(RegexConstants.DefaultTimeoutMs);

    [GeneratedRegex(@"^https?:\/\/(?:drive|docs)\.google\.com\/(?:file\/d\/|[^?]*\?(?:[^#]*&)?id=)([^/?&]+)", RegexOptions.IgnoreCase, RegexConstants.DefaultTimeoutMs)]
    private static partial Regex GoogleDriveRegexCompiled();

    [GeneratedRegex(@"^https?:\/\/github\.com\/([^\/]+)\/([^\/]+)\/blob\/([^\/]+)\/(.+)$", RegexOptions.IgnoreCase, RegexConstants.DefaultTimeoutMs)]
    private static partial Regex GitHubBlobRegexCompiled();

    [GeneratedRegex(@"(?<=[?&])dl=0(?=[&#]|$)", RegexOptions.IgnoreCase, RegexConstants.DefaultTimeoutMs)]
    private static partial Regex DropboxDlRegexCompiled();

    [GeneratedRegex(@"(?<=[?&])dl=1(?=[&#]|$)", RegexOptions.IgnoreCase, RegexConstants.DefaultTimeoutMs)]
    private static partial Regex DropboxDl1RegexCompiled();

    [GeneratedRegex(@"href=[""']([^""']*confirm=[^""']*)[""']", RegexOptions.IgnoreCase, RegexConstants.DefaultTimeoutMs)]
    private static partial Regex GoogleDriveConfirmHrefRegexCompiled();

    [GeneratedRegex(@"<form\b(?=[^>]*\baction=[""']([^""']*)[""'])(?=[^>]*\bmethod=[""'](?:post|get)[""'])[^>]*>(.*?)(?:<\/form>|$)", RegexOptions.IgnoreCase | RegexOptions.Singleline, RegexConstants.DefaultTimeoutMs)]
    private static partial Regex GoogleDriveFormActionRegexCompiled();

    [GeneratedRegex(@"<input\s+(?=[^>]*\bname=[""']([^""']+)[""'])(?=[^>]*\bvalue=[""']([^""']*)[""'])[^>]*>", RegexOptions.IgnoreCase, RegexConstants.DefaultTimeoutMs)]
    private static partial Regex GoogleDriveFormInputRegexCompiled();

    private static Regex GoogleDriveRegex => GoogleDriveRegexCompiled();

    private static Regex GitHubBlobRegex => GitHubBlobRegexCompiled();

    private static Regex DropboxDlRegex => DropboxDlRegexCompiled();

    private static Regex DropboxDl1Regex => DropboxDl1RegexCompiled();

    private static Regex GoogleDriveConfirmHrefRegex => GoogleDriveConfirmHrefRegexCompiled();

    private static Regex GoogleDriveFormActionRegex => GoogleDriveFormActionRegexCompiled();

    private static Regex GoogleDriveFormInputRegex => GoogleDriveFormInputRegexCompiled();

    /// <summary>
    /// Normalizes a given URL so that it points directly to the raw content stream rather than an interactive HTML viewer page.
    /// </summary>
    /// <param name="url">The URL to normalize.</param>
    /// <returns>The normalized direct download URL, or the original URL if no normalization applies.</returns>
    public static string NormalizeDirectDownloadUrl(string? url) => NormalizeCloudUrl(url);

    /// <summary>
    /// Normalizes a download URL from known cloud storage hosts (Google Drive, Dropbox, GitHub) into a direct download URL.
    /// If the URL is not from a known cloud provider or is already normalized, it is returned unchanged.
    /// </summary>
    /// <param name="url">The URL to normalize.</param>
    /// <returns>A normalized direct download URL, or the original URL if not recognized.</returns>
    public static string NormalizeCloudUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
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

        return TryExtractFromConfirmHref(html, requestUri) ?? TryExtractFromFormAction(html, requestUri);
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

    private static Uri? ResolveGoogleDriveUri(string rawTarget, Uri? requestUri)
    {
        var rawUrl = rawTarget.Replace("&amp;", "&");
        if (Uri.TryCreate(rawUrl, UriKind.Absolute, out var absUri) &&
            (absUri.Scheme == Uri.UriSchemeHttp || absUri.Scheme == Uri.UriSchemeHttps))
        {
            return absUri;
        }

        var baseUri = requestUri ?? new Uri(Uri.UriSchemeHttps + "://drive.google.com");
        if (Uri.TryCreate(baseUri, rawUrl, out var combinedUri) &&
            (combinedUri.Scheme == Uri.UriSchemeHttp || combinedUri.Scheme == Uri.UriSchemeHttps))
        {
            return combinedUri;
        }

        return null;
    }

    private static string? TryExtractFromConfirmHref(string html, Uri? requestUri)
    {
        var confirmMatch = GoogleDriveConfirmHrefRegex.Match(html);
        if (!confirmMatch.Success)
        {
            return null;
        }

        var resolvedUri = ResolveGoogleDriveUri(confirmMatch.Groups[1].Value, requestUri);
        return resolvedUri is not null && IsAllowedGoogleDriveHost(resolvedUri)
            ? resolvedUri.ToString()
            : null;
    }

    private static string? TryExtractFromFormAction(string html, Uri? requestUri)
    {
        var actionMatch = GoogleDriveFormActionRegex.Match(html);
        if (!actionMatch.Success)
        {
            return null;
        }

        var resolvedUri = ResolveGoogleDriveUri(actionMatch.Groups[1].Value, requestUri);
        if (resolvedUri is null || !IsAllowedGoogleDriveHost(resolvedUri))
        {
            return null;
        }

        var action = resolvedUri.ToString();
        var formInnerHtml = actionMatch.Groups[2].Value;
        var inputMatches = GoogleDriveFormInputRegex.Matches(formInnerHtml);
        var queryParams = inputMatches
            .Select(m => $"{Uri.EscapeDataString(System.Net.WebUtility.HtmlDecode(m.Groups[1].Value))}={Uri.EscapeDataString(System.Net.WebUtility.HtmlDecode(m.Groups[2].Value))}")
            .ToList();

        if (queryParams.Count > 0)
        {
            var separator = action.Contains('?') ? "&" : "?";
            return $"{action}{separator}{string.Join("&", queryParams)}";
        }

        return action;
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
                var appendText = $"{separator}dl=1";
                var fragmentIndex = url.IndexOf('#');
                normalizedUrl = fragmentIndex == -1 ? url + appendText : url.Insert(fragmentIndex, appendText);
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

    private static bool IsMatchingHost(string url, params string[] allowedHosts)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return allowedHosts.Any(h =>
            uri.Host.Equals(h, StringComparison.OrdinalIgnoreCase) ||
            uri.Host.EndsWith("." + h, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsAllowedGoogleDriveHost(Uri? uri)
    {
        return uri is { Scheme: "https" } &&
            (uri.Host.Equals(HostingConstants.GoogleDriveHost, StringComparison.OrdinalIgnoreCase) ||
             uri.Host.EndsWith(HostingConstants.GoogleDomainSuffix, StringComparison.OrdinalIgnoreCase) ||
             uri.Host.EndsWith(HostingConstants.GoogleUserContentDomainSuffix, StringComparison.OrdinalIgnoreCase));
    }
}
