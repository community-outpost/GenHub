using AngleSharp;
using AngleSharp.Dom;
using GenHub.Core.Constants;
using GenHub.Core.Models.Info;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Info.Services;

/// <summary>
/// Default implementation of the patch notes service using AngleSharp for parsing.
/// </summary>
public class GeneralsOnlinePatchNotesService(IHttpClientFactory httpClientFactory, ILogger<GeneralsOnlinePatchNotesService> logger) : IGeneralsOnlinePatchNotesService
{
    private const string BaseUrl = GeneralsOnlineConstants.PlayGeneralsOnlineBaseUrl;
    private const string PatchNotesUrl = GeneralsOnlineConstants.PatchNotesUrl;

    private readonly ConcurrentDictionary<string, string> _formattedCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _patchNotesLock = new(1, 1);
    private IReadOnlyList<PatchNote>? _cachedPatchNotes;

    /// <summary>
    /// Formats patch notes from a parsed HTML document.
    /// </summary>
    /// <param name="document">The parsed HTML document.</param>
    /// <param name="datePart">The date part string.</param>
    /// <returns>Formatted patch notes string, or null if no changes found.</returns>
    public static string? FormatPatchNotesDocument(IDocument document, string datePart)
    {
        var postText = document.QuerySelector(".blog-read .post-text");
        var dateElement = document.QuerySelector("#subheader .subtitle") ?? document.QuerySelector(".d-date");
        var titleElement = document.QuerySelector("#subheader h2") ?? document.QuerySelector("h4");

        var title = titleElement?.TextContent.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            title = $"Update {datePart}";
        }

        var date = dateElement?.TextContent.Trim();
        var header = !string.IsNullOrEmpty(date) ? $"{title} ({date})" : title;

        var changes = new List<string>();
        if (postText != null)
        {
            var listItems = postText.QuerySelectorAll("ul li");
            foreach (var li in listItems)
            {
                var decoded = WebUtility.HtmlDecode(li.TextContent.Trim());
                if (!string.IsNullOrEmpty(decoded))
                {
                    changes.Add(decoded);
                }
            }
        }

        if (changes.Count == 0)
        {
            return null;
        }

        var sb = new StringBuilder();
        sb.AppendLine(header);
        sb.AppendLine();
        foreach (var change in changes)
        {
            sb.AppendLine($"- {change}");
        }

        return sb.ToString().TrimEnd();
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<PatchNote>> GetPatchNotesAsync() => GetPatchNotesAsync(CancellationToken.None);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PatchNote>> GetPatchNotesAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await FetchPatchNotesInternalAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching patch notes from {Url}", PatchNotesUrl);
            return [];
        }
    }

    /// <inheritdoc/>
    public async Task GetPatchDetailsAsync(PatchNote patchNote, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(patchNote.DetailsUrl) || patchNote.IsDetailsLoaded || patchNote.IsLoadingDetails) return;

        try
        {
            patchNote.IsLoadingDetails = true;
            using var client = httpClientFactory.CreateClient();
            AddDefaultHeaders(client);
            var html = await client.GetStringAsync(patchNote.DetailsUrl, cancellationToken).ConfigureAwait(false);

            var context = BrowsingContext.New(Configuration.Default);
            var document = await context.OpenAsync(req => req.Content(html), cancellationToken).ConfigureAwait(false);

            var postText = document.QuerySelector(".blog-read .post-text");
            if (postText != null)
            {
                patchNote.Changes.Clear();
                var listItems = postText.QuerySelectorAll("ul li");
                foreach (var li in listItems)
                {
                    patchNote.Changes.Add(WebUtility.HtmlDecode(li.TextContent.Trim()));
                }

                patchNote.IsDetailsLoaded = true;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching patch details from {Url}", patchNote.DetailsUrl);
        }
        finally
        {
            patchNote.IsLoadingDetails = false;
        }
    }

    /// <inheritdoc/>
    public async Task<string?> GetPatchNotesFormattedAsync(string version, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        var normalizedKey = version.Trim();
        if (_formattedCache.TryGetValue(normalizedKey, out var cached))
        {
            return string.IsNullOrEmpty(cached) ? null : cached;
        }

        if (!TryExtractDatePart(normalizedKey, out var datePart))
        {
            return null;
        }

        if (_formattedCache.TryGetValue(datePart, out var dateCached))
        {
            _formattedCache[normalizedKey] = dateCached;
            return string.IsNullOrEmpty(dateCached) ? null : dateCached;
        }

        var detailsUrl = $"{PatchNotesUrl}/{datePart}";

        try
        {
            var formatted = await FetchDirectOrFallbackFormattedNotesAsync(datePart, detailsUrl, cancellationToken).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(formatted))
            {
                _formattedCache[datePart] = formatted;
                _formattedCache[normalizedKey] = formatted;
                return formatted;
            }

            _formattedCache[datePart] = string.Empty;
            _formattedCache[normalizedKey] = string.Empty;
            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error fetching formatted patch notes for version {Version} from {Url}", version, detailsUrl);
            return null;
        }
    }

    private static bool TryExtractDatePart(string version, out string datePart)
    {
        var clean = version;
        if (clean.StartsWith('v') || clean.StartsWith('V'))
        {
            clean = clean[1..];
        }

        datePart = clean.Split('_', StringSplitOptions.TrimEntries)[0];
        return datePart.Length == 6 && datePart.All(char.IsAsciiDigit);
    }

    private static bool TryParseVersionDate(string datePart, out DateTime date)
    {
        date = default;
        if (datePart.Length != 6 || !datePart.All(char.IsAsciiDigit))
        {
            return false;
        }

        if (!int.TryParse(datePart.AsSpan(0, 2), CultureInfo.InvariantCulture, out var month) ||
            !int.TryParse(datePart.AsSpan(2, 2), CultureInfo.InvariantCulture, out var day) ||
            !int.TryParse(datePart.AsSpan(4, 2), CultureInfo.InvariantCulture, out var shortYear))
        {
            return false;
        }

        var year = 2000 + shortYear;
        if (month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month))
        {
            return false;
        }

        date = new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc);
        return true;
    }

    private static PatchNote? FindBestMatchingPatchNote(IEnumerable<PatchNote> patchNotes, DateTime targetDate)
    {
        PatchNote? bestCandidate = null;
        var minDiff = TimeSpan.MaxValue;
        var bestIsFutureOrEqual = false;

        foreach (var note in patchNotes)
        {
            if (!TryParseVersionDate(note.Id, out var noteDate))
            {
                continue;
            }

            if (IsBetterCandidate(noteDate, targetDate, bestCandidate != null, bestIsFutureOrEqual, minDiff))
            {
                bestCandidate = note;
                bestIsFutureOrEqual = noteDate >= targetDate;
                minDiff = (noteDate - targetDate).Duration();
            }
        }

        return bestCandidate;
    }

    private static bool IsBetterCandidate(
        DateTime candidateDate,
        DateTime targetDate,
        bool hasBestCandidate,
        bool bestIsFutureOrEqual,
        TimeSpan minDiff)
    {
        var diff = candidateDate - targetDate;
        var absDiff = diff.Duration();
        if (absDiff > TimeSpan.FromDays(14))
        {
            return false;
        }

        var isFutureOrEqual = diff >= TimeSpan.Zero;
        if (!hasBestCandidate)
        {
            return true;
        }

        if (isFutureOrEqual && !bestIsFutureOrEqual)
        {
            return true;
        }

        return isFutureOrEqual == bestIsFutureOrEqual && absDiff < minDiff;
    }

    private static void AddDefaultHeaders(HttpClient client)
    {
        client.DefaultRequestHeaders.UserAgent.ParseAdd(ApiConstants.BrowserUserAgent);
        client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8");
        client.DefaultRequestHeaders.Add("Referer", BaseUrl);
    }

    private async Task<string?> FetchDirectOrFallbackFormattedNotesAsync(string datePart, string detailsUrl, CancellationToken cancellationToken)
    {
        using var client = httpClientFactory.CreateClient();
        AddDefaultHeaders(client);

        using var response = await client.GetAsync(detailsUrl, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return await FetchFallbackPatchNotesAsync(datePart, cancellationToken).ConfigureAwait(false);
        }

        response.EnsureSuccessStatusCode();

        var html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var context = BrowsingContext.New(Configuration.Default);
        var document = await context.OpenAsync(req => req.Content(html), cancellationToken).ConfigureAwait(false);
        var formatted = FormatPatchNotesDocument(document, datePart);

        if (string.IsNullOrWhiteSpace(formatted))
        {
            return await FetchFallbackPatchNotesAsync(datePart, cancellationToken).ConfigureAwait(false);
        }

        return formatted;
    }

    private async Task<IReadOnlyList<PatchNote>> FetchPatchNotesInternalAsync(CancellationToken cancellationToken)
    {
        if (_cachedPatchNotes != null)
        {
            return _cachedPatchNotes;
        }

        await _patchNotesLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cachedPatchNotes != null)
            {
                return _cachedPatchNotes;
            }

            using var client = httpClientFactory.CreateClient();
            AddDefaultHeaders(client);
            using var response = await client.GetAsync(PatchNotesUrl, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            var context = BrowsingContext.New(Configuration.Default);
            var document = await context.OpenAsync(req => req.Content(html), cancellationToken).ConfigureAwait(false);

            var patchNotes = new List<PatchNote>();
            var rows = document.QuerySelectorAll(".row.g-4 .col-lg-4.col-md-6.mb10");

            foreach (var row in rows)
            {
                var patchNote = new PatchNote();
                var postText = row.QuerySelector(".post-text");
                if (postText == null) continue;

                var dateElement = postText.QuerySelector(".d-date");
                var titleElement = postText.QuerySelector("h4 a");
                var summaryElement = postText.QuerySelector("p");

                patchNote.Date = dateElement?.TextContent.Trim() ?? string.Empty;
                patchNote.Title = titleElement?.TextContent.Trim() ?? string.Empty;
                patchNote.Summary = summaryElement?.TextContent.Trim() ?? string.Empty;
                patchNote.DetailsUrl = titleElement?.GetAttribute("href") ?? string.Empty;

                if (!string.IsNullOrEmpty(patchNote.DetailsUrl))
                {
                    patchNote.Id = patchNote.DetailsUrl.TrimEnd('/').Split('/').LastOrDefault() ?? string.Empty;
                    if (!patchNote.DetailsUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    {
                        patchNote.DetailsUrl = BaseUrl + patchNote.DetailsUrl;
                    }
                }

                patchNotes.Add(patchNote);
            }

            var result = patchNotes.OrderByDescending(p => p.Id).ToList();
            _cachedPatchNotes = result;
            return result;
        }
        finally
        {
            _patchNotesLock.Release();
        }
    }

    private async Task<string?> FetchFallbackPatchNotesAsync(string datePart, CancellationToken cancellationToken)
    {
        if (!TryParseVersionDate(datePart, out var targetDate))
        {
            return null;
        }

        var allNotes = await FetchPatchNotesInternalAsync(cancellationToken).ConfigureAwait(false);
        var bestMatch = FindBestMatchingPatchNote(allNotes, targetDate);
        if (bestMatch == null || string.IsNullOrWhiteSpace(bestMatch.Id) || string.Equals(bestMatch.Id, datePart, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (_formattedCache.TryGetValue(bestMatch.Id, out var cachedFallback) && !string.IsNullOrEmpty(cachedFallback))
        {
            return cachedFallback;
        }

        var fallbackDetailsUrl = $"{PatchNotesUrl}/{bestMatch.Id}";
        using var client = httpClientFactory.CreateClient();
        AddDefaultHeaders(client);
        using var response = await client.GetAsync(fallbackDetailsUrl, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        var context = BrowsingContext.New(Configuration.Default);
        var document = await context.OpenAsync(req => req.Content(html), cancellationToken).ConfigureAwait(false);

        var fallbackFormatted = FormatPatchNotesDocument(document, bestMatch.Id);
        if (!string.IsNullOrWhiteSpace(fallbackFormatted))
        {
            _formattedCache[bestMatch.Id] = fallbackFormatted;
        }

        return fallbackFormatted;
    }
}
