using System;
using System.Text;
using System.Text.RegularExpressions;

namespace GenHub.Core.Helpers;

/// <summary>
/// Formats text and markdown to make GitHub PR/issue references, mentions, and raw URLs clickable,
/// normalizes list formatting so descriptions render correctly in Markdown viewers,
/// and sanitizes untrusted markdown links and images to prevent arbitrary scheme execution.
/// </summary>
public static partial class MarkdownLinkFormatter
{
    /// <summary>
    /// Formats the input text into markdown with clickable links for PRs, issues, GitHub users, and bare URLs,
    /// normalizes bullet lists and line breaks for Markdown viewers, and sanitizes untrusted links/images.
    /// </summary>
    /// <param name="text">The raw text or markdown to format.</param>
    /// <param name="sourceUrl">The optional repository or source URL to resolve relative issue/PR numbers.</param>
    /// <returns>The formatted markdown string.</returns>
    public static string FormatLinks(string? text, string? sourceUrl = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var (owner, repo) = ExtractGitHubOwnerRepo(sourceUrl, text);

        var result = ConvertHtmlImagesToMarkdown(text);
        result = UnwrapHtmlBlockWrappers(result);
        result = SanitizeMarkdownLinksAndImages(result);
        result = TransformOutsideHtmlTags(result, TransformGitHubUrls);
        result = TransformOutsideHtmlTags(result, segment => TransformIssueReferences(segment, owner, repo));
        result = TransformOutsideHtmlTags(result, TransformGitHubMentions);
        result = TransformOutsideHtmlTags(result, TransformBareUrls);
        result = NormalizeBulletLists(result);

        return result;
    }

    /// <summary>
    /// Extracts GitHub owner and repository name from a source URL or from text content.
    /// </summary>
    /// <param name="sourceUrl">The source URL to inspect.</param>
    /// <param name="fallbackText">Fallback text to search for a GitHub repository URL if sourceUrl is absent.</param>
    /// <returns>A tuple of (Owner, Repo) if found; otherwise (null, null).</returns>
    public static (string? Owner, string? Repo) ExtractGitHubOwnerRepo(string? sourceUrl, string? fallbackText = null)
    {
        if (!string.IsNullOrWhiteSpace(sourceUrl))
        {
            var match = GitHubRepoUrlRegex().Match(sourceUrl);
            if (match.Success)
            {
                return (match.Groups["owner"].Value, match.Groups["repo"].Value);
            }
        }

        if (!string.IsNullOrWhiteSpace(fallbackText))
        {
            var match = GitHubRepoUrlRegex().Match(fallbackText);
            if (match.Success)
            {
                return (match.Groups["owner"].Value, match.Groups["repo"].Value);
            }
        }

        return (null, null);
    }

    /// <summary>
    /// Preserves single line breaks within descriptions by ensuring regular lines end with two spaces,
    /// enabling Markdown renderers to render newlines without collapsing into single lines.
    /// Code blocks, headers, blockquotes, and lists are preserved untouched.
    /// </summary>
    /// <param name="text">The raw description text.</param>
    /// <returns>Text with hard line breaks preserved for CommonMark rendering.</returns>
    public static string PreserveLineBreaks(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalized.Split('\n');
        var builder = new StringBuilder(normalized.Length + (lines.Length * 2));
        var inCodeFence = false;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.Trim();

            if (trimmed.StartsWith("```", StringComparison.Ordinal) && !trimmed[3..].Contains('`'))
            {
                inCodeFence = !inCodeFence;
                builder.Append(line);
                if (i < lines.Length - 1)
                {
                    builder.Append('\n');
                }

                continue;
            }

            if (inCodeFence || string.IsNullOrWhiteSpace(line) ||
                trimmed.StartsWith('#') ||
                trimmed.StartsWith('>') ||
                trimmed.StartsWith('|') ||
                trimmed.StartsWith("- ", StringComparison.Ordinal) ||
                trimmed.StartsWith("* ", StringComparison.Ordinal) ||
                trimmed.StartsWith("+ ", StringComparison.Ordinal) ||
                IsOrderedListMarker(trimmed) ||
                line.EndsWith("  ", StringComparison.Ordinal) ||
                line.EndsWith('\\'))
            {
                builder.Append(line);
            }
            else
            {
                builder.Append(line).Append("  ");
            }

            if (i < lines.Length - 1)
            {
                builder.Append('\n');
            }
        }

        return builder.ToString();
    }

    private static string ConvertHtmlImagesToMarkdown(string text)
    {
        return HtmlImageRegex().Replace(text, m =>
        {
            var url = HtmlSrcAttributeRegex().Match(m.Value).Groups["url"].Value.Trim();
            var alt = HtmlAltAttributeRegex().Match(m.Value).Groups["alt"].Value.Trim()
                .Replace("[", string.Empty)
                .Replace("]", string.Empty);
            if (string.IsNullOrWhiteSpace(url))
            {
                return alt;
            }

            return $"![{alt}]({EncodeMarkdownDestination(url)})";
        });
    }

    private static string EncodeMarkdownDestination(string url)
    {
        var builder = new StringBuilder(url.Length);
        foreach (var character in url)
        {
            if (char.IsWhiteSpace(character) || char.IsControl(character))
            {
                foreach (var b in Encoding.UTF8.GetBytes(character.ToString()))
                {
                    builder.Append('%');
                    builder.Append(b.ToString("X2"));
                }
            }
            else
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Replaces HTML paragraph and division wrappers with blank lines so markdown inside
    /// them (such as converted images) is parsed instead of treated as raw HTML.
    /// </summary>
    /// <param name="text">The text to unwrap.</param>
    /// <returns>The text with block wrappers replaced by blank lines.</returns>
    private static string UnwrapHtmlBlockWrappers(string text)
    {
        return HtmlBlockWrapperRegex().Replace(text, "\n\n");
    }

    private static string TransformOutsideHtmlTags(string text, Func<string, string> transform)
    {
        var matches = HtmlTagRegex().Matches(text);
        if (matches.Count == 0)
        {
            return transform(text);
        }

        var builder = new StringBuilder(text.Length);
        var position = 0;
        foreach (Match match in matches)
        {
            builder.Append(transform(text[position..match.Index]));
            builder.Append(match.Value);
            position = match.Index + match.Length;
        }

        builder.Append(transform(text[position..]));
        return builder.ToString();
    }

    private static string SanitizeMarkdownLinksAndImages(string text)
    {
        // 1. Sanitize outer hyperlinks first to prevent bypasses via badge-style image links
        var result = MarkdownHyperlinkRegex().Replace(text, m =>
        {
            var url = m.Groups["url"].Value.Trim();
            if (IsSafeWebUrl(url))
            {
                return m.Value;
            }

            return m.Groups["text"].Value;
        });

        // 2. Sanitize embedded images
        return MarkdownImageRegex().Replace(result, m =>
        {
            var url = m.Groups["url"].Value.Trim();
            if (IsSafeWebUrl(url))
            {
                return m.Value;
            }

            return m.Groups["alt"].Value;
        });
    }

    private static bool IsSafeWebUrl(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private static string TransformGitHubUrls(string text)
    {
        var result = GitHubPullUrlRegex().Replace(text, m =>
        {
            if (m.Groups["mdlink"].Success)
            {
                return m.Value;
            }

            var url = m.Groups["url"].Value;
            var num = m.Groups["num"].Value;
            return $"[#{num}]({url})";
        });

        return GitHubCommitUrlRegex().Replace(result, m =>
        {
            if (m.Groups["mdlink"].Success)
            {
                return m.Value;
            }

            var url = m.Groups["url"].Value;
            var sha = m.Groups["sha"].Value;
            var shortSha = sha.Length > 7 ? sha[..7] : sha;
            return $"[`{shortSha}`]({url})";
        });
    }

    private static string TransformIssueReferences(string text, string? owner, string? repo)
    {
        if (string.IsNullOrEmpty(owner) || string.IsNullOrEmpty(repo))
        {
            return text;
        }

        var baseRepoUrl = $"https://github.com/{owner}/{repo}";

        var result = ParenthesizedIssueRegex().Replace(text, m =>
        {
            var num = m.Groups["num"].Value;
            return $"([#{num}]({baseRepoUrl}/pull/{num}))";
        });

        return StandaloneIssueRegex().Replace(result, m =>
        {
            var prefix = m.Groups["prefix"].Value;
            var num = m.Groups["num"].Value;
            return $"{prefix}[#{num}]({baseRepoUrl}/pull/{num})";
        });
    }

    private static string TransformGitHubMentions(string text)
    {
        return GitHubMentionRegex().Replace(text, m =>
        {
            var prefix = m.Groups["prefix"].Value;
            var user = m.Groups["user"].Value;
            return $"{prefix}[@{user}](https://github.com/{user})";
        });
    }

    private static string TransformBareUrls(string text)
    {
        return BareUrlRegex().Replace(text, m =>
        {
            if (m.Groups["mdlink"].Success)
            {
                return m.Value;
            }

            var url = m.Groups["url"].Value;
            var trimmedLength = GetTrimmedUrlLength(url);

            if (trimmedLength == url.Length)
            {
                return $"[{url}]({url})";
            }

            var cleanUrl = url[..trimmedLength];
            var trailing = url[trimmedLength..];
            return $"[{cleanUrl}]({cleanUrl}){trailing}";
        });
    }

    private static int GetTrimmedUrlLength(string url)
    {
        var trimmedLength = url.Length;

        while (trimmedLength > 0)
        {
            var ch = url[trimmedLength - 1];
            if (".,;:?!]".Contains(ch))
            {
                trimmedLength--;
            }
            else if (ch == ')' && HasUnbalancedTrailingParen(url, trimmedLength))
            {
                trimmedLength--;
            }
            else
            {
                break;
            }
        }

        return trimmedLength;
    }

    private static bool HasUnbalancedTrailingParen(string url, int length)
    {
        var openCount = 0;
        var closeCount = 0;
        for (var i = 0; i < length; i++)
        {
            if (url[i] == '(')
            {
                openCount++;
            }
            else if (url[i] == ')')
            {
                closeCount++;
            }
        }

        return closeCount > openCount;
    }

    private static string NormalizeBulletLists(string text)
    {
        if (!text.Contains("```"))
        {
            var result = BulletListRegex().Replace(text, "${indent}- ");
            return ListPrecedingBlankLineRegex().Replace(result, "${prev}\n\n${curr}");
        }

        var segments = text.Split("```");
        for (var i = 0; i < segments.Length; i += 2)
        {
            var segment = BulletListRegex().Replace(segments[i], "${indent}- ");
            segments[i] = ListPrecedingBlankLineRegex().Replace(segment, "${prev}\n\n${curr}");
        }

        return string.Join("```", segments);
    }

    [GeneratedRegex(@"!\[(?<alt>[^\]]*)\]\(\s*(?<url>(?:[^\s()]|\([^\s()]*\))+)(?:\s+[""'][^""']*[""'])?\s*\)")]
    private static partial Regex MarkdownImageRegex();

    [GeneratedRegex(@"<[^<>]*>")]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"<img\b[^<>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlImageRegex();

    [GeneratedRegex(@"\bsrc\s*=\s*(?:""(?<url>[^""]*)""|'(?<url>[^']*)')", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlSrcAttributeRegex();

    [GeneratedRegex(@"\balt\s*=\s*(?:""(?<alt>[^""]*)""|'(?<alt>[^']*)')", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlAltAttributeRegex();

    [GeneratedRegex(@"</?(?:p|div)\b[^<>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlBlockWrapperRegex();

    [GeneratedRegex(@"(?<!\!)\[(?<text>(?:[^\[\]]|\[[^\]]*\])*)\]\(\s*(?<url>(?:[^\s()]|\([^\s()]*\))+)(?:\s+[""'][^""']*[""'])?\s*\)")]
    private static partial Regex MarkdownHyperlinkRegex();

    [GeneratedRegex(@"https?://github\.com/(?<owner>[a-zA-Z0-9_\-\.]+)/(?<repo>[a-zA-Z0-9_\-\.]+)(?:/|$|\.git)", RegexOptions.IgnoreCase)]
    private static partial Regex GitHubRepoUrlRegex();

    [GeneratedRegex(@"(?<mdlink>\[(?:[^\[\]]|\[[^\]]*\])*\]\([^)]*\))|(?<url>https?://github\.com/(?<owner>[a-zA-Z0-9_\-\.]+)/(?<repo>[a-zA-Z0-9_\-\.]+)/(?:pull|issues)/(?<num>\d+))", RegexOptions.IgnoreCase)]
    private static partial Regex GitHubPullUrlRegex();

    [GeneratedRegex(@"(?<mdlink>\[(?:[^\[\]]|\[[^\]]*\])*\]\([^)]*\))|(?<url>https?://github\.com/(?<owner>[a-zA-Z0-9_\-\.]+)/(?<repo>[a-zA-Z0-9_\-\.]+)/commit/(?<sha>[a-fA-F0-9]{7,40}))", RegexOptions.IgnoreCase)]
    private static partial Regex GitHubCommitUrlRegex();

    [GeneratedRegex(@"\((?:#(?<num>\d+))\)")]
    private static partial Regex ParenthesizedIssueRegex();

    [GeneratedRegex(@"(?<prefix>(?:^|[\s,;]))#(?<num>\d+)\b")]
    private static partial Regex StandaloneIssueRegex();

    [GeneratedRegex(@"(?<prefix>(?:^|[\s(]))@(?<user>[a-zA-Z0-9_\-]+)\b(?!\.)")]
    private static partial Regex GitHubMentionRegex();

    [GeneratedRegex(@"(?<mdlink>\[(?:[^\[\]]|\[[^\]]*\])*\]\([^)]*\))|(?<url>https?://[^\s<>""]+)")]
    private static partial Regex BareUrlRegex();

    [GeneratedRegex(@"^(?<indent>[ \t]*)[\u2022\u25cf\u25aa\u25ab][ \t]+", RegexOptions.Multiline)]
    private static partial Regex BulletListRegex();

    [GeneratedRegex(@"(?<prev>^[ \t]*[^\s\-*+>#|`].*)\r?\n(?<curr>[ \t]*[-*+][ \t]+)", RegexOptions.Multiline)]
    private static partial Regex ListPrecedingBlankLineRegex();
    private static bool IsOrderedListMarker(string trimmed)
    {
        var i = 0;
        while (i < trimmed.Length && char.IsDigit(trimmed[i]) && i < 10)
        {
            i++;
        }

        return i > 0 && i < trimmed.Length - 1
            && (trimmed[i] == '.' || trimmed[i] == ')')
            && char.IsWhiteSpace(trimmed[i + 1]);
    }
}
