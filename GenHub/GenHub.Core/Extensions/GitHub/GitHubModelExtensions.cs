using System.Text.RegularExpressions;
using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GitHub;

namespace GenHub.Core.Extensions.GitHub;

/// <summary>
/// Extension methods for GitHub-related model classes.
/// </summary>
public static class GitHubModelExtensions
{
    /// <summary>
    /// Creates a deep copy of a GitHubArtifact instance.
    /// </summary>
    /// <param name="artifact">The artifact to copy.</param>
    /// <returns>A deep copy of the artifact.</returns>
    public static GitHubArtifact CreateCopy(this GitHubArtifact artifact)
    {
        if (artifact == null) return new GitHubArtifact();

        return new GitHubArtifact
        {
            Id = artifact.Id,
            Name = artifact.Name,
            WorkflowId = artifact.WorkflowId,
            RunId = artifact.RunId,
            WorkflowNumber = artifact.WorkflowNumber,
            SizeInBytes = artifact.SizeInBytes,
            IsRelease = artifact.IsRelease,
            DownloadUrl = artifact.DownloadUrl,
            ArchiveDownloadUrl = artifact.ArchiveDownloadUrl,
            Expired = artifact.Expired,
            CreatedAt = artifact.CreatedAt,
            ExpiresAt = artifact.ExpiresAt,
            PullRequestNumber = artifact.PullRequestNumber,
            PullRequestTitle = artifact.PullRequestTitle,
            CommitSha = artifact.CommitSha,
            CommitMessage = artifact.CommitMessage,
            EventType = artifact.EventType,
            BuildPreset = artifact.BuildPreset,
            BuildInfo = artifact.BuildInfo?.CreateCopy() ?? new GitHubBuild(),
            RepositoryInfo = artifact.RepositoryInfo?.CreateCopy() ?? new GitHubRepository(),
            IsActive = artifact.IsActive,
            IsInstalled = artifact.IsInstalled,
            WorkflowRun = artifact.WorkflowRun?.CreateCopy(),
        };
    }

    /// <summary>
    /// Gets a display name for an artifact based on its metadata.
    /// </summary>
    /// <param name="artifact">The artifact.</param>
    /// <returns>The display name.</returns>
    public static string GetDisplayName(this GitHubArtifact artifact)
    {
        if (artifact == null)
            return GitHubConstants.UnknownArtifactDisplayName;

        if (artifact.PullRequestNumber.HasValue && !string.IsNullOrWhiteSpace(artifact.PullRequestTitle))
            return string.Format(GitHubConstants.PullRequestDisplayFormat, artifact.PullRequestNumber, artifact.PullRequestTitle, artifact.Name);

        if (artifact.WorkflowNumber > 0)
            return string.Format(GitHubConstants.BuildDisplayFormat, artifact.WorkflowNumber, artifact.Name);

        return artifact.Name;
    }

    /// <summary>
    /// Gets a formatted size string for an artifact.
    /// </summary>
    /// <param name="artifact">The artifact.</param>
    /// <returns>The formatted size.</returns>
    public static string GetFormattedSize(this GitHubArtifact artifact)
    {
        if (artifact == null)
            return $"0 {GitHubConstants.BytesUnit}";

        string[] sizes = { GitHubConstants.BytesUnit, GitHubConstants.KilobytesUnit, GitHubConstants.MegabytesUnit, GitHubConstants.GigabytesUnit, GitHubConstants.TerabytesUnit };
        int order = 0;
        double size = artifact.SizeInBytes;

        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size = size / 1024;
        }

        return $"{size:0.##} {sizes[order]}";
    }

    /// <summary>
    /// Creates a deep copy of a GitHubBuild instance.
    /// </summary>
    /// <param name="build">The build to copy.</param>
    /// <returns>A deep copy of the build.</returns>
    public static GitHubBuild CreateCopy(this GitHubBuild build)
    {
        if (build == null) return new GitHubBuild();

        return new GitHubBuild
        {
            GameVariant = build.GameVariant,
            Compiler = build.Compiler,
            Configuration = build.Configuration,
            Version = build.Version,
            HasTFlag = build.HasTFlag,
            HasEFlag = build.HasEFlag,
        };
    }

    /// <summary>
    /// Creates a deep copy of a GitHubRepository instance.
    /// </summary>
    /// <param name="settings">The repository to copy.</param>
    /// <returns>A deep copy of the repository.</returns>
    public static GitHubRepository CreateCopy(this GitHubRepository settings)
    {
        if (settings == null) return new GitHubRepository();

        return new GitHubRepository
        {
            RepoOwner = settings.RepoOwner,
            RepoName = settings.RepoName,
            DisplayName = settings.DisplayName,
        };
    }

    /// <summary>
    /// Creates a copy of the GitHubWorkflow instance.
    /// </summary>
    /// <param name="workflow">The workflow to copy.</param>
    /// <returns>A new GitHubWorkflow instance with copied values.</returns>
    public static GitHubWorkflow CreateCopy(this GitHubWorkflow workflow)
    {
        if (workflow == null) return new GitHubWorkflow();

        return new GitHubWorkflow
        {
            Id = workflow.Id,
            Name = workflow.Name,
            Path = workflow.Path,
            State = workflow.State,
            CreatedAt = workflow.CreatedAt,
            UpdatedAt = workflow.UpdatedAt,
        };
    }

    /// <summary>
    /// Creates a deep copy of a GitHubWorkflowRun instance.
    /// </summary>
    /// <param name="run">The workflow run to copy.</param>
    /// <returns>A new GitHubWorkflowRun instance with copied values.</returns>
    public static GitHubWorkflowRun CreateCopy(this GitHubWorkflowRun run)
    {
        if (run == null) return new GitHubWorkflowRun();

        return new GitHubWorkflowRun
        {
            Id = run.Id,
            WorkflowId = run.WorkflowId,
            RunNumber = run.RunNumber,
            RunAttempt = run.RunAttempt,
            Status = run.Status,
            Conclusion = run.Conclusion,
            HeadBranch = run.HeadBranch,
            HeadSha = run.HeadSha,
            Event = run.Event,
            CreatedAt = run.CreatedAt,
            UpdatedAt = run.UpdatedAt,
            HtmlUrl = run.HtmlUrl,
            Workflow = run.Workflow?.CreateCopy() ?? new GitHubWorkflow(),
        };
    }

    /// <summary>
    /// Parses build information from an artifact name.
    /// </summary>
    /// <param name="artifactName">The name of the artifact.</param>
    /// <returns>Parsed GitHubBuild info or a default instance if parsing fails.</returns>
    public static GitHubBuild ParseBuildInfo(string artifactName)
    {
        if (string.IsNullOrEmpty(artifactName))
            return new GitHubBuild();

        var build = new GitHubBuild();

        // Extract game variant (e.g., Zero Hour, Generals)
        if (artifactName.Contains(GitHubConstants.ZeroHourIdentifier, StringComparison.OrdinalIgnoreCase))
            build.GameVariant = GameType.ZeroHour;
        else if (artifactName.Contains(GitHubConstants.GeneralsIdentifier, StringComparison.OrdinalIgnoreCase))
            build.GameVariant = GameType.Generals;

        // Extract configuration (Debug/Release)
        if (artifactName.Contains(GitHubConstants.DebugConfiguration, StringComparison.OrdinalIgnoreCase))
            build.Configuration = GitHubConstants.DebugConfiguration;
        else if (artifactName.Contains(GitHubConstants.ReleaseConfiguration, StringComparison.OrdinalIgnoreCase))
            build.Configuration = GitHubConstants.ReleaseConfiguration;

        // Extract compiler (if available)
        if (artifactName.Contains(GitHubConstants.MsvcCompiler, StringComparison.OrdinalIgnoreCase))
            build.Compiler = GitHubConstants.MsvcCompiler;
        else if (artifactName.Contains(GitHubConstants.GccCompiler, StringComparison.OrdinalIgnoreCase))
            build.Compiler = GitHubConstants.GccCompiler;

        // Extract version (if using format like "v1.2.3")
        var versionMatch = Regex.Match(artifactName, GitHubConstants.VersionRegexPattern);
        if (versionMatch.Success)
            build.Version = versionMatch.Groups[1].Value;

        // Extract special flags
        build.HasTFlag = artifactName.Contains(GitHubConstants.TFlag, StringComparison.OrdinalIgnoreCase);
        build.HasEFlag = artifactName.Contains(GitHubConstants.EFlag, StringComparison.OrdinalIgnoreCase);

        return build;
    }
}
