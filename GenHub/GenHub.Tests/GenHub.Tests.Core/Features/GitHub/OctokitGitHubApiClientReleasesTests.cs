using GenHub.Core.Constants;
using GenHub.Features.GitHub.Services;
using GenHub.Tests.Core.Collections;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Octokit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.GitHub;

/// <summary>
/// Contains unit tests for the release listing behavior of <see cref="OctokitGitHubApiClient"/>.
/// </summary>
[Collection(GitHubAuthEnvironmentCollection.Name)]
public class OctokitGitHubApiClientReleasesTests : IDisposable
{
    private readonly string? _originalGenHubToken;
    private readonly string? _originalGitHubToken;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="OctokitGitHubApiClientReleasesTests"/> class.
    /// </summary>
    public OctokitGitHubApiClientReleasesTests()
    {
        _originalGenHubToken = Environment.GetEnvironmentVariable(GitHubConstants.GenHubTokenEnvVar);
        _originalGitHubToken = Environment.GetEnvironmentVariable(GitHubConstants.GitHubTokenEnvVar);
        Environment.SetEnvironmentVariable(GitHubConstants.GenHubTokenEnvVar, null);
        Environment.SetEnvironmentVariable(GitHubConstants.GitHubTokenEnvVar, null);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Environment.SetEnvironmentVariable(GitHubConstants.GenHubTokenEnvVar, _originalGenHubToken);
        Environment.SetEnvironmentVariable(GitHubConstants.GitHubTokenEnvVar, _originalGitHubToken);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Verifies that draft releases are excluded while published prereleases still pass through.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GetReleasesAsync_WhenDraftPresent_ExcludesDraftAsync()
    {
        // Arrange
        var releases = new List<Release>
        {
            CreateRelease("v0.0.3", draft: false, prerelease: true),
            CreateRelease("v0.1.0", draft: true, prerelease: false),
        };
        var client = CreateClient(releases);

        // Act
        var result = (await client.GetReleasesAsync("owner", "repo")).ToList();

        // Assert
        var release = Assert.Single(result);
        Assert.Equal("v0.0.3", release.TagName);
        Assert.False(release.IsDraft);
    }

    private static OctokitGitHubApiClient CreateClient(IReadOnlyList<Release> releases)
    {
        var releasesClient = new Mock<IReleasesClient>();
        releasesClient.Setup(x => x.GetAll("owner", "repo")).ReturnsAsync(releases);
        var repositoriesClient = new Mock<IRepositoriesClient>();
        repositoriesClient.SetupGet(x => x.Release).Returns(releasesClient.Object);
        var gitHubClient = new Mock<IGitHubClient>();
        gitHubClient.SetupGet(x => x.Repository).Returns(repositoriesClient.Object);
        return new OctokitGitHubApiClient(
            gitHubClient.Object,
            Mock.Of<IHttpClientFactory>(),
            NullLogger<OctokitGitHubApiClient>.Instance,
            new MemoryCache(new MemoryCacheOptions()));
    }

    private static Release CreateRelease(string tagName, bool draft, bool prerelease)
    {
        var author = new Author(
            "octocat",
            1,
            "node-id",
            "https://example.com/avatar",
            "https://example.com/author",
            "https://example.com/author",
            "https://example.com/followers",
            "https://example.com/following",
            "https://example.com/gists",
            "User",
            "https://example.com/starred",
            "https://example.com/subscriptions",
            "https://example.com/orgs",
            "https://example.com/repos",
            "https://example.com/events",
            "https://example.com/received-events",
            false);
        return new Release(
            "https://example.com/release",
            "https://example.com/release",
            "https://example.com/assets",
            "https://example.com/upload",
            1,
            "node-id",
            tagName,
            "development",
            tagName,
            "Body",
            draft,
            prerelease,
            DateTimeOffset.UtcNow,
            draft ? null : DateTimeOffset.UtcNow,
            author,
            "https://example.com/tarball",
            "https://example.com/zipball",
            []);
    }
}
