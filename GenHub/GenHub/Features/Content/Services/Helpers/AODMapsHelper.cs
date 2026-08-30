using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Models.Enums;

namespace GenHub.Features.Content.Services.Helpers;

/// <summary>
/// Provides helper methods for AODMaps content processing.
/// </summary>
[SuppressMessage("Minor Code Smell", "S101:Types should be named in PascalCase", Justification = "Domain acronym")]
public static partial class AODMapsHelper
{
    /// <summary>
    /// Gets the next non-empty text sibling of an element.
    /// </summary>
    /// <param name="element">The element to search from.</param>
    /// <returns>The next non-empty text sibling, or null if not found.</returns>
    public static string? GetNextNonEmptyTextSibling(IElement? element)
    {
        if (element == null) return null;
        var node = element.NextSibling;
        while (node != null)
        {
            if (node.NodeType == NodeType.Text && !string.IsNullOrWhiteSpace(node.TextContent))
            {
                return node.TextContent.Trim();
            }

            node = node.NextSibling;
        }

        return null;
    }

    /// <summary>
    /// Normalizes an HTML description by stripping tags.
    /// </summary>
    /// <param name="html">The HTML string to normalize.</param>
    /// <returns>The plain text content.</returns>
    public static string NormalizeHtmlDescription(string html)
    {
        return HtmlTextHelper.NormalizeHtml(html);
    }

    /// <summary>
    /// Extracts author name from the map title or source URL.
    /// </summary>
    /// <param name="title">The map title.</param>
    /// <param name="sourceUrl">The source URL containing the item.</param>
    /// <returns>The extracted author name, or null if not found.</returns>
    public static string? ExtractAuthor(string? title, string? sourceUrl)
    {
        if (!string.IsNullOrWhiteSpace(title))
        {
            var match = AuthorFromTitleRegex().Match(title);
            if (match.Success)
            {
                var candidate = match.Groups[1].Value.Trim();
                var recognized = AODMapsConstants.RecognizedMapMakers.FirstOrDefault(m =>
                    m.Equals(candidate, StringComparison.OrdinalIgnoreCase));
                return recognized ?? candidate;
            }
        }

        if (!string.IsNullOrWhiteSpace(sourceUrl))
        {
            var urlMatch = AuthorFromMapMakerUrlRegex().Match(sourceUrl);
            if (urlMatch.Success)
            {
                var candidate = urlMatch.Groups["maker"].Value.Trim();
                var recognized = AODMapsConstants.RecognizedMapMakers.FirstOrDefault(m =>
                    m.Equals(candidate, StringComparison.OrdinalIgnoreCase));
                return recognized ?? candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// Extracts special AI or game rule notes from the title.
    /// </summary>
    /// <param name="title">The map title.</param>
    /// <returns>A string with extracted notes, or null if none found.</returns>
    public static string? ExtractSpecialNotes(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var matches = SpecialNotesRegex().Matches(title);
        if (matches.Count == 0)
        {
            return null;
        }

        var notes = new List<string>();
        foreach (Match match in matches)
        {
            var cleaned = match.Value.Trim('[', ']', '(', ')').Trim();
            if (!string.IsNullOrWhiteSpace(cleaned) && !notes.Contains(cleaned, StringComparer.OrdinalIgnoreCase))
            {
                notes.Add(cleaned);
            }
        }

        return notes.Count > 0 ? string.Join(", ", notes) : null;
    }

    /// <summary>
    /// Builds a rich, natural description for a gallery map item.
    /// </summary>
    /// <param name="title">The map title.</param>
    /// <param name="playerCount">The number of players, if known.</param>
    /// <param name="category">The inferred category label.</param>
    /// <param name="author">The extracted author name.</param>
    /// <returns>A formatted descriptive string.</returns>
    public static string BuildRichMapDescription(string? title, int? playerCount, string? category, string? author)
    {
        var categoryName = category switch
        {
            AODMapsConstants.CategoryAoa => "Art of Attack",
            AODMapsConstants.CategoryContra => "Contra Art of Defense",
            AODMapsConstants.CategoryCompstomp => "Compstomp",
            AODMapsConstants.CategoryRace => "Race",
            AODMapsConstants.CategoryAir => "Air",
            AODMapsConstants.CategoryMapPacks => "Map Pack",
            _ => "Art of Defense",
        };

        var builder = new StringBuilder("Community ");
        builder.Append(categoryName);
        builder.Append(" map");

        if (playerCount.HasValue && playerCount.Value > 0)
        {
            builder.Append(playerCount.Value == 1 ? " for 1 player" : $" for {playerCount.Value} players");
        }

        if (!string.IsNullOrWhiteSpace(author) && !author.Equals(AODMapsConstants.DefaultAuthorName, StringComparison.OrdinalIgnoreCase))
        {
            builder.Append($" by {author}");
        }

        builder.Append('.');

        var notes = ExtractSpecialNotes(title);
        if (!string.IsNullOrWhiteSpace(notes))
        {
            builder.Append($" Notes: {notes}.");
        }

        return builder.ToString();
    }

    /// <summary>
    /// Extracts full description details from a Map Maker HTML content element.
    /// </summary>
    /// <param name="content">The map maker container element.</param>
    /// <param name="playerCount">The parsed player count.</param>
    /// <param name="category">The inferred category.</param>
    /// <param name="author">The map author.</param>
    /// <returns>A formatted multi-sentence or paragraph description.</returns>
    public static string ExtractMapMakerDescription(IElement content, int? playerCount, string? category, string? author)
    {
        var p1Text = content.QuerySelector(AODMapsConstants.MapMakerInfoSelector)?.TextContent?.Trim();
        var paragraphs = new List<string>();

        if (!string.IsNullOrWhiteSpace(p1Text))
        {
            var cleanedP1 = p1Text.TrimStart('-').Trim();
            paragraphs.Add(cleanedP1);
        }

        var pElements = content.QuerySelectorAll("p");
        foreach (var p in pElements)
        {
            var text = p.TextContent?.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            if (DownloadCounterScriptRegex().IsMatch(text))
            {
                continue;
            }

            var normalized = NormalizeHtmlDescription(text);
            if (!string.IsNullOrWhiteSpace(normalized) &&
                !normalized.Equals("info text will be here soon", StringComparison.OrdinalIgnoreCase) &&
                !normalized.Equals("&nbsp;", StringComparison.OrdinalIgnoreCase))
            {
                paragraphs.Add(normalized.TrimStart('-').Trim());
            }
        }

        if (paragraphs.Count > 0)
        {
            return string.Join(". ", paragraphs.Where(p => !string.IsNullOrWhiteSpace(p)));
        }

        var titleEl = content.QuerySelector(AODMapsConstants.MapMakerTitleSelector);
        var title = titleEl?.TextContent?.Trim();
        return BuildRichMapDescription(title, playerCount, category, author);
    }

    /// <summary>
    /// Gets the game type and content type from a document's breadcrumb.
    /// </summary>
    /// <param name="document">The HTML document to analyze.</param>
    /// <returns>A tuple containing the game type and content type.</returns>
    public static (GameType GameType, ContentType ContentType) ExtractBreadcrumbCategory(IDocument document)
    {
        var breadcrumbs = document.QuerySelector(AODMapsConstants.BreadcrumbHeaderSelector);
        if (breadcrumbs == null)
        {
            return (GameType.ZeroHour, ContentType.Map);
        }

        var text = breadcrumbs.TextContent;

        var gameType = GameType.ZeroHour;
        if (text.Contains("Generals", StringComparison.OrdinalIgnoreCase) && !text.Contains("Zero Hour", StringComparison.OrdinalIgnoreCase))
        {
            gameType = GameType.Generals;
        }

        var contentType = ContentType.Map;
        if (text.Contains("Mission", StringComparison.OrdinalIgnoreCase))
        {
            contentType = ContentType.Mission;
        }
        else if (text.Contains("Pack", StringComparison.OrdinalIgnoreCase))
        {
            contentType = ContentType.Map; // Or another type if appropriate for packs
        }

        return (gameType, contentType);
    }

    [GeneratedRegex(@"\b(?:created\s+by|remade\s+by|remoded\s+by|made\s+by|by)\s+([A-Za-z0-9_\^\-\[\]]+)", RegexOptions.IgnoreCase)]
    private static partial Regex AuthorFromTitleRegex();

    [GeneratedRegex(@"/mapmakers/MM_P/(?<maker>[^/]+)/", RegexOptions.IgnoreCase)]
    private static partial Regex AuthorFromMapMakerUrlRegex();

    [GeneratedRegex(@"(\[(?:No\s+[^\]]+|[^\]]*Laser[^\]]*|[^\]]*EMP[^\]]*)\]|\((?:AI\b[^)]*|USA\s+AI[^)]*|China\s+AI[^)]*|GLA\s+AI[^)]*|No\s+USA[^)]*|for\s+proplayers|Money|All\s+players\b[^)]*)\))", RegexOptions.IgnoreCase)]
    private static partial Regex SpecialNotesRegex();

    [GeneratedRegex(@"ccount_display|\btimes\s*downloaded\b", RegexOptions.IgnoreCase)]
    private static partial Regex DownloadCounterScriptRegex();
}
