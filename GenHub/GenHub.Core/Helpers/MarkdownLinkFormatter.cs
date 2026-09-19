using System;
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

        var result = SanitizeMarkdownLinksAndImages(text);
        result = TransformGitHubUrls(result);
        result = TransformIssueReferences(result, owner, repo);
        result = TransformGitHubMentions(result);
        result = TransformBareUrls(result);
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
}
