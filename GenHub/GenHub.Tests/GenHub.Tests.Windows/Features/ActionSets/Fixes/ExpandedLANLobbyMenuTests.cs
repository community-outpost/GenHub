namespace GenHub.Tests.Windows.Features.ActionSets.Fixes;

using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using GenHub.Windows.Features.ActionSets.Fixes;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

/// <summary>
/// Unit tests for <see cref="ExpandedLanLobbyMenu"/>.
/// </summary>
public class ExpandedLANLobbyMenuTests : IDisposable
{
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock = new();
    private readonly Mock<ILogger<ExpandedLanLobbyMenu>> _loggerMock = new();
    private readonly string _testDir;
    private readonly ExpandedLanLobbyMenu _fix;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExpandedLANLobbyMenuTests"/> class.
    /// </summary>
    public ExpandedLANLobbyMenuTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"ExpandedLANLobbyMenuTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        var markerPath = Path.Combine(_testDir, "ExpandedLANLobbyMenu.done");
        _fix = new ExpandedLanLobbyMenu(_httpClientFactoryMock.Object, _loggerMock.Object, markerPath);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup failures
        }
    }

    /// <summary>
    /// Verifies properties return expected defaults.
    /// </summary>
    [Fact]
    public void Properties_ReturnExpectedDefaults()
    {
        Assert.Equal("ExpandedLANLobbyMenu", _fix.Id);
        Assert.Equal("Expanded LAN Lobby Menu (Addon)", _fix.Title);
        Assert.Equal(ActionSetConstants.Categories.QualityOfLife, _fix.Category);
        Assert.False(_fix.IsCoreFix);
        Assert.False(_fix.IsCrucialFix);
    }

    /// <summary>
    /// Verifies that IsApplicableAsync returns true when either game component is present.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test.</returns>
    [Fact]
    public async Task IsApplicableAsync_WhenGeneralsOrZeroHourPresent_ReturnsTrueAsync()
    {
        var installation = new GameInstallation(_testDir, GameInstallationType.Steam)
        {
            HasZeroHour = true,
            ZeroHourPath = _testDir,
        };

        var result = await _fix.IsApplicableAsync(installation);

        Assert.True(result);
    }

    /// <summary>
    /// Verifies that IsAppliedAsync returns false when no marker or custom window files exist.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test.</returns>
    [Fact]
    public async Task IsAppliedAsync_WhenNoFilesPresent_ReturnsFalseAsync()
    {
        var zhDir = Path.Combine(_testDir, "ZeroHour");
        Directory.CreateDirectory(zhDir);

        var installation = new GameInstallation(_testDir, GameInstallationType.Steam)
        {
            HasZeroHour = true,
            ZeroHourPath = zhDir,
        };

        var result = await _fix.IsAppliedAsync(installation);

        Assert.False(result);
    }

    /// <summary>
    /// Verifies that IsAppliedAsync returns true when a custom BIG file exists in the installation.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test.</returns>
    [Fact]
    public async Task IsAppliedAsync_WhenCustomBigExists_ReturnsTrueAsync()
    {
        var zhDir = Path.Combine(_testDir, "ZeroHour");
        Directory.CreateDirectory(zhDir);
        File.WriteAllText(Path.Combine(zhDir, "!ExpandedLANMenu.big"), "content");

        var installation = new GameInstallation(_testDir, GameInstallationType.Steam)
        {
            HasZeroHour = true,
            ZeroHourPath = zhDir,
        };

        var result = await _fix.IsAppliedAsync(installation);

        Assert.True(result);
    }

    /// <summary>
    /// Verifies that UndoAsync removes recorded custom window files and marker when marker exists.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test.</returns>
    [Fact]
    public async Task UndoAsync_WhenMarkerExists_RemovesRecordedFilesAndMarkerAsync()
    {
        var zhDir = Path.Combine(_testDir, "ZeroHour");
        Directory.CreateDirectory(zhDir);
        var bigFile = Path.Combine(zhDir, "!ExpandedLANMenu.big");
        File.WriteAllText(bigFile, "content");

        var markerPath = Path.Combine(_testDir, "ExpandedLANLobbyMenu.done");
        File.WriteAllLines(markerPath, [bigFile]);

        var installation = new GameInstallation(_testDir, GameInstallationType.Steam)
        {
            HasZeroHour = true,
            ZeroHourPath = zhDir,
        };

        var result = await _fix.UndoAsync(installation);

        Assert.True(result.Success);
        Assert.False(File.Exists(bigFile));
        Assert.False(File.Exists(markerPath));
    }

    /// <summary>
    /// Verifies that UndoAsync succeeds when no marker exists and no files are present.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test.</returns>
    [Fact]
    public async Task UndoAsync_WhenNoMarkerExistsAndNoFilesPresent_SucceedsAsync()
    {
        var zhDir = Path.Combine(_testDir, "ZeroHour");
        Directory.CreateDirectory(zhDir);

        var installation = new GameInstallation(_testDir, GameInstallationType.Steam)
        {
            HasZeroHour = true,
            ZeroHourPath = zhDir,
        };

        var result = await _fix.UndoAsync(installation);

        Assert.True(result.Success);
    }

    /// <summary>
    /// Verifies that UndoAsync deletes only recorded files when an unrecorded known file is also present.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test.</returns>
    [Fact]
    public async Task UndoAsync_WhenMarkerExistsAndUnrecordedFilePresent_LeavesUnrecordedFileIntactAsync()
    {
        var zhDir = Path.Combine(_testDir, "ZeroHour");
        Directory.CreateDirectory(zhDir);
        var recordedFile = Path.Combine(zhDir, "!ExpandedLANMenu.big");
        var unrecordedFile = Path.Combine(zhDir, "CustomWindows.big");
        File.WriteAllText(recordedFile, "content1");
        File.WriteAllText(unrecordedFile, "content2");

        var markerPath = Path.Combine(_testDir, "ExpandedLANLobbyMenu.done");
        File.WriteAllLines(markerPath, [recordedFile]);

        var installation = new GameInstallation(_testDir, GameInstallationType.Steam)
        {
            HasZeroHour = true,
            ZeroHourPath = zhDir,
        };

        var result = await _fix.UndoAsync(installation);

        Assert.True(result.Success);
        Assert.False(File.Exists(recordedFile));
        Assert.True(File.Exists(unrecordedFile));
        Assert.False(File.Exists(markerPath));
    }

    /// <summary>
    /// Verifies that UndoAsync returns a warning failure when files are present on disk but no marker exists.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test.</returns>
    [Fact]
    public async Task UndoAsync_WhenNoMarkerExistsAndFilesPresent_ReturnsWarningFailureAsync()
    {
        var zhDir = Path.Combine(_testDir, "ZeroHour");
        Directory.CreateDirectory(zhDir);
        var bigFile = Path.Combine(zhDir, "!ExpandedLANMenu.big");
        File.WriteAllText(bigFile, "content");

        var installation = new GameInstallation(_testDir, GameInstallationType.Steam)
        {
            HasZeroHour = true,
            ZeroHourPath = zhDir,
        };

        var result = await _fix.UndoAsync(installation);

        Assert.False(result.Success);
        Assert.True(File.Exists(bigFile));
        Assert.Contains("No deployment marker found", result.ErrorMessage ?? string.Empty);
    }

    /// <summary>
    /// Verifies that UndoAsync migrates legacy timestamp markers and removes recognized custom window files.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test.</returns>
    [Fact]
    public async Task UndoAsync_WhenLegacyTimestampMarkerExists_MigratesAndRemovesRecognizedFilesAsync()
    {
        var zhDir = Path.Combine(_testDir, "ZeroHour");
        Directory.CreateDirectory(zhDir);
        var bigFile = Path.Combine(zhDir, "!ExpandedLANMenu.big");
        File.WriteAllText(bigFile, "content");

        var markerPath = Path.Combine(_testDir, "ExpandedLANLobbyMenu.done");
        File.WriteAllText(markerPath, "2024-01-01T00:00:00Z");

        var installation = new GameInstallation(_testDir, GameInstallationType.Steam)
        {
            HasZeroHour = true,
            ZeroHourPath = zhDir,
        };

        var result = await _fix.UndoAsync(installation);

        Assert.True(result.Success);
        Assert.False(File.Exists(bigFile));
        Assert.False(File.Exists(markerPath));
    }
}
