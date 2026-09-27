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
    /// <param name="defaultBranch">The branch used when the input names none.</param>
    /// <returns>The parsed reference, or null when the input is not a valid repository reference.</returns>
    public static GitHubRepositoryReference? TryParse(string? input, string? defaultBranch = null)
    {
        if (string.IsNullOrWhiteSpace(input) || input.Length > MaxInputLength)
        {
            return null;
        }

        var branch = string.IsNullOrWhiteSpace(defaultBranch) ? ModBuilderConstants.GitHubDefaultBranch : defaultBranch.Trim();
        var candidate = input.Trim().TrimEnd('/');

        if (!TryStripUrlPrefix(ref candidate) || candidate.Contains("://", StringComparison.Ordinal))
        {
            return null;
        }

        candidate = StripGitSuffix(candidate);

        var atIndex = candidate.IndexOf('@');
        if (atIndex >= 0)
        {
            var explicitBranch = candidate.Substring(atIndex + 1).Trim();
            candidate = candidate.Substring(0, atIndex).TrimEnd('/');
            if (!IsValidBranch(explicitBranch))
            {
                return null;
            }

            branch = explicitBranch;
        }

        var segments = candidate.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length < 2)
        {
            return null;
        }

        var owner = segments[0];
        var repo = segments[1];
        if (!IsValidSegment(owner) || !IsValidSegment(repo))
        {
            return null;
        }

        if (segments.Length > 2)
        {
            if (!TryParseTreeBranch(segments, ref branch))
            {
                return null;
            }
        }

        if (!IsValidBranch(branch))
        {
            return null;
        }

        return new GitHubRepositoryReference(owner, repo, branch);
    }

    private static bool TryStripUrlPrefix(ref string candidate)
    {
        var lower = candidate.ToLowerInvariant();
        var hostIndex = lower.IndexOf(ApiConstants.GitHubDomain, StringComparison.Ordinal);
        if (hostIndex < 0)
        {
            return true;
        }

        var afterHost = candidate.Substring(hostIndex + ApiConstants.GitHubDomain.Length).Trim();
        if (hostIndex > 0)
        {
            var prefix = candidate.Substring(0, hostIndex);
            if (!IsAllowedUrlPrefix(prefix))
            {
                return false;
            }
        }

        candidate = afterHost.TrimStart('/').Trim();
        return candidate.Length > 0;
    }

    private static bool IsAllowedUrlPrefix(string prefix)
    {
        var normalized = prefix.Trim().TrimEnd('/').ToLowerInvariant();
        return normalized is "" or "https:" or "http:" or "https:/" or "http:/" or "www." or "https://www." or "http://www.";
    }

    private static string StripGitSuffix(string candidate)
    {
        return candidate.EndsWith(".git", StringComparison.OrdinalIgnoreCase)
            ? candidate.Substring(0, candidate.Length - 4).TrimEnd('/')
            : candidate;
    }

    private static bool TryParseTreeBranch(string[] segments, ref string branch)
    {
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
        if (string.IsNullOrWhiteSpace(branch) || branch.Length > MaxSegmentLength)
        {
            return false;
        }

        foreach (var c in branch)
        {
            var allowed = char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.' || c == '/';
            if (!allowed)
            {
                return false;
            }
        }

        return true;
    }
}
