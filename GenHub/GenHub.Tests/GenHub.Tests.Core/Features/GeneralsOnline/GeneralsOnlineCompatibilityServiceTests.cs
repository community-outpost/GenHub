using GenHub.Core.Constants;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Tools.Checksum;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.GeneralsOnline;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Features.GeneralsOnline.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace GenHub.Tests.Core.Features.GeneralsOnline;

/// <summary>
/// Unit tests for <see cref="GeneralsOnlineCompatibilityService"/> profile CRC caching.
/// </summary>
public class GeneralsOnlineCompatibilityServiceTests : IDisposable
{
    private readonly string _tempDir;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="GeneralsOnlineCompatibilityServiceTests"/> class.
    /// </summary>
    public GeneralsOnlineCompatibilityServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "GenHubCompatibilityTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(Path.Combine(_tempDir, GameClientConstants.ZeroHourExecutable), "fake-binary");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch (IOException)
        {
            // Best effort cleanup of the temp directory.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort cleanup of the temp directory.
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Tests that a second match within the TTL reuses the cached CRCs.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task MatchProfileAsync_WithinTtl_ReusesCachedCrcsAsync()
    {
        // Arrange
        var clock = new Mock<TimeProvider>();
        clock.Setup(c => c.GetUtcNow()).Returns(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var calculator = CreateCalculator("A1B2C3D4", () => "1A2B3C4D");
        var service = CreateService(calculator.Object, clock.Object);
        var lobby = new GeneralsOnlineLobby { LobbyId = 1, ExeCrc = 0xA1B2C3D4, IniCrc = 0x1A2B3C4D };

        // Act
        var first = await service.MatchProfileAsync(CreateProfile(), lobby);
        var second = await service.MatchProfileAsync(CreateProfile(), lobby);

        // Assert
        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.Equal(GeneralsOnlineCompatibility.Compatible, first.Data!.Compatibility);
        Assert.Equal(GeneralsOnlineCompatibility.Compatible, second.Data!.Compatibility);
        calculator.Verify(
            c => c.CalculateIniCrcAsync(
                It.IsAny<string>(),
                It.IsAny<GameType>(),
                It.IsAny<IReadOnlyList<string>?>(),
                It.IsAny<string?>(),
                It.IsAny<IReadOnlyCollection<string>?>(),
                It.IsAny<IReadOnlyList<string>?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Tests that a match past the TTL recomputes CRCs instead of reusing stale ones.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task MatchProfileAsync_AfterTtlExpiry_RecomputesCrcsAsync()
    {
        // Arrange
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var clock = new Mock<TimeProvider>();
        clock.Setup(c => c.GetUtcNow()).Returns(() => now);
        var iniCrc = "1A2B3C4D";
        var calculator = CreateCalculator("A1B2C3D4", () => iniCrc);
        var service = CreateService(calculator.Object, clock.Object);
        var lobby = new GeneralsOnlineLobby { LobbyId = 1, ExeCrc = 0xA1B2C3D4, IniCrc = 0x1A2B3C4D };

        // Act: the first match populates the cache.
        var first = await service.MatchProfileAsync(CreateProfile(), lobby);

        now = now.AddMinutes(OnlineConstants.ProfileSetupCacheTtlMinutes + 1);
        iniCrc = "FFFFFFFF";
        var second = await service.MatchProfileAsync(CreateProfile(), lobby);

        // Assert
        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.Equal(GeneralsOnlineCompatibility.Compatible, first.Data!.Compatibility);
        Assert.Equal(GeneralsOnlineCompatibility.IniMismatch, second.Data!.Compatibility);
        calculator.Verify(
            c => c.CalculateIniCrcAsync(
                It.IsAny<string>(),
                It.IsAny<GameType>(),
                It.IsAny<IReadOnlyList<string>?>(),
                It.IsAny<string?>(),
                It.IsAny<IReadOnlyCollection<string>?>(),
                It.IsAny<IReadOnlyList<string>?>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    /// <summary>
    /// Tests that matching uses the engine-exact exe CRC lobbies report, not the legacy replay-oriented one.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task MatchProfileAsync_UsesEngineExeCrcAsync()
    {
        // Arrange
        var clock = new Mock<TimeProvider>();
        clock.Setup(c => c.GetUtcNow()).Returns(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var calculator = CreateCalculator("A1B2C3D4", () => "1A2B3C4D");
        var service = CreateService(calculator.Object, clock.Object);
        var lobby = new GeneralsOnlineLobby { LobbyId = 1, ExeCrc = 0xA1B2C3D4, IniCrc = 0x1A2B3C4D };

        // Act
        var result = await service.MatchProfileAsync(CreateProfile(), lobby);

        // Assert
        Assert.True(result.Success);
        calculator.Verify(
            c => c.CalculateEngineExeCrcAsync(
                Path.Combine(_tempDir, GameClientConstants.ZeroHourExecutable),
                GameType.ZeroHour,
                _tempDir,
                It.IsAny<CancellationToken>()),
            Times.Once);
        calculator.Verify(
            c => c.CalculateExeCrcAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<int?>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static Mock<IGameCrcCalculatorService> CreateCalculator(string exeCrc, Func<string> iniCrc)
    {
        var calculator = new Mock<IGameCrcCalculatorService>();
        calculator.Setup(c => c.CalculateEngineExeCrcAsync(
                It.IsAny<string>(),
                It.IsAny<GameType>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<string>.CreateSuccess(exeCrc));
        calculator.Setup(c => c.CalculateIniCrcAsync(
                It.IsAny<string>(),
                It.IsAny<GameType>(),
                It.IsAny<IReadOnlyList<string>?>(),
                It.IsAny<string?>(),
                It.IsAny<IReadOnlyCollection<string>?>(),
                It.IsAny<IReadOnlyList<string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => OperationResult<string>.CreateSuccess(iniCrc()));
        return calculator;
    }

    private static GeneralsOnlineCompatibilityService CreateService(
        IGameCrcCalculatorService calculator,
        TimeProvider clock)
    {
        var profiles = new Mock<IGameProfileManager>();
        profiles.Setup(p => p.GetAvailableContentAsync(It.IsAny<GameClient>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<ContentManifest>>.CreateSuccess(new List<ContentManifest>()));
        return new GeneralsOnlineCompatibilityService(
            profiles.Object,
            Mock.Of<ILogger<GeneralsOnlineCompatibilityService>>(),
            calculator,
            null,
            null,
            clock);
    }

    private GameProfile CreateProfile()
    {
        return new GameProfile
        {
            Id = "profile-ttl",
            Name = "TTL Profile",
            GameClient = new GameClient { GameType = GameType.ZeroHour },
            WorkingDirectory = _tempDir,
        };
    }
}
