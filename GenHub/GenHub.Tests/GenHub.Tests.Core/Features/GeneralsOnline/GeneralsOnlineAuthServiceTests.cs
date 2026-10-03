using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.GeneralsOnline;
using GenHub.Core.Models.GeneralsOnline;
using GenHub.Core.Models.Results;
using GenHub.Features.GeneralsOnline.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace GenHub.Tests.Core.Features.GeneralsOnline;

/// <summary>
/// Unit tests for <see cref="GeneralsOnlineAuthService"/> silent login.
/// </summary>
public class GeneralsOnlineAuthServiceTests
{
    /// <summary>
    /// Tests that a transient backend failure keeps the stored refresh token
    /// so the next launch can retry instead of forcing a browser sign-in.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task TryLoginWithStoredTokenAsync_OnServiceUnavailable_ShouldKeepStoredTokenAsync()
    {
        // Arrange
        var storage = new Mock<IGeneralsOnlineTokenStorage>();
        storage.Setup(s => s.LoadTokenAsync()).ReturnsAsync(SecureStringHelper.ToSecureString("refresh-token"));
        var api = new Mock<IGeneralsOnlineApiClient>();
        api.Setup(a => a.LoginWithTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<LoginResult>.CreateFailure(GeneralsOnlineConstants.ErrorServiceUnavailable));
        var service = CreateService(api.Object, storage.Object);

        // Act
        var result = await service.TryLoginWithStoredTokenAsync();

        // Assert
        Assert.False(result.Success);
        Assert.Equal(GeneralsOnlineAuthState.Unauthenticated, service.AuthState);
        storage.Verify(s => s.DeleteTokenAsync(), Times.Never);
    }

    /// <summary>
    /// Tests that a definite backend rejection clears the stored refresh token.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task TryLoginWithStoredTokenAsync_OnLoginRejected_ShouldClearStoredTokenAsync()
    {
        // Arrange
        var storage = new Mock<IGeneralsOnlineTokenStorage>();
        storage.Setup(s => s.LoadTokenAsync()).ReturnsAsync(SecureStringHelper.ToSecureString("refresh-token"));
        var api = new Mock<IGeneralsOnlineApiClient>();
        api.Setup(a => a.LoginWithTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<LoginResult>.CreateFailure(GeneralsOnlineConstants.ErrorLoginFailed));
        var service = CreateService(api.Object, storage.Object);

        // Act
        var result = await service.TryLoginWithStoredTokenAsync();

        // Assert
        Assert.False(result.Success);
        Assert.Equal(GeneralsOnlineAuthState.Unauthenticated, service.AuthState);
        storage.Verify(s => s.DeleteTokenAsync(), Times.Once);
    }

    /// <summary>
    /// Tests that a successful silent login authenticates and rotates the token.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task TryLoginWithStoredTokenAsync_OnSuccess_ShouldAuthenticateAsync()
    {
        // Arrange
        var storage = new Mock<IGeneralsOnlineTokenStorage>();
        storage.Setup(s => s.LoadTokenAsync()).ReturnsAsync(SecureStringHelper.ToSecureString("refresh-token"));
        var api = new Mock<IGeneralsOnlineApiClient>();
        api.Setup(a => a.LoginWithTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<LoginResult>.CreateSuccess(new LoginResult
            {
                Result = PendingLoginState.LoginSuccess,
                SessionToken = "session",
                RefreshToken = "rotated",
                DisplayName = "PlayerName",
            }));
        var service = CreateService(api.Object, storage.Object);

        // Act
        var result = await service.TryLoginWithStoredTokenAsync();

        // Assert
        Assert.True(result.Success);
        Assert.Equal(GeneralsOnlineAuthState.Authenticated, service.AuthState);
        Assert.Equal("PlayerName", service.CurrentDisplayName);
        storage.Verify(s => s.SaveTokenAsync(It.IsAny<System.Security.SecureString>()), Times.Once);
        storage.Verify(s => s.DeleteTokenAsync(), Times.Never);
    }

    /// <summary>
    /// Tests that silent login without a stored token fails without calling the API.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task TryLoginWithStoredTokenAsync_WithoutStoredToken_ShouldReturnAuthRequiredAsync()
    {
        // Arrange
        var storage = new Mock<IGeneralsOnlineTokenStorage>();
        storage.Setup(s => s.LoadTokenAsync()).ReturnsAsync((System.Security.SecureString?)null);
        var api = new Mock<IGeneralsOnlineApiClient>();
        var service = CreateService(api.Object, storage.Object);

        // Act
        var result = await service.TryLoginWithStoredTokenAsync();

        // Assert
        Assert.False(result.Success);
        Assert.Contains(GeneralsOnlineConstants.ErrorAuthRequired, result.Errors);
        api.Verify(a => a.LoginWithTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Tests that cancelling the browser login resets the pending state.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task LoginWithBrowserAsync_OnCancel_ShouldResetPendingStateAsync()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var storage = new Mock<IGeneralsOnlineTokenStorage>();
        var api = new Mock<IGeneralsOnlineApiClient>();
        api.Setup(a => a.GetLoginCodeAsync(It.IsAny<CancellationToken>()))
            .Callback(() => cts.Cancel())
            .ReturnsAsync(OperationResult<string>.CreateSuccess("game-code"));
        var service = CreateService(api.Object, storage.Object);
        service.BrowserOpener = _ => true;

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() => service.LoginWithBrowserAsync(cts.Token));
        Assert.Equal(GeneralsOnlineAuthState.Unauthenticated, service.AuthState);
    }

    /// <summary>
    /// Tests that a login success without a session token fails instead of authenticating.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task TryLoginWithStoredTokenAsync_OnEmptySessionToken_ShouldFailAndClearTokenAsync()
    {
        // Arrange
        var storage = new Mock<IGeneralsOnlineTokenStorage>();
        storage.Setup(s => s.LoadTokenAsync()).ReturnsAsync(SecureStringHelper.ToSecureString("refresh-token"));
        var api = new Mock<IGeneralsOnlineApiClient>();
        api.Setup(a => a.LoginWithTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<LoginResult>.CreateSuccess(new LoginResult
            {
                Result = PendingLoginState.LoginSuccess,
                SessionToken = string.Empty,
                RefreshToken = "rotated",
                DisplayName = "PlayerName",
            }));
        var service = CreateService(api.Object, storage.Object);

        // Act
        var result = await service.TryLoginWithStoredTokenAsync();

        // Assert
        Assert.False(result.Success);
        Assert.Equal(GeneralsOnlineAuthState.Unauthenticated, service.AuthState);
        Assert.Null(service.CurrentSessionToken);
        storage.Verify(s => s.DeleteTokenAsync(), Times.Once);
        storage.Verify(s => s.SaveTokenAsync(It.IsAny<System.Security.SecureString>()), Times.Never);
    }

    /// <summary>
    /// Tests that a token persistence failure fails the login instead of faulting.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task TryLoginWithStoredTokenAsync_OnTokenSaveFailure_ShouldFailAndResetStateAsync()
    {
        // Arrange
        var storage = new Mock<IGeneralsOnlineTokenStorage>();
        storage.Setup(s => s.LoadTokenAsync()).ReturnsAsync(SecureStringHelper.ToSecureString("refresh-token"));
        storage.Setup(s => s.SaveTokenAsync(It.IsAny<System.Security.SecureString>()))
            .ThrowsAsync(new IOException("Disk unavailable."));
        var api = new Mock<IGeneralsOnlineApiClient>();
        api.Setup(a => a.LoginWithTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<LoginResult>.CreateSuccess(new LoginResult
            {
                Result = PendingLoginState.LoginSuccess,
                SessionToken = "session",
                RefreshToken = "rotated",
                DisplayName = "PlayerName",
            }));
        var service = CreateService(api.Object, storage.Object);

        // Act
        var result = await service.TryLoginWithStoredTokenAsync();

        // Assert
        Assert.False(result.Success);
        Assert.Equal(GeneralsOnlineAuthState.Unauthenticated, service.AuthState);
    }

    /// <summary>
    /// Tests that a subscriber calling back into the service cannot deadlock the login.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task SilentLogin_WithReentrantSubscriber_ShouldCompleteWithoutDeadlockAsync()
    {
        // Arrange
        var storage = new Mock<IGeneralsOnlineTokenStorage>();
        storage.Setup(s => s.LoadTokenAsync()).ReturnsAsync(SecureStringHelper.ToSecureString("refresh-token"));
        var api = new Mock<IGeneralsOnlineApiClient>();
        api.Setup(a => a.LoginWithTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<LoginResult>.CreateSuccess(new LoginResult
            {
                Result = PendingLoginState.LoginSuccess,
                SessionToken = "session",
                RefreshToken = "rotated",
                DisplayName = "PlayerName",
            }));
        var service = CreateService(api.Object, storage.Object);
        service.AuthStateChanged += (_, state) =>
        {
            if (state == GeneralsOnlineAuthState.Authenticated)
            {
                service.LogoutAsync().GetAwaiter().GetResult();
            }
        };

        // Act
        var loginTask = service.TryLoginWithStoredTokenAsync();
        var completed = await Task.WhenAny(loginTask, Task.Delay(5000)) == loginTask;

        // Assert
        Assert.True(completed);
        Assert.True((await loginTask).Success);
        Assert.Equal(GeneralsOnlineAuthState.Unauthenticated, service.AuthState);
    }

    private static GeneralsOnlineAuthService CreateService(
        IGeneralsOnlineApiClient apiClient,
        IGeneralsOnlineTokenStorage tokenStorage)
    {
        return new GeneralsOnlineAuthService(
            apiClient,
            tokenStorage,
            Mock.Of<ILogger<GeneralsOnlineAuthService>>());
    }
}
