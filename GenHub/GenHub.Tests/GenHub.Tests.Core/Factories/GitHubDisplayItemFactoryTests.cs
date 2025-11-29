using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Models.GitHub;
using GenHub.Features.GitHub.Factories;
using Microsoft.Extensions.Logging;
using Moq;

namespace GenHub.Tests.Core.Factories;

/// <summary>
/// Collection definition for GitHubDisplayItemFactory tests.
/// </summary>
[CollectionDefinition("GitHubDisplayItemFactoryTests")]
public class GitHubDisplayItemFactoryTestCollection : ICollectionFixture<GitHubDisplayItemFactoryFixture>
{
}

/// <summary>
/// Fixture for GitHubDisplayItemFactory tests.
/// </summary>
public class GitHubDisplayItemFactoryFixture
{
    /// <summary>
    /// Gets the mock for IGitHubApiClient.
    /// </summary>
    public Mock<IGitHubApiClient> ApiClientMock { get; } = new();

    /// <summary>
    /// Gets the mock for ILogger.
    /// </summary>
    public Mock<ILogger<GitHubDisplayItemFactory>> LoggerMock { get; } = new();
}

/// <summary>
/// Tests for GitHubDisplayItemFactory.
/// </summary>
[Collection("GitHubDisplayItemFactoryTests")]
public class GitHubDisplayItemFactoryTests
{
    private readonly GitHubDisplayItemFactoryFixture _fixture;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitHubDisplayItemFactoryTests"/> class.
    /// </summary>
    /// <param name="fixture">The test fixture.</param>
    public GitHubDisplayItemFactoryTests(GitHubDisplayItemFactoryFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// Tests creating display items from valid artifacts.
    /// </summary>
    [Fact]
    public void CreateFromArtifacts_WithValidArtifacts_ReturnsItems()
    {
        var factory = new GitHubDisplayItemFactory(_fixture.ApiClientMock.Object, _fixture.LoggerMock.Object);
        var artifacts = new List<GitHubArtifact>
        {
            new GitHubArtifact { Id = 1, Name = "artifact1" },
        };

        var items = factory.CreateFromArtifacts(artifacts, "owner", "repo");

        Assert.Single(items);
        Assert.Equal("artifact1", items.ToList()[0].DisplayName);
    }

    /// <summary>
    /// Tests creating display items from valid releases.
    /// </summary>
    [Fact]
    public void CreateFromReleases_WithValidReleases_ReturnsItems()
    {
        var factory = new GitHubDisplayItemFactory(_fixture.ApiClientMock.Object, _fixture.LoggerMock.Object);
        var releases = new List<GitHubRelease>
        {
            new GitHubRelease { Id = 1, TagName = "v1.0", Name = "Release 1" },
        };

        var items = factory.CreateFromReleases(releases, "owner", "repo");

        Assert.Single(items);
        Assert.Equal("v1.0", items.ToList()[0].DisplayName);
    }

    /// <summary>
    /// Tests creating display items from valid workflow runs.
    /// </summary>
    [Fact]
    public void CreateFromWorkflowRuns_WithValidRuns_ReturnsItems()
    {
        var factory = new GitHubDisplayItemFactory(_fixture.ApiClientMock.Object, _fixture.LoggerMock.Object);
        var runs = new List<GitHubWorkflowRun>
        {
            new GitHubWorkflowRun { Id = 1, RunNumber = 1, DisplayTitle = "CI", Workflow = new GitHubWorkflow { Name = "CI" } },
        };

        var items = factory.CreateFromWorkflowRuns(runs, "owner", "repo");

        Assert.Single(items);
        Assert.Equal("CI", items.ToList()[0].DisplayName);
    }

    /// <summary>
    /// Tests creating display items from null artifacts.
    /// </summary>
    [Fact]
    public void CreateFromArtifacts_WithNull_ReturnsEmpty()
    {
        var factory = new GitHubDisplayItemFactory(_fixture.ApiClientMock.Object, _fixture.LoggerMock.Object);
        var items = factory.CreateFromArtifacts(null!, "owner", "repo");

        Assert.Empty(items);
    }
}
