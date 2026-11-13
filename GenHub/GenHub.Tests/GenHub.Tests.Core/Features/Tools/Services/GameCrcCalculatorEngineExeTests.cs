using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using GenHub.Core.Services.Tools.Checksum;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.Services;

/// <summary>
/// Unit tests for the engine-exact executable CRC calculation.
/// </summary>
public sealed class GameCrcCalculatorEngineExeTests : IDisposable
{
    private readonly string _tempDir;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameCrcCalculatorEngineExeTests"/> class.
    /// </summary>
    public GameCrcCalculatorEngineExeTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "GenHubEngineExeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
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
    /// Tests that a Zero Hour executable produces the hand-computed engine value.
    /// Bytes 0x01 0x02 accumulate to 4; the packed version 0x00010004 feeds bytes
    /// 04 00 01 00, stepping 4 to 12 to 24 to 49 to 98 (0x62).
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task CalculateEngineExeCrcAsync_WithZeroHourExe_MatchesHandComputedVectorAsync()
    {
        // Arrange
        var service = new GameCrcCalculatorService();
        var exePath = WriteExe("zerohour.dat", [0x01, 0x02]);

        // Act
        var result = await service.CalculateEngineExeCrcAsync(exePath, GameType.ZeroHour, _tempDir);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("0x00000062", result.Data);
    }

    /// <summary>
    /// Tests that a Generals executable produces the hand-computed engine value.
    /// Byte 0x00 leaves the accumulator at 0; the packed version 0x00010008 feeds
    /// bytes 08 00 01 00, stepping 0 to 8 to 16 to 33 to 66 (0x42).
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task CalculateEngineExeCrcAsync_WithGeneralsExe_MatchesHandComputedVectorAsync()
    {
        // Arrange
        var service = new GameCrcCalculatorService();
        var exePath = WriteExe("generals.dat", [0x00]);

        // Act
        var result = await service.CalculateEngineExeCrcAsync(exePath, GameType.Generals, _tempDir);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("0x00000042", result.Data);
    }

    /// <summary>
    /// Tests that multiplayer script files under the scripts root feed the checksum.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task CalculateEngineExeCrcAsync_WithMultiplayerScripts_PullsScriptBytesIntoChecksumAsync()
    {
        // Arrange
        var service = new GameCrcCalculatorService();
        var exePath = WriteExe("scripted.dat", [0x01, 0x02]);
        var plain = await service.CalculateEngineExeCrcAsync(exePath, GameType.ZeroHour, _tempDir);
        var scriptPath = Path.Combine(_tempDir, SageChecksumConstants.MultiplayerScriptsRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(scriptPath)!);
        await File.WriteAllBytesAsync(scriptPath, [0x07]);

        // Act
        var withScripts = await service.CalculateEngineExeCrcAsync(exePath, GameType.ZeroHour, _tempDir);

        // Assert
        Assert.True(plain.Success);
        Assert.True(withScripts.Success);
        Assert.NotEqual(plain.Data, withScripts.Data);
    }

    /// <summary>
    /// Tests that skirmish script files under the scripts root feed the checksum.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task CalculateEngineExeCrcAsync_WithSkirmishScripts_PullsScriptBytesIntoChecksumAsync()
    {
        // Arrange
        var service = new GameCrcCalculatorService();
        var exePath = WriteExe("skirmish.dat", [0x01, 0x02]);
        var plain = await service.CalculateEngineExeCrcAsync(exePath, GameType.ZeroHour, _tempDir);
        var skirmishScriptPath = Path.Combine(_tempDir, SageChecksumConstants.SkirmishScriptsRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(skirmishScriptPath)!);
        await File.WriteAllBytesAsync(skirmishScriptPath, [0x07]);

        // Act
        var withSkirmish = await service.CalculateEngineExeCrcAsync(exePath, GameType.ZeroHour, _tempDir);

        // Assert
        Assert.True(plain.Success);
        Assert.True(withSkirmish.Success);
        Assert.NotEqual(plain.Data, withSkirmish.Data);
    }

    /// <summary>
    /// Tests that repeated calculations are deterministic.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task CalculateEngineExeCrcAsync_RepeatedCalls_ReturnSameValueAsync()
    {
        // Arrange
        var service = new GameCrcCalculatorService();
        var exePath = WriteExe("repeat.dat", [0x01, 0x02]);

        // Act
        var first = await service.CalculateEngineExeCrcAsync(exePath, GameType.ZeroHour, _tempDir);
        var second = await service.CalculateEngineExeCrcAsync(exePath, GameType.ZeroHour, _tempDir);

        // Assert
        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.Equal(first.Data, second.Data);
    }

    /// <summary>
    /// Tests that the engine value deliberately differs from the legacy replay-oriented value.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task CalculateEngineExeCrcAsync_ComparedToLegacy_DiffersAsync()
    {
        // Arrange
        var service = new GameCrcCalculatorService();
        var exePath = WriteExe("diverge.dat", [0x01, 0x02]);

        // Act
        var engine = await service.CalculateEngineExeCrcAsync(exePath, GameType.ZeroHour, _tempDir);
        var legacy = await service.CalculateExeCrcAsync(exePath, _tempDir, 1, 4);

        // Assert
        Assert.True(engine.Success);
        Assert.True(legacy.Success);
        Assert.NotEqual(legacy.Data, engine.Data);
    }

    /// <summary>
    /// Tests that a missing executable returns a failure.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task CalculateEngineExeCrcAsync_MissingFile_ReturnsFailureAsync()
    {
        // Arrange
        var service = new GameCrcCalculatorService();

        // Act
        var result = await service.CalculateEngineExeCrcAsync(
            Path.Combine(_tempDir, "missing.dat"),
            GameType.ZeroHour,
            _tempDir);

        // Assert
        Assert.False(result.Success);
    }

    /// <summary>
    /// Tests that cancellation propagates instead of returning a value.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task CalculateEngineExeCrcAsync_WhenCancelled_ThrowsOperationCanceledExceptionAsync()
    {
        // Arrange
        var service = new GameCrcCalculatorService();
        var exePath = WriteExe("cancelled.dat", [0x01, 0x02]);
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        // Act and assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.CalculateEngineExeCrcAsync(exePath, GameType.ZeroHour, _tempDir, source.Token));
    }

    private string WriteExe(string fileName, byte[] bytes)
    {
        var path = Path.Combine(_tempDir, fileName);
        File.WriteAllBytes(path, bytes);
        return path;
    }
}
