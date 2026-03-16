using System;
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

        // Check for raw Match ID (e.g., "151553")
        if (long.TryParse(url, out _))
        {
            return ReplaySource.GeneralsOnline;
        }

        if (url.Contains(ApiConstants.UploadThingUrlFragment))
        {
            return ReplaySource.UploadThing;
        }

        if (url.Contains(ApiConstants.GeneralsOnlineViewMatchFragment))
        {
            return ReplaySource.GeneralsOnline;
        }

        if (url.Contains(ApiConstants.GenToolUrlFragment))
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
        var source = IdentifySource(url);
        logger.LogInformation(LogMessages.IdentifyingUrlSource, url, source);

        try
        {
            return source switch
            {
                ReplaySource.UploadThing => url, // UploadThing links are usually direct (utfs.io/f/...)
                ReplaySource.DirectLink => url,
                ReplaySource.GeneralsOnline => await ExtractGeneralsOnlineUrlAsync(url, ct),
                ReplaySource.GenTool => await ExtractGenToolUrlAsync(url, ct),
                _ => null,
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, LogMessages.FailedToExtractDownloadUrl, url);
            return null;
        }
    }

    [GeneratedRegex(RegexConstants.GeneralsOnlineReplayPattern)]
    private static partial Regex GeneralsOnlineRegex();

    [GeneratedRegex(RegexConstants.GenToolReplayPattern, RegexOptions.IgnoreCase)]
    private static partial Regex GenToolRegex();

    private async Task<string?> ExtractGeneralsOnlineUrlAsync(string url, CancellationToken ct)
    {
        // If the URL is just a number, treat it as a match ID
        if (long.TryParse(url, out long matchId))
        {
            // Reconstruct: https://www.playgenerals.online/viewmatch?match=123
            // Note: ApiConstants.GeneralsOnlineViewMatchFragment is "playgenerals.online/viewmatch"
            // We use GeneralsOnlineConstants.WebsiteUrl which is "https://www.playgenerals.online"
            url = $"{GeneralsOnlineConstants.WebsiteUrl}/viewmatch?match={matchId}";
        }

        // Example: https://www.playgenerals.online/viewmatch?match=354994
        // Search for a link matching *_replay.rep
        var html = await httpClient.GetStringAsync(url, ct);

        // Regex to find matchdata link: https://matchdata.playgenerals.online/..._replay.rep
        var match = GeneralsOnlineRegex().Match(html);
        if (match.Success)
        {
            return match.Value;
        }

        logger.LogWarning(LogMessages.CouldNotFindReplayLinkGeneralsOnline, url);
        return null;
    }

    private async Task<string?> ExtractGenToolUrlAsync(string url, CancellationToken ct)
    {
        var html = await httpClient.GetStringAsync(url, ct);
        var match = GenToolRegex().Match(html);
        if (match.Success)
        {
            var relativeUrl = match.Groups[1].Value;
            if (Uri.IsWellFormedUriString(relativeUrl, UriKind.Absolute))
            {
                return relativeUrl;
            }

            var baseUri = new Uri(url);
            var absoluteUri = new Uri(baseUri, relativeUrl);
            return absoluteUri.ToString();
        }

        logger.LogWarning(LogMessages.CouldNotFindReplayLinkGenTool, url);
        return null;
    }
}
