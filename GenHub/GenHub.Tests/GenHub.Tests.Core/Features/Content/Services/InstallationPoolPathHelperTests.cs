using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Results;
using GenHub.Features.Content.Services;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.Content.Services;

/// <summary>
/// Unit tests for <see cref="InstallationPoolPathHelper"/>.
/// </summary>
public class InstallationPoolPathHelperTests
{
    private readonly Mock<IGameInstallationService> _installationServiceMock = new();
    private readonly Mock<IInstallationCasPoolService> _installationCasPoolServiceMock = new();
    private readonly Mock<ILogger> _loggerMock = new();

    /// <summary>
    /// Verifies that cancellation is rethrown when requested.
    /// </summary>
    /// <returns>The async task.</returns>
    [Fact]
    public async Task EnsureInstallationPoolPathAsync_WhenCancelled_RethrowsOperationCanceledExceptionAsync()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        _installationServiceMock
            .Setup(x => x.GetAllInstallationsAsync(cts.Token))
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            InstallationPoolPathHelper.EnsureInstallationPoolPathAsync(
                _installationServiceMock.Object,
                _installationCasPoolServiceMock.Object,
                _loggerMock.Object,
                cts.Token));
    }

    /// <summary>
    /// Verifies that when getting installations fails, true is returned so primary CAS pool can be used.
    /// </summary>
    /// <returns>The async task.</returns>
    [Fact]
    public async Task EnsureInstallationPoolPathAsync_WhenInstallationsFail_ReturnsTrueAsync()
    {
        // Arrange
        _installationServiceMock
            .Setup(x => x.GetAllInstallationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<GameInstallation>>.CreateFailure("no installations found"));

        // Act
        var result = await InstallationPoolPathHelper.EnsureInstallationPoolPathAsync(
            _installationServiceMock.Object,
            _installationCasPoolServiceMock.Object,
            _loggerMock.Object,
            CancellationToken.None);

        // Assert
        Assert.True(result);
        _installationServiceMock.Verify(x => x.InvalidateCache(), Times.Once);
        _installationCasPoolServiceMock.Verify(
            x => x.EnsurePoolPathAsync(It.IsAny<IReadOnlyList<GameInstallation>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies that when pool path is successfully ensured, true is returned.
    /// </summary>
    /// <returns>The async task.</returns>
    [Fact]
    public async Task EnsureInstallationPoolPathAsync_WhenPoolPathEnsured_ReturnsTrueAsync()
    {
        // Arrange
        var installations = new List<GameInstallation>();
        _installationServiceMock
            .Setup(x => x.GetAllInstallationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<GameInstallation>>.CreateSuccess(installations));
        _installationCasPoolServiceMock
            .Setup(x => x.EnsurePoolPathAsync(It.IsAny<IReadOnlyList<GameInstallation>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await InstallationPoolPathHelper.EnsureInstallationPoolPathAsync(
            _installationServiceMock.Object,
            _installationCasPoolServiceMock.Object,
            _loggerMock.Object,
            CancellationToken.None);

        // Assert
        Assert.True(result);
        _installationCasPoolServiceMock.Verify(
            x => x.EnsurePoolPathAsync(It.IsAny<IReadOnlyList<GameInstallation>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that when an unexpected exception is thrown, false is returned.
    /// </summary>
    /// <returns>The async task.</returns>
    [Fact]
    public async Task EnsureInstallationPoolPathAsync_WhenUnexpectedExceptionOccurs_ReturnsFalseAsync()
    {
        // Arrange
        _installationServiceMock
            .Setup(x => x.GetAllInstallationsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("unexpected failure"));

        // Act
        var result = await InstallationPoolPathHelper.EnsureInstallationPoolPathAsync(
            _installationServiceMock.Object,
            _installationCasPoolServiceMock.Object,
            _loggerMock.Object,
            CancellationToken.None);

        // Assert
        Assert.False(result);
    }
}
