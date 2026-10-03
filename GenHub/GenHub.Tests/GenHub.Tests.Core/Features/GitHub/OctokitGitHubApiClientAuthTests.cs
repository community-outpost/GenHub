using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Models.GitHub;
using GenHub.Features.GitHub.Services;
using GenHub.Tests.Core.Collections;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Octokit;
using System;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.GitHub;

/// <summary>
/// Contains unit tests for the authentication behavior of <see cref="OctokitGitHubApiClient"/>.
/// </summary>
[Collection(GitHubAuthEnvironmentCollection.Name)]
public class OctokitGitHubApiClientAuthTests : IDisposable
{
    private readonly string? _originalGenHubToken;
    private readonly string? _originalGitHubToken;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="OctokitGitHubApiClientAuthTests"/> class.
    /// </summary>
    public OctokitGitHubApiClientAuthTests()
    {
        _originalGenHubToken = Environment.GetEnvironmentVariable(GitHubConstants.GenHubTokenEnvVar);
        _originalGitHubToken = Environment.GetEnvironmentVariable(GitHubConstants.GitHubTokenEnvVar);
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
    /// Verifies that a sign-out before the first API call keeps the client anonymous even when an environment token is present.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task EnsureAuthenticatedAsync_AfterSignOut_StaysAnonymousAsync()
    {
        // Arrange
        Environment.SetEnvironmentVariable(GitHubConstants.GenHubTokenEnvVar, "env-token-value");
        Environment.SetEnvironmentVariable(GitHubConstants.GitHubTokenEnvVar, null);
        var authService = new Mock<IGitHubAuthService>();
        authService.SetupGet(x => x.IsAuthenticated).Returns(false);
        var client = CreateClient(authService.Object, hasStoredToken: false);

        // Act
        var authenticated = await client.EnsureAuthenticatedAsync();

        // Assert
        Assert.False(authenticated);
        Assert.False(client.IsAuthenticated);
    }

    /// <summary>
    /// Verifies that the client loads the stored token on first use when the auth service reports signed in.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task EnsureAuthenticatedAsync_WhenSignedIn_LoadsStoredTokenAsync()
    {
        // Arrange
        Environment.SetEnvironmentVariable(GitHubConstants.GenHubTokenEnvVar, null);
        Environment.SetEnvironmentVariable(GitHubConstants.GitHubTokenEnvVar, null);
        var authService = new Mock<IGitHubAuthService>();
        authService.SetupGet(x => x.IsAuthenticated).Returns(true);
        var client = CreateClient(authService.Object, hasStoredToken: true);

        // Act
        var authenticated = await client.EnsureAuthenticatedAsync();

        // Assert
        Assert.True(authenticated);
        Assert.True(client.IsAuthenticated);
    }

    /// <summary>
    /// Verifies that the sign-out event clears previously loaded credentials.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task AuthStateChanged_WhenSignedOut_ClearsCredentialsAsync()
    {
        // Arrange
        Environment.SetEnvironmentVariable(GitHubConstants.GenHubTokenEnvVar, null);
        Environment.SetEnvironmentVariable(GitHubConstants.GitHubTokenEnvVar, null);
        var authService = new Mock<IGitHubAuthService>();
        authService.SetupGet(x => x.IsAuthenticated).Returns(true);
        var client = CreateClient(authService.Object, hasStoredToken: true);
        Assert.True(await client.EnsureAuthenticatedAsync());

        // Act
        authService.SetupGet(x => x.IsAuthenticated).Returns(false);
        authService.Raise(x => x.AuthStateChanged += null, new GitHubAuthStateChangedEventArgs(false, null));

        // Assert
        Assert.False(client.IsAuthenticated);
        Assert.False(await client.EnsureAuthenticatedAsync());
    }

    private static OctokitGitHubApiClient CreateClient(IGitHubAuthService authService, bool hasStoredToken)
    {
        var storage = new Mock<IGitHubTokenStorage>();
        storage.Setup(x => x.HasToken()).Returns(hasStoredToken);
        storage
            .Setup(x => x.LoadTokenAsync())
            .ReturnsAsync(() => hasStoredToken ? SecureStringHelper.ToSecureString("stored-token-value") : null);
        var gitHubClient = new GitHubClient(new ProductHeaderValue("GenHub-Tests"));
        return new OctokitGitHubApiClient(
            gitHubClient,
            Mock.Of<IHttpClientFactory>(),
            NullLogger<OctokitGitHubApiClient>.Instance,
            Mock.Of<IMemoryCache>(),
            storage.Object,
            null,
            authService);
    }
}
