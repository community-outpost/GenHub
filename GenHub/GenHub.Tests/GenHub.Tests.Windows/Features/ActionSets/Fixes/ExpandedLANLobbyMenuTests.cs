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
/// Unit tests for <see cref="ExpandedLANLobbyMenu"/>.
/// </summary>
public class ExpandedLANLobbyMenuTests : IDisposable
{
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock = new();
    private readonly Mock<ILogger<ExpandedLANLobbyMenu>> _loggerMock = new();
    private readonly string _testDir;
    private readonly ExpandedLANLobbyMenu _fix;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExpandedLANLobbyMenuTests"/> class.
    /// </summary>
    public ExpandedLANLobbyMenuTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"ExpandedLANLobbyMenuTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        var markerPath = Path.Combine(_testDir, "ExpandedLANLobbyMenu.done");
        _fix = new ExpandedLANLobbyMenu(_httpClientFactoryMock.Object, _loggerMock.Object, markerPath);
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
    /// Verifies that UndoAsync does not delete unrecorded custom window files when no marker exists.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous test.</returns>
    [Fact]
    public async Task UndoAsync_WhenNoMarkerExists_DoesNotDeleteFilesAsync()
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

        Assert.True(result.Success);
        Assert.True(File.Exists(bigFile));
    }
}
