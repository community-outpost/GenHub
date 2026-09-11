using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.Tools.GenHotkeys;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.GenHotkeys;
using GenHub.Features.Tools.GenHotkeys.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Tools.GenHotkeys;

/// <summary>
/// Unit tests for <see cref="HotkeyPackageService"/>.
/// </summary>
public class HotkeyPackageServiceTests
{
    private readonly Mock<ITechTreeService> _mockTechTree;
    private readonly Mock<IIconOverlayService> _mockOverlay;
    private readonly Mock<ILocalContentService> _mockLocalContent;
    private readonly Mock<IServiceScopeFactory> _mockScopeFactory;
    private readonly Mock<ILogger<HotkeyPackageService>> _mockLogger;
    private readonly HotkeyPackageService _service;

    /// <summary>
    /// Initializes a new instance of the <see cref="HotkeyPackageServiceTests"/> class.
    /// </summary>
    public HotkeyPackageServiceTests()
    {
        _mockTechTree = new Mock<ITechTreeService>();
        _mockOverlay = new Mock<IIconOverlayService>();
        _mockLocalContent = new Mock<ILocalContentService>();
        _mockLogger = new Mock<ILogger<HotkeyPackageService>>();

        var mockScope = new Mock<IServiceScope>();
        var mockServiceProvider = new Mock<IServiceProvider>();
        mockServiceProvider.Setup(sp => sp.GetService(typeof(ILocalContentService)))
            .Returns(_mockLocalContent.Object);
        mockScope.Setup(s => s.ServiceProvider).Returns(mockServiceProvider.Object);

        _mockScopeFactory = new Mock<IServiceScopeFactory>();
        _mockScopeFactory.Setup(f => f.CreateScope()).Returns(mockScope.Object);

        _service = new HotkeyPackageService(
            _mockTechTree.Object,
            _mockOverlay.Object,
            _mockScopeFactory.Object,
            _mockLogger.Object);
    }

    /// <summary>
    /// Verifies that CreateHotkeysAddonAsync throws on null profile.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CreateHotkeysAddonAsync_WithNullProfile_ThrowsArgumentNullExceptionAsync()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _service.CreateHotkeysAddonAsync(null!));
    }

    /// <summary>
    /// Verifies that CreateHotkeysAddonAsync returns success when manifest creation succeeds.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CreateHotkeysAddonAsync_WhenSuccessful_ReturnsManifestAsync()
    {
        var profile = new HotkeyProfile
        {
            Name = "Test Profile",
            TargetGame = GameType.ZeroHour,
            OverlayEnabled = false,
        };

        var expectedManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.local.addon.hotkeys-test"),
            Name = "Custom Hotkeys: Test Profile",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
        };

        _mockLocalContent.Setup(l => l.CreateLocalContentManifestAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                ContentType.Addon,
                GameType.ZeroHour,
                It.IsAny<string?>(),
                It.IsAny<IProgress<ContentStorageProgress>?>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>()))
            .ReturnsAsync(OperationResult<ContentManifest>.CreateSuccess(expectedManifest));

        var result = await _service.CreateHotkeysAddonAsync(profile);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal("Custom Hotkeys: Test Profile", result.Data.Name);
    }

    /// <summary>
    /// Verifies that CreateHotkeysAddonAsync returns failure when manifest creation fails.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CreateHotkeysAddonAsync_WhenManifestCreationFails_ReturnsFailureAsync()
    {
        var profile = new HotkeyProfile
        {
            Name = "Failed Profile",
            TargetGame = GameType.ZeroHour,
            OverlayEnabled = false,
        };

        _mockLocalContent.Setup(l => l.CreateLocalContentManifestAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                ContentType.Addon,
                GameType.ZeroHour,
                It.IsAny<string?>(),
                It.IsAny<IProgress<ContentStorageProgress>?>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>()))
            .ReturnsAsync(OperationResult<ContentManifest>.CreateFailure("Storage full"));

        var result = await _service.CreateHotkeysAddonAsync(profile);

        Assert.False(result.Success);
        Assert.Contains("Storage full", string.Join(", ", result.Errors));
    }

    /// <summary>
    /// Verifies that actions in ClearedKeys skip icon overlay rendering.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CreateHotkeysAddonAsync_WithClearedKeys_SkipsOverlayForClearedActionsAsync()
    {
        var profile = new HotkeyProfile
        {
            Name = "Cleared Keys Profile",
            TargetGame = GameType.ZeroHour,
            OverlayEnabled = true,
        };
        profile.ClearedKeys.Add("CONTROLBAR:ConstructAmericaDozer");

        var factions = new List<HotkeyFaction>
        {
            new()
            {
                ShortName = "USA",
                DisplayName = "America",
                GameObjects =
                {
                    new HotkeyGameObject
                    {
                        Name = "CommandCenter",
                        KeyboardLayouts =
                        {
                            new List<HotkeyAction>
                            {
                                new()
                                {
                                    IconName = "SADozer",
                                    HotkeyString = "CONTROLBAR:ConstructAmericaDozer",
                                    DefaultHotkey = 'D',
                                },
                            },
                        },
                    },
                },
            },
        };

        _mockTechTree.Setup(t => t.LoadTechTreeAsync(GameType.ZeroHour, It.IsAny<CancellationToken>()))
            .ReturnsAsync(factions);

        var manifestCreated = false;
        _mockLocalContent.Setup(l => l.CreateLocalContentManifestAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                ContentType.Addon,
                GameType.ZeroHour,
                It.IsAny<string?>(),
                It.IsAny<IProgress<ContentStorageProgress>?>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>()))
            .Callback<string, string, ContentType, GameType, string?, IProgress<ContentStorageProgress>?, CancellationToken, string?>((stagingDir, _, _, _, _, _, _, _) =>
                {
                    // Verify that no TGA was generated for the cleared action
                    var tgaPath = Path.Combine(stagingDir, "Art", "Textures", "SADozer.tga");
                    Assert.False(File.Exists(tgaPath), "Overlay TGA should not be generated for cleared hotkeys.");
                    manifestCreated = true;
                })
            .ReturnsAsync(OperationResult<ContentManifest>.CreateSuccess(new ContentManifest
            {
                Id = ManifestId.Create("1.0.local.addon.hotkeys-cleared"),
                Name = "Custom Hotkeys: Cleared",
                ContentType = ContentType.Addon,
                TargetGame = GameType.ZeroHour,
            }));

        var result = await _service.CreateHotkeysAddonAsync(profile);

        Assert.True(result.Success);
        Assert.True(manifestCreated);

        // Verify that GenerateOverlayTgaAsync was NEVER called for the cleared key
        _mockOverlay.Verify(
            o => o.GenerateOverlayTgaAsync(
                It.IsAny<byte[]>(),
                It.IsAny<char>(),
                It.IsAny<OverlayCorner>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
