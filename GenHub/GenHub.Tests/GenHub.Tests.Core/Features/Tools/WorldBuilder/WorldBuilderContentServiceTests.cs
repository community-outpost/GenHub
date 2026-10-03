using FluentAssertions;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.Tools.WorldBuilder;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.WorldBuilder;
using GenHub.Features.Tools.WorldBuilder.Rendering;
using GenHub.Features.Tools.WorldBuilder.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.WorldBuilder;

/// <summary>
/// Tests for installation selection and one-time mounting in
/// <see cref="WorldBuilderContentService"/>.
/// </summary>
public sealed class WorldBuilderContentServiceTests
{
    /// <summary>
    /// Verifies the richest installation (both games) is mounted first.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task EnsureContentLoaded_PrefersInstallationWithBothGamesAsync()
    {
        // Arrange
        var both = CreateInstallation("both", hasGenerals: true, hasZeroHour: true);
        var zeroHourOnly = CreateInstallation("zh", hasGenerals: false, hasZeroHour: true);
        var harness = CreateHarness([zeroHourOnly, both]);

        // Act
        var result = await harness.Service.EnsureContentLoadedAsync();

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().BeTrue();
        harness.Service.MountedInstallationId.Should().Be(both.Id);
    }

    /// <summary>
    /// Verifies Zero Hour wins over Generals-only when no combined install exists.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task EnsureContentLoaded_PrefersZeroHourOverGeneralsAsync()
    {
        // Arrange
        var generals = CreateInstallation("g", hasGenerals: true, hasZeroHour: false);
        var zeroHour = CreateInstallation("z", hasGenerals: false, hasZeroHour: true);
        var harness = CreateHarness([generals, zeroHour]);

        // Act
        var result = await harness.Service.EnsureContentLoadedAsync();

        // Assert
        result.Success.Should().BeTrue();
        harness.Service.MountedInstallationId.Should().Be(zeroHour.Id);
    }

    /// <summary>
    /// Verifies a second call does not remount and reports already-ready.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task EnsureContentLoaded_Twice_MountsOnceAsync()
    {
        // Arrange
        var harness = CreateHarness([CreateInstallation("only", hasGenerals: true, hasZeroHour: true)]);

        // Act
        var first = await harness.Service.EnsureContentLoadedAsync();
        var second = await harness.Service.EnsureContentLoadedAsync();

        // Assert
        first.Data.Should().BeTrue();
        second.Success.Should().BeTrue();
        second.Data.Should().BeFalse();
        harness.FileSystem.Verify(
            f => f.MountAsync(It.IsAny<GameAssetMountSpec>(), It.IsAny<CancellationToken>()),
            Times.Once);
        harness.TextureCache.Verify(t => t.Clear(), Times.Once);
    }

    /// <summary>
    /// Verifies a failure result when no usable installation exists.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task EnsureContentLoaded_NoInstallations_FailsAsync()
    {
        // Arrange
        var harness = CreateHarness([]);

        // Act
        var result = await harness.Service.EnsureContentLoadedAsync();

        // Assert
        result.Success.Should().BeFalse();
        harness.Service.IsContentReady.Should().BeFalse();
        harness.FileSystem.Verify(
            f => f.MountAsync(It.IsAny<GameAssetMountSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies a mount failure is reported without marking content ready.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task EnsureContentLoaded_MountFails_FailsAsync()
    {
        // Arrange
        var harness = CreateHarness([CreateInstallation("broken", hasGenerals: true, hasZeroHour: true)]);
        harness.FileSystem
            .Setup(f => f.MountAsync(It.IsAny<GameAssetMountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateFailure("mount exploded"));

        // Act
        var result = await harness.Service.EnsureContentLoadedAsync();

        // Assert
        result.Success.Should().BeFalse();
        harness.Service.IsContentReady.Should().BeFalse();
    }

    /// <summary>
    /// Verifies an INI load failure is reported without marking content ready.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task EnsureContentLoaded_IniFails_FailsAsync()
    {
        // Arrange
        var harness = CreateHarness([CreateInstallation("ini-broken", hasGenerals: true, hasZeroHour: true)]);
        harness.IniDatabase
            .Setup(d => d.LoadSubsystemsAsync(It.IsAny<IGameAssetFileSystem>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<SageIniLoadReport>.CreateFailure("ini exploded"));

        // Act
        var result = await harness.Service.EnsureContentLoadedAsync();

        // Assert
        result.Success.Should().BeFalse();
        harness.Service.IsContentReady.Should().BeFalse();
    }

    private static GameInstallation CreateInstallation(string id, bool hasGenerals, bool hasZeroHour)
    {
        var installation = new GameInstallation($"/games/{id}", GameInstallationType.Steam)
        {
            Id = id,
            HasGenerals = hasGenerals,
            HasZeroHour = hasZeroHour,
        };
        return installation;
    }

    private static Harness CreateHarness(IReadOnlyList<GameInstallation> installations)
    {
        var installationService = new Mock<IGameInstallationService>();
        installationService
            .Setup(s => s.GetAllInstallationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<GameInstallation>>.CreateSuccess(installations));
        var fileSystem = new Mock<IGameAssetFileSystem>();
        fileSystem
            .Setup(f => f.MountAsync(It.IsAny<GameAssetMountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));
        var iniDatabase = new Mock<ISageIniDatabase>();
        iniDatabase
            .Setup(d => d.LoadSubsystemsAsync(It.IsAny<IGameAssetFileSystem>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<SageIniLoadReport>.CreateSuccess(
                new SageIniLoadReport([], 1, 2, [])));
        var textureCache = new Mock<ITextureCache>();
        var models = new WbModelRenderService(
            Mock.Of<IW3DAssetLoader>(),
            Mock.Of<IThingTemplateCatalog>(),
            Mock.Of<ITextureCache>(),
            Mock.Of<ILogger<WbModelRenderService>>());
        var service = new WorldBuilderContentService(
            installationService.Object,
            fileSystem.Object,
            iniDatabase.Object,
            textureCache.Object,
            models,
            NullLogger<WorldBuilderContentService>.Instance);
        return new Harness(service, fileSystem, iniDatabase, textureCache);
    }

    private sealed record Harness(
        WorldBuilderContentService Service,
        Mock<IGameAssetFileSystem> FileSystem,
        Mock<ISageIniDatabase> IniDatabase,
        Mock<ITextureCache> TextureCache);
}
