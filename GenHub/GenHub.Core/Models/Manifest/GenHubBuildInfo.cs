using System.Collections.Generic;

namespace GenHub.Core.Models.Manifest;

/// <summary>
/// Contains metadata and categorization extracted from a GenHub application executable, installer, or package.
/// </summary>
public sealed record GenHubBuildInfo
{
    /// <summary>
    /// Gets a value indicating whether the inspected file or directory is a valid GenHub build.
    /// </summary>
    public bool IsGenHubBuild { get; init; }

    /// <summary>
    /// Gets the normalized semantic version (e.g., "0.0.150", "1.0.0-custom.1").
    /// </summary>
    public string? Version { get; init; }

    /// <summary>
    /// Gets the raw product version string (e.g. "0.0.150+abc1234").
    /// </summary>
    public string? ProductVersion { get; init; }

    /// <summary>
    /// Gets the raw file version string (e.g. "0.0.150.0").
    /// </summary>
    public string? FileVersion { get; init; }

    /// <summary>
    /// Gets the product name (e.g., "GenHub").
    /// </summary>
    public string? ProductName { get; init; }

    /// <summary>
    /// Gets the company or author name (e.g., "Community Outpost").
    /// </summary>
    public string? CompanyName { get; init; }

    /// <summary>
    /// Gets the file description or assembly description.
    /// </summary>
    public string? FileDescription { get; init; }

    /// <summary>
    /// Gets the git commit short hash if embedded in build metadata.
    /// </summary>
    public string? GitShortHash { get; init; }

    /// <summary>
    /// Gets the pull request number if this is a PR build.
    /// </summary>
    public int? PullRequestNumber { get; init; }

    /// <summary>
    /// Gets the build channel: "Release", "PR", "Dev", "Test", or "CustomFork".
    /// </summary>
    public string BuildChannel { get; init; } = "Release";

    /// <summary>
    /// Gets a value indicating whether this build is a custom or modified build (not official Community Outpost release).
    /// </summary>
    public bool IsCustomBuild { get; init; }

    /// <summary>
    /// Gets a value indicating whether this build originated from an identified community fork.
    /// </summary>
    public bool IsFork { get; init; }

    /// <summary>
    /// Gets the name of the fork if detected.
    /// </summary>
    public string? ForkName { get; init; }

    /// <summary>
    /// Gets the suggested content ID for Publisher Studio (e.g. "genhub-release", "genhub-pr-123", "genhub-fork-user").
    /// </summary>
    public string SuggestedContentId { get; init; } = string.Empty;

    /// <summary>
    /// Gets the suggested human-readable content name (e.g. "GenHub (PR #123 Build)").
    /// </summary>
    public string SuggestedContentName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the suggested category for releases (e.g. "Release", "Test", "Dev", "CustomFork").
    /// </summary>
    public string SuggestedCategory { get; init; } = "Release";

    /// <summary>
    /// Gets the suggested description summarizing the build.
    /// </summary>
    public string SuggestedDescription { get; init; } = string.Empty;

    /// <summary>
    /// Gets suggested tags (e.g. "genhub", "build", "pr-123", "dev").
    /// </summary>
    public IReadOnlyList<string> SuggestedTags { get; init; } = [];

    /// <summary>
    /// Gets the primary executable entry point relative to the payload root (e.g. "GenHub.Windows.exe").
    /// </summary>
    public string? EntryPoint { get; init; }
}
