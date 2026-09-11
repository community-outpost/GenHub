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
    private readonly string _tempDir;
    private readonly HotkeyProfileStorageService _service;

    /// <summary>
    /// Initializes a new instance of the <see cref="HotkeyProfileStorageServiceTests"/> class.
    /// </summary>
    public HotkeyProfileStorageServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "GenHubTests_Hotkeys_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        var mockConfig = new Mock<IAppConfiguration>();
        mockConfig.Setup(c => c.GetConfiguredDataPath()).Returns(_tempDir);

        _service = new HotkeyProfileStorageService(
            mockConfig.Object,
            NullLogger<HotkeyProfileStorageService>.Instance);
    }

    /// <summary>
    /// Cleans up temporary test directory.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Verifies that GetProfilesAsync initializes a default profile if none exists.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GetProfilesAsync_WhenEmpty_CreatesAndReturnsDefaultProfileAsync()
    {
        var profiles = await _service.GetProfilesAsync(GameType.ZeroHour);

        Assert.NotEmpty(profiles);
        var first = profiles[0];
        Assert.Equal(GameType.ZeroHour, first.TargetGame);
        Assert.Contains("Default", first.Name);
    }

    /// <summary>
    /// Verifies that SaveProfileAsync persists a profile and GetProfilesAsync retrieves it.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task SaveProfileAsync_PersistsProfile_CanBeRetrievedAsync()
    {
        var profile = new HotkeyProfile
        {
            Name = "My Custom Profile",
            TargetGame = GameType.ZeroHour,
            OverlayEnabled = true,
            OverlayCorner = OverlayCorner.TopRight,
        };
        profile.KeyMappings["CONTROLBAR:ConstructAmericaDozer"] = 'D';
        profile.ClearedKeys.Add("CONTROLBAR:LeafletDrop");

        await _service.SaveProfileAsync(profile);

        var retrieved = await _service.GetProfilesAsync(GameType.ZeroHour);

        Assert.NotEmpty(retrieved);
        var found = Assert.Single(retrieved, p => p.Id == profile.Id);
        Assert.Equal("My Custom Profile", found.Name);
        Assert.Equal(OverlayCorner.TopRight, found.OverlayCorner);
        Assert.True(found.KeyMappings.ContainsKey("CONTROLBAR:ConstructAmericaDozer"));
        Assert.Equal('D', found.KeyMappings["CONTROLBAR:ConstructAmericaDozer"]);
        Assert.Contains("CONTROLBAR:LeafletDrop", found.ClearedKeys);
    }

    /// <summary>
    /// Verifies that SaveProfileAsync rejects path traversal in profile Id.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task SaveProfileAsync_WithTraversalId_ThrowsArgumentExceptionAsync()
    {
        var maliciousProfile = new HotkeyProfile
        {
            Id = "../evil",
            Name = "Malicious",
            TargetGame = GameType.ZeroHour,
        };

        await Assert.ThrowsAsync<ArgumentException>(() => _service.SaveProfileAsync(maliciousProfile));
    }

    /// <summary>
    /// Verifies that DeleteProfileAsync removes a persisted profile file.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task DeleteProfileAsync_RemovesProfileFileAsync()
    {
        var profile = new HotkeyProfile
        {
            Name = "To Delete",
            TargetGame = GameType.ZeroHour,
        };

        await _service.SaveProfileAsync(profile);
        var before = await _service.GetProfilesAsync(GameType.ZeroHour);
        Assert.Contains(before, p => p.Id == profile.Id);

        await _service.DeleteProfileAsync(profile.Id);
        var after = await _service.GetProfilesAsync(GameType.ZeroHour);
        Assert.DoesNotContain(after, p => p.Id == profile.Id);
    }

    /// <summary>
    /// Verifies that DeleteProfileAsync rejects path traversal in profile Id.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task DeleteProfileAsync_WithTraversalId_ThrowsArgumentExceptionAsync()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.DeleteProfileAsync("../../evil"));
    }

    /// <summary>
    /// Verifies that LoadPresetAsync loads a preset profile when requested.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task LoadPresetAsync_LoadsKnownPresetAsync()
    {
        var preset = await _service.LoadPresetAsync("Vanilla", GameType.ZeroHour);

        Assert.NotNull(preset);
        Assert.Contains("Vanilla", preset.Name);
    }

    /// <summary>
    /// Cleans up managed resources.
    /// </summary>
    /// <param name="disposing">Whether disposing.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing && Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, true);
            }
            catch
            {
                // Best effort cleanup
            }
        }
    }
}
