using System;
using System.Linq;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;

namespace GenHub.Features.Content.Services.Helpers;

/// <summary>
/// Provides helper methods for AODMaps content processing.
/// </summary>
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
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;

        // Strip HTML tags using compiled regex
        return HtmlTagRegex().Replace(html, string.Empty).Trim();
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

    /// <summary>
    /// Regex to strip HTML tags from text.
    /// </summary>
    [GeneratedRegex("<.*?>", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlTagRegex();
}
