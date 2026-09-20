using FluentAssertions;
using GenHub.Features.GitHub.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;
using System.Net;
using System.Security;
using System.Text;
using System.Threading;

namespace GenHub.Tests.Core.Features.AppUpdate.Services;

/// <summary>
/// Tests for OctokitGitHubApiClient.
/// </summary>
public class OctokitGitHubApiClientTests
{
    /// <summary>
    /// Verifies that GetLatestReleaseAsync returns null when not found.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task GetLatestReleaseAsync_ReturnsNullWhenNotFoundAsync()
    {
        // Arrange
        var releasesClientMock = new Mock<Octokit.IReleasesClient>();
        releasesClientMock
            .Setup(x => x.GetLatest(It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new NotFoundException("Not found", HttpStatusCode.NotFound));

        var repositoriesClientMock = new Mock<Octokit.IRepositoriesClient>();
        repositoriesClientMock
            .SetupGet(x => x.Release)
            .Returns(releasesClientMock.Object);

        var gitHubClientMock = new Mock<Octokit.IGitHubClient>();
        gitHubClientMock.SetupGet(x => x.Repository).Returns(repositoriesClientMock.Object);

        var api = new OctokitGitHubApiClient(
            gitHubClientMock.Object,
            Mock.Of<IHttpClientFactory>(),
            Mock.Of<ILogger<OctokitGitHubApiClient>>(),
            Mock.Of<IMemoryCache>());

        // Act
        var result = await api.GetLatestReleaseAsync("owner", "repo");

        // Assert
        result.Should().BeNull();
    }

    /// <summary>
    /// Verifies that GetReleasesAsync returns empty collection when no releases exist.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task GetReleasesAsync_ReturnsEmptyCollectionWhenNoReleasesAsync()
    {
        // Arrange
        var releasesClientMock = new Mock<Octokit.IReleasesClient>();
        releasesClientMock
            .Setup(x => x.GetAll(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new List<Octokit.Release>());

        var repositoriesClientMock = new Mock<Octokit.IRepositoriesClient>();
        repositoriesClientMock
            .SetupGet(x => x.Release)
            .Returns(releasesClientMock.Object);

        var gitHubClientMock = new Mock<Octokit.IGitHubClient>();
        gitHubClientMock.SetupGet(x => x.Repository).Returns(repositoriesClientMock.Object);

        // A real one is easier for extension method support like cache.Set/TryGetValue
        var cache = new MemoryCache(new MemoryCacheOptions());

        var api = new OctokitGitHubApiClient(
            gitHubClientMock.Object,
            Mock.Of<IHttpClientFactory>(),
            Mock.Of<ILogger<OctokitGitHubApiClient>>(),
            cache); // Added the missing parameter

        // Act
        var result = await api.GetReleasesAsync("owner", "repo");

        // Assert
        result.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that SetAuthenticationToken works with concrete GitHubClient.
    /// </summary>
    [Fact]
    public void SetAuthenticationToken_WorksWithConcreteClient()
    {
        // Arrange
        var concreteClient = new GitHubClient(new ProductHeaderValue("test"));
        var api = new OctokitGitHubApiClient(
            concreteClient,
            Mock.Of<IHttpClientFactory>(),
            Mock.Of<ILogger<OctokitGitHubApiClient>>(),
            Mock.Of<IMemoryCache>());

        var secureToken = new SecureString();
        foreach (char c in "test-token")
        {
            secureToken.AppendChar(c);
        }

        // Act & Assert
        api.SetAuthenticationToken(secureToken);
        concreteClient.Credentials.Should().NotBeNull();
    }

    /// <summary>
    /// Verifies that SetAuthenticationToken throws when client doesn't support credentials.
    /// </summary>
    [Fact]
    public void SetAuthenticationToken_ThrowsWithMockClient()
    {
        // Arrange
        var mockClient = new Mock<Octokit.IGitHubClient>();
        var api = new OctokitGitHubApiClient(
            mockClient.Object,
            Mock.Of<IHttpClientFactory>(),
            Mock.Of<ILogger<OctokitGitHubApiClient>>(),
            Mock.Of<IMemoryCache>());

        var secureToken = new SecureString();
        foreach (char c in "test-token")
        {
            secureToken.AppendChar(c);
        }

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => api.SetAuthenticationToken(secureToken));
    }

    /// <summary>
    /// Verifies that credentials are automatically loaded from IGitHubTokenStorage.
    /// </summary>
    [Fact]
    public void EnsureCredentialsLoaded_LoadsFromTokenStorage()
    {
        // Arrange
        var concreteClient = new GitHubClient(new ProductHeaderValue("test"));
        var secureToken = new SecureString();
        foreach (char c in "stored-secret-pat")
        {
            secureToken.AppendChar(c);
        }

        var tokenStorageMock = new Mock<GenHub.Core.Interfaces.GitHub.IGitHubTokenStorage>();
        tokenStorageMock.Setup(x => x.HasToken()).Returns(true);
        tokenStorageMock.Setup(x => x.LoadTokenAsync()).ReturnsAsync(secureToken);

        var api = new OctokitGitHubApiClient(
            concreteClient,
            Mock.Of<IHttpClientFactory>(),
            Mock.Of<ILogger<OctokitGitHubApiClient>>(),
            Mock.Of<IMemoryCache>(),
            tokenStorageMock.Object);

        // Act & Assert
        api.IsAuthenticated.Should().BeTrue();
        concreteClient.Credentials.Should().NotBeNull();
        concreteClient.Credentials.Password.Should().Be("stored-secret-pat");
    }

    /// <summary>
    /// Verifies that ClearAuthenticationToken resets credentials to Anonymous.
    /// </summary>
    [Fact]
    public void ClearAuthenticationToken_ResetsCredentialsToAnonymous()
    {
        // Arrange
        var concreteClient = new GitHubClient(new ProductHeaderValue("test"));
        var api = new OctokitGitHubApiClient(
            concreteClient,
            Mock.Of<IHttpClientFactory>(),
            Mock.Of<ILogger<OctokitGitHubApiClient>>(),
            Mock.Of<IMemoryCache>());

        var secureToken = new SecureString();
        foreach (char c in "test-token")
        {
            secureToken.AppendChar(c);
        }

        api.SetAuthenticationToken(secureToken);
        api.IsAuthenticated.Should().BeTrue();

        // Act
        api.ClearAuthenticationToken();

        // Assert
        api.IsAuthenticated.Should().BeFalse();
        concreteClient.Credentials.Should().Be(Credentials.Anonymous);
    }

    /// <summary>
    /// Verifies that rate limit tracker is updated when RateLimitExceededException occurs.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task GetLatestReleaseAsync_WhenRateLimitExceeded_UpdatesTrackerAsync()
    {
        // Arrange
        var resetEpoch = ((DateTimeOffset)DateTime.UtcNow.AddMinutes(30)).ToUnixTimeSeconds();
        var headers = new Dictionary<string, string>
        {
            ["X-RateLimit-Reset"] = resetEpoch.ToString(),
        };
        var responseMock = new Mock<Octokit.IResponse>();
        responseMock.SetupGet(x => x.Headers).Returns(headers);
        var rateLimit = new Octokit.RateLimit(60, 0, resetEpoch);
        var apiInfo = new Octokit.ApiInfo(new Dictionary<string, Uri>(), new List<string>(), new List<string>(), "etag", rateLimit);
        responseMock.SetupGet(x => x.ApiInfo).Returns(apiInfo);

        var rateLimitException = new RateLimitExceededException(responseMock.Object);

        var releasesClientMock = new Mock<Octokit.IReleasesClient>();
        releasesClientMock
            .Setup(x => x.GetLatest(It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(rateLimitException);

        var repositoriesClientMock = new Mock<Octokit.IRepositoriesClient>();
        repositoriesClientMock
            .SetupGet(x => x.Release)
            .Returns(releasesClientMock.Object);

        var gitHubClientMock = new Mock<Octokit.IGitHubClient>();
        gitHubClientMock.SetupGet(x => x.Repository).Returns(repositoriesClientMock.Object);

        var tracker = new GitHubRateLimitTracker(Mock.Of<ILogger<GitHubRateLimitTracker>>());

        var api = new OctokitGitHubApiClient(
            gitHubClientMock.Object,
            Mock.Of<IHttpClientFactory>(),
            Mock.Of<ILogger<OctokitGitHubApiClient>>(),
            Mock.Of<IMemoryCache>(),
            rateLimitTracker: tracker);

        // Act
        var result = await api.GetLatestReleaseAsync("owner", "repo");

        // Assert
        result.Should().BeNull();
        api.IsRateLimited.Should().BeTrue();
        tracker.IsAtLimit.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that GetReadmeAsync honors cancellation before issuing the API call.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task GetReadmeAsync_WhenCancelled_ThrowsBeforeApiCallAsync()
    {
        // Arrange
        var contentsClientMock = new Mock<Octokit.IRepositoryContentsClient>();

        var repositoriesClientMock = new Mock<Octokit.IRepositoriesClient>();
        repositoriesClientMock
            .SetupGet(x => x.Content)
            .Returns(contentsClientMock.Object);

        var gitHubClientMock = new Mock<Octokit.IGitHubClient>();
        gitHubClientMock.SetupGet(x => x.Repository).Returns(repositoriesClientMock.Object);

        var api = new OctokitGitHubApiClient(
            gitHubClientMock.Object,
            Mock.Of<IHttpClientFactory>(),
            Mock.Of<ILogger<OctokitGitHubApiClient>>(),
            new MemoryCache(new MemoryCacheOptions()));

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() => api.GetReadmeAsync("owner", "repo", cts.Token));
        contentsClientMock.Verify(
            x => x.GetReadme(It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies that GetReadmeAsync base64-decodes the raw API payload.
    /// Octokit's Readme DTO carries the encoded content verbatim.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task GetReadmeAsync_DecodesBase64ReadmeContentAsync()
    {
        // Arrange
        const string markdown = "# coolmod\nA great mod.";
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(markdown));
        var readme = new Octokit.Readme(
            new Lazy<Task<string>>(() => Task.FromResult(string.Empty)),
            encoded,
            "README.md",
            "https://github.com/owner/repo",
            "https://api.github.com/repos/owner/repo/readme");

        var contentsClientMock = new Mock<Octokit.IRepositoryContentsClient>();
        contentsClientMock
            .Setup(x => x.GetReadme(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(readme);

        var repositoriesClientMock = new Mock<Octokit.IRepositoriesClient>();
        repositoriesClientMock
            .SetupGet(x => x.Content)
            .Returns(contentsClientMock.Object);

        var gitHubClientMock = new Mock<Octokit.IGitHubClient>();
        gitHubClientMock.SetupGet(x => x.Repository).Returns(repositoriesClientMock.Object);

        var api = new OctokitGitHubApiClient(
            gitHubClientMock.Object,
            Mock.Of<IHttpClientFactory>(),
            Mock.Of<ILogger<OctokitGitHubApiClient>>(),
            new MemoryCache(new MemoryCacheOptions()));

        // Act
        var result = await api.GetReadmeAsync("owner", "repo");

        // Assert
        result.Should().Be(markdown);
    }
}
