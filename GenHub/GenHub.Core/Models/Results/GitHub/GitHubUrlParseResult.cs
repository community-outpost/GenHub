using GenHub.Core.Models.Results;

namespace GenHub.Core.Models.GitHub;

/// <summary>Result type for parsing GitHub repository URLs. Inherits from ResultBase to align with project result semantics.</summary>
public sealed class GitHubUrlParseResult(bool success, string owner = "", string repo = "", string? tag = null, IEnumerable<string>? errors = null)
    : ResultBase(success, errors)
{
    /// <summary>Gets the repository owner (username or organization) parsed from the URL.</summary>
    public string Owner { get; } = owner;

    /// <summary>Gets the repository name parsed from the URL.</summary>
    public string Repo { get; } = repo;

    /// <summary>Gets the optional tag (release) parsed from the URL, if present.</summary>
    public string? Tag { get; } = tag;

    /// <summary>Creates a successful parse result containing the owner, repository and optional tag.</summary>
    /// <param name="owner">Repository owner (username or organization).</param>
    /// <param name="repo">Repository name.</param>
    /// <param name="tag">Optional release tag.</param>
    /// <returns>
    /// A <see cref="GitHubUrlParseResult"/> representing a successful parse.
    /// </returns>
    public static GitHubUrlParseResult CreateSuccess(
        string owner,
        string repo,
        string? tag) => new(true, owner, repo, tag);

    /// <summary>
    /// Creates a failed parse result containing one or more error messages.
    /// </summary>
    /// <param name="errors">Error messages describing the failure reason(s).</param>
    /// <returns>
    /// A <see cref="GitHubUrlParseResult"/> representing a failed parse.
    /// </returns>
    public static GitHubUrlParseResult CreateFailure(
        params string[] errors) => new(false, errors: errors);
}
