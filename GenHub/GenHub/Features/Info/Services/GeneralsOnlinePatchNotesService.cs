using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp;
using AngleSharp.Dom;
using GenHub.Core.Constants;
using GenHub.Core.Models.Info;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Info.Services;

/// <summary>
/// Default implementation of the patch notes service using AngleSharp for parsing.
/// </summary>
public class GeneralsOnlinePatchNotesService(IHttpClientFactory httpClientFactory, ILogger<GeneralsOnlinePatchNotesService> logger) : IGeneralsOnlinePatchNotesService
{
    private const string BaseUrl = "https://www.playgenerals.online";
    private const string PatchNotesUrl = BaseUrl + "/patchnotes";

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
    public async Task<IEnumerable<PatchNote>> GetPatchNotesAsync()
    {
        try
        {
            using var client = httpClientFactory.CreateClient();
            AddDefaultHeaders(client);
            var html = await client.GetStringAsync(PatchNotesUrl);

            var context = BrowsingContext.New(Configuration.Default);
            var document = await context.OpenAsync(req => req.Content(html));

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

                if (!string.IsNullOrEmpty(patchNote.DetailsUrl) && !patchNote.DetailsUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    patchNote.Id = patchNote.DetailsUrl.Split('/').LastOrDefault() ?? string.Empty;
                    patchNote.DetailsUrl = BaseUrl + patchNote.DetailsUrl;
                }

                patchNotes.Add(patchNote);
            }

            return patchNotes.OrderByDescending(p => p.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching patch notes from {Url}", PatchNotesUrl);
            return [];
        }
    }

    /// <inheritdoc/>
    public async Task GetPatchDetailsAsync(PatchNote patchNote)
    {
        if (string.IsNullOrEmpty(patchNote.DetailsUrl) || patchNote.IsDetailsLoaded || patchNote.IsLoadingDetails) return;

        try
        {
            patchNote.IsLoadingDetails = true;
            using var client = httpClientFactory.CreateClient();
            AddDefaultHeaders(client);
            var html = await client.GetStringAsync(patchNote.DetailsUrl);

            var context = BrowsingContext.New(Configuration.Default);
            var document = await context.OpenAsync(req => req.Content(html));

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

        var datePart = version.Split('_', StringSplitOptions.TrimEntries)[0];
        if (datePart.Length != 6 || !datePart.All(char.IsAsciiDigit))
        {
            return null;
        }

        var detailsUrl = $"{PatchNotesUrl}/{datePart}";

        try
        {
            using var client = httpClientFactory.CreateClient();
            AddDefaultHeaders(client);
            var html = await client.GetStringAsync(detailsUrl, cancellationToken);

            var context = BrowsingContext.New(Configuration.Default);
            var document = await context.OpenAsync(req => req.Content(html), cancellationToken);

            return FormatPatchNotesDocument(document, datePart);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Error fetching formatted patch notes for version {Version} from {Url}", version, detailsUrl);
            return null;
        }
    }

    private static void AddDefaultHeaders(HttpClient client)
    {
        client.DefaultRequestHeaders.UserAgent.ParseAdd(ApiConstants.BrowserUserAgent);
        client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8");
        client.DefaultRequestHeaders.Add("Referer", BaseUrl);
    }
}
