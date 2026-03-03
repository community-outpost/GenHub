using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using AngleSharp;
using AngleSharp.Dom;
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

                if (!string.IsNullOrEmpty(patchNote.DetailsUrl))
                {
                    if (!patchNote.DetailsUrl.StartsWith("http"))
                    {
                        patchNote.Id = patchNote.DetailsUrl.Split('/').LastOrDefault() ?? string.Empty;
                        patchNote.DetailsUrl = BaseUrl + patchNote.DetailsUrl;
                    }
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
                    patchNote.Changes.Add(li.TextContent.Trim());
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

    private static void AddDefaultHeaders(HttpClient client)
    {
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8");
        client.DefaultRequestHeaders.Add("Referer", BaseUrl);
    }
}
