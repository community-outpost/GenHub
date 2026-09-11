using System;
using System.IO;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Tools.GenHotkeys;
using GenHub.Features.Tools.GenHotkeys.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.GenHotkeys;

/// <summary>
/// Unit tests for <see cref="HotkeyProfileStorageService"/>.
/// </summary>
public class HotkeyProfileStorageServiceTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly Mock<IAppConfiguration> _mockAppConfig;
    private readonly HotkeyProfileStorageService _service;

    /// <summary>
    /// Initializes a new instance of the <see cref="HotkeyProfileStorageServiceTests"/> class.
    /// </summary>
    public HotkeyProfileStorageServiceTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"GenHub_Test_Hotkeys_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);

        _mockAppConfig = new Mock<IAppConfiguration>();
        _mockAppConfig.Setup(c => c.GetConfiguredDataPath()).Returns(_tempDirectory);

        _service = new HotkeyProfileStorageService(
            _mockAppConfig.Object,
            NullLogger<HotkeyProfileStorageService>.Instance);
    }

    /// <summary>
    /// Cleans up temporary test directories.
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            try
            {
                Directory.Delete(_tempDirectory, true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    /// <summary>
    /// Verifies that GetProfilesAsync initializes a default profile if none exists.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task GetProfilesAsync_WhenEmpty_CreatesAndReturnsDefaultProfile()
    {
        var profiles = await _service.GetProfilesAsync(GameType.ZeroHour);

        Assert.NotEmpty(profiles);
        var first = profiles[0];
        Assert.Equal(GameType.ZeroHour, first.TargetGame);
        Assert.Contains("Default", first.Name);
    }

    /// <summary>
    /// Verifies that SaveProfileAsync writes a JSON file that can be reloaded.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task SaveProfileAsync_PersistsProfileToJsonFile()
    {
        var profile = new HotkeyProfile
        {
            Name = "My Test Profile",
            TargetGame = GameType.ZeroHour,
            OverlayEnabled = true,
            OverlayCorner = OverlayCorner.BottomRight,
        };
        profile.KeyMappings["CONTROLBAR:ConstructAmericaDozer"] = 'D';

        var saved = await _service.SaveProfileAsync(profile);
        Assert.NotNull(saved);

        var loadedList = await _service.GetProfilesAsync(GameType.ZeroHour);
        var found = Assert.Single(loadedList, p => p.Id == profile.Id);

        Assert.Equal("My Test Profile", found.Name);
        Assert.True(found.OverlayEnabled);
        Assert.Equal(OverlayCorner.BottomRight, found.OverlayCorner);
        Assert.True(found.KeyMappings.TryGetValue("CONTROLBAR:ConstructAmericaDozer", out var key));
        Assert.Equal('D', key);
    }

    /// <summary>
    /// Verifies that DeleteProfileAsync removes the profile JSON file from disk.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task DeleteProfileAsync_RemovesProfileFile()
    {
        var profile = new HotkeyProfile
        {
            Name = "To Delete",
            TargetGame = GameType.Generals,
        };

        await _service.SaveProfileAsync(profile);
        var deleted = await _service.DeleteProfileAsync(profile.Id);
        Assert.True(deleted);

        var profiles = await _service.GetProfilesAsync(GameType.Generals);
        Assert.DoesNotContain(profiles, p => p.Id == profile.Id);
    }
}
