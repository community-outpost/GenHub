using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Models.GitHub;
using GenHub.Features.GitHub.Services;
using GenHub.Tests.Core.Collections;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Octokit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.GitHub;

/// <summary>
/// Contains unit tests for <see cref="GitHubAuthService"/>.
/// </summary>
[Collection(GitHubAuthEnvironmentCollection.Name)]
public class GitHubAuthServiceTests
{
    /// <summary>
    /// Verifies that initiating login requests the minimal scopes with the configured client ID.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task InitiateLoginAsync_RequestsMinimalScopesAsync()
    {
        // Arrange
        SetGitHubEnvironment(clientId: "test-client-id");
        var harness = new AuthHarness();
        OauthDeviceFlowRequest? captured = null;
        harness.Oauth
            .Setup(x => x.InitiateDeviceFlow(It.IsAny<OauthDeviceFlowRequest>(), It.IsAny<CancellationToken>()))
            .Callback<OauthDeviceFlowRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(new OauthDeviceFlowResponse("device-code", "USER-CODE", "https://github.com/login/device", 900, 5));

        // Act
        var result = await harness.Service.InitiateLoginAsync();

        // Assert
        Assert.True(result.Success);
        Assert.Equal("USER-CODE", result.Data!.UserCode);
        Assert.Equal("https://github.com/login/device", result.Data.VerificationUri);
        Assert.NotNull(captured);
        Assert.Equal("test-client-id", captured.ClientId);
        Assert.Contains(GitHubConstants.OAuthScopePublicRepo, captured.Scopes);
        Assert.Contains(GitHubConstants.OAuthScopeReadUser, captured.Scopes);
        Assert.Equal(2, captured.Scopes.Count);
    }

    /// <summary>
    /// Verifies that initiating login uses the embedded default client ID when no override is configured.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task InitiateLoginAsync_WithoutEnvOverride_UsesEmbeddedDefaultAsync()
    {
        // Arrange
        SetGitHubEnvironment(clientId: null);
        var harness = new AuthHarness();
        OauthDeviceFlowRequest? captured = null;
        harness.Oauth
            .Setup(x => x.InitiateDeviceFlow(It.IsAny<OauthDeviceFlowRequest>(), It.IsAny<CancellationToken>()))
            .Callback<OauthDeviceFlowRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(new OauthDeviceFlowResponse("device-code", "USER-CODE", "https://github.com/login/device", 900, 5));

        // Act
        var result = await harness.Service.InitiateLoginAsync();

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(captured);
        Assert.False(string.IsNullOrEmpty(GitHubConstants.DefaultOAuthClientId));
        Assert.Equal(GitHubConstants.DefaultOAuthClientId, captured.ClientId);
    }

    /// <summary>
    /// Verifies that initiating login returns a failure when the API request errors.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task InitiateLoginAsync_WhenApiThrows_ReturnsFailureAsync()
    {
        // Arrange
        SetGitHubEnvironment(clientId: "test-client-id");
        var harness = new AuthHarness();
        harness.Oauth
            .Setup(x => x.InitiateDeviceFlow(It.IsAny<OauthDeviceFlowRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("No network"));

        // Act
        var result = await harness.Service.InitiateLoginAsync();

        // Assert
        Assert.False(result.Success);
        Assert.Contains("No network", result.Errors.First());
    }

    /// <summary>
    /// Verifies that initiating login propagates cooperative cancellation.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task InitiateLoginAsync_WhenCancelled_ThrowsAsync()
    {
        // Arrange
        SetGitHubEnvironment(clientId: "test-client-id");
        var harness = new AuthHarness();
        harness.Oauth
            .Setup(x => x.InitiateDeviceFlow(It.IsAny<OauthDeviceFlowRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() => harness.Service.InitiateLoginAsync(cts.Token));
    }

    /// <summary>
    /// Verifies that completing authorization persists the token and publishes the signed in profile.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task WaitForAuthorizationAsync_WhenApproved_PersistsTokenAndPublishesProfileAsync()
    {
        // Arrange
        SetGitHubEnvironment(clientId: "test-client-id");
        var harness = new AuthHarness();
        var storedToken = false;
        harness.TokenStorage.Setup(x => x.HasToken()).Returns(() => storedToken);
        harness.TokenStorage
            .Setup(x => x.SaveTokenAsync(It.IsAny<SecureString>()))
            .Callback(() => storedToken = true)
            .Returns(Task.CompletedTask);
        harness.Oauth
            .Setup(x => x.CreateAccessTokenForDeviceFlow(It.IsAny<string>(), It.IsAny<OauthDeviceFlowResponse>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OauthToken("bearer", "oauth-access-token", 0, null!, 0, [], null!, null!, null!));
        harness.UserClient.Setup(x => x.Current()).ReturnsAsync(CreateOctokitUser());

        GitHubAuthStateChangedEventArgs? raised = null;
        harness.Service.AuthStateChanged += (_, args) => raised = args;
        var deviceCode = new GitHubDeviceCodeResponse("device-code", "USER-CODE", "https://github.com/login/device", 900, 5);

        // Act
        var result = await harness.Service.WaitForAuthorizationAsync(deviceCode);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("octocat", result.Data!.Login);
        harness.TokenStorage.Verify(x => x.SaveTokenAsync(It.IsAny<SecureString>()), Times.Once);
        Assert.True(harness.Service.IsAuthenticated);
        Assert.Equal("octocat", harness.Service.CurrentUser?.Login);
        Assert.NotNull(raised);
        Assert.True(raised.IsAuthenticated);
        Assert.Equal("octocat", raised.User?.Login);
    }

    /// <summary>
    /// Verifies that completing authorization returns tailored guidance when the user denies the request.
    /// Octokit throws for terminal device-flow states instead of returning an error token.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task WaitForAuthorizationAsync_WhenDenied_ReturnsFailureAsync()
    {
        // Arrange
        SetGitHubEnvironment(clientId: "test-client-id");
        var harness = new AuthHarness();
        harness.Oauth
            .Setup(x => x.CreateAccessTokenForDeviceFlow(It.IsAny<string>(), It.IsAny<OauthDeviceFlowResponse>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException("access_denied: User denied access\nhttps://github.com/login/device", HttpStatusCode.BadRequest));
        var deviceCode = new GitHubDeviceCodeResponse("device-code", "USER-CODE", "https://github.com/login/device", 900, 5);

        // Act
        var result = await harness.Service.WaitForAuthorizationAsync(deviceCode);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("denied", result.Errors.First(), StringComparison.OrdinalIgnoreCase);
        harness.TokenStorage.Verify(x => x.SaveTokenAsync(It.IsAny<SecureString>()), Times.Never);
        Assert.False(harness.Service.IsAuthenticated);
    }

    /// <summary>
    /// Verifies that completing authorization returns tailored guidance when the device code expires.
    /// Octokit throws for terminal device-flow states instead of returning an error token.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task WaitForAuthorizationAsync_WhenExpired_ReturnsFailureAsync()
    {
        // Arrange
        SetGitHubEnvironment(clientId: "test-client-id");
        var harness = new AuthHarness();
        harness.Oauth
            .Setup(x => x.CreateAccessTokenForDeviceFlow(It.IsAny<string>(), It.IsAny<OauthDeviceFlowResponse>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException("expired_token: Device code expired\nhttps://github.com/login/device", HttpStatusCode.BadRequest));
        var deviceCode = new GitHubDeviceCodeResponse("device-code", "USER-CODE", "https://github.com/login/device", 900, 5);

        // Act
        var result = await harness.Service.WaitForAuthorizationAsync(deviceCode);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("expired", result.Errors.First(), StringComparison.OrdinalIgnoreCase);
        harness.TokenStorage.Verify(x => x.SaveTokenAsync(It.IsAny<SecureString>()), Times.Never);
        Assert.False(harness.Service.IsAuthenticated);
    }

    /// <summary>
    /// Verifies that completing authorization propagates cooperative cancellation.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task WaitForAuthorizationAsync_WhenCancelled_ThrowsAsync()
    {
        // Arrange
        SetGitHubEnvironment(clientId: "test-client-id");
        var harness = new AuthHarness();
        harness.Oauth
            .Setup(x => x.CreateAccessTokenForDeviceFlow(It.IsAny<string>(), It.IsAny<OauthDeviceFlowResponse>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var deviceCode = new GitHubDeviceCodeResponse("device-code", "USER-CODE", "https://github.com/login/device", 900, 5);

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() => harness.Service.WaitForAuthorizationAsync(deviceCode, cts.Token));
        harness.TokenStorage.Verify(x => x.SaveTokenAsync(It.IsAny<SecureString>()), Times.Never);
    }

    /// <summary>
    /// Verifies that completing authorization validates its input contract.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task WaitForAuthorizationAsync_WithInvalidDeviceCode_ThrowsAsync()
    {
        // Arrange
        SetGitHubEnvironment(clientId: "test-client-id");
        var harness = new AuthHarness();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => harness.Service.WaitForAuthorizationAsync(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => harness.Service.WaitForAuthorizationAsync(
            new GitHubDeviceCodeResponse(string.Empty, "USER-CODE", "https://github.com/login/device", 900, 5)));
    }

    /// <summary>
    /// Verifies that the current user lookup returns null when signed out.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GetCurrentUserAsync_WhenSignedOut_ReturnsNullAsync()
    {
        // Arrange
        SetGitHubEnvironment(clientId: "test-client-id");
        var harness = new AuthHarness();

        // Act
        var profile = await harness.Service.GetCurrentUserAsync();

        // Assert
        Assert.Null(profile);
        Assert.False(harness.Service.IsAuthenticated);
        harness.UserClient.Verify(x => x.Current(), Times.Never);
    }

    /// <summary>
    /// Verifies that the access token lookup falls back to the environment variable.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GetAccessTokenAsync_WithEnvironmentToken_ReturnsTokenAsync()
    {
        // Arrange
        SetGitHubEnvironment(clientId: "test-client-id", genHubToken: "env-token-value");
        var harness = new AuthHarness();

        // Act
        using var token = await harness.Service.GetAccessTokenAsync();

        // Assert
        Assert.NotNull(token);
        Assert.Equal("env-token-value", SecureStringHelper.ToUnsecureString(token));
        Assert.True(harness.Service.IsAuthenticated);
    }

    /// <summary>
    /// Verifies that signing out clears the token, suppresses the environment fallback, and raises the event.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task SignOutAsync_ClearsTokenAndRaisesEventAsync()
    {
        // Arrange
        SetGitHubEnvironment(clientId: "test-client-id", genHubToken: "env-token-value");
        var harness = new AuthHarness();
        GitHubAuthStateChangedEventArgs? raised = null;
        harness.Service.AuthStateChanged += (_, args) => raised = args;

        // Act
        await harness.Service.SignOutAsync();

        // Assert
        harness.TokenStorage.Verify(x => x.DeleteTokenAsync(), Times.Once);
        Assert.False(harness.Service.IsAuthenticated);
        Assert.Null(harness.Service.CurrentUser);
        Assert.Null(await harness.Service.GetAccessTokenAsync());
        Assert.NotNull(raised);
        Assert.False(raised.IsAuthenticated);
    }

    private static void SetGitHubEnvironment(string? clientId, string? genHubToken = null, string? gitHubToken = null)
    {
        Environment.SetEnvironmentVariable(GitHubConstants.OAuthClientIdEnvVar, clientId);
        Environment.SetEnvironmentVariable(GitHubConstants.GenHubTokenEnvVar, genHubToken);
        Environment.SetEnvironmentVariable(GitHubConstants.GitHubTokenEnvVar, gitHubToken);
    }

    private static User CreateOctokitUser()
    {
        return new User(
            avatarUrl: "https://avatars.example/octocat",
            bio: string.Empty,
            blog: string.Empty,
            collaborators: 0,
            company: string.Empty,
            createdAt: DateTimeOffset.UtcNow,
            updatedAt: DateTimeOffset.UtcNow,
            diskUsage: 0,
            email: string.Empty,
            followers: 0,
            following: 0,
            hireable: null,
            htmlUrl: "https://github.com/octocat",
            totalPrivateRepos: 0,
            id: 1,
            location: string.Empty,
            login: "octocat",
            name: "Octocat",
            nodeId: string.Empty,
            ownedPrivateRepos: 0,
            plan: null!,
            privateGists: 0,
            publicGists: 0,
            publicRepos: 0,
            url: string.Empty,
            permissions: null!,
            siteAdmin: false,
            ldapDistinguishedName: string.Empty,
            suspendedAt: null);
    }

    private sealed class AuthHarness
    {
        internal AuthHarness()
        {
            Oauth = new Mock<IOauthClient>();
            UserClient = new Mock<IUsersClient>();
            GitHubClient = new Mock<IGitHubClient>();
            GitHubClient.SetupGet(x => x.Oauth).Returns(Oauth.Object);
            GitHubClient.SetupGet(x => x.User).Returns(UserClient.Object);
            TokenStorage = new Mock<IGitHubTokenStorage>();
            Service = new GitHubAuthService(GitHubClient.Object, TokenStorage.Object, NullLogger<GitHubAuthService>.Instance);
        }

        internal Mock<IOauthClient> Oauth { get; }

        internal Mock<IUsersClient> UserClient { get; }

        internal Mock<IGitHubClient> GitHubClient { get; }

        internal Mock<IGitHubTokenStorage> TokenStorage { get; }

        internal GitHubAuthService Service { get; }
    }
}
