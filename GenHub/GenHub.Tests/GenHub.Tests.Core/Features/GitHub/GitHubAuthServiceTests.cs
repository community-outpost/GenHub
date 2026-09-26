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
using System.IO;
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
public class GitHubAuthServiceTests : IDisposable
{
    private readonly string? _originalClientId;
    private readonly string? _originalGenHubToken;
    private readonly string? _originalGitHubToken;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitHubAuthServiceTests"/> class.
    /// </summary>
    public GitHubAuthServiceTests()
    {
        _originalClientId = Environment.GetEnvironmentVariable(GitHubConstants.OAuthClientIdEnvVar);
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
        Environment.SetEnvironmentVariable(GitHubConstants.OAuthClientIdEnvVar, _originalClientId);
        Environment.SetEnvironmentVariable(GitHubConstants.GenHubTokenEnvVar, _originalGenHubToken);
        Environment.SetEnvironmentVariable(GitHubConstants.GitHubTokenEnvVar, _originalGitHubToken);
        GC.SuppressFinalize(this);
    }

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
        Assert.Contains("Approve the request in your browser", result.Errors.First(), StringComparison.Ordinal);
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
        Assert.Contains("Try signing in again", result.Errors.First(), StringComparison.Ordinal);
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

    /// <summary>
    /// Verifies that a failed profile fetch rolls back the staged credentials without persisting the token or raising the event.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task WaitForAuthorizationAsync_WhenProfileLoadFails_DoesNotPersistTokenAsync()
    {
        // Arrange
        SetGitHubEnvironment(clientId: "test-client-id");
        var harness = new AuthHarness();
        harness.Oauth
            .Setup(x => x.CreateAccessTokenForDeviceFlow(It.IsAny<string>(), It.IsAny<OauthDeviceFlowResponse>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OauthToken("bearer", "oauth-access-token", 0, null!, 0, [], null!, null!, null!));
        harness.UserClient.Setup(x => x.Current()).ThrowsAsync(new ApiException("Server error", HttpStatusCode.InternalServerError));
        var raised = false;
        harness.Service.AuthStateChanged += (_, _) => raised = true;
        var deviceCode = new GitHubDeviceCodeResponse("device-code", "USER-CODE", "https://github.com/login/device", 900, 5);

        // Act
        var result = await harness.Service.WaitForAuthorizationAsync(deviceCode);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("profile could not be loaded", result.Errors.First(), StringComparison.Ordinal);
        harness.TokenStorage.Verify(x => x.SaveTokenAsync(It.IsAny<SecureString>()), Times.Never);
        Assert.False(raised);
        Assert.False(harness.Service.IsAuthenticated);
        Assert.Null(harness.Service.CurrentUser);
    }

    /// <summary>
    /// Verifies that a sign-out during device flow polling aborts the login without persisting the token.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task WaitForAuthorizationAsync_WhenSignOutDuringPoll_AbortsLoginAsync()
    {
        // Arrange
        SetGitHubEnvironment(clientId: "test-client-id");
        var harness = new AuthHarness();
        harness.Oauth
            .Setup(x => x.CreateAccessTokenForDeviceFlow(It.IsAny<string>(), It.IsAny<OauthDeviceFlowResponse>(), It.IsAny<CancellationToken>()))
            .Callback(() => harness.Service.SignOutAsync().GetAwaiter().GetResult())
            .ReturnsAsync(new OauthToken("bearer", "oauth-access-token", 0, null!, 0, [], null!, null!, null!));
        harness.UserClient.Setup(x => x.Current()).ReturnsAsync(CreateOctokitUser());
        var signInRaised = false;
        harness.Service.AuthStateChanged += (_, args) => signInRaised |= args.IsAuthenticated;
        var deviceCode = new GitHubDeviceCodeResponse("device-code", "USER-CODE", "https://github.com/login/device", 900, 5);

        // Act
        var result = await harness.Service.WaitForAuthorizationAsync(deviceCode);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("was cancelled", result.Errors.First(), StringComparison.Ordinal);
        harness.TokenStorage.Verify(x => x.SaveTokenAsync(It.IsAny<SecureString>()), Times.Never);
        Assert.False(signInRaised);
        Assert.False(harness.Service.IsAuthenticated);
    }

    /// <summary>
    /// Verifies that token persistence I/O failures surface as login failures instead of escaping.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task WaitForAuthorizationAsync_WhenTokenSaveFails_ReturnsFailureAsync()
    {
        // Arrange
        SetGitHubEnvironment(clientId: "test-client-id");
        var harness = new AuthHarness();
        harness.TokenStorage
            .Setup(x => x.SaveTokenAsync(It.IsAny<SecureString>()))
            .ThrowsAsync(new IOException("Disk full"));
        harness.Oauth
            .Setup(x => x.CreateAccessTokenForDeviceFlow(It.IsAny<string>(), It.IsAny<OauthDeviceFlowResponse>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OauthToken("bearer", "oauth-access-token", 0, null!, 0, [], null!, null!, null!));
        harness.UserClient.Setup(x => x.Current()).ReturnsAsync(CreateOctokitUser());
        var raised = false;
        harness.Service.AuthStateChanged += (_, _) => raised = true;
        var deviceCode = new GitHubDeviceCodeResponse("device-code", "USER-CODE", "https://github.com/login/device", 900, 5);

        // Act
        var result = await harness.Service.WaitForAuthorizationAsync(deviceCode);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("could not be saved", result.Errors.First(), StringComparison.Ordinal);
        Assert.False(raised);
        Assert.False(harness.Service.IsAuthenticated);
        Assert.Null(harness.Service.CurrentUser);
    }

    /// <summary>
    /// Verifies that a rejected credential marks the session expired, reports signed out, and raises the event.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GetCurrentUserAsync_WhenTokenRejected_MarksSessionExpiredAsync()
    {
        // Arrange
        SetGitHubEnvironment(clientId: "test-client-id", genHubToken: "rejected-token-value");
        var harness = new AuthHarness();
        harness.UserClient.Setup(x => x.Current()).ThrowsAsync(new AuthorizationException(Mock.Of<IResponse>()));
        GitHubAuthStateChangedEventArgs? raised = null;
        harness.Service.AuthStateChanged += (_, args) => raised = args;

        // Act
        var profile = await harness.Service.GetCurrentUserAsync();

        // Assert
        Assert.Null(profile);
        Assert.False(harness.Service.IsAuthenticated);
        Assert.True(harness.Service.IsSessionExpired);
        Assert.Null(harness.Service.CurrentUser);
        Assert.Null(await harness.Service.GetAccessTokenAsync());
        Assert.NotNull(raised);
        Assert.False(raised.IsAuthenticated);
    }

    /// <summary>
    /// Verifies that an authorization failure without a presented credential reports signed out
    /// without marking the session expired and without deleting the credential that was never presented.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GetCurrentUserAsync_WhenRejectedWithoutCredential_ReportsSignedOutAsync()
    {
        // Arrange
        SetGitHubEnvironment(clientId: "test-client-id");
        var harness = new AuthHarness();
        harness.TokenStorage.Setup(x => x.HasToken()).Returns(true);
        harness.TokenStorage.Setup(x => x.LoadTokenAsync()).ReturnsAsync((SecureString?)null);
        harness.UserClient.Setup(x => x.Current()).ThrowsAsync(new AuthorizationException(Mock.Of<IResponse>()));
        GitHubAuthStateChangedEventArgs? raised = null;
        harness.Service.AuthStateChanged += (_, args) => raised = args;

        // Act
        var profile = await harness.Service.GetCurrentUserAsync();

        // Assert
        Assert.Null(profile);
        Assert.False(harness.Service.IsSessionExpired);
        Assert.False(harness.Service.IsAuthenticated);
        Assert.Null(harness.Service.CurrentUser);
        Assert.Null(await harness.Service.GetAccessTokenAsync());
        Assert.NotNull(raised);
        Assert.False(raised.IsAuthenticated);
        harness.TokenStorage.Verify(x => x.DeleteTokenAsync(), Times.Never);
    }

    /// <summary>
    /// Verifies that starting a login attempt keeps the expired state so an abandoned re-link cannot resurrect a signed in state.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task InitiateLoginAsync_WhenSessionExpired_KeepsExpiredStateAsync()
    {
        // Arrange
        SetGitHubEnvironment(clientId: "test-client-id", genHubToken: "rejected-token-value");
        var harness = new AuthHarness();
        harness.UserClient.Setup(x => x.Current()).ThrowsAsync(new AuthorizationException(Mock.Of<IResponse>()));
        Assert.Null(await harness.Service.GetCurrentUserAsync());
        Assert.True(harness.Service.IsSessionExpired);
        harness.Oauth
            .Setup(x => x.InitiateDeviceFlow(It.IsAny<OauthDeviceFlowRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OauthDeviceFlowResponse("device-code", "USER-CODE", "https://github.com/login/device", 900, 5));

        // Act
        var result = await harness.Service.InitiateLoginAsync();

        // Assert
        Assert.True(result.Success);
        Assert.True(harness.Service.IsSessionExpired);
        Assert.False(harness.Service.IsAuthenticated);
    }

    /// <summary>
    /// Verifies that a successful authorization after expiry clears the expired state and publishes the profile.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task WaitForAuthorizationAsync_AfterSessionExpired_ResetsExpiredStateAsync()
    {
        // Arrange
        SetGitHubEnvironment(clientId: "test-client-id", genHubToken: "rejected-token-value");
        var harness = new AuthHarness();
        harness.UserClient.Setup(x => x.Current()).ThrowsAsync(new AuthorizationException(Mock.Of<IResponse>()));
        Assert.Null(await harness.Service.GetCurrentUserAsync());
        Assert.True(harness.Service.IsSessionExpired);
        harness.UserClient.Setup(x => x.Current()).ReturnsAsync(CreateOctokitUser());
        harness.Oauth
            .Setup(x => x.CreateAccessTokenForDeviceFlow(It.IsAny<string>(), It.IsAny<OauthDeviceFlowResponse>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OauthToken("bearer", "oauth-access-token", 0, null!, 0, [], null!, null!, null!));
        var deviceCode = new GitHubDeviceCodeResponse("device-code", "USER-CODE", "https://github.com/login/device", 900, 5);

        // Act
        var result = await harness.Service.WaitForAuthorizationAsync(deviceCode);

        // Assert
        Assert.True(result.Success);
        Assert.False(harness.Service.IsSessionExpired);
        Assert.True(harness.Service.IsAuthenticated);
        Assert.Equal("octocat", harness.Service.CurrentUser?.Login);
    }

    /// <summary>
    /// Verifies that signing out clears the expired state instead of reporting an expired session.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task SignOutAsync_WhenSessionExpired_ClearsExpiredFlagAsync()
    {
        // Arrange
        SetGitHubEnvironment(clientId: "test-client-id", genHubToken: "rejected-token-value");
        var harness = new AuthHarness();
        harness.UserClient.Setup(x => x.Current()).ThrowsAsync(new AuthorizationException(Mock.Of<IResponse>()));
        Assert.Null(await harness.Service.GetCurrentUserAsync());
        Assert.True(harness.Service.IsSessionExpired);

        // Act
        await harness.Service.SignOutAsync();

        // Assert
        Assert.False(harness.Service.IsSessionExpired);
        Assert.False(harness.Service.IsAuthenticated);
        Assert.Null(harness.Service.CurrentUser);
    }

    /// <summary>
    /// Verifies that a sign-out completing while a profile fetch is in flight wins over the late authorization failure.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GetCurrentUserAsync_WhenSignOutDuringFetch_DoesNotMarkExpiredAsync()
    {
        // Arrange
        SetGitHubEnvironment(clientId: "test-client-id", genHubToken: "rejected-token-value");
        var harness = new AuthHarness();
        using var entered = new ManualResetEventSlim(false);
        var profileFetch = new TaskCompletionSource<User>();
        harness.UserClient
            .Setup(x => x.Current())
            .Callback(() => entered.Set())
            .Returns(profileFetch.Task);
        var raisedCount = 0;
        harness.Service.AuthStateChanged += (_, _) => raisedCount++;
        var fetchTask = harness.Service.GetCurrentUserAsync();
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));

        // Act
        await harness.Service.SignOutAsync();
        profileFetch.SetException(new AuthorizationException(Mock.Of<IResponse>()));

        // Assert
        Assert.Null(await fetchTask);
        Assert.False(harness.Service.IsSessionExpired);
        Assert.False(harness.Service.IsAuthenticated);
        Assert.Null(harness.Service.CurrentUser);
        Assert.Equal(1, raisedCount);
    }

    /// <summary>
    /// Verifies that a rejected stored token is deleted so the next launch does not reload and re-reject it.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GetCurrentUserAsync_WhenStoredTokenRejected_DeletesStoredTokenAsync()
    {
        // Arrange
        SetGitHubEnvironment(clientId: "test-client-id");
        var harness = new AuthHarness();
        harness.TokenStorage.Setup(x => x.HasToken()).Returns(true);
        harness.TokenStorage
            .Setup(x => x.LoadTokenAsync())
            .Returns(() => Task.FromResult<SecureString?>(SecureStringHelper.ToSecureString("stored-token-value")));
        harness.UserClient.Setup(x => x.Current()).ThrowsAsync(new AuthorizationException(Mock.Of<IResponse>()));
        GitHubAuthStateChangedEventArgs? raised = null;
        harness.Service.AuthStateChanged += (_, args) => raised = args;

        // Act
        var profile = await harness.Service.GetCurrentUserAsync();

        // Assert
        Assert.Null(profile);
        Assert.True(harness.Service.IsSessionExpired);
        Assert.False(harness.Service.IsAuthenticated);
        harness.TokenStorage.Verify(x => x.DeleteTokenAsync(), Times.Once);
        Assert.NotNull(raised);
        Assert.False(raised.IsAuthenticated);
    }

    /// <summary>
    /// Verifies that a sign-in completing while a profile fetch is in flight wins over the late
    /// authorization failure, preserving the fresh session and the token just saved.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GetCurrentUserAsync_WhenSignInCompletesDuringFetch_DoesNotMarkExpiredAsync()
    {
        // Arrange
        SetGitHubEnvironment(clientId: "test-client-id");
        var harness = new AuthHarness();
        harness.TokenStorage.Setup(x => x.HasToken()).Returns(true);
        harness.TokenStorage
            .Setup(x => x.LoadTokenAsync())
            .Returns(() => Task.FromResult<SecureString?>(SecureStringHelper.ToSecureString("stored-token-value")));
        using var entered = new ManualResetEventSlim(false);
        var staleFetch = new TaskCompletionSource<User>();
        var profileCalls = 0;
        harness.UserClient.Setup(x => x.Current()).Returns(() =>
        {
            if (Interlocked.Increment(ref profileCalls) == 1)
            {
                entered.Set();
                return staleFetch.Task;
            }

            return Task.FromResult(CreateOctokitUser());
        });
        harness.Oauth
            .Setup(x => x.CreateAccessTokenForDeviceFlow(It.IsAny<string>(), It.IsAny<OauthDeviceFlowResponse>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OauthToken("bearer", "oauth-access-token", 0, null!, 0, [], null!, null!, null!));
        var raisedCount = 0;
        harness.Service.AuthStateChanged += (_, _) => raisedCount++;
        var fetchTask = harness.Service.GetCurrentUserAsync();
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        var deviceCode = new GitHubDeviceCodeResponse("device-code", "USER-CODE", "https://github.com/login/device", 900, 5);

        // Act
        var loginResult = await harness.Service.WaitForAuthorizationAsync(deviceCode);
        staleFetch.SetException(new AuthorizationException(Mock.Of<IResponse>()));

        // Assert
        Assert.True(loginResult.Success);
        Assert.Null(await fetchTask);
        Assert.False(harness.Service.IsSessionExpired);
        Assert.True(harness.Service.IsAuthenticated);
        Assert.Equal("octocat", harness.Service.CurrentUser?.Login);
        Assert.Equal(1, raisedCount);
        harness.TokenStorage.Verify(x => x.DeleteTokenAsync(), Times.Never);
    }

    /// <summary>
    /// Verifies that a sign-in persisting while a stale rejected-token cleanup is in flight keeps
    /// the fresh token: the save waits for the delete to finish instead of racing it.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GetCurrentUserAsync_WhenSignInPersistsDuringStaleCleanup_PreservesFreshTokenAsync()
    {
        // Arrange
        SetGitHubEnvironment(clientId: "test-client-id");
        var harness = new AuthHarness();
        harness.TokenStorage.Setup(x => x.HasToken()).Returns(true);
        harness.TokenStorage
            .Setup(x => x.LoadTokenAsync())
            .Returns(() => Task.FromResult<SecureString?>(SecureStringHelper.ToSecureString("stored-token-value")));
        using var entered = new ManualResetEventSlim(false);
        var staleFetch = new TaskCompletionSource<User>();
        var profileCalls = 0;
        harness.UserClient.Setup(x => x.Current()).Returns(() =>
        {
            if (Interlocked.Increment(ref profileCalls) == 1)
            {
                entered.Set();
                return staleFetch.Task;
            }

            return Task.FromResult(CreateOctokitUser());
        });
        harness.Oauth
            .Setup(x => x.CreateAccessTokenForDeviceFlow(It.IsAny<string>(), It.IsAny<OauthDeviceFlowResponse>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OauthToken("bearer", "oauth-access-token", 0, null!, 0, [], null!, null!, null!));
        using var deleteEntered = new ManualResetEventSlim(false);
        var deleteGate = new TaskCompletionSource();
        var completionOrder = new List<string>();
        harness.TokenStorage
            .Setup(x => x.DeleteTokenAsync())
            .Callback(() => deleteEntered.Set())
            .Returns(async () =>
            {
                await deleteGate.Task;
                lock (completionOrder)
                {
                    completionOrder.Add("delete");
                }
            });
        var saveCompleted = new TaskCompletionSource();
        harness.TokenStorage
            .Setup(x => x.SaveTokenAsync(It.IsAny<SecureString>()))
            .Callback(() =>
            {
                lock (completionOrder)
                {
                    completionOrder.Add("save");
                }

                saveCompleted.TrySetResult();
            })
            .Returns(Task.CompletedTask);
        var fetchTask = harness.Service.GetCurrentUserAsync();
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        staleFetch.SetException(new AuthorizationException(Mock.Of<IResponse>()));
        Assert.True(deleteEntered.Wait(TimeSpan.FromSeconds(5)));

        // Act: start the sign-in while the stale cleanup is blocked inside the delete.
        // The save cannot proceed until the test releases the delete, so the wait below
        // always expires; without the gate the save wins the race and the stale delete
        // then removes the fresh token.
        var deviceCode = new GitHubDeviceCodeResponse("device-code", "USER-CODE", "https://github.com/login/device", 900, 5);
        var loginTask = harness.Service.WaitForAuthorizationAsync(deviceCode);
        _ = await Task.WhenAny(saveCompleted.Task, Task.Delay(TimeSpan.FromSeconds(2)));
        deleteGate.TrySetResult();

        // Assert
        var loginResult = await loginTask;
        Assert.True(loginResult.Success);
        Assert.Null(await fetchTask);
        Assert.False(harness.Service.IsSessionExpired);
        Assert.True(harness.Service.IsAuthenticated);
        Assert.Equal("octocat", harness.Service.CurrentUser?.Login);
        Assert.Equal(["delete", "save"], completionOrder);
        harness.TokenStorage.Verify(x => x.SaveTokenAsync(It.IsAny<SecureString>()), Times.Once);
        harness.TokenStorage.Verify(x => x.DeleteTokenAsync(), Times.Once);
    }

    /// <summary>
    /// Verifies that a successful authorization after an unloadable credential clears the
    /// unusable state and publishes the profile.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task WaitForAuthorizationAsync_AfterCredentialUnusable_ResetsUnusableStateAsync()
    {
        // Arrange
        SetGitHubEnvironment(clientId: "test-client-id");
        var harness = new AuthHarness();
        harness.TokenStorage.Setup(x => x.HasToken()).Returns(true);
        harness.TokenStorage.Setup(x => x.LoadTokenAsync()).ReturnsAsync((SecureString?)null);
        harness.UserClient.Setup(x => x.Current()).ThrowsAsync(new AuthorizationException(Mock.Of<IResponse>()));
        Assert.Null(await harness.Service.GetCurrentUserAsync());
        Assert.False(harness.Service.IsAuthenticated);
        harness.UserClient.Setup(x => x.Current()).ReturnsAsync(CreateOctokitUser());
        harness.Oauth
            .Setup(x => x.CreateAccessTokenForDeviceFlow(It.IsAny<string>(), It.IsAny<OauthDeviceFlowResponse>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OauthToken("bearer", "oauth-access-token", 0, null!, 0, [], null!, null!, null!));
        var deviceCode = new GitHubDeviceCodeResponse("device-code", "USER-CODE", "https://github.com/login/device", 900, 5);

        // Act
        var result = await harness.Service.WaitForAuthorizationAsync(deviceCode);

        // Assert
        Assert.True(result.Success);
        Assert.False(harness.Service.IsSessionExpired);
        Assert.True(harness.Service.IsAuthenticated);
        Assert.Equal("octocat", harness.Service.CurrentUser?.Login);
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
