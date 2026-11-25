using GenHub.Core.Constants;
using System;

namespace GenHub.Core.Models.Tools.ModBuilder;

/// <summary>
/// Identifies a GitHub repository branch to import as a ModBuilder project.
/// </summary>
/// <param name="Owner">The repository owner.</param>
/// <param name="Repo">The repository name.</param>
/// <param name="Branch">The branch to import.</param>
public sealed record GitHubRepositoryReference(string Owner, string Repo, string Branch)
{
    private const int MaxSegmentLength = 100;
    private const int MaxInputLength = 500;

    /// <summary>
    /// Gets the owner and repository in owner/repo form.
    /// </summary>
    public string FullName => $"{Owner}/{Repo}";

    /// <summary>
    /// Parses user input into a repository reference. Accepts owner/repo, owner/repo@branch,
    /// and GitHub URLs such as https://github.com/owner/repo or .../tree/branch.
    /// </summary>
    /// <param name="input">The raw user input.</param>
    /// <param name="defaultBranch">The fallback branch used when the input does not embed one.</param>
    /// <returns>The parsed reference, or null when the input is not a valid repository reference.</returns>
    public static GitHubRepositoryReference? TryParse(string? input, string? defaultBranch = null)
    {
        if (string.IsNullOrWhiteSpace(input) || input.Length > MaxInputLength)
        {
            return null;
        }

        var candidate = input.Trim().TrimEnd('/');

        if (!TryStripUrlPrefix(ref candidate))
        {
            return null;
        }

        if (!TryExtractAtBranch(ref candidate, out var embeddedBranch))
        {
            return null;
        }

        var segments = candidate.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length < 2)
        {
            return null;
        }

        var owner = segments[0];
        var repo = StripGitSuffix(segments[1]);
        if (!IsValidSegment(owner) || !IsValidSegment(repo))
        {
            return null;
        }

        if (segments.Length > 2)
        {
            if (!TryParseTreeBranch(segments, out var treeBranch))
            {
                return null;
            }

            embeddedBranch ??= treeBranch;
        }

        var effectiveBranch = ResolveEffectiveBranch(embeddedBranch, defaultBranch);
        if (!IsValidBranch(effectiveBranch))
        {
            return null;
        }

        return new GitHubRepositoryReference(owner, repo, effectiveBranch);
    }

    private static bool TryExtractAtBranch(ref string candidate, out string? branch)
    {
        branch = null;
        if (candidate.Contains("/tree/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var atIndex = candidate.IndexOf('@');
        if (atIndex < 0)
        {
            return true;
        }

        var explicitBranch = candidate.Substring(atIndex + 1).Trim();
        candidate = candidate.Substring(0, atIndex).TrimEnd('/');
        if (!IsValidBranch(explicitBranch))
        {
            return false;
        }

        branch = explicitBranch;
        return true;
    }

    private static string ResolveEffectiveBranch(string? embeddedBranch, string? defaultBranch)
    {
        if (!string.IsNullOrWhiteSpace(embeddedBranch))
        {
            return embeddedBranch.Trim();
        }

        if (!string.IsNullOrWhiteSpace(defaultBranch))
        {
            return defaultBranch.Trim();
        }

        return ModBuilderConstants.GitHubDefaultBranch;
    }

    private static bool TryStripUrlPrefix(ref string candidate)
    {
        var span = candidate.AsSpan().Trim();

        var insecurePrefix = string.Concat(Uri.UriSchemeHttp, Uri.SchemeDelimiter);
        if (span.StartsWith(insecurePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var securePrefix = string.Concat(Uri.UriSchemeHttps, Uri.SchemeDelimiter);
        var hadUrlPrefix = false;
        if (span.StartsWith(securePrefix, StringComparison.OrdinalIgnoreCase))
        {
            span = span[securePrefix.Length..].TrimStart();
            hadUrlPrefix = true;
        }

        const string wwwPrefix = "www.";
        if (span.StartsWith(wwwPrefix, StringComparison.OrdinalIgnoreCase))
        {
            span = span[wwwPrefix.Length..].TrimStart();
            hadUrlPrefix = true;
        }

        if (span.StartsWith(ApiConstants.GitHubDomain, StringComparison.OrdinalIgnoreCase))
        {
            var remainder = span[ApiConstants.GitHubDomain.Length..];
            if (remainder.IsEmpty || remainder.StartsWith("/") || remainder.StartsWith("\\"))
            {
                candidate = remainder.TrimStart("/\\ ").ToString();
                return candidate.Length > 0;
            }

            if (hadUrlPrefix)
            {
                return false;
            }
        }

        if (hadUrlPrefix || span.IndexOf("://".AsSpan(), StringComparison.Ordinal) >= 0)
        {
            return false;
        }

        candidate = span.ToString();
        return true;
    }

    private static string StripGitSuffix(string candidate)
    {
        return candidate.EndsWith(".git", StringComparison.OrdinalIgnoreCase)
            ? candidate.Substring(0, candidate.Length - 4).TrimEnd('/')
            : candidate;
    }

    private static bool TryParseTreeBranch(string[] segments, out string? branch)
    {
        branch = null;
        if (!segments[2].Equals("tree", StringComparison.OrdinalIgnoreCase) || segments.Length < 4)
        {
            return false;
        }

        var treeBranch = string.Join('/', segments, 3, segments.Length - 3);
        if (!IsValidBranch(treeBranch))
        {
            return false;
        }

        branch = treeBranch;
        return true;
    }

    private static bool IsValidSegment(string segment)
    {
        if (segment.Length == 0 || segment.Length > MaxSegmentLength)
        {
            return false;
        }

        foreach (var c in segment)
        {
            var allowed = char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.';
            if (!allowed)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsValidBranch(string branch)
    {
        const int maxRefLength = 255;
        if (string.IsNullOrWhiteSpace(branch) || branch.Length > maxRefLength)
        {
            return false;
        }

        if (branch.StartsWith('/') || branch.EndsWith('/') || branch.StartsWith('.') || branch.EndsWith('.'))
        {
            return false;
        }

        if (branch.EndsWith(".lock", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (branch.Contains("//", StringComparison.Ordinal) ||
            branch.Contains("..", StringComparison.Ordinal) ||
            branch.Contains("@{", StringComparison.Ordinal))
        {
            return false;
        }

        if (branch == "@")
        {
            return false;
        }

        foreach (var c in branch)
        {
            if (c < 32 || c == 127)
            {
                return false;
            }

            if (c == ' ' || c == '~' || c == '^' || c == ':' || c == '?' || c == '*' || c == '[' || c == '\\')
            {
                return false;
            }
        }

        return true;
    }
}
