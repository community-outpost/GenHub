using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp;
using AngleSharp.Dom;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Info;
using GenHub.Core.Models.Info;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Info.Services;

/// <summary>
/// Service for fetching and parsing FAQs from legi.cc.
/// </summary>
/// <param name="httpClientFactory">The HTTP client factory.</param>
/// <param name="logger">The logger.</param>
public class FaqService(IHttpClientFactory httpClientFactory, ILogger<FaqService> logger) : IFaqService
{
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly ILogger<FaqService> _logger = logger;

    /// <inheritdoc/>
    public IReadOnlyList<string> SupportedLanguages => InfoConstants.SupportedFaqLanguages;

    /// <inheritdoc/>
    public async Task<OperationResult<IReadOnlyList<FaqCategory>>> GetFaqAsync(
        string language = "en",
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!SupportedLanguages.Contains(language))
            {
                language = InfoConstants.FaqDefaultLanguage;
            }

            var url = $"{InfoConstants.FaqBaseUrl}?lang={language}";
            using var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(DownloadDefaults.TimeoutSeconds);
            var html = await client.GetStringAsync(url, cancellationToken);

            var context = BrowsingContext.New(Configuration.Default);
            using var document = await context.OpenAsync(req => req.Content(html), cancellationToken);

            var categories = ParseFaq(document);
            return OperationResult<IReadOnlyList<FaqCategory>>.CreateSuccess(categories);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch FAQ.");
            return OperationResult<IReadOnlyList<FaqCategory>>.CreateFailure("Failed to load FAQ. Please check your internet connection.");
        }
    }

    private static List<FaqCategory> ParseFaq(IDocument document)
    {
        var categories = new List<FaqCategory>();
        var sections = document.QuerySelectorAll("section.chapter");

        FaqCategory? currentCategory = null;
        var currentItems = new List<FaqItem>();

        foreach (var section in sections)
        {
            // Check for Category Header (H2)
            var categoryHeader = section.QuerySelector("h2");
            if (categoryHeader != null)
            {
                // If we have an existing category collecting items, add it to the list
                if (currentCategory != null)
                {
                    categories.Add(currentCategory with { Items = [.. currentItems] });
                    currentItems.Clear();
                }

                var title = CleanText(categoryHeader.TextContent.Trim());

                // Skip "Index" or "Frequently Asked Questions" if they act as major headers but we want "Problems with the game" etc.
                // Based on HTML, "Problems with the game" is in a section with h2.
                // "Frequently Asked Questions" is also a section with h2.
                // We'll treat them all as categories.
                if (!string.Equals(title, "Index", StringComparison.OrdinalIgnoreCase))
                {
                    currentCategory = new FaqCategory(title, []);
                }

                continue;
            }

            // Check for Question Item (H3)
            var questionHeader = section.QuerySelector("h3");
            if (questionHeader != null && currentCategory != null)
            {
                var id = section.Id;
                var question = CleanText(questionHeader.TextContent.Trim());

                // Parse content: aside, p, ul, ol, h4
                var answer = ExtractAnswerText(section, questionHeader);

                var itemId = id ?? Guid.NewGuid().ToString();
                currentItems.Add(new FaqItem(itemId, question, answer, itemId));
            }
        }

        // Add the last category
        if (currentCategory != null && currentItems.Count > 0)
        {
            categories.Add(currentCategory with { Items = [.. currentItems] });
        }

        return categories;
    }

    private static string ExtractAnswerText(IElement section, IElement questionHeader)
    {
        var sb = new System.Text.StringBuilder();

        // Get all siblings after the h3, or just all children that serve as content
        foreach (var child in section.Children)
        {
            if (child == questionHeader) continue;
            if (child.ClassList.Contains("chapter-footer")) continue; // Skip footer

            if (child.TagName.Equals("ASIDE", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine(child.TextContent.Trim());
                sb.AppendLine();
            }
            else if (child.TagName.Equals("H4", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine();
                sb.AppendLine(child.TextContent.Trim());
            }
            else if (child.TagName.Equals("P", StringComparison.OrdinalIgnoreCase))
            {
                var text = child.TextContent.Trim();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    sb.AppendLine(text);
                    sb.AppendLine();
                }
            }
            else if (child.TagName.Equals("OL", StringComparison.OrdinalIgnoreCase) || child.TagName.Equals("UL", StringComparison.OrdinalIgnoreCase))
            {
                var items = child.QuerySelectorAll("li");
                int index = 1;
                foreach (var item in items)
                {
                    var prefix = child.TagName.Equals("OL", StringComparison.OrdinalIgnoreCase) ? $"{index++}." : "•";
                    sb.AppendLine($"{prefix} {item.TextContent.Trim()}");
                }

                sb.AppendLine();
            }
            else if (child.TagName.Equals("TABLE", StringComparison.OrdinalIgnoreCase))
            {
                 // Simple table extraction: just row by row
                 var rows = child.QuerySelectorAll("tr");
                 foreach (var row in rows)
                 {
                     var cells = row.QuerySelectorAll("td");
                     var rowText = string.Join(" | ", cells.Select(c => c.TextContent.Trim()));
                     sb.AppendLine(rowText);
                 }

                 sb.AppendLine();
            }
        }

        return CleanText(sb.ToString().Trim());
    }

    private static string CleanText(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return input;

        // Remove HTML tags that might have been double-encoded or preserved
        return System.Text.RegularExpressions.Regex.Replace(input, "<.*?>", string.Empty);
    }
}
