using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
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
using GenHub.Core.Services.Tools.GenHotkeys;
using GenHub.Features.Tools.GenHotkeys.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;
using RegexMatch = System.Text.RegularExpressions.Match;

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

    /// <summary>
    /// Verifies that key mappings for labels missing from the base CSF do not inject malformed badges or corrupt the CSF string table.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CreateHotkeysAddonAsync_WithUnknownCsfLabel_DoesNotCorruptCsfAsync()
    {
        var profile = new HotkeyProfile
        {
            Name = "Unknown Label Profile",
            TargetGame = GameType.ZeroHour,
            OverlayEnabled = false,
        };
        profile.KeyMappings["UNKNOWN:NonExistentLabel"] = 'X';

        string? bigFileText = null;
        _mockLocalContent.Setup(l => l.CreateLocalContentManifestAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                ContentType.Addon,
                GameType.ZeroHour,
                It.IsAny<string?>(),
                It.IsAny<IProgress<ContentStorageProgress>?>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>()))
            .Callback<string, string, ContentType, GameType, string?, IProgress<ContentStorageProgress>?, CancellationToken, string?>((packageDir, _, _, _, _, _, _, _) =>
            {
                var bigFiles = Directory.GetFiles(packageDir, "*.big");
                if (bigFiles.Length > 0)
                {
                    bigFileText = File.ReadAllText(bigFiles[0]);
                }
            })
            .ReturnsAsync(OperationResult<ContentManifest>.CreateSuccess(new ContentManifest
            {
                Id = ManifestId.Create("1.0.local.addon.hotkeys-unknown"),
                Name = "Custom Hotkeys: Unknown",
                ContentType = ContentType.Addon,
                TargetGame = GameType.ZeroHour,
            }));

        var result = await _service.CreateHotkeysAddonAsync(profile);

        Assert.True(result.Success);
        Assert.NotNull(bigFileText);
        Assert.DoesNotContain("UNKNOWN:NonExistentLabel", bigFileText);
    }

    /// <summary>
    /// Verifies that when overlays are enabled, the generated .big archive includes
    /// retail SAGE ButtonImage aliases (e.g. SAPowerPlant, SASupplyCntr) and both MappedImages INI files.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CreateHotkeysAddonAsync_WithOverlayEnabled_EmitsRetailButtonImageAliasesAsync()
    {
        var profile = new HotkeyProfile
        {
            Name = "Retail Aliases Profile",
            TargetGame = GameType.ZeroHour,
            OverlayEnabled = true,
        };

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
                        Name = "Dozer",
                        KeyboardLayouts =
                        {
                            new List<HotkeyAction>
                            {
                                new()
                                {
                                    IconName = "USAColdFusionReactor",
                                    HotkeyString = "CONTROLBAR:ConstructAmericaPowerPlant",
                                    DefaultHotkey = 'P',
                                },
                                new()
                                {
                                    IconName = "USASupplyCenter",
                                    HotkeyString = "CONTROLBAR:ConstructAmericaSupplyCenter",
                                    DefaultHotkey = 'S',
                                },
                            },
                        },
                    },
                },
            },
        };

        _mockTechTree.Setup(t => t.LoadTechTreeAsync(GameType.ZeroHour, It.IsAny<CancellationToken>()))
            .ReturnsAsync(factions);

        using var testImg = new Image<Rgba32>(60, 48);
        using var ms = new MemoryStream();
        await testImg.SaveAsPngAsync(ms);
        var iconBytes = ms.ToArray();

        _mockTechTree.Setup(t => t.GetIconBytesAsync(It.IsAny<string>(), GameType.ZeroHour, It.IsAny<CancellationToken>()))
            .ReturnsAsync(iconBytes);

        _mockOverlay.Setup(o => o.GenerateOverlayTgaAsync(
                It.IsAny<byte[]>(),
                It.IsAny<char>(),
                It.IsAny<OverlayCorner>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new byte[32]);

        string? bigFileText = null;

        _mockLocalContent.Setup(l => l.CreateLocalContentManifestAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                ContentType.Addon,
                GameType.ZeroHour,
                It.IsAny<string?>(),
                It.IsAny<IProgress<ContentStorageProgress>?>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>()))
            .Callback<string, string, ContentType, GameType, string?, IProgress<ContentStorageProgress>?, CancellationToken, string?>((packageDir, _, _, _, _, _, _, _) =>
            {
                var bigFiles = Directory.GetFiles(packageDir, "*.big");
                if (bigFiles.Length > 0)
                {
                    bigFileText = File.ReadAllText(bigFiles[0]);
                }
            })
            .ReturnsAsync(OperationResult<ContentManifest>.CreateSuccess(new ContentManifest
            {
                Id = ManifestId.Create("1.0.local.addon.hotkeys-retail"),
                Name = "Custom Hotkeys: Retail",
                ContentType = ContentType.Addon,
                TargetGame = GameType.ZeroHour,
            }));

        var result = await _service.CreateHotkeysAddonAsync(profile);

        Assert.True(result.Success);
        Assert.NotNull(bigFileText);

        // Verify retail SAGE ButtonImage names are present in the packed archive
        Assert.Contains("MappedImage SAPowerPlant", bigFileText);
        Assert.Contains("MappedImage SASupplyCntr", bigFileText);
        Assert.Contains("MappedImage USAColdFusionReactor", bigFileText);
        Assert.Contains(@"Data\INI\MappedImages\HandCreated\Hotkeys.ini", bigFileText);
        Assert.Contains(@"Data\INI\MappedImages\TextureSize_512\zzHotkeys.ini", bigFileText);
    }

    /// <summary>
    /// Verifies that custom hotkeys on special powers (e.g. Spy Drone) are synchronized
    /// to both the Command Center label (CONTROLBAR:SpyDrone) and the sidebar shortcut label (OBJECT:SpyDrone).
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task CreateHotkeysAddonAsync_WithSpecialPowerHotkeys_SynchronizesSidebarShortcutAliasesAsync()
    {
        var profile = new HotkeyProfile
        {
            Name = "Sidebar Shortcuts Profile",
            TargetGame = GameType.ZeroHour,
            OverlayEnabled = false,
        };

        // Explicitly map Spy Drone to 'Y'
        profile.KeyMappings["CONTROLBAR:SpyDrone"] = 'Y';

        // Explicitly clear Cash Hack
        profile.ClearedKeys.Add("CONTROLBAR:CashHack");

        byte[]? csfBytes = null;

        _mockLocalContent.Setup(l => l.CreateLocalContentManifestAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                ContentType.Addon,
                GameType.ZeroHour,
                It.IsAny<string?>(),
                It.IsAny<IProgress<ContentStorageProgress>?>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>()))
            .Callback<string, string, ContentType, GameType, string?, IProgress<ContentStorageProgress>?, CancellationToken, string?>((packageDir, _, _, _, _, _, _, _) =>
            {
                var bigFiles = Directory.GetFiles(packageDir, "*.big");
                if (bigFiles.Length > 0)
                {
                    using var fs = File.OpenRead(bigFiles[0]);
                    using var br = new BinaryReader(fs);
                    br.ReadBytes(8); // Signature + TotalSize
                    var numFiles = (br.ReadByte() << 24) | (br.ReadByte() << 16) | (br.ReadByte() << 8) | br.ReadByte();
                    br.ReadBytes(4); // HeaderSize

                    for (int i = 0; i < numFiles; i++)
                    {
                        var offset = (br.ReadByte() << 24) | (br.ReadByte() << 16) | (br.ReadByte() << 8) | br.ReadByte();
                        var size = (br.ReadByte() << 24) | (br.ReadByte() << 16) | (br.ReadByte() << 8) | br.ReadByte();
                        var nameBytes = new List<byte>();
                        byte b;
                        while ((b = br.ReadByte()) != 0)
                        {
                            nameBytes.Add(b);
                        }

                        var name = System.Text.Encoding.ASCII.GetString(nameBytes.ToArray());
                        if (name.EndsWith("generals.csf", StringComparison.OrdinalIgnoreCase))
                        {
                            var pos = fs.Position;
                            fs.Seek(offset, SeekOrigin.Begin);
                            csfBytes = br.ReadBytes(size);
                            fs.Seek(pos, SeekOrigin.Begin);
                            break;
                        }
                    }
                }
            })
            .ReturnsAsync(OperationResult<ContentManifest>.CreateSuccess(new ContentManifest
            {
                Id = ManifestId.Create("1.0.local.addon.hotkeys-sidebar"),
                Name = "Custom Hotkeys: Sidebar",
                ContentType = ContentType.Addon,
                TargetGame = GameType.ZeroHour,
            }));

        var result = await _service.CreateHotkeysAddonAsync(profile);

        Assert.True(result.Success);
        Assert.NotNull(csfBytes);

        using var ms = new MemoryStream(csfBytes);
        var csf = CsfFile.Load(ms);

        // 1. Verify explicitly customized hotkey propagated to both primary and sidebar alias
        var primarySpyDrone = csf.GetString("CONTROLBAR:SpyDrone");
        var aliasSpyDrone = csf.GetString("OBJECT:SpyDrone");
        Assert.NotNull(primarySpyDrone);
        Assert.NotNull(aliasSpyDrone);
        Assert.Equal('Y', CsfFile.ExtractHotkey(primarySpyDrone));
        Assert.Equal('Y', CsfFile.ExtractHotkey(aliasSpyDrone));

        // 2. Verify base preset hotkey (A10 Thunderbolt) synchronized to sidebar shortcut label
        var primaryA10 = csf.GetString("CONTROLBAR:A10ThunderboltMissileStrike");
        var aliasA10 = csf.GetString("GUI:SuperweaponA10ThunderboltMissileStrike");
        Assert.NotNull(primaryA10);
        Assert.NotNull(aliasA10);
        var expectedA10Key = CsfFile.ExtractHotkey(primaryA10);
        Assert.NotNull(expectedA10Key);
        Assert.Equal(expectedA10Key, CsfFile.ExtractHotkey(aliasA10));

        // 3. Verify cleared key stripped from both primary and sidebar alias
        var primaryCashHack = csf.GetString("CONTROLBAR:CashHack");
        var aliasCashHack = csf.GetString("GUI:SuperweaponCashHack");
        Assert.Null(CsfFile.ExtractHotkey(primaryCashHack));
        Assert.Null(CsfFile.ExtractHotkey(aliasCashHack));
    }
}
