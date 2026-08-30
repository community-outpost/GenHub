using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Tools.ReplayManager;
using GenHub.Core.Models.Tools.ReplayManager;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Tools.ReplayManager.Services;

/// <summary>
/// Service for parsing replay URLs and extracting direct download links.
/// </summary>
public sealed partial class UrlParserService(HttpClient httpClient, ILogger<UrlParserService> logger) : IUrlParserService
{
    /// <inheritdoc />
    public ReplaySource IdentifySource(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return ReplaySource.Unknown;
        }

        // Check for raw match ID (e.g., "151553")
        if (long.TryParse(url, out _))
        {
            return ReplaySource.GeneralsOnline;
        }

        if (url.Contains(ApiConstants.UploadThingUrlFragment, StringComparison.OrdinalIgnoreCase) ||
            url.Contains(ApiConstants.UploadThingUfsUrlFragment, StringComparison.OrdinalIgnoreCase) ||
            url.Contains(ApiConstants.UploadThingUfsShortUrlFragment, StringComparison.OrdinalIgnoreCase))
        {
            return ReplaySource.UploadThing;
        }

        if (url.Contains(ApiConstants.StrataUrlFragment, StringComparison.OrdinalIgnoreCase) ||
            url.Contains(ApiConstants.GameReplaysDomainFragment, StringComparison.OrdinalIgnoreCase))
        {
            return ReplaySource.Strata;
        }

        if (url.Contains(ApiConstants.GeneralsOnlineViewMatchFragment, StringComparison.OrdinalIgnoreCase))
        {
            return ReplaySource.GeneralsOnline;
        }

        if (url.Contains(ApiConstants.GenToolUrlFragment, StringComparison.OrdinalIgnoreCase))
        {
            return ReplaySource.GenTool;
        }

        if (url.EndsWith(FileTypes.ReplayFileExtension, StringComparison.OrdinalIgnoreCase) ||
            url.EndsWith(FileTypes.ZipFileExtension, StringComparison.OrdinalIgnoreCase))
        {
            return ReplaySource.DirectLink;
        }

        return ReplaySource.Unknown;
    }

    /// <inheritdoc />
    public bool IsValidReplayUrl(string url)
    {
        return IdentifySource(url) != ReplaySource.Unknown;
    }

    /// <inheritdoc />
    public async Task<string?> GetDirectDownloadUrlAsync(string url, CancellationToken ct = default)
    {
        var urls = await GetDirectDownloadUrlsAsync(url, ct);
        return urls.Count > 0 ? urls[0] : null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetDirectDownloadUrlsAsync(string url, CancellationToken ct = default)
    {
        var source = IdentifySource(url);
        logger.LogInformation(LogMessages.IdentifyingUrlSource, url, source);

        try
        {
            return source switch
            {
                ReplaySource.UploadThing => [url],
                ReplaySource.DirectLink => [url],
                ReplaySource.GeneralsOnline => await ExtractGeneralsOnlineUrlsAsync(url, ct),
                ReplaySource.GenTool => await ExtractGenToolUrlsAsync(url, ct),
                ReplaySource.Strata => await ExtractStrataUrlsAsync(url, ct),
                _ => [],
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, LogMessages.FailedToExtractDownloadUrl, url);
            return [];
        }
    }

    [GeneratedRegex(RegexConstants.GeneralsOnlineReplayPattern)]
    private static partial Regex GeneralsOnlineRegex();

    [GeneratedRegex(RegexConstants.GenToolReplayPattern, RegexOptions.IgnoreCase)]
    private static partial Regex GenToolRegex();

    [GeneratedRegex(RegexConstants.StrataReplayPattern, RegexOptions.IgnoreCase)]
    private static partial Regex StrataRegex();

    private async Task<IReadOnlyList<string>> ExtractGeneralsOnlineUrlsAsync(string url, CancellationToken ct)
    {
        if (long.TryParse(url, out long matchId))
        {
            url = $"{GeneralsOnlineConstants.WebsiteUrl}/viewmatch?match={matchId}";
        }

        var html = await httpClient.GetStringAsync(url, ct);
        var matches = GeneralsOnlineRegex().Matches(html);
        var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in matches)
        {
            if (match.Success && !string.IsNullOrWhiteSpace(match.Value))
            {
                results.Add(match.Value);
            }
        }

        if (results.Count == 0)
        {
            logger.LogWarning(LogMessages.CouldNotFindReplayLinkGeneralsOnline, url);
        }

        return results.ToList();
    }

    private async Task<IReadOnlyList<string>> ExtractGenToolUrlsAsync(string url, CancellationToken ct)
    {
        var html = await httpClient.GetStringAsync(url, ct);
        var matches = GenToolRegex().Matches(html);
        var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var baseUri = new Uri(url);

        foreach (Match match in matches)
        {
            if (!match.Success)
            {
                continue;
            }

            var relativeUrl = match.Groups[1].Value;
            if (Uri.IsWellFormedUriString(relativeUrl, UriKind.Absolute))
            {
                results.Add(relativeUrl);
            }
            else if (Uri.TryCreate(baseUri, relativeUrl, out var absoluteUri))
            {
                results.Add(absoluteUri.ToString());
            }
        }

        if (results.Count == 0)
        {
            logger.LogWarning(LogMessages.CouldNotFindReplayLinkGenTool, url);
        }

        return results.ToList();
    }

    private async Task<IReadOnlyList<string>> ExtractStrataUrlsAsync(string url, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd(ApiConstants.BrowserUserAgent);
        using var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync(ct);

        var matches = StrataRegex().Matches(html);
        var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var baseUri = new Uri(url);

        foreach (Match match in matches)
        {
            var extracted = match.Groups["url"].Success ? match.Groups["url"].Value : match.Value;
            if (string.IsNullOrWhiteSpace(extracted))
            {
                continue;
            }

            if (Uri.IsWellFormedUriString(extracted, UriKind.Absolute))
            {
                results.Add(extracted);
            }
            else if (Uri.TryCreate(baseUri, extracted, out var absoluteUri))
            {
                results.Add(absoluteUri.ToString());
            }
        }

        logger.LogInformation("Extracted {Count} replay URLs from Strata match: {Url}", results.Count, url);
        return results.ToList();
    }
}
