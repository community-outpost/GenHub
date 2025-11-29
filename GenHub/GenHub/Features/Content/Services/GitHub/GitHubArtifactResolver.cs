using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GitHub;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Features.Content.Services.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Content.Services.ContentResolvers;

/// <summary>
/// Resolves GitHub workflow artifacts into ContentManifest objects.
/// </summary>
public class GitHubArtifactResolver(
    IServiceProvider serviceProvider,
    ILogger<GitHubArtifactResolver> logger) : IContentResolver
{
    /// <summary>
    /// Gets the unique identifier for the GitHub artifact content resolver.
    /// </summary>
    public string ResolverId => GitHubConstants.GitHubArtifactResolverId;

    /// <summary>
    /// Resolves a discovered GitHub workflow artifact into a full ContentManifest.
    /// </summary>
    /// <param name="discoveredItem">The discovered content to resolve.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A <see cref="OperationResult{ContentManifest}"/> containing the resolved manifest or an error.</returns>
    public async Task<OperationResult<ContentManifest>> ResolveAsync(
        ContentSearchResult discoveredItem,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Extract metadata from the discovered item
            if (!discoveredItem.ResolverMetadata.TryGetValue("owner", out var owner)
                || !discoveredItem.ResolverMetadata.TryGetValue("repo", out var repo))
            {
                return OperationResult<ContentManifest>.CreateFailure("Missing required metadata for GitHub artifact resolution (owner, repo)");
            }

            // The artifact with all metadata (including WorkflowRun) is passed in discoveredItem.Data
            if (discoveredItem.Data is not GitHubArtifact artifact)
            {
                return OperationResult<ContentManifest>.CreateFailure("Invalid data type - expected GitHubArtifact");
            }

            var artifactName = artifact.Name ?? discoveredItem.Name;

            // Get the workflow run info from the artifact (already attached)
            var workflowRun = artifact.WorkflowRun;
            if (workflowRun == null)
            {
                logger.LogWarning("Artifact {ArtifactId} has no WorkflowRun attached, using fallback data", artifact.Id);

                // Fallback: use artifact properties directly
                var runNumber = artifact.WorkflowNumber;

                // Content name for manifest ID: "owner-repo-runN"
                var contentName = $"{owner}-{repo}-run{runNumber}";

                // Create a new manifest builder for each resolve operation to ensure clean state
                var manifestBuilder = serviceProvider.GetRequiredService<IContentManifestBuilder>();

                var manifest = manifestBuilder
                    .WithBasicInfo(
                    "github", // Publisher ID (builder generates manifest ID)
                    contentName, // Content name
                    runNumber) // Version
                    .WithContentType(discoveredItem.ContentType, discoveredItem.TargetGame)
                    .WithPublisher(
                    name: $"github-{owner}",
                    website: string.Empty,
                    supportUrl: string.Empty,
                    contactEmail: string.Empty,
                    publisherType: "github")
                    .WithMetadata(
                        $"Artifact: {artifactName}, Run #{runNumber}",
                        tags: new List<string> { "workflow", "artifact" },
                        changelogUrl: string.Empty)
                    .WithInstallationInstructions(WorkspaceStrategy.HybridCopySymlink);

                // Add artifact as remote file - use ArchiveDownloadUrl for authenticated downloads
                var downloadUrl = !string.IsNullOrEmpty(artifact.ArchiveDownloadUrl)
                    ? artifact.ArchiveDownloadUrl
                    : artifact.DownloadUrl;

                await manifest.AddRemoteFileAsync(
                    artifactName,
                    downloadUrl,
                    ContentSourceType.RemoteDownload,
                    isExecutable: GitHubInferenceHelper.IsExecutableFile(artifactName));

                return OperationResult<ContentManifest>.CreateSuccess(manifest.Build());
            }

            // Normal path: use full workflow run details
            // Content name: "owner-repo-runN"
            var artifactContentName = $"{owner}-{repo}-run{workflowRun.RunNumber}";

            logger.LogInformation(
                "Resolving GitHub artifact {ArtifactName} from workflow {WorkflowName}, run #{RunNumber}",
                artifactName,
                workflowRun.Name,
                workflowRun.RunNumber);

            // Create a new manifest builder for each resolve operation to ensure clean state
            var runManifestBuilder = serviceProvider.GetRequiredService<IContentManifestBuilder>();

            var manifestWithRun = runManifestBuilder
                .WithBasicInfo(
                "github", // Publisher ID (builder generates manifest ID)
                artifactContentName, // Content name
                workflowRun.RunNumber) // Version
                .WithContentType(discoveredItem.ContentType, discoveredItem.TargetGame)
                .WithPublisher(
                name: $"github-{owner}",
                website: string.Empty,
                supportUrl: string.Empty,
                contactEmail: string.Empty,
                publisherType: "github")
                .WithMetadata(
                    $"Workflow: {workflowRun.Name}, Run #{workflowRun.RunNumber}",
                    tags: new List<string> { "workflow", "artifact", workflowRun.Status ?? "unknown" },
                    changelogUrl: workflowRun.HtmlUrl ?? string.Empty)
                .WithInstallationInstructions(WorkspaceStrategy.HybridCopySymlink);

            // Add artifact as remote file - use ArchiveDownloadUrl for authenticated downloads
            var downloadUrlFinal = !string.IsNullOrEmpty(artifact.ArchiveDownloadUrl)
                ? artifact.ArchiveDownloadUrl
                : artifact.DownloadUrl;

            await manifestWithRun.AddRemoteFileAsync(
                artifactName,
                downloadUrlFinal,
                ContentSourceType.RemoteDownload,
                isExecutable: GitHubInferenceHelper.IsExecutableFile(artifactName));

            return OperationResult<ContentManifest>.CreateSuccess(manifestWithRun.Build());
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to resolve GitHub artifact for {ItemName}", discoveredItem.Name);
            return OperationResult<ContentManifest>.CreateFailure($"Resolution failed: {ex.Message}");
        }
    }
}
