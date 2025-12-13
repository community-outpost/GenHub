using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Models.GitHub;
using Microsoft.Extensions.Logging;
using Octokit;

namespace GenHub.Features.GitHub.Services;

/// <summary>
/// GitHub API client implementation using Octokit.
/// </summary>
public class OctokitGitHubApiClient(
   IGitHubClient gitHubClient,
   IHttpClientFactory httpClientFactory,
   ILogger<OctokitGitHubApiClient> logger)
   : IGitHubApiClient
{
    private const int MaxPerPage = 100;
    private SecureString? token;

    /// <summary>
    /// Gets a value indicating whether the client is authenticated.
    /// </summary>
    public bool IsAuthenticated => ((GitHubClient)gitHubClient).Credentials != Credentials.Anonymous;

    /// <summary>
    /// Sets the GitHub token for authentication.
    /// </summary>
    /// <param name="token">The GitHub token.</param>
    public void SetToken(SecureString token)
    {
        this.token = token;
        if (gitHubClient is GitHubClient client)
        {
            var tokenString = new System.Net.NetworkCredential(string.Empty, token).Password;
            client.Credentials = new Credentials(tokenString);
        }
    }

    /// <summary>
    /// Gets the current GitHub token.
    /// </summary>
    /// <returns>The GitHub token.</returns>
    public SecureString GetToken() => token!;

    /// <summary>
    /// Downloads a release asset to the specified path.
    /// </summary>
    /// <param name="owner">The repository owner.</param>
    /// <param name="repo">The repository name.</param>
    /// <param name="asset">The release asset to download.</param>
    /// <param name="destinationPath">The destination file path.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the download operation.</returns>
    public async Task DownloadReleaseAssetAsync(
        string owner,
        string repo,
        GitHubReleaseAsset asset,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogDebug("Downloading release asset {AssetId} to {Destination}", asset.Id, destinationPath);

            var httpClient = httpClientFactory.CreateClient("GitHubApi");
            using var response = await httpClient.GetAsync(asset.BrowserDownloadUrl, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var fileStream = File.Create(destinationPath);
            await contentStream.CopyToAsync(fileStream, cancellationToken);

            logger.LogInformation("Successfully downloaded release asset {AssetId}", asset.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to download release asset {AssetId}", asset.Id);
            throw;
        }
    }

    /// <summary>
    /// Downloads an artifact to the specified path.
    /// </summary>
    /// <param name="owner">The repository owner.</param>
    /// <param name="repo">The repository name.</param>
    /// <param name="artifact">The artifact to download.</param>
    /// <param name="destinationPath">The destination file path.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the download operation.</returns>
    public async Task DownloadArtifactAsync(
        string owner,
        string repo,
        GitHubArtifact artifact,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogInformation("Starting download of artifact {ArtifactId} from {Owner}/{Repo}", artifact.Id, owner, repo);

            // Check authentication status first
            if (gitHubClient is GitHubClient concreteClient)
            {
                var isAuth = concreteClient.Credentials != Credentials.Anonymous;
                var authType = concreteClient.Credentials?.AuthenticationType.ToString() ?? "None";
                logger.LogInformation("Authentication status: IsAuthenticated={IsAuth}, Type={AuthType}", isAuth, authType);

                if (!isAuth)
                {
                    logger.LogError("No authentication available for artifact download. Please configure a GitHub token.");
                    throw new InvalidOperationException("GitHub authentication required for artifact downloads. Please configure a GitHub token.");
                }
            }

            var artifactUrl = $"https://api.github.com/repos/{owner}/{repo}/actions/artifacts/{artifact.Id}/zip";
            logger.LogInformation("Requesting artifact from URL: {Url}", artifactUrl);

            var httpClient = httpClientFactory.CreateClient("GitHubApi");
            httpClient.DefaultRequestHeaders.Add("User-Agent", "GenHub/1.0");

            // Add authentication headers
            if (gitHubClient is GitHubClient authClient && authClient.Credentials != Credentials.Anonymous)
            {
                if (authClient.Credentials.AuthenticationType == AuthenticationType.Bearer)
                {
                    httpClient.DefaultRequestHeaders.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", authClient.Credentials.Password);
                    logger.LogInformation("Added Bearer authentication header");
                }
                else if (authClient.Credentials.AuthenticationType == AuthenticationType.Basic)
                {
                    var authValue = Convert.ToBase64String(
                        System.Text.Encoding.ASCII.GetBytes($"{authClient.Credentials.Login}:{authClient.Credentials.Password}"));
                    httpClient.DefaultRequestHeaders.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authValue);
                    logger.LogInformation("Added Basic authentication header for user: {Login}", authClient.Credentials.Login);
                }
            }
            else
            {
                logger.LogError("Failed to add authentication headers - no valid credentials found");
                throw new InvalidOperationException("Authentication failed - no valid GitHub credentials available");
            }

            var response = await httpClient.GetAsync(artifactUrl, cancellationToken);
            logger.LogInformation("Received HTTP response: {StatusCode} {ReasonPhrase}", response.StatusCode, response.ReasonPhrase);

            if (!response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogError("HTTP request failed with status {StatusCode}: {Content}", response.StatusCode, responseContent);
            }

            response.EnsureSuccessStatusCode();

            await using var fileStream = File.Create(destinationPath);
            await response.Content.CopyToAsync(fileStream, cancellationToken);

            logger.LogInformation("Successfully downloaded artifact {ArtifactId}", artifact.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to download artifact {ArtifactId}", artifact.Id);
            throw;
        }
    }

    /// <summary>
    /// Gets the latest release for the specified repository.
    /// </summary>
    /// <param name="owner">The repository owner.</param>
    /// <param name="repositoryName">The repository name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The latest <see cref="GitHubRelease"/> or null if not found.</returns>
    public async Task<GitHubRelease> GetLatestReleaseAsync(
        string owner,
        string repositoryName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var octo = await gitHubClient.Repository.Release.GetLatest(owner, repositoryName)
                .ConfigureAwait(false);
            return MapToGitHubRelease(octo);
        }
        catch (Octokit.NotFoundException)
        {
            return null!;
        }
        catch (RateLimitExceededException ex)
        {
            logger.LogWarning("Rate limit exceeded when fetching latest release for {Owner}/{Repo}. Reset at: {ResetTime}", owner, repositoryName, ex.Reset);
            return null!;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get latest release for {Owner}/{Repo}", owner, repositoryName);
            throw;
        }
    }

    /// <summary>
    /// Gets a specific release by tag for the specified repository.
    /// </summary>
    /// <param name="owner">The repository owner.</param>
    /// <param name="repositoryName">The repository name.</param>
    /// <param name="tag">The release tag.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The <see cref="GitHubRelease"/> with the specified tag or null if not found.</returns>
    public async Task<GitHubRelease> GetReleaseByTagAsync(
        string owner,
        string repositoryName,
        string tag,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var octo = await gitHubClient.Repository.Release.Get(owner, repositoryName, tag)
                .ConfigureAwait(false);
            return MapToGitHubRelease(octo);
        }
        catch (Octokit.NotFoundException)
        {
            logger.LogDebug("Release with tag '{Tag}' not found for {Owner}/{Repo}", tag, owner, repositoryName);
            return null!;
        }
        catch (RateLimitExceededException ex)
        {
            logger.LogWarning("Rate limit exceeded when fetching release by tag '{Tag}' for {Owner}/{Repo}. Reset at: {ResetTime}", tag, owner, repositoryName, ex.Reset);
            return null!;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get release by tag '{Tag}' for {Owner}/{Repo}", tag, owner, repositoryName);
            throw;
        }
    }

    /// <summary>
    /// Gets all releases for the specified repository.
    /// </summary>
    /// <param name="owner">The repository owner.</param>
    /// <param name="repo">The repository name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task with the releases.</returns>
    public async Task<IEnumerable<GitHubRelease>> GetReleasesAsync(
        string owner,
        string repo,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var releases = await gitHubClient.Repository.Release.GetAll(owner, repo)
                .ConfigureAwait(false);
            return releases.Select(MapToGitHubRelease);
        }
        catch (RateLimitExceededException ex)
        {
            logger.LogWarning("Rate limit exceeded when fetching releases for {Owner}/{Repo}. Reset at: {ResetTime}", owner, repo, ex.Reset);
            return Enumerable.Empty<GitHubRelease>();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get releases for {Owner}/{Repo}", owner, repo);
            throw;
        }
    }

    /// <summary>
    /// Gets workflow runs for the specified repository.
    /// </summary>
    /// <param name="owner">The repository owner.</param>
    /// <param name="repo">The repository name.</param>
    /// <param name="perPage">Number of runs per page.</param>
    /// <param name="page">The page number to fetch (1-indexed).</param>
    /// <param name="progress">Progress reporter for streaming workflow results.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A paginated result containing GitHub workflow runs and pagination info.</returns>
    public async Task<GitHubWorkflowRunsResult> GetWorkflowRunsForRepositoryAsync(
        string owner,
        string repo,
        int perPage = 5,
        int page = 1,
        IProgress<GitHubWorkflowRun>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsAuthenticated)
        {
            logger.LogWarning("Authentication required for workflow runs. Returning empty list. Configure token in settings to access workflows.");
            return new GitHubWorkflowRunsResult
            {
                WorkflowRuns = Enumerable.Empty<GitHubWorkflowRun>(),
                HasMore = false,
            };
        }

        try
        {
            logger.LogInformation("Starting workflow runs fetch for {Owner}/{Repo} page {Page}", owner, repo, page);

            // Fetch more than requested to account for filtering out workflows without artifacts
            var options = new ApiOptions
            {
                PageSize = perPage * 2,
                StartPage = page,
                PageCount = 1,
            };

            var request = new WorkflowRunsRequest
            {
                Status = CheckRunStatusFilter.Completed,
            };

            var runs = await gitHubClient.Actions.Workflows.Runs.List(owner, repo, request, options)
                .ConfigureAwait(false);

            logger.LogInformation("Successfully fetched {Count} workflow runs for {Owner}/{Repo} page {Page}", runs.WorkflowRuns.Count, owner, repo, page);

            // Filter to only successful workflows WITH artifacts
            var workflowsWithArtifacts = new List<GitHubWorkflowRun>();
            bool stoppedAtPerPageLimit = false;

            foreach (var run in runs.WorkflowRuns)
            {
                // Skip if not successful
                if (!string.Equals(run.Conclusion?.StringValue, WorkflowRunConclusion.Success.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Check if workflow has artifacts
                try
                {
                    var artifacts = await gitHubClient.Actions.Artifacts.ListWorkflowArtifacts(owner, repo, run.Id)
                        .ConfigureAwait(false);

                    if (artifacts.TotalCount > 0)
                    {
                        var workflowRun = MapToGitHubWorkflowRun(run);
                        workflowsWithArtifacts.Add(workflowRun);
                        logger.LogDebug("Workflow {RunId} '{Title}' has {Count} artifacts", run.Id, run.DisplayTitle, artifacts.TotalCount);

                        // Report this workflow immediately for streaming display
                        progress?.Report(workflowRun);

                        if (workflowsWithArtifacts.Count >= perPage)
                        {
                            stoppedAtPerPageLimit = true;
                            break;
                        }
                    }
                    else
                    {
                        logger.LogDebug("Skipping workflow {RunId} '{Title}' - no artifacts", run.Id, run.DisplayTitle);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to check artifacts for workflow {RunId}, skipping", run.Id);
                }
            }

            // If we stopped because we hit perPage limit, there might be more
            // If we processed all fetched workflows and got less than perPage, we might still have more if we fetched the full page
            bool mightHaveMore = stoppedAtPerPageLimit || runs.WorkflowRuns.Count >= options.PageSize;

            logger.LogInformation(
                "Returning {Count} workflows with artifacts for {Owner}/{Repo} page {Page}. Fetched {Fetched} total, stopped at limit: {StoppedAtLimit}, might have more: {MightHaveMore}",
                workflowsWithArtifacts.Count,
                owner,
                repo,
                page,
                runs.WorkflowRuns.Count,
                stoppedAtPerPageLimit,
                mightHaveMore);

            return new GitHubWorkflowRunsResult
            {
                WorkflowRuns = workflowsWithArtifacts,
                HasMore = mightHaveMore,
            };
        }
        catch (OperationCanceledException ex)
        {
            logger.LogWarning(ex, "Workflow API call was cancelled for {Owner}/{Repo}", owner, repo);
            return new GitHubWorkflowRunsResult
            {
                WorkflowRuns = Enumerable.Empty<GitHubWorkflowRun>(),
                HasMore = false,
            };
        }
        catch (RateLimitExceededException ex)
        {
            logger.LogWarning("Rate limit exceeded when fetching workflow runs for {Owner}/{Repo}. Reset at: {ResetTime}", owner, repo, ex.Reset);
            return new GitHubWorkflowRunsResult
            {
                WorkflowRuns = Enumerable.Empty<GitHubWorkflowRun>(),
                HasMore = false,
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get workflow runs for {Owner}/{Repo}", owner, repo);
            Console.WriteLine($"[WORKFLOW DEBUG] ERROR: {ex.Message}");
            return new GitHubWorkflowRunsResult
            {
                WorkflowRuns = Enumerable.Empty<GitHubWorkflowRun>(),
                HasMore = false,
            };
        }
    }

    /// <summary>
    /// Gets artifacts for a workflow run.
    /// </summary>
    /// <param name="owner">The repository owner.</param>
    /// <param name="repo">The repository name.</param>
    /// <param name="runId">The workflow run ID.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A collection of <see cref="GitHubArtifact"/>.</returns>
    public async Task<IEnumerable<GitHubArtifact>> GetArtifactsForWorkflowRunAsync(
        string owner,
        string repo,
        long runId,
        CancellationToken cancellationToken = default)
    {
        if (!IsAuthenticated)
        {
            logger.LogWarning("Authentication required for artifacts. Returning empty list. Configure token in settings to access artifacts.");
            return Enumerable.Empty<GitHubArtifact>();
        }

        try
        {
            var artifacts = await gitHubClient.Actions.Artifacts.ListWorkflowArtifacts(owner, repo, runId)
                .ConfigureAwait(false);
            return artifacts.Artifacts.Select(MapToGitHubArtifact);
        }
        catch (RateLimitExceededException ex)
        {
            logger.LogWarning("Rate limit exceeded when fetching artifacts for workflow run {RunId}. Reset at: {ResetTime}", runId, ex.Reset);
            return Enumerable.Empty<GitHubArtifact>();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get artifacts for workflow run {RunId}", runId);
            return Enumerable.Empty<GitHubArtifact>();
        }
    }

    /// <summary>
    /// Sets the authentication token for the GitHub client.
    /// </summary>
    /// <param name="token">The authentication token.</param>
    public void SetAuthenticationToken(SecureString token)
    {
        if (token == null || token.Length == 0)
        {
            throw new ArgumentException("Token cannot be null or empty.", nameof(token));
        }

        IntPtr tokenPtr = IntPtr.Zero;
        try
        {
            tokenPtr = Marshal.SecureStringToGlobalAllocUnicode(token);
            string tokenString = Marshal.PtrToStringUni(tokenPtr) ?? throw new InvalidOperationException("Failed to convert secure token to string.");

            if (gitHubClient is GitHubClient concreteClient)
            {
                concreteClient.Credentials = new Credentials(tokenString);
                logger.LogInformation(
                    "GitHub authentication token set successfully. IsAuthenticated: {IsAuth}, Type: {AuthType}",
                    concreteClient.Credentials != Credentials.Anonymous,
                    concreteClient.Credentials.AuthenticationType);
            }
            else
            {
                logger.LogError("Failed to set GitHub token - client does not support setting credentials");
                throw new InvalidOperationException("The GitHub client does not support setting credentials.");
            }
        }
        finally
        {
            if (tokenPtr != IntPtr.Zero)
            {
                Marshal.ZeroFreeGlobalAllocUnicode(tokenPtr);
            }
        }
    }

    /// <inheritdoc />
    public async Task<GitHubUser?> GetAuthenticatedUserAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var user = await gitHubClient.User.Current().ConfigureAwait(false);
            return new GitHubUser
            {
                Login = user.Login,
                Id = user.Id,
                Name = user.Name,
                Email = user.Email,
                AvatarUrl = user.AvatarUrl,
                HtmlUrl = user.HtmlUrl,
                Type = user.Type?.ToString(),
            };
        }
        catch (AuthorizationException)
        {
            logger.LogWarning("Not authenticated or token invalid");
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get authenticated user");
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<GitHubRepositorySearchResponse> SearchRepositoriesByTopicAsync(
        string topic,
        int perPage = 30,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        return await SearchRepositoriesByTopicsAsync(new[] { topic }, perPage, page, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<GitHubRepositorySearchResponse> SearchRepositoriesByTopicsAsync(
        IEnumerable<string> topics,
        int perPage = 30,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var topicList = topics.ToList();
            if (topicList.Count == 0)
            {
                logger.LogWarning("No topics provided for repository search");
                return new GitHubRepositorySearchResponse();
            }

            // Build query: topic:genhub topic:generalsonline etc.
            var topicQuery = string.Join(" ", topicList.Select(t => $"topic:{t}"));
            logger.LogDebug("Searching repositories with query: {Query}", topicQuery);

            var request = new SearchRepositoriesRequest(topicQuery)
            {
                PerPage = Math.Min(perPage, MaxPerPage),
                Page = page,
                SortField = RepoSearchSort.Updated,
                Order = SortDirection.Descending,
            };

            var result = await gitHubClient.Search.SearchRepo(request).ConfigureAwait(false);

            var response = new GitHubRepositorySearchResponse
            {
                TotalCount = result.TotalCount,
                IncompleteResults = result.IncompleteResults,
                Items = result.Items.Select(MapToSearchItem).ToList(),
            };

            logger.LogInformation("Found {Count} repositories for topics: {Topics}", response.TotalCount, string.Join(", ", topicList));
            return response;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to search repositories by topics: {Topics}", string.Join(", ", topics));
            return new GitHubRepositorySearchResponse();
        }
    }

    /// <inheritdoc />
    public async Task<GitHubRepository?> GetRepositoryAsync(
        string owner,
        string repo,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var repository = await gitHubClient.Repository.Get(owner, repo).ConfigureAwait(false);
            return new GitHubRepository
            {
                Id = repository.Id,
                RepoOwner = repository.Owner?.Login ?? owner,
                RepoName = repository.Name,
                Description = repository.Description,
                HtmlUrl = repository.HtmlUrl,
                Topics = repository.Topics?.ToList() ?? new List<string>(),
                StarCount = repository.StargazersCount,
                ForkCount = repository.ForksCount,
                DisplayName = repository.Name,
            };
        }
        catch (NotFoundException)
        {
            logger.LogWarning("Repository {Owner}/{Repo} not found", owner, repo);
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get repository {Owner}/{Repo}", owner, repo);
            return null;
        }
    }

    /// <summary>
    /// Maps an Octokit Repository to our search item model.
    /// </summary>
    private static GitHubRepositorySearchItem MapToSearchItem(Repository repo)
    {
        return new GitHubRepositorySearchItem
        {
            Id = repo.Id,
            NodeId = repo.NodeId ?? string.Empty,
            Name = repo.Name,
            FullName = repo.FullName,
            Owner = new GitHubSearchOwner
            {
                Login = repo.Owner?.Login ?? string.Empty,
                Id = repo.Owner?.Id ?? 0,
                AvatarUrl = repo.Owner?.AvatarUrl ?? string.Empty,
                HtmlUrl = repo.Owner?.HtmlUrl ?? string.Empty,
                Type = repo.Owner?.Type?.ToString() ?? "User",
            },
            IsPrivate = repo.Private,
            HtmlUrl = repo.HtmlUrl,
            Description = repo.Description,
            IsFork = repo.Fork,
            Url = repo.Url,
            CreatedAt = repo.CreatedAt.DateTime,
            UpdatedAt = repo.UpdatedAt.DateTime,
            PushedAt = repo.PushedAt?.DateTime,
            Homepage = repo.Homepage,
            Size = repo.Size,
            StargazersCount = repo.StargazersCount,
            Language = repo.Language,
            ForksCount = repo.ForksCount,
            OpenIssuesCount = repo.OpenIssuesCount,
            DefaultBranch = repo.DefaultBranch ?? "main",
            Topics = repo.Topics?.ToList() ?? new List<string>(),
            IsArchived = repo.Archived,
            Visibility = repo.Visibility?.ToString() ?? "public",
            ReleasesUrl = $"https://api.github.com/repos/{repo.FullName}/releases{{/id}}",
            License = repo.License != null ? new GitHubLicense
            {
                Key = repo.License.Key ?? string.Empty,
                Name = repo.License.Name ?? string.Empty,
                SpdxId = repo.License.SpdxId ?? string.Empty,
                Url = repo.License.Url,
            }
            : null,
        };
    }

    /// <summary>
    /// Maps an Octokit Release to our domain model.
    /// </summary>
    /// <param name="octokitRelease">The Octokit release.</param>
    /// <returns>The mapped release.</returns>
    private static GitHubRelease MapToGitHubRelease(Release octokitRelease)
    {
        return new GitHubRelease
        {
            Id = octokitRelease.Id,
            TagName = octokitRelease.TagName,
            Name = octokitRelease.Name ?? octokitRelease.TagName,
            Body = octokitRelease.Body,
            HtmlUrl = octokitRelease.HtmlUrl,
            PublishedAt = octokitRelease.PublishedAt,
            IsDraft = octokitRelease.Draft,
            IsPrerelease = octokitRelease.Prerelease,
            Assets = octokitRelease.Assets.Select(MapToGitHubReleaseAsset).ToList(),
        };
    }

    /// <summary>
    /// Maps an Octokit ReleaseAsset to our domain model.
    /// </summary>
    /// <param name="octokitAsset">The Octokit asset.</param>
    /// <returns>The mapped asset.</returns>
    private static GitHubReleaseAsset MapToGitHubReleaseAsset(ReleaseAsset octokitAsset)
    {
        return new GitHubReleaseAsset
        {
            Id = octokitAsset.Id,
            Name = octokitAsset.Name,
            Size = octokitAsset.Size,
            BrowserDownloadUrl = octokitAsset.BrowserDownloadUrl,
            ContentType = octokitAsset.ContentType,
        };
    }

    /// <summary>
    /// Maps an Octokit WorkflowRun to our domain model.
    /// </summary>
    /// <param name="octokitRun">The Octokit workflow run.</param>
    /// <returns>The mapped workflow run.</returns>
    private static GitHubWorkflowRun MapToGitHubWorkflowRun(WorkflowRun octokitRun)
    {
        return new GitHubWorkflowRun
        {
            Id = octokitRun.Id,
            RunNumber = (int)octokitRun.RunNumber,
            RunAttempt = (int)octokitRun.RunAttempt,
            Name = octokitRun.DisplayTitle ?? octokitRun.Name ?? $"Run #{octokitRun.RunNumber}",
            DisplayTitle = octokitRun.DisplayTitle ?? octokitRun.Name ?? string.Empty,
            Status = octokitRun.Status.StringValue ?? "unknown",
            Conclusion = octokitRun.Conclusion?.StringValue ?? "pending",
            HtmlUrl = octokitRun.HtmlUrl,
            HeadBranch = octokitRun.HeadBranch ?? string.Empty,
            HeadSha = octokitRun.HeadSha ?? string.Empty,
            Event = octokitRun.Event ?? string.Empty,
            Actor = octokitRun.Actor?.Login ?? octokitRun.TriggeringActor?.Login ?? string.Empty,
            PullRequestNumbers = octokitRun.PullRequests?.Select(pr => pr.Number).ToList() ?? new List<int>(),
            Workflow = new GitHubWorkflow
            {
                Id = octokitRun.WorkflowId,
                Name = octokitRun.Name ?? octokitRun.Path ?? "Unknown",
            },
            CreatedAt = octokitRun.CreatedAt,
            UpdatedAt = octokitRun.UpdatedAt,
        };
    }

    /// <summary>
    /// Maps an Octokit Artifact to our domain model.
    /// </summary>
    /// <param name="octokitArtifact">The Octokit artifact.</param>
    /// <returns>The mapped artifact.</returns>
    private static GitHubArtifact MapToGitHubArtifact(Artifact octokitArtifact)
    {
        return new GitHubArtifact
        {
            Id = octokitArtifact.Id,
            Name = octokitArtifact.Name ?? string.Empty,
            SizeInBytes = octokitArtifact.SizeInBytes,
            CreatedAt = octokitArtifact.CreatedAt,
            ExpiresAt = octokitArtifact.ExpiresAt,
        };
    }
}