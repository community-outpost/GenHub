using GenHub.Core.Constants;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace GenHub.Features.Content.Services.Catalog;

/// <summary>
/// Generates custom tab IDs from tab headers for tabs that do not declare one.
/// </summary>
public static class CustomTabIdGenerator
{
    private const string FallbackPrefix = "tab-";
    private const int FallbackHashLength = 8;

    /// <summary>
    /// Builds a lowercase ASCII slug from a header, such as <c>how-to-play</c> for "How to Play".
    /// </summary>
    /// <remarks>
    /// Accents are folded to their base letters. A header with no ASCII letters or digits, such as Arabic
    /// or Cyrillic text, gets a stable <c>tab-</c> ID derived from a hash of the header.
    /// </remarks>
    /// <param name="header">The tab header.</param>
    /// <returns>The slug, or an empty string for a blank header.</returns>
    public static string Slugify(string? header)
    {
        if (string.IsNullOrWhiteSpace(header))
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var character in header.Trim().Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            var lower = char.ToLowerInvariant(character);
            if (lower is (>= 'a' and <= 'z') or (>= '0' and <= '9'))
            {
                builder.Append(lower);
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        var slug = Truncate(builder.ToString().Trim('-'), CustomTabConstants.MaxTabIdLength);
        if (slug.Length > 0)
        {
            return slug;
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(header.Trim()));
        return FallbackPrefix + Convert.ToHexString(hash)[..FallbackHashLength].ToLowerInvariant();
    }

    /// <summary>
    /// Suggests an ID for each tab without one, unique within the scope among declared and suggested IDs.
    /// </summary>
    /// <param name="tabs">The declared ID and header of each tab, in order.</param>
    /// <returns>The suggested ID per tab, or null when the tab declares an ID or has no header.</returns>
    public static IReadOnlyList<string?> Suggest(IReadOnlyList<(string? TabId, string? Header)> tabs)
    {
        ArgumentNullException.ThrowIfNull(tabs);

        var taken = new HashSet<string>(
            tabs.Where(tab => !string.IsNullOrWhiteSpace(tab.TabId)).Select(tab => tab.TabId!.Trim()),
            StringComparer.OrdinalIgnoreCase);
        var suggestions = new string?[tabs.Count];
        for (var i = 0; i < tabs.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(tabs[i].TabId))
            {
                continue;
            }

            var slug = Slugify(tabs[i].Header);
            if (slug.Length == 0)
            {
                continue;
            }

            var candidate = slug;
            for (var suffix = 2; !taken.Add(candidate); suffix++)
            {
                var tail = $"-{suffix}";
                candidate = Truncate(slug, CustomTabConstants.MaxTabIdLength - tail.Length).TrimEnd('-') + tail;
            }

            suggestions[i] = candidate;
        }

        return suggestions;
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength].TrimEnd('-');
}
