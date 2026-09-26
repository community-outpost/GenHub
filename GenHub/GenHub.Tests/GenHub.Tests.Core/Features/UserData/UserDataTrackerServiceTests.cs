using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameSettings;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.UserData;
using GenHub.Features.UserData.Services;
using GenHub.Tests.Core.Infrastructure;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.UserData;

/// <summary>
/// Unit tests for <see cref="UserDataTrackerService"/>.
/// </summary>
public sealed class UserDataTrackerServiceTests : IDisposable
{
    private const string TestManifestId = "1.1015255.generalsonline.patch.gamedata";
    private const string TestProfileId = "profile-zh-1";
    private const string TestVersion = "101525_QFE5";
    private const string TestManifestName = "GameData Patch";

    private readonly string _tempDir;
    private readonly string _appDataDir;
    private readonly string _zeroHourDataDir;
    private readonly string _generalsDataDir;
    private readonly Mock<IConfigurationProviderService> _configProviderMock;
    private readonly Mock<IFileOperationsService> _fileOperationsMock;
    private readonly Mock<ILogger<UserDataTrackerService>> _loggerMock;
    private readonly Mock<IGamePathProvider> _pathProviderMock;
    private readonly UserDataTrackerService _trackerService;

    /// <summary>
    /// Initializes a new instance of the <see cref="UserDataTrackerServiceTests"/> class.
    /// </summary>
    public UserDataTrackerServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "GenHub_UserDataTrackerTests_" + Guid.NewGuid().ToString("N"));
        _appDataDir = Path.Combine(_tempDir, "AppData");
        _zeroHourDataDir = Path.Combine(_tempDir, GameSettingsConstants.FolderNames.ZeroHour);
        _generalsDataDir = Path.Combine(_tempDir, GameSettingsConstants.FolderNames.Generals);

        Directory.CreateDirectory(_appDataDir);
        Directory.CreateDirectory(_zeroHourDataDir);
        Directory.CreateDirectory(_generalsDataDir);

        _configProviderMock = new Mock<IConfigurationProviderService>();
        _configProviderMock.Setup(c => c.GetApplicationDataPath()).Returns(_appDataDir);

        _fileOperationsMock = new Mock<IFileOperationsService>();
        _loggerMock = new Mock<ILogger<UserDataTrackerService>>();

        _pathProviderMock = new Mock<IGamePathProvider>();
        _pathProviderMock.Setup(p => p.GetOptionsDirectory(GameType.ZeroHour)).Returns(_zeroHourDataDir);
        _pathProviderMock.Setup(p => p.GetOptionsDirectory(GameType.Generals)).Returns(_generalsDataDir);

        // Default mock for CAS linking: creates a file at targetPath
        _fileOperationsMock
            .Setup(f => f.LinkFromCasAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<ContentType?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, bool, ContentType?, CancellationToken>((hash, targetPath, useHardLink, contentType, token) =>
            {
                var dir = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllText(targetPath, "cas-content-" + hash);
            })
            .ReturnsAsync(true);

        // Default mock for CAS copying: user-writable destinations are always copied, never linked
        _fileOperationsMock
            .Setup(f => f.CopyFromCasAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<ContentType?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, ContentType?, CancellationToken>((hash, targetPath, contentType, token) =>
            {
                var dir = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllText(targetPath, "cas-content-" + hash);
            })
            .ReturnsAsync(true);

        _fileOperationsMock
            .Setup(f => f.VerifyFileHashAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _fileOperationsMock
            .Setup(f => f.CheckFileHashAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(FileHashVerification.Match);

        _trackerService = new UserDataTrackerService(
            _configProviderMock.Object,
            _fileOperationsMock.Object,
            _loggerMock.Object,
            _pathProviderMock.Object);
    }

    /// <summary>
    /// Verifies that installing a map with loose files (e.g. Last Stand_8.tga, Last Stand_8.map)
    /// places both files inside a dedicated subdirectory matching the map name in the Maps folder.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task InstallUserDataAsync_FlatMapAndTga_InstallsIntoDedicatedMapSubdirectoryAsync()
    {
        var files = new List<ManifestFile>
        {
            new()
            {
                RelativePath = "Last Stand_8.map",
                Hash = "hash-map",
                Size = 1000,
                InstallTarget = ContentInstallTarget.UserMapsDirectory,
            },
            new()
            {
                RelativePath = "Last Stand_8.tga",
                Hash = "hash-tga",
                Size = 500,
                InstallTarget = ContentInstallTarget.UserMapsDirectory,
            },
        };

        var result = await _trackerService.InstallUserDataAsync(
            TestManifestId,
            "profile-flat-map",
            GameType.ZeroHour,
            files,
            TestVersion,
            TestManifestName,
            CancellationToken.None);

        Assert.True(result.Success);
        var expectedMapPath = Path.Combine(_zeroHourDataDir, "Maps", "Last Stand_8", "Last Stand_8.map");
        var expectedTgaPath = Path.Combine(_zeroHourDataDir, "Maps", "Last Stand_8", "Last Stand_8.tga");
        Assert.True(File.Exists(expectedMapPath));
        Assert.True(File.Exists(expectedTgaPath));
    }

    /// <summary>
    /// Verifies that installing a map from a folder ending in .map normalizes the destination path
    /// so that .map is stripped from the directory name.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task InstallUserDataAsync_DirectoryEndingInDotMap_StripsDotMapFromDirectoryAsync()
    {
        var files = new List<ManifestFile>
        {
            new()
            {
                RelativePath = "Last Stand_8.map/Last Stand_8.map",
                Hash = "hash-map-nested",
                Size = 1000,
                InstallTarget = ContentInstallTarget.UserMapsDirectory,
            },
            new()
            {
                RelativePath = "Last Stand_8.map/Last Stand_8.tga",
                Hash = "hash-tga-nested",
                Size = 500,
                InstallTarget = ContentInstallTarget.UserMapsDirectory,
            },
        };

        var result = await _trackerService.InstallUserDataAsync(
            TestManifestId,
            "profile-dot-map-dir",
            GameType.ZeroHour,
            files,
            TestVersion,
            TestManifestName,
            CancellationToken.None);

        Assert.True(result.Success);
        var expectedMapPath = Path.Combine(_zeroHourDataDir, "Maps", "Last Stand_8", "Last Stand_8.map");
        var expectedTgaPath = Path.Combine(_zeroHourDataDir, "Maps", "Last Stand_8", "Last Stand_8.tga");
        Assert.True(File.Exists(expectedMapPath));
        Assert.True(File.Exists(expectedTgaPath));
        Assert.False(Directory.Exists(Path.Combine(_zeroHourDataDir, "Maps", "Last Stand_8.map")));
    }

    /// <summary>
    /// Verifies that installing nested map files keeps companion files (such as .map, map.ini, Custom.ini, preview.tga)
    /// together in the same parent directory rather than splitting them by basename.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task InstallUserDataAsync_NestedMapWithCustomIniAndPreview_KeepsAllFilesUnderSingleMapDirectoryAsync()
    {
        var files = new List<ManifestFile>
        {
            new()
            {
                RelativePath = "Desert/desert.map",
                Hash = "hash-map-desert",
                Size = 1000,
                InstallTarget = ContentInstallTarget.UserMapsDirectory,
            },
            new()
            {
                RelativePath = "Desert/map.ini",
                Hash = "hash-ini-map",
                Size = 300,
                InstallTarget = ContentInstallTarget.UserMapsDirectory,
            },
            new()
            {
                RelativePath = "Desert/Custom.ini",
                Hash = "hash-ini-custom",
                Size = 200,
                InstallTarget = ContentInstallTarget.UserMapsDirectory,
            },
            new()
            {
                RelativePath = "Desert/preview.tga",
                Hash = "hash-tga-preview",
                Size = 500,
                InstallTarget = ContentInstallTarget.UserMapsDirectory,
            },
        };

        var result = await _trackerService.InstallUserDataAsync(
            TestManifestId,
            "profile-nested-map-grouping",
            GameType.ZeroHour,
            files,
            TestVersion,
            TestManifestName,
            CancellationToken.None);

        Assert.True(result.Success);
        var expectedMapPath = Path.Combine(_zeroHourDataDir, "Maps", "Desert", "desert.map");
        var expectedMapIniPath = Path.Combine(_zeroHourDataDir, "Maps", "Desert", "map.ini");
        var expectedCustomIniPath = Path.Combine(_zeroHourDataDir, "Maps", "Desert", "Custom.ini");
        var expectedTgaPath = Path.Combine(_zeroHourDataDir, "Maps", "Desert", "Desert.tga");

        Assert.True(File.Exists(expectedMapPath));
        Assert.True(File.Exists(expectedMapIniPath));
        Assert.True(File.Exists(expectedCustomIniPath));
        Assert.True(File.Exists(expectedTgaPath));

        // Ensure no stray directories were created based on basenames
        Assert.False(Directory.Exists(Path.Combine(_zeroHourDataDir, "Maps", "Custom")));
    }

    /// <summary>
    /// Verifies that flat generic companions (map.tga, preview.tga, map.ini) are installed
    /// into the sibling map's folder instead of orphaned Maps/map or Maps/preview directories.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task InstallUserDataAsync_FlatMapWithGenericCompanions_InstallsIntoSiblingMapSubdirectoryAsync()
    {
        var files = new List<ManifestFile>
        {
            new()
            {
                RelativePath = "River.map",
                Hash = "hash-map-river",
                Size = 1000,
                InstallTarget = ContentInstallTarget.UserMapsDirectory,
            },
            new()
            {
                RelativePath = "map.tga",
                Hash = "hash-tga-thumb",
                Size = 500,
                InstallTarget = ContentInstallTarget.UserMapsDirectory,
            },
            new()
            {
                RelativePath = "map.ini",
                Hash = "hash-ini-script",
                Size = 200,
                InstallTarget = ContentInstallTarget.UserMapsDirectory,
            },
            new()
            {
                RelativePath = "map.str",
                Hash = "hash-str-strings",
                Size = 100,
                InstallTarget = ContentInstallTarget.UserMapsDirectory,
            },
        };

        var result = await _trackerService.InstallUserDataAsync(
            TestManifestId,
            "profile-generic-companions",
            GameType.ZeroHour,
            files,
            TestVersion,
            TestManifestName,
            CancellationToken.None);

        Assert.True(result.Success);
        var expectedMapPath = Path.Combine(_zeroHourDataDir, "Maps", "River", "River.map");
        var expectedTgaPath = Path.Combine(_zeroHourDataDir, "Maps", "River", "River.tga");
        var expectedIniPath = Path.Combine(_zeroHourDataDir, "Maps", "River", "map.ini");
        var expectedStrPath = Path.Combine(_zeroHourDataDir, "Maps", "River", "map.str");

        Assert.True(File.Exists(expectedMapPath));
        Assert.True(File.Exists(expectedTgaPath));
        Assert.True(File.Exists(expectedIniPath));
        Assert.True(File.Exists(expectedStrPath));

        Assert.False(Directory.Exists(Path.Combine(_zeroHourDataDir, "Maps", "map")));
    }

    /// <summary>
    /// Verifies that flat multi-map payloads containing sharing prefixes (e.g. River.map and River_v2.map)
    /// install each map into its own subdirectory rather than shadowing exact matches with prefix matches.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task InstallUserDataAsync_MultiMapFlatPayload_InstallsEachMapIntoOwnSubdirectoryAsync()
    {
        var files = new List<ManifestFile>
        {
            new()
            {
                RelativePath = "River.map",
                Hash = "hash-map-river",
                Size = 1000,
                InstallTarget = ContentInstallTarget.UserMapsDirectory,
            },
            new()
            {
                RelativePath = "River_v2.map",
                Hash = "hash-map-river-v2",
                Size = 1200,
                InstallTarget = ContentInstallTarget.UserMapsDirectory,
            },
            new()
            {
                RelativePath = "river.tga",
                Hash = "hash-tga-river",
                Size = 500,
                InstallTarget = ContentInstallTarget.UserMapsDirectory,
            },
            new()
            {
                RelativePath = "River_v2_art.tga",
                Hash = "hash-tga-river-v2",
                Size = 600,
                InstallTarget = ContentInstallTarget.UserMapsDirectory,
            },
        };

        var result = await _trackerService.InstallUserDataAsync(
            TestManifestId,
            "profile-multi-map-flat",
            GameType.ZeroHour,
            files,
            TestVersion,
            TestManifestName,
            CancellationToken.None);

        Assert.True(result.Success);
        var riverMapPath = Path.Combine(_zeroHourDataDir, "Maps", "River", "River.map");
        var riverTgaPath = Path.Combine(_zeroHourDataDir, "Maps", "River", "River.tga");
        var riverV2MapPath = Path.Combine(_zeroHourDataDir, "Maps", "River_v2", "River_v2.map");
        var riverV2TgaPath = Path.Combine(_zeroHourDataDir, "Maps", "River_v2", "River_v2.tga");

        Assert.True(File.Exists(riverMapPath));
        Assert.True(File.Exists(riverTgaPath));
        Assert.True(File.Exists(riverV2MapPath));
        Assert.True(File.Exists(riverV2TgaPath));

        Assert.False(File.Exists(Path.Combine(_zeroHourDataDir, "Maps", "River", "River_v2.map")));
    }

    /// <summary>
    /// Verifies that flat multi-map payloads where the longer prefixed map is listed first
    /// install each map into its own subdirectory.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task InstallUserDataAsync_MultiMapFlatPayload_ReverseOrder_InstallsEachMapIntoOwnSubdirectoryAsync()
    {
        var files = new List<ManifestFile>
        {
            new()
            {
                RelativePath = "River_v2.map",
                Hash = "hash-map-river-v2",
                Size = 1200,
                InstallTarget = ContentInstallTarget.UserMapsDirectory,
            },
            new()
            {
                RelativePath = "River.map",
                Hash = "hash-map-river",
                Size = 1000,
                InstallTarget = ContentInstallTarget.UserMapsDirectory,
            },
        };

        var result = await _trackerService.InstallUserDataAsync(
            TestManifestId,
            "profile-multi-map-flat-rev",
            GameType.ZeroHour,
            files,
            TestVersion,
            TestManifestName,
            CancellationToken.None);

        Assert.True(result.Success);
        var riverMapPath = Path.Combine(_zeroHourDataDir, "Maps", "River", "River.map");
        var riverV2MapPath = Path.Combine(_zeroHourDataDir, "Maps", "River_v2", "River_v2.map");

        Assert.True(File.Exists(riverMapPath));
        Assert.True(File.Exists(riverV2MapPath));
        Assert.False(File.Exists(Path.Combine(_zeroHourDataDir, "Maps", "River_v2", "River.map")));
    }

    /// <summary>
    /// Verifies that flat payloads with variant companion thumbnails (e.g. OldMap.backup.tga)
    /// preserve their filename in the map directory without clobbering the main thumbnail (OldMap.tga).
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task InstallUserDataAsync_FlatPayloadWithVariantThumbnail_PreservesVariantFilenameWithoutClobberingAsync()
    {
        var files = new List<ManifestFile>
        {
            new()
            {
                RelativePath = "OldMap.map",
                Hash = "hash-map-oldmap",
                Size = 1000,
                InstallTarget = ContentInstallTarget.UserMapsDirectory,
            },
            new()
            {
                RelativePath = "OldMap.tga",
                Hash = "hash-tga-oldmap",
                Size = 500,
                InstallTarget = ContentInstallTarget.UserMapsDirectory,
            },
            new()
            {
                RelativePath = "OldMap.backup.tga",
                Hash = "hash-tga-oldmap-backup",
                Size = 500,
                InstallTarget = ContentInstallTarget.UserMapsDirectory,
            },
        };

        var result = await _trackerService.InstallUserDataAsync(
            TestManifestId,
            "profile-variant-tga",
            GameType.ZeroHour,
            files,
            TestVersion,
            TestManifestName,
            CancellationToken.None);

        Assert.True(result.Success);
        var mapPath = Path.Combine(_zeroHourDataDir, "Maps", "OldMap", "OldMap.map");
        var tgaPath = Path.Combine(_zeroHourDataDir, "Maps", "OldMap", "OldMap.tga");
        var backupTgaPath = Path.Combine(_zeroHourDataDir, "Maps", "OldMap", "OldMap.backup.tga");

        Assert.True(File.Exists(mapPath));
        Assert.True(File.Exists(tgaPath));
        Assert.True(File.Exists(backupTgaPath));

        // Reinstall to ensure dictionary key on AbsolutePath does not throw on reinstall
        var reinstallResult = await _trackerService.InstallUserDataAsync(
            TestManifestId,
            "profile-variant-tga",
            GameType.ZeroHour,
            files,
            TestVersion,
            TestManifestName,
            CancellationToken.None);

        Assert.True(reinstallResult.Success);
    }

    /// <summary>
    /// Verifies that installing map files with an _art suffix (e.g. River_art.tga)
    /// normalizes the filename to River.tga inside the map folder so the game recognizes the thumbnail.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task InstallUserDataAsync_MapWithArtTga_NormalizesTgaToMapNameAsync()
    {
        var files = new List<ManifestFile>
        {
            new()
            {
                RelativePath = "River_art.tga",
                Hash = "hash-tga-art",
                Size = 500,
                InstallTarget = ContentInstallTarget.UserMapsDirectory,
            },
            new()
            {
                RelativePath = "River.map",
                Hash = "hash-map-river",
                Size = 1000,
                InstallTarget = ContentInstallTarget.UserMapsDirectory,
            },
        };

        var result = await _trackerService.InstallUserDataAsync(
            TestManifestId,
            "profile-art-tga",
            GameType.ZeroHour,
            files,
            TestVersion,
            TestManifestName,
            CancellationToken.None);

        Assert.True(result.Success);
        var expectedMapPath = Path.Combine(_zeroHourDataDir, "Maps", "River", "River.map");
        var expectedTgaPath = Path.Combine(_zeroHourDataDir, "Maps", "River", "River.tga");

        Assert.True(File.Exists(expectedMapPath));
        Assert.True(File.Exists(expectedTgaPath));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                ReadOnlyFolderFixtures.RestoreWritable(_tempDir);
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Ignore test cleanup errors
        }
    }

    /// <summary>
    /// Verifies that deploying into an existing map folder made read-only outside GenHub replaces the file.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task InstallUserDataAsync_IntoReadOnlyExistingMapFolder_ReplacesFileAsync()
    {
        var mapDir = Path.Combine(_zeroHourDataDir, "Maps", "Last Stand_8");
        var mapPath = Path.Combine(mapDir, "Last Stand_8.map");
        Directory.CreateDirectory(mapDir);
        await File.WriteAllTextAsync(mapPath, "user map");
        ReadOnlyFolderFixtures.MakeReadOnly(mapDir);

        var result = await _trackerService.InstallUserDataAsync(
            TestManifestId,
            "profile-readonly-install",
            GameType.ZeroHour,
            [CreateMapFile("Last Stand_8.map", "hash-map")],
            TestVersion,
            TestManifestName,
            CancellationToken.None);

        Assert.True(result.Success, result.FirstError);
        Assert.Equal("cas-content-hash-map", await File.ReadAllTextAsync(mapPath));
    }

    /// <summary>
    /// Verifies that uninstalling removes deployed maps whose folder was made read-only after deployment.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task UninstallUserDataAsync_WithReadOnlyMapFolder_RemovesDeployedMapAsync()
    {
        const string profileId = "profile-readonly-uninstall";
        var install = await _trackerService.InstallUserDataAsync(
            TestManifestId,
            profileId,
            GameType.ZeroHour,
            [CreateMapFile("Last Stand_8.map", "hash-map"), CreateMapFile("Last Stand_8.tga", "hash-tga")],
            TestVersion,
            TestManifestName,
            CancellationToken.None);
        Assert.True(install.Success, install.FirstError);
        var mapDir = Path.Combine(_zeroHourDataDir, "Maps", "Last Stand_8");
        ReadOnlyFolderFixtures.MakeReadOnly(mapDir);

        var result = await _trackerService.UninstallUserDataAsync(TestManifestId, profileId, CancellationToken.None);

        Assert.True(result.Success, result.FirstError);
        Assert.False(File.Exists(Path.Combine(mapDir, "Last Stand_8.map")));
        Assert.False(File.Exists(Path.Combine(mapDir, "Last Stand_8.tga")));
    }

    /// <summary>
    /// Verifies that deactivating a profile removes deployed maps whose folder was made read-only after deployment.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task DeactivateProfileUserDataAsync_WithReadOnlyMapFolder_RemovesDeployedMapAsync()
    {
        const string profileId = "profile-readonly-deactivate";
        var install = await _trackerService.InstallUserDataAsync(
            TestManifestId,
            profileId,
            GameType.ZeroHour,
            [CreateMapFile("Last Stand_8.map", "hash-map")],
            TestVersion,
            TestManifestName,
            CancellationToken.None);
        Assert.True(install.Success, install.FirstError);
        var mapDir = Path.Combine(_zeroHourDataDir, "Maps", "Last Stand_8");
        ReadOnlyFolderFixtures.MakeReadOnly(mapDir);

        var result = await _trackerService.DeactivateProfileUserDataAsync(profileId, CancellationToken.None);

        Assert.True(result.Success, result.FirstError);
        Assert.False(File.Exists(Path.Combine(mapDir, "Last Stand_8.map")));
    }

    /// <summary>
    /// Verifies that a deployment into a map folder whose permissions cannot be changed fails with an error naming the folder.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task InstallUserDataAsync_WhenMapFolderCannotBeMadeWritable_ReturnsErrorNamingFolderAsync()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var mapDir = Path.Combine(_zeroHourDataDir, "Maps", "Last Stand_8");
        var mapPath = Path.Combine(mapDir, "Last Stand_8.map");
        Directory.CreateDirectory(mapDir);
        await File.WriteAllTextAsync(mapPath, "user map");
        ReadOnlyFolderFixtures.MakeReadOnly(mapDir);
        ReadOnlyFolderFixtures.LockImmutable(mapDir);

        var result = await _trackerService.InstallUserDataAsync(
            TestManifestId,
            "profile-immutable-install",
            GameType.ZeroHour,
            [CreateMapFile("Last Stand_8.map", "hash-map")],
            TestVersion,
            TestManifestName,
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains($"'{mapDir}'", result.FirstError);
        Assert.Equal("user map", await File.ReadAllTextAsync(mapPath));
    }

    /// <summary>
    /// Verifies that data patch files targeting UserDataDirectory are placed into the correct Zero Hour Documents directory.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task InstallUserDataAsync_ZeroHourGameDataPatch_DeploysPreservingSubdirectoriesAsync()
    {
        // Arrange
        var files = new List<ManifestFile>
        {
            new()
            {
                RelativePath = "GeneralsOnlineGameData/splash.bmp",
                Hash = "hash-splash-123",
                Size = 1024,
                InstallTarget = ContentInstallTarget.UserDataDirectory,
            },
            new()
            {
                RelativePath = "GeneralsOnlineGameData/500_900_CommunityPatch_CoreINI.big",
                Hash = "hash-big-456",
                Size = 2048,
                InstallTarget = ContentInstallTarget.UserDataDirectory,
            },
        };

        // Act
        var result = await _trackerService.InstallUserDataAsync(
            TestManifestId,
            TestProfileId,
            GameType.ZeroHour,
            files,
            TestVersion,
            TestManifestName,
            CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(2, result.Data.InstalledFiles.Count);

        var expectedSplashPath = Path.Combine(_zeroHourDataDir, "GeneralsOnlineGameData", "splash.bmp");
        var expectedBigPath = Path.Combine(_zeroHourDataDir, "GeneralsOnlineGameData", "500_900_CommunityPatch_CoreINI.big");

        Assert.True(File.Exists(expectedSplashPath));
        Assert.True(File.Exists(expectedBigPath));
        Assert.Equal("cas-content-hash-splash-123", File.ReadAllText(expectedSplashPath));
        Assert.Equal("cas-content-hash-big-456", File.ReadAllText(expectedBigPath));
    }

    /// <summary>
    /// Verifies that data patch files targeting UserDataDirectory are placed into the correct Generals Documents directory.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task InstallUserDataAsync_GeneralsGameDataPatch_DeploysToGeneralsDirectoryAsync()
    {
        // Arrange
        var files = new List<ManifestFile>
        {
            new()
            {
                RelativePath = "GeneralsOnlineGameData/splash.bmp",
                Hash = "hash-gen-splash",
                Size = 512,
                InstallTarget = ContentInstallTarget.UserDataDirectory,
            },
        };

        // Act
        var result = await _trackerService.InstallUserDataAsync(
            TestManifestId,
            "profile-gen-1",
            GameType.Generals,
            files,
            TestVersion,
            TestManifestName,
            CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        var expectedSplashPath = Path.Combine(_generalsDataDir, "GeneralsOnlineGameData", "splash.bmp");
        Assert.True(File.Exists(expectedSplashPath));
    }

    /// <summary>
    /// Verifies that pre-existing user files are safely backed up before being overwritten, and restored on uninstall.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task InstallAndUninstall_WithExistingUserFile_SafelyBacksUpAndRestoresOriginalAsync()
    {
        // Arrange: simulate pre-existing user file in Documents\...\GeneralsOnlineGameData\splash.bmp
        var gameDataDir = Path.Combine(_zeroHourDataDir, "GeneralsOnlineGameData");
        Directory.CreateDirectory(gameDataDir);
        var existingSplashPath = Path.Combine(gameDataDir, "splash.bmp");
        var originalUserContent = "original-user-splash-bmp";
        File.WriteAllText(existingSplashPath, originalUserContent);

        var files = new List<ManifestFile>
        {
            new()
            {
                RelativePath = "GeneralsOnlineGameData/splash.bmp",
                Hash = "hash-patch-splash",
                Size = 100,
                InstallTarget = ContentInstallTarget.UserDataDirectory,
            },
        };

        // Act 1: Install data patch
        var installResult = await _trackerService.InstallUserDataAsync(
            TestManifestId,
            "profile-backup-test",
            GameType.ZeroHour,
            files,
            TestVersion,
            TestManifestName,
            CancellationToken.None);

        // Assert 1: File overwritten with patch content, backup recorded
        Assert.True(installResult.Success);
        Assert.True(installResult.Data!.InstalledFiles[0].WasOverwritten);
        Assert.NotNull(installResult.Data.InstalledFiles[0].BackupPath);
        Assert.True(File.Exists(installResult.Data.InstalledFiles[0].BackupPath));
        Assert.Equal(originalUserContent, File.ReadAllText(installResult.Data.InstalledFiles[0].BackupPath!));
        Assert.Equal("cas-content-hash-patch-splash", File.ReadAllText(existingSplashPath));

        // Act 2: Uninstall data patch
        var uninstallResult = await _trackerService.UninstallUserDataAsync(
            TestManifestId,
            "profile-backup-test",
            CancellationToken.None);

        // Assert 2: Original user content restored
        Assert.True(uninstallResult.Success);
        Assert.True(File.Exists(existingSplashPath));
        Assert.Equal(originalUserContent, File.ReadAllText(existingSplashPath));
    }

    /// <summary>
    /// Verifies that deactivating and reactivating a profile preserves and restores state cleanly.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task DeactivateAndActivateProfileUserDataAsync_ProperlyTogglesFilesAsync()
    {
        // Arrange
        var files = new List<ManifestFile>
        {
            new()
            {
                RelativePath = "GeneralsOnlineGameData/patch.big",
                Hash = "hash-big-file",
                Size = 1000,
                InstallTarget = ContentInstallTarget.UserDataDirectory,
            },
        };

        var installResult = await _trackerService.InstallUserDataAsync(
            TestManifestId,
            "profile-switch-test",
            GameType.ZeroHour,
            files,
            TestVersion,
            TestManifestName,
            CancellationToken.None);

        Assert.True(installResult.Success);
        var targetBigPath = Path.Combine(_zeroHourDataDir, "GeneralsOnlineGameData", "patch.big");
        Assert.True(File.Exists(targetBigPath));

        // Act 1: Deactivate profile
        var deactivateResult = await _trackerService.DeactivateProfileUserDataAsync("profile-switch-test", CancellationToken.None);

        // Assert 1: Deactivated files removed, empty subfolder cleaned up, base folder preserved
        Assert.True(deactivateResult.Success);
        Assert.False(File.Exists(targetBigPath));
        Assert.True(Directory.Exists(_zeroHourDataDir));

        // Act 2: Reactivate profile
        var activateResult = await _trackerService.ActivateProfileUserDataAsync("profile-switch-test", CancellationToken.None);

        // Assert 2: Files re-materialized from CAS
        Assert.True(activateResult.Success);
        Assert.True(File.Exists(targetBigPath));
        Assert.Equal("cas-content-hash-big-file", File.ReadAllText(targetBigPath));
    }

    /// <summary>
    /// Verifies that DeactivateProfileUserDataAsync with removeFiles=false marks manifests inactive
    /// while preserving physical files on disk, allowing another profile to adopt identical files without conflict.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task DeactivateProfileUserDataAsync_WhenRemoveFilesFalse_PreservesFilesOnDiskAndAllowsAdoptionAsync()
    {
        // Arrange
        var files = new List<ManifestFile>
        {
            new()
            {
                RelativePath = "GeneralsOnlineGameData/shared.big",
                Hash = "hash-shared-big",
                Size = 1000,
                InstallTarget = ContentInstallTarget.UserDataDirectory,
            },
        };

        var installResult = await _trackerService.InstallUserDataAsync(
            TestManifestId,
            "profile-first",
            GameType.ZeroHour,
            files,
            TestVersion,
            TestManifestName,
            CancellationToken.None);

        Assert.True(installResult.Success);
        var targetBigPath = Path.Combine(_zeroHourDataDir, "GeneralsOnlineGameData", "shared.big");
        Assert.True(File.Exists(targetBigPath));

        // Act: Deactivate with removeFiles: false
        var deactivateResult = await _trackerService.DeactivateProfileUserDataAsync(
            "profile-first",
            removeFiles: false,
            CancellationToken.None);

        // Assert: File is still on disk, manifest is inactive
        Assert.True(deactivateResult.Success);
        Assert.True(File.Exists(targetBigPath));

        var manifestsResult = await _trackerService.GetProfileUserDataAsync("profile-first", CancellationToken.None);
        Assert.True(manifestsResult.Success);
        Assert.NotNull(manifestsResult.Data);
        Assert.Single(manifestsResult.Data);
        Assert.False(manifestsResult.Data[0].IsActive);

        // Act 2: New profile installs identical manifest/file - should adopt without conflict
        var secondInstallResult = await _trackerService.InstallUserDataAsync(
            TestManifestId,
            "profile-second",
            GameType.ZeroHour,
            files,
            TestVersion,
            TestManifestName,
            CancellationToken.None);

        Assert.True(secondInstallResult.Success);
        Assert.True(File.Exists(targetBigPath));
    }

    /// <summary>
    /// Verifies that uninstall cleans up empty subdirectories without deleting the root game data folder.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task UninstallUserDataAsync_CleansUpEmptySubdirectory_PreservesRootUserDataFolderAsync()
    {
        // Arrange
        var files = new List<ManifestFile>
        {
            new()
            {
                RelativePath = "GeneralsOnlineGameData/temp.big",
                Hash = "hash-temp-big",
                Size = 500,
                InstallTarget = ContentInstallTarget.UserDataDirectory,
            },
        };

        await _trackerService.InstallUserDataAsync(
            TestManifestId,
            "profile-cleanup-test",
            GameType.ZeroHour,
            files,
            TestVersion,
            TestManifestName,
            CancellationToken.None);

        var subDir = Path.Combine(_zeroHourDataDir, "GeneralsOnlineGameData");
        Assert.True(Directory.Exists(subDir));

        // Act
        var uninstallResult = await _trackerService.UninstallUserDataAsync(
            TestManifestId,
            "profile-cleanup-test",
            CancellationToken.None);

        // Assert
        Assert.True(uninstallResult.Success);
        Assert.False(Directory.Exists(subDir)); // Empty subfolder cleaned up
        Assert.True(Directory.Exists(_zeroHourDataDir)); // Root folder kept safe
    }

    /// <summary>
    /// Verifies that if a user modifies a deployed file, deactivation preserves the modified file and does not overwrite it with the backup.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task DeactivateProfileUserDataAsync_WhenUserModifiesDeployedFile_PreservesModifiedFileAndDoesNotOverwriteWithBackupAsync()
    {
        // Arrange: pre-existing user file
        var gameDataDir = Path.Combine(_zeroHourDataDir, "GeneralsOnlineGameData");
        Directory.CreateDirectory(gameDataDir);
        var splashPath = Path.Combine(gameDataDir, "splash.bmp");
        var originalUserContent = "original-user-splash";
        File.WriteAllText(splashPath, originalUserContent);

        var files = new List<ManifestFile>
        {
            new()
            {
                RelativePath = "GeneralsOnlineGameData/splash.bmp",
                Hash = "hash-splash-expected",
                Size = 100,
                InstallTarget = ContentInstallTarget.UserDataDirectory,
            },
        };

        var installResult = await _trackerService.InstallUserDataAsync(
            TestManifestId,
            "profile-user-edit-test",
            GameType.ZeroHour,
            files,
            TestVersion,
            TestManifestName,
            CancellationToken.None);

        Assert.True(installResult.Success);
        Assert.True(installResult.Data!.InstalledFiles[0].WasOverwritten);

        // Simulate user editing the deployed splash.bmp after install
        var modifiedContent = "user-edited-splash-content";
        File.WriteAllText(splashPath, modifiedContent);

        // Configure hash verification to fail for the modified file
        _fileOperationsMock
            .Setup(f => f.VerifyFileHashAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act: Deactivate profile
        var deactivateResult = await _trackerService.DeactivateProfileUserDataAsync("profile-user-edit-test", CancellationToken.None);

        // Assert: Modified file was preserved and NOT overwritten by the backup
        Assert.True(deactivateResult.Success);
        Assert.True(File.Exists(splashPath));
        Assert.Equal(modifiedContent, File.ReadAllText(splashPath));

        // Backup file remains intact in the backup store for recovery
        var backupPath = installResult.Data.InstalledFiles[0].BackupPath;
        Assert.NotNull(backupPath);
        Assert.True(File.Exists(backupPath));
        Assert.Equal(originalUserContent, File.ReadAllText(backupPath!));
    }

    /// <summary>
    /// Verifies that when a user modifies a restored backup while deactivated, reactivation creates a new backup of the new content and restores it cleanly on subsequent deactivation.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ReactivateProfileUserDataAsync_WhenUserModifiesRestoredFileWhileDeactivated_BacksUpNewContentAndRestoresItOnSubsequentDeactivationAsync()
    {
        // Arrange
        var gameDataDir = Path.Combine(_zeroHourDataDir, "GeneralsOnlineGameData");
        Directory.CreateDirectory(gameDataDir);
        var splashPath = Path.Combine(gameDataDir, "splash.bmp");
        var originalUserContent = "original-user-splash";
        File.WriteAllText(splashPath, originalUserContent);

        var files = new List<ManifestFile>
        {
            new()
            {
                RelativePath = "GeneralsOnlineGameData/splash.bmp",
                Hash = "hash-splash-expected",
                Size = 100,
                InstallTarget = ContentInstallTarget.UserDataDirectory,
            },
        };

        // 1. Install profile
        var installResult = await _trackerService.InstallUserDataAsync(
            TestManifestId,
            "profile-reactivate-test",
            GameType.ZeroHour,
            files,
            TestVersion,
            TestManifestName,
            CancellationToken.None);

        Assert.True(installResult.Success);

        // 2. Deactivate profile (restores original backup)
        var deactivateResult = await _trackerService.DeactivateProfileUserDataAsync("profile-reactivate-test", CancellationToken.None);
        Assert.True(deactivateResult.Success);
        Assert.Equal(originalUserContent, File.ReadAllText(splashPath));

        // 3. User modifies the file while profile is inactive
        var newerUserContent = "newer-user-splash-created-while-inactive";
        File.WriteAllText(splashPath, newerUserContent);

        // Configure hash check: deployed CAS file matches "hash-splash-expected", user file does not
        _fileOperationsMock
            .Setup(f => f.VerifyFileHashAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string path, string hash, CancellationToken _) => File.Exists(path) && File.ReadAllText(path) == "cas-content-" + hash);

        // 4. Reactivate profile (should back up newerUserContent and deploy CAS file)
        var reactivateResult = await _trackerService.ActivateProfileUserDataAsync("profile-reactivate-test", CancellationToken.None);
        Assert.True(reactivateResult.Success);
        Assert.Equal("cas-content-hash-splash-expected", File.ReadAllText(splashPath));

        // 5. Deactivate profile again (should restore the newerUserContent, NOT the stale original)
        var secondDeactivateResult = await _trackerService.DeactivateProfileUserDataAsync("profile-reactivate-test", CancellationToken.None);
        Assert.True(secondDeactivateResult.Success);
        Assert.True(File.Exists(splashPath));
        Assert.Equal(newerUserContent, File.ReadAllText(splashPath));
    }

    /// <summary>
    /// Verifies that when activation materialization fails, rollback restores the existing user file backup.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ActivateProfileUserDataAsync_WhenMaterializationFails_RollsBackAndRestoresBackupAsync()
    {
        // Arrange
        var gameDataDir = Path.Combine(_zeroHourDataDir, "GeneralsOnlineGameData");
        Directory.CreateDirectory(gameDataDir);
        var splashPath = Path.Combine(gameDataDir, "splash.bmp");
        var originalUserContent = "original-user-splash";
        File.WriteAllText(splashPath, originalUserContent);

        var files = new List<ManifestFile>
        {
            new()
            {
                RelativePath = "GeneralsOnlineGameData/splash.bmp",
                Hash = "hash-splash-expected",
                Size = 100,
                InstallTarget = ContentInstallTarget.UserDataDirectory,
            },
        };

        // 1. Install & Deactivate
        await _trackerService.InstallUserDataAsync(
            TestManifestId,
            "profile-fail-materialize-test",
            GameType.ZeroHour,
            files,
            TestVersion,
            TestManifestName,
            CancellationToken.None);

        await _trackerService.DeactivateProfileUserDataAsync("profile-fail-materialize-test", CancellationToken.None);
        Assert.Equal(originalUserContent, File.ReadAllText(splashPath));

        // 2. Mock CAS materialization failure and hash check
        _fileOperationsMock
            .Setup(f => f.VerifyFileHashAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _fileOperationsMock
            .Setup(f => f.LinkFromCasAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<ContentType?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _fileOperationsMock
            .Setup(f => f.CopyFromCasAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ContentType?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // 3. Act: Activate profile
        var activateResult = await _trackerService.ActivateProfileUserDataAsync("profile-fail-materialize-test", CancellationToken.None);

        // 4. Assert: Activation failed, rollback restored user backup
        Assert.False(activateResult.Success);
        Assert.True(File.Exists(splashPath));
        Assert.Equal(originalUserContent, File.ReadAllText(splashPath));
    }

    /// <summary>
    /// Verifies that when activation is cancelled mid-materialization, rollback restores user files and rethrows OperationCanceledException.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ActivateProfileUserDataAsync_WhenCanceled_RollsBackAndRethrowsAsync()
    {
        // Arrange
        var gameDataDir = Path.Combine(_zeroHourDataDir, "GeneralsOnlineGameData");
        Directory.CreateDirectory(gameDataDir);
        var splashPath = Path.Combine(gameDataDir, "splash.bmp");
        var originalUserContent = "original-user-splash";
        File.WriteAllText(splashPath, originalUserContent);

        var files = new List<ManifestFile>
        {
            new()
            {
                RelativePath = "GeneralsOnlineGameData/splash.bmp",
                Hash = "hash-splash-expected",
                Size = 100,
                InstallTarget = ContentInstallTarget.UserDataDirectory,
            },
        };

        await _trackerService.InstallUserDataAsync(
            TestManifestId,
            "profile-cancel-activate-test",
            GameType.ZeroHour,
            files,
            TestVersion,
            TestManifestName,
            CancellationToken.None);

        await _trackerService.DeactivateProfileUserDataAsync("profile-cancel-activate-test", CancellationToken.None);
        Assert.Equal(originalUserContent, File.ReadAllText(splashPath));

        using var cts = new CancellationTokenSource();

        _fileOperationsMock
            .Setup(f => f.VerifyFileHashAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _fileOperationsMock
            .Setup(f => f.LinkFromCasAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<ContentType?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        _fileOperationsMock
            .Setup(f => f.CopyFromCasAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<ContentType?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _trackerService.ActivateProfileUserDataAsync("profile-cancel-activate-test", cts.Token));

        // Rollback restores user backup
        Assert.True(File.Exists(splashPath));
        Assert.Equal(originalUserContent, File.ReadAllText(splashPath));
    }

    /// <summary>
    /// Verifies that when deactivation is cancelled mid-loop, manifest remains active on disk so a retry completes remaining files.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task DeactivateProfileUserDataAsync_WhenCanceled_PreservesManifestActiveForRetryAsync()
    {
        // Arrange
        var files = new List<ManifestFile>
        {
            new()
            {
                RelativePath = "GeneralsOnlineGameData/file1.bmp",
                Hash = "hash-file1-expected",
                Size = 100,
                InstallTarget = ContentInstallTarget.UserDataDirectory,
            },
            new()
            {
                RelativePath = "GeneralsOnlineGameData/file2.bmp",
                Hash = "hash-file2-expected",
                Size = 100,
                InstallTarget = ContentInstallTarget.UserDataDirectory,
            },
        };

        await _trackerService.InstallUserDataAsync(
            TestManifestId,
            "profile-cancel-deactivate-test",
            GameType.ZeroHour,
            files,
            TestVersion,
            TestManifestName,
            CancellationToken.None);

        using var cts = new CancellationTokenSource();

        var verifiedCount = 0;
        _fileOperationsMock
            .Setup(f => f.VerifyFileHashAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns<string, string, CancellationToken>((path, hash, token) =>
            {
                if (Interlocked.Increment(ref verifiedCount) > 1)
                {
                    cts.Cancel();
                    return Task.FromException<bool>(new OperationCanceledException(cts.Token));
                }

                return Task.FromResult(true);
            });

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _trackerService.DeactivateProfileUserDataAsync("profile-cancel-deactivate-test", cts.Token));

        // Manifest remains active on disk to allow retry
        var profileData = await _trackerService.GetProfileUserDataAsync("profile-cancel-deactivate-test", CancellationToken.None);
        Assert.True(profileData.Success);
        Assert.Single(profileData.Data!);
        Assert.True(profileData.Data![0].IsActive);
    }

    /// <summary>
    /// Tests that installing a file targeted to UserMapsDirectory normalizes the path correctly and detects conflicts.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task InstallUserDataAsync_WithUserMapsDirectoryTarget_NormalizesPathAndDetectsConflictAsync()
    {
        // Arrange
        var files = new List<ManifestFile>
        {
            new()
            {
                RelativePath = "Maps/CustomMap/map.ini",
                Hash = "hash-custom-map",
                Size = 50,
                InstallTarget = ContentInstallTarget.UserMapsDirectory,
            },
        };

        // Act
        var result = await _trackerService.InstallUserDataAsync(
            "1.1015255.generalsonline.patch.custommap",
            "profile-map-test",
            GameType.ZeroHour,
            files,
            TestVersion,
            "Custom Map",
            CancellationToken.None);

        Assert.True(result.Success);

        var expectedPath = Path.Combine(_zeroHourDataDir, "Maps", "CustomMap", "map.ini");
        var conflictResult = await _trackerService.CheckFileConflictAsync(expectedPath);
        Assert.True(conflictResult.Success);
        Assert.Equal("1.1015255.generalsonline.patch.custommap_profile-map-test", conflictResult.Data);

        // Assert that a second installation from a different profile targeting the same path fails with conflict
        var conflictingResult = await _trackerService.InstallUserDataAsync(
            "1.1015255.generalsonline.patch.othermap",
            "profile-other-test",
            GameType.ZeroHour,
            files,
            TestVersion,
            "Other Map",
            CancellationToken.None);

        Assert.False(conflictingResult.Success);
        Assert.Contains("already managed by installation", conflictingResult.FirstError, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Tests that installing a manifest with a relative path escaping user data directory fails containment check without leaving partial artifacts.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task InstallUserDataAsync_WhenRelativePathEscapesUserDataDirectory_FailsAsync()
    {
        // Arrange
        var validFilePath = Path.Combine(_zeroHourDataDir, "GeneralsOnlineGameData", "valid.bmp");
        var files = new List<ManifestFile>
        {
            new()
            {
                RelativePath = "GeneralsOnlineGameData/valid.bmp",
                Hash = "hash-valid",
                Size = 100,
                InstallTarget = ContentInstallTarget.UserDataDirectory,
            },
            new()
            {
                RelativePath = "../../evil.ini",
                Hash = "hash-evil",
                Size = 10,
                InstallTarget = ContentInstallTarget.UserDataDirectory,
            },
        };

        // Act
        var result = await _trackerService.InstallUserDataAsync(
            "1.1015255.generalsonline.patch.evil",
            "profile-evil-test",
            GameType.ZeroHour,
            files,
            TestVersion,
            "Evil Patch",
            CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.False(File.Exists(validFilePath));
    }

    /// <summary>
    /// Verifies that when backup creation fails (e.g. file is locked), installation aborts to prevent data loss.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task InstallUserDataAsync_WhenBackupFails_AbortsInstallationToPreventDataLossAsync()
    {
        // Arrange
        var gameDataDir = Path.Combine(_zeroHourDataDir, "GeneralsOnlineGameData");
        Directory.CreateDirectory(gameDataDir);
        var existingSplashPath = Path.Combine(gameDataDir, "splash.bmp");
        var originalUserContent = "original-user-splash-cannot-backup";
        File.WriteAllText(existingSplashPath, originalUserContent);

        var files = new List<ManifestFile>
        {
            new()
            {
                RelativePath = "GeneralsOnlineGameData/splash.bmp",
                Hash = "hash-patch-splash",
                Size = 100,
                InstallTarget = ContentInstallTarget.UserDataDirectory,
            },
        };

        // Lock file with exclusive access so File.Copy fails inside BackupExistingFileAsync
        using (new System.IO.FileStream(existingSplashPath, System.IO.FileMode.Open, System.IO.FileAccess.ReadWrite, System.IO.FileShare.None))
        {
            // Act
            var result = await _trackerService.InstallUserDataAsync(
                TestManifestId,
                TestProfileId,
                GameType.ZeroHour,
                files,
                TestVersion,
                TestManifestName,
                CancellationToken.None);

            // Assert
            Assert.False(result.Success);
            Assert.Contains("Failed to create safety backup", result.FirstError, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Verifies that when a profile is deactivated, another profile can install the same user data files without encountering a conflict.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task InstallUserDataAsync_WhenPriorOwnerProfileIsDeactivated_SucceedsWithoutConflictAsync()
    {
        // Arrange
        var files = new List<ManifestFile>
        {
            new()
            {
                RelativePath = "Maps/Arabia v2/AdrianeMapSettings.ini",
                Hash = "hash-map-settings",
                Size = 500,
                InstallTarget = ContentInstallTarget.UserDataDirectory,
            },
        };

        // 1. Profile A installs the map pack
        var installA = await _trackerService.InstallUserDataAsync(
            "mappack-id",
            "profile-a",
            GameType.ZeroHour,
            files,
            "1.0",
            "Map Pack",
            CancellationToken.None);

        Assert.True(installA.Success);

        // 2. Profile A is deactivated
        var deactivateA = await _trackerService.DeactivateProfileUserDataAsync("profile-a", CancellationToken.None);
        Assert.True(deactivateA.Success);

        // 3. Profile B installs the same map pack
        var installB = await _trackerService.InstallUserDataAsync(
            "mappack-id",
            "profile-b",
            GameType.ZeroHour,
            files,
            "1.0",
            "Map Pack",
            CancellationToken.None);

        // Assert: Installation succeeds for profile B and ownership transfers
        Assert.True(installB.Success);

        var targetPath = Path.Combine(_zeroHourDataDir, "Maps", "Arabia v2", "AdrianeMapSettings.ini");
        Assert.True(File.Exists(targetPath));

        var conflictResult = await _trackerService.CheckFileConflictAsync(targetPath, CancellationToken.None);
        Assert.True(conflictResult.Success);
        Assert.Equal("mappack-id_profile-b", conflictResult.Data);

        var indexPath = Path.Combine(_appDataDir, DirectoryNames.UserData, FileTypes.UserDataIndexFileName);
        var indexJson = await File.ReadAllTextAsync(indexPath);
        var index = JsonSerializer.Deserialize<UserDataIndex>(indexJson);
        Assert.NotNull(index);
        Assert.True(index.FileToInstallationMap.TryGetValue(Path.GetFullPath(targetPath), out var ownerKey));
        Assert.Equal("mappack-id_profile-b", ownerKey);
    }

    /// <summary>
    /// Verifies that cleaning up an uninstalled or old profile does not delete files or prune mappings owned by a newer active profile.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task CleanupProfileAsync_WhenPriorOwnerProfileCleanedUpAfterTransfer_PreservesNewOwnerFilesAndIndexMappingAsync()
    {
        // Arrange
        var files = new List<ManifestFile>
        {
            new()
            {
                RelativePath = "Maps/TransferCheck/map.ini",
                Hash = "hash-transfer-test",
                Size = 300,
                InstallTarget = ContentInstallTarget.UserDataDirectory,
            },
        };

        // 1. Profile A installs the map pack
        var installA = await _trackerService.InstallUserDataAsync(
            "transfer-manifest",
            "profile-a",
            GameType.ZeroHour,
            files,
            "1.0",
            "Transfer Test",
            CancellationToken.None);
        Assert.True(installA.Success);

        // 2. Profile A is deactivated
        var deactivateA = await _trackerService.DeactivateProfileUserDataAsync("profile-a", CancellationToken.None);
        Assert.True(deactivateA.Success);

        // 3. Profile B installs the same map pack
        var installB = await _trackerService.InstallUserDataAsync(
            "transfer-manifest",
            "profile-b",
            GameType.ZeroHour,
            files,
            "1.0",
            "Transfer Test",
            CancellationToken.None);
        Assert.True(installB.Success);

        var targetPath = Path.Combine(_zeroHourDataDir, "Maps", "TransferCheck", "map.ini");
        Assert.True(File.Exists(targetPath));

        // 4. Profile A is cleaned up
        var cleanupA = await _trackerService.CleanupProfileAsync("profile-a", CancellationToken.None);
        Assert.True(cleanupA.Success);

        // Assert: Profile B's file and index mapping remain intact
        Assert.True(File.Exists(targetPath));

        var conflictResult = await _trackerService.CheckFileConflictAsync(targetPath, CancellationToken.None);
        Assert.True(conflictResult.Success);
        Assert.Equal("transfer-manifest_profile-b", conflictResult.Data);

        var indexPath = Path.Combine(_appDataDir, DirectoryNames.UserData, FileTypes.UserDataIndexFileName);
        var indexJson = await File.ReadAllTextAsync(indexPath);
        var index = JsonSerializer.Deserialize<UserDataIndex>(indexJson);
        Assert.NotNull(index);
        Assert.True(index.FileToInstallationMap.TryGetValue(Path.GetFullPath(targetPath), out var ownerKey));
        Assert.Equal("transfer-manifest_profile-b", ownerKey);
    }

    /// <summary>
    /// Verifies that when a file is temporarily missing on disk but its manifest is active, conflict checking still reports conflict.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task CheckFileConflictAsync_WhenFileMissingOnDiskButManifestActive_ReportsConflictAsync()
    {
        // Arrange
        var files = new List<ManifestFile>
        {
            new()
            {
                RelativePath = "Maps/TempMissing/map.ini",
                Hash = "hash-missing-test",
                Size = 100,
                InstallTarget = ContentInstallTarget.UserDataDirectory,
            },
        };

        var installResult = await _trackerService.InstallUserDataAsync(
            "missing-test-manifest",
            "profile-missing-test",
            GameType.ZeroHour,
            files,
            "1.0",
            "Missing Test",
            CancellationToken.None);

        Assert.True(installResult.Success);

        var targetPath = Path.Combine(_zeroHourDataDir, "Maps", "TempMissing", "map.ini");
        Assert.True(File.Exists(targetPath));

        // Temporarily delete the file from disk
        File.Delete(targetPath);
        Assert.False(File.Exists(targetPath));

        // Act
        var conflictResult = await _trackerService.CheckFileConflictAsync(targetPath, CancellationToken.None);

        // Assert: Conflict is still reported because the owning manifest is active
        Assert.True(conflictResult.Success);
        Assert.Equal("missing-test-manifest_profile-missing-test", conflictResult.Data);
    }

    /// <summary>
    /// Verifies that when a manifest is deactivated, CheckFileConflictAsync prunes the stale mapping and reports no conflict.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task CheckFileConflictAsync_WhenManifestDeactivated_PrunesStaleMappingAndReturnsNoConflictAsync()
    {
        // Arrange
        var files = new List<ManifestFile>
        {
            new()
            {
                RelativePath = "Maps/DeactivatedCheck/map.ini",
                Hash = "hash-deact-test",
                Size = 100,
                InstallTarget = ContentInstallTarget.UserDataDirectory,
            },
        };

        var installResult = await _trackerService.InstallUserDataAsync(
            "deact-test-manifest",
            "profile-deact-test",
            GameType.ZeroHour,
            files,
            "1.0",
            "Deact Test",
            CancellationToken.None);

        Assert.True(installResult.Success);

        var targetPath = Path.Combine(_zeroHourDataDir, "Maps", "DeactivatedCheck", "map.ini");

        // Deactivate the profile
        var deactivateResult = await _trackerService.DeactivateProfileUserDataAsync("profile-deact-test", CancellationToken.None);
        Assert.True(deactivateResult.Success);

        // Act
        var conflictResult = await _trackerService.CheckFileConflictAsync(targetPath, CancellationToken.None);

        // Assert: No conflict reported and stale mapping is pruned
        Assert.True(conflictResult.Success);
        Assert.Null(conflictResult.Data);

        // Verify index file persisted on disk no longer maps the path
        var indexPath = Path.Combine(_appDataDir, DirectoryNames.UserData, FileTypes.UserDataIndexFileName);
        var indexJson = await File.ReadAllTextAsync(indexPath);
        var index = JsonSerializer.Deserialize<UserDataIndex>(indexJson);
        Assert.NotNull(index);
        Assert.False(index.FileToInstallationMap.ContainsKey(Path.GetFullPath(targetPath)));
    }

    /// <summary>
    /// Verifies that multiple profiles can install the same map pack / user data manifest without encountering a conflict.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task InstallUserDataAsync_WhenMultipleProfilesInstallSameFileWithDifferentActiveStates_DoesNotFailDueToSharedActiveState()
    {
        var files = new List<ManifestFile>
        {
            new()
            {
                RelativePath = "Maps/Arabia v2/AdrianeMapSettings.ini",
                Hash = "hash-map-settings-shared",
                Size = 500,
                InstallTarget = ContentInstallTarget.UserDataDirectory,
            },
        };

        // 1. Profile A installs the map pack (and remains active)
        var installA = await _trackerService.InstallUserDataAsync(
            "1.813263.generalsonline.mappack.quickmatchmaps",
            "42b186b61cc8471583f6ca18c49ddd7c",
            GameType.ZeroHour,
            files,
            "1.0",
            "QuickMatch Maps",
            CancellationToken.None);

        Assert.True(installA.Success);

        // 2. Profile B installs the same map pack without prior deactivation of Profile A
        var installB = await _trackerService.InstallUserDataAsync(
            "1.813263.generalsonline.mappack.quickmatchmaps",
            "964412add8364ac5bf266568e32e45e4",
            GameType.ZeroHour,
            files,
            "1.0",
            "QuickMatch Maps",
            CancellationToken.None);

        // Assert: Both installations succeed without file conflict
        Assert.True(installB.Success);
        Assert.NotNull(installB.Data);
        Assert.Single(installB.Data.InstalledFiles);
    }

    /// <summary>
    /// Verifies that activating and deactivating profile user data tracks the active profile ID.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ActivateAndDeactivateProfileUserDataAsync_TracksActiveProfileIdAsync()
    {
        // Arrange
        var files = new List<ManifestFile>
        {
            new()
            {
                RelativePath = "Maps/ActiveTrackingTest/map.ini",
                Hash = "hash-active-track",
                Size = 250,
                InstallTarget = ContentInstallTarget.UserDataDirectory,
            },
        };

        await _trackerService.InstallUserDataAsync(
            "track-manifest",
            "profile-active-track",
            GameType.ZeroHour,
            files,
            "1.0",
            "Track Manifest",
            CancellationToken.None);

        // Act & Assert 1: Activate profile
        var activateResult = await _trackerService.ActivateProfileUserDataAsync("profile-active-track", CancellationToken.None);
        Assert.True(activateResult.Success);

        var activeIdResult1 = await _trackerService.GetActiveProfileIdAsync(CancellationToken.None);
        Assert.True(activeIdResult1.Success);
        Assert.Equal("profile-active-track", activeIdResult1.Data);

        // Act & Assert 2: Deactivate profile
        var deactivateResult = await _trackerService.DeactivateProfileUserDataAsync("profile-active-track", CancellationToken.None);
        Assert.True(deactivateResult.Success);

        var activeIdResult2 = await _trackerService.GetActiveProfileIdAsync(CancellationToken.None);
        Assert.True(activeIdResult2.Success);
        Assert.Null(activeIdResult2.Data);
    }

    /// <summary>
    /// Verifies that when CAS materialization throws an exception during install, original files are restored from backup.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task InstallUserDataAsync_WhenMaterializationThrowsException_RestoresOriginalFileAsync()
    {
        // Arrange
        var gameDataDir = Path.Combine(_zeroHourDataDir, "GeneralsOnlineGameData");
        Directory.CreateDirectory(gameDataDir);
        var splashPath = Path.Combine(gameDataDir, "splash.bmp");
        var originalUserContent = "original-user-splash-for-throw";
        File.WriteAllText(splashPath, originalUserContent);

        var files = new List<ManifestFile>
        {
            new()
            {
                RelativePath = "GeneralsOnlineGameData/splash.bmp",
                Hash = "throwhash123",
                Size = 100,
                InstallTarget = ContentInstallTarget.UserDataDirectory,
            },
        };

        _fileOperationsMock.Setup(f => f.LinkFromCasAsync("throwhash123", It.IsAny<string>(), true, null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("Simulated disk error"));
        _fileOperationsMock.Setup(f => f.CopyFromCasAsync("throwhash123", It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("Simulated disk error"));

        // Act
        var result = await _trackerService.InstallUserDataAsync(
            "1.1015255.generalsonline.patch.gamedata",
            "profile-fail-install-throw",
            GameType.ZeroHour,
            files,
            "101525_QFE5",
            "GameData Patch",
            CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.True(File.Exists(splashPath));
        Assert.Equal(originalUserContent, File.ReadAllText(splashPath));
    }

    /// <summary>
    /// Verifies that when CAS materialization throws an exception during activation, activated files are rolled back and original backups are restored.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ActivateUserDataManifestsAsync_WhenMaterializationThrowsException_RollsBackActivatedFilesAndRestoresBackupsAsync()
    {
        // Arrange
        var gameDataDir = Path.Combine(_zeroHourDataDir, "GeneralsOnlineGameData");
        Directory.CreateDirectory(gameDataDir);
        var splashPath = Path.Combine(gameDataDir, "splash.bmp");
        var originalUserContent = "original-user-splash-for-activation-throw";
        File.WriteAllText(splashPath, originalUserContent);

        var files = new List<ManifestFile>
        {
            new()
            {
                RelativePath = "GeneralsOnlineGameData/splash.bmp",
                Hash = "actthrowhash123",
                Size = 100,
                InstallTarget = ContentInstallTarget.UserDataDirectory,
            },
        };

        _fileOperationsMock.Setup(f => f.VerifyFileHashAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var installResult = await _trackerService.InstallUserDataAsync(
            "1.1015255.generalsonline.patch.gamedata",
            "profile-act-throw",
            GameType.ZeroHour,
            files,
            "101525_QFE5",
            "GameData Patch",
            CancellationToken.None);

        Assert.True(installResult.Success);

        // Now de-activate
        var deactivateResult = await _trackerService.DeactivateProfileUserDataAsync("profile-act-throw", CancellationToken.None);
        Assert.True(deactivateResult.Success);

        // On re-activation, the file is restored to original user content (doesn't match CAS hash), and materialization throws
        _fileOperationsMock.Setup(f => f.VerifyFileHashAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _fileOperationsMock.Setup(f => f.LinkFromCasAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<ContentType?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("Simulated activation disk error"));
        _fileOperationsMock.Setup(f => f.CopyFromCasAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ContentType?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("Simulated activation disk error"));

        // Act
        var activateResult = await _trackerService.ActivateProfileUserDataAsync("profile-act-throw", CancellationToken.None);

        // Assert
        Assert.False(activateResult.Success);
        Assert.True(File.Exists(splashPath));
        Assert.Equal(originalUserContent, File.ReadAllText(splashPath));
    }

    /// <summary>
    /// Verifies that installing the same manifest for a new profile adopts ownership of existing files without failing with conflict.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task InstallUserDataAsync_WhenSameManifestInstalledByAnotherProfile_AdoptsOwnershipAndSucceedsAsync()
    {
        // Arrange
        const string manifestId = "1.0.0.map.desertstorm";
        const string oldProfileId = "profile-primary";
        const string newProfileId = "profile-secondary";
        var mapPath = Path.Combine(_zeroHourDataDir, "Maps", "DesertStorm", "map.ini");

        var files = new List<ManifestFile>
        {
            new()
            {
                RelativePath = "Maps/DesertStorm/map.ini",
                Hash = "hash-desert",
                Size = 512,
                InstallTarget = ContentInstallTarget.UserMapsDirectory,
            },
        };

        _fileOperationsMock.Setup(f => f.VerifyFileHashAsync(It.IsAny<string>(), "hash-desert", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _fileOperationsMock.Setup(f => f.LinkFromCasAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<ContentType?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // 1. Primary profile installs map
        var firstResult = await _trackerService.InstallUserDataAsync(
            manifestId,
            oldProfileId,
            GameType.ZeroHour,
            files,
            "1.0.0",
            "Desert Storm",
            CancellationToken.None);

        Assert.True(firstResult.Success);

        // 2. Secondary profile adopts the same map (skipCleanup scenario)
        var secondResult = await _trackerService.InstallUserDataAsync(
            manifestId,
            newProfileId,
            GameType.ZeroHour,
            files,
            "1.0.0",
            "Desert Storm",
            CancellationToken.None);

        // Assert
        Assert.True(secondResult.Success);
        var conflictResult = await _trackerService.CheckFileConflictAsync(mapPath);
        Assert.True(conflictResult.Success);

        // Ownership transferred to new profile
        Assert.Equal($"{manifestId}_{newProfileId}", conflictResult.Data);

        // 3. Deactivating old profile must NOT delete the adopted file
        var deactResult = await _trackerService.DeactivateProfileUserDataAsync(oldProfileId, CancellationToken.None);
        Assert.True(deactResult.Success);
        Assert.True(File.Exists(mapPath));
    }

    /// <summary>
    /// Verifies that when an adopted file on disk has been modified and safety backup fails,
    /// installation aborts to prevent data loss and the modified file is preserved.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task InstallUserDataAsync_WhenAdoptedFileModifiedAndBackupFails_AbortsInstallationToPreventDataLossAsync()
    {
        // Arrange
        const string manifestId = "1.0.0.map.desertstorm.failbackup";
        const string oldProfileId = "profile-primary-fb";
        const string newProfileId = "profile-secondary-fb";
        var mapPath = Path.Combine(_zeroHourDataDir, "Maps", "DesertStormFailBackup", "map.ini");

        var files = new List<ManifestFile>
        {
            new()
            {
                RelativePath = "Maps/DesertStormFailBackup/map.ini",
                Hash = "hash-desert-original",
                Size = 512,
                InstallTarget = ContentInstallTarget.UserMapsDirectory,
            },
        };

        _fileOperationsMock.Setup(f => f.VerifyFileHashAsync(It.IsAny<string>(), "hash-desert-original", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _fileOperationsMock.Setup(f => f.LinkFromCasAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<ContentType?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // 1. Primary profile installs map
        var firstResult = await _trackerService.InstallUserDataAsync(
            manifestId,
            oldProfileId,
            GameType.ZeroHour,
            files,
            "1.0.0",
            "Desert Storm",
            CancellationToken.None);

        Assert.True(firstResult.Success);

        // User modifies the adopted file on disk, so hash verification fails
        _fileOperationsMock.Setup(f => f.VerifyFileHashAsync(mapPath, "hash-desert-original", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Lock the modified file exclusively so BackupExistingFileAsync fails
        using (new System.IO.FileStream(mapPath, System.IO.FileMode.Open, System.IO.FileAccess.ReadWrite, System.IO.FileShare.None))
        {
            // 2. Secondary profile attempts adoption
            var secondResult = await _trackerService.InstallUserDataAsync(
                manifestId,
                newProfileId,
                GameType.ZeroHour,
                files,
                "1.0.0",
                "Desert Storm",
                CancellationToken.None);

            // Assert
            Assert.False(secondResult.Success);
            Assert.Contains("Failed to create safety backup", secondResult.FirstError, StringComparison.OrdinalIgnoreCase);
        }

        Assert.True(File.Exists(mapPath));
    }

    /// <summary>
    /// Verifies that when an adoption target is indexed under an owner installation but missing
    /// from its manifest, installation aborts with failure to avoid inconsistent state.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task InstallUserDataAsync_WhenAdoptionTargetMissingFromOwnerManifest_AbortsInstallationAsync()
    {
        // Arrange
        const string manifestId = "1.0.0.map.desertstorm.missingmanifest";
        const string oldProfileId = "profile-owner-missing";
        const string newProfileId = "profile-adopter-missing";
        var mapPath = Path.Combine(_zeroHourDataDir, "Maps", "DesertStormMissing", "map.ini");

        var files = new List<ManifestFile>
        {
            new()
            {
                RelativePath = "Maps/DesertStormMissing/map.ini",
                Hash = "hash-desert-original",
                Size = 512,
                InstallTarget = ContentInstallTarget.UserMapsDirectory,
            },
        };

        // 1. Primary profile installs map
        var firstResult = await _trackerService.InstallUserDataAsync(
            manifestId,
            oldProfileId,
            GameType.ZeroHour,
            files,
            "1.0.0",
            "Desert Storm",
            CancellationToken.None);

        Assert.True(firstResult.Success);
        Assert.True(File.Exists(mapPath));

        // 2. Corrupt owner manifest by clearing its InstalledFiles list on disk while keeping file index entry
        var ownerKey = $"{manifestId}_{oldProfileId}";
        var manifestPath = Path.Combine(_appDataDir, DirectoryNames.UserData, DirectoryNames.UserDataManifests, $"{ownerKey}{FileTypes.UserDataManifestExtension}");
        Assert.True(File.Exists(manifestPath));

        var json = await File.ReadAllTextAsync(manifestPath);
        var manifestObj = JsonSerializer.Deserialize<UserDataManifest>(json);
        Assert.NotNull(manifestObj);
        manifestObj.InstalledFiles.Clear();
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifestObj));

        // 3. New profile attempts adoption of the indexed file
        var secondResult = await _trackerService.InstallUserDataAsync(
            manifestId,
            newProfileId,
            GameType.ZeroHour,
            files,
            "1.0.0",
            "Desert Storm",
            CancellationToken.None);

        // Assert
        Assert.False(secondResult.Success);
        Assert.Contains("missing from its manifest", secondResult.FirstError, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that when a target file already exists on disk untracked by any profile,
    /// a safety backup is created before overwriting it during installation.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task InstallUserDataAsync_WhenExistingUserFileUntracked_CreatesSafetyBackupAsync()
    {
        // Arrange
        const string manifestId = "1.0.0.map.desertstorm.untracked";
        const string profileId = "profile-untracked-user";
        var mapDir = Path.Combine(_zeroHourDataDir, "Maps", "DesertStormUntracked");
        var mapPath = Path.Combine(mapDir, "map.ini");

        Directory.CreateDirectory(mapDir);
        await File.WriteAllTextAsync(mapPath, "; existing untracked user map configuration");

        var files = new List<ManifestFile>
        {
            new()
            {
                RelativePath = "Maps/DesertStormUntracked/map.ini",
                Hash = "hash-desert-new",
                Size = 1024,
                InstallTarget = ContentInstallTarget.UserMapsDirectory,
            },
        };

        // Act
        var result = await _trackerService.InstallUserDataAsync(
            manifestId,
            profileId,
            GameType.ZeroHour,
            files,
            "1.0.0",
            "Desert Storm",
            CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        var entry = Assert.Single(result.Data.InstalledFiles);
        Assert.True(entry.WasOverwritten);
        Assert.NotNull(entry.BackupPath);
        Assert.True(File.Exists(entry.BackupPath));

        var backupContent = await File.ReadAllTextAsync(entry.BackupPath);
        Assert.Equal("; existing untracked user map configuration", backupContent);
    }

    /// <summary>Already matching files can be activated without changing immutable directory permissions.</summary>
    /// <returns>The asynchronous test.</returns>
    [Fact]
    public async Task ActivateProfileUserDataAsync_MatchingImmutableMap_DoesNotRequireWriteAccessAsync()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        const string profileId = "immutable-matching";
        var installed = await _trackerService.InstallUserDataAsync(TestManifestId, profileId, GameType.ZeroHour, [CreateMapFile("Matching.map", "matching-hash")], TestVersion, TestManifestName, CancellationToken.None);
        Assert.True(installed.Success, installed.FirstError);
        var deactivated = await _trackerService.DeactivateProfileUserDataAsync(profileId, CancellationToken.None);
        Assert.True(deactivated.Success, deactivated.FirstError);
        var mapDir = Path.Combine(_zeroHourDataDir, "Maps", "Matching");
        Directory.CreateDirectory(mapDir);
        await File.WriteAllTextAsync(Path.Combine(mapDir, "Matching.map"), "cas-content-matching-hash");
        ReadOnlyFolderFixtures.MakeReadOnly(mapDir);
        ReadOnlyFolderFixtures.LockImmutable(mapDir);
        _fileOperationsMock.Setup(f => f.VerifyFileHashAsync(It.IsAny<string>(), "matching-hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var activated = await _trackerService.ActivateProfileUserDataAsync(profileId, CancellationToken.None);

        Assert.True(activated.Success, activated.FirstError);
        _fileOperationsMock.Verify(f => f.VerifyFileHashAsync(It.IsAny<string>(), "matching-hash", It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        Assert.True(ReadOnlyFolderFixtures.IsReadOnly(mapDir));
    }

    private static ManifestFile CreateMapFile(string relativePath, string hash) => new()
    {
        RelativePath = relativePath,
        Hash = hash,
        Size = 100,
        InstallTarget = ContentInstallTarget.UserMapsDirectory,
    };
}
