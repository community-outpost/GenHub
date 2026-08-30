using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Services;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.GameProfiles;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Core.Models.Tools;
using GenHub.Core.Models.Tools.UploadThing;
using GenHub.Features.Content.Services.Publishers;
using GenHub.Features.GameProfiles.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.GameProfiles;

/// <summary>
/// Unit tests for <see cref="ProfileSharingService"/>.
/// </summary>
public class ProfileSharingServiceTests
{
    private static readonly JsonSerializerOptions TestJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly Mock<IGameProfileRepository> _profileRepositoryMock = new();
    private readonly Mock<IContentManifestPool> _manifestPoolMock = new();
    private readonly Mock<IGameInstallationService> _installationServiceMock = new();
    private readonly Mock<IContentOrchestrator> _contentOrchestratorMock = new();
    private readonly PublisherManifestFactoryResolver _factoryResolver;
    private readonly ProfileSharingService _service;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProfileSharingServiceTests"/> class.
    /// </summary>
    public ProfileSharingServiceTests()
    {
        _profileRepositoryMock.Setup(r => r.LoadAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([]));
        _installationServiceMock.Setup(i => i.GetAllInstallationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<GameInstallation>>.CreateSuccess([]));

        _factoryResolver = new PublisherManifestFactoryResolver([], NullLogger<PublisherManifestFactoryResolver>.Instance);
        _service = new ProfileSharingService(
            _profileRepositoryMock.Object,
            _manifestPoolMock.Object,
            _installationServiceMock.Object,
            _contentOrchestratorMock.Object,
            _factoryResolver,
            NullLogger<ProfileSharingService>.Instance);
    }

    /// <summary>
    /// Verifies that exporting a valid profile produces a properly formatted genhub:// URI.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task ExportProfileToUriAsync_Should_ReturnValidUri_WhenProfileExistsAsync()
    {
        // Arrange
        var profile = CreateTestProfile("profile-1", "Generals Online Ranked");
        _profileRepositoryMock.Setup(r => r.LoadProfileAsync("profile-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));

        var manifest = CreateTestManifest("1.0.generalsonline.gameclient.generalsonline", "Generals Online", ContentType.GameClient);
        _manifestPoolMock.Setup(m => m.GetManifestAsync("1.0.generalsonline.gameclient.generalsonline", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(manifest));

        // Act
        var result = await _service.ExportProfileToUriAsync("profile-1");

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.StartsWith($"{CommandLineConstants.ProfileImportUriPrefix}?{CommandLineConstants.DataQueryParam}", result.Data, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that exporting a non-existent profile returns a failure.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task ExportProfileToUriAsync_Should_ReturnFailure_WhenProfileDoesNotExistAsync()
    {
        // Arrange
        _profileRepositoryMock.Setup(r => r.LoadProfileAsync("unknown", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateFailure("Not found"));

        // Act
        var result = await _service.ExportProfileToUriAsync("unknown");

        // Assert
        Assert.False(result.Success);
        Assert.Contains("not found", result.FirstError, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that exporting to a file creates the .ghprofile file with valid JSON content.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task ExportProfileToFileAsync_Should_WriteJsonFile_SuccessfullyAsync()
    {
        // Arrange
        var profile = CreateTestProfile("profile-file-1", "Shockwave Export");
        _profileRepositoryMock.Setup(r => r.LoadProfileAsync("profile-file-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));

        var tempPath = Path.Combine(Path.GetTempPath(), $"test_profile_{Guid.NewGuid():N}.ghprofile");

        try
        {
            // Act
            var result = await _service.ExportProfileToFileAsync("profile-file-1", tempPath);

            // Assert
            Assert.True(result.Success);
            Assert.True(File.Exists(tempPath));
            var text = await File.ReadAllTextAsync(tempPath);
            Assert.Contains("Shockwave Export", text);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    /// <summary>
    /// Verifies that inspecting a URI returns correct manifest cache status, installation matching, and suggested name.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task InspectSharedProfileAsync_Should_ItemizeDependenciesAndCheckInstallationsAsync()
    {
        // Arrange
        var package = new SharedGameProfilePackage
        {
            SchemaVersion = 1,
            Profile = new SharedProfileMetadata
            {
                Name = "Generals Online Ranked",
                GameType = GameType.ZeroHour,
                GameVersion = "1.04",
                CommandLineArguments = "-win -quickstart & malicious_command",
            },
            RequiredManifests =
            [
                new SharedManifestDependency
                {
                    ManifestId = "1.0.community.mod.cachedmod",
                    DisplayName = "Cached Mod",
                    Version = "1.0",
                    ContentType = ContentType.Mod,
                },
                new SharedManifestDependency
                {
                    ManifestId = "1.0.community.mod.missingmod",
                    DisplayName = "Missing Mod",
                    Version = "2.0",
                    ContentType = ContentType.Mod,
                    DownloadSize = 50 * 1024 * 1024,
                },
            ],
        };

        var json = JsonSerializer.Serialize(package, TestJsonOptions);
        var encoded = ProfileSharingCompressionHelper.CompressAndEncode(json);
        var uri = $"{CommandLineConstants.ProfileImportUriPrefix}?{CommandLineConstants.DataQueryParam}{encoded}";

        _manifestPoolMock.Setup(m => m.IsManifestAcquiredAsync("1.0.community.mod.cachedmod", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));
        _manifestPoolMock.Setup(m => m.IsManifestAcquiredAsync("1.0.community.mod.missingmod", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(false));

        var installation = new GameInstallation("/games/zerohour", GameInstallationType.Steam)
        {
            Id = "inst-1",
            HasZeroHour = true,
            AvailableGameClients =
            [
                new GameClient { Id = "c1", Name = "Zero Hour", GameType = GameType.ZeroHour },
            ],
        };

        _installationServiceMock.Setup(i => i.GetAllInstallationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<GameInstallation>>.CreateSuccess(new List<GameInstallation> { installation }));

        var existingProfile = new GameProfile { Id = "p-existing", Name = "Generals Online Ranked" };
        _profileRepositoryMock.Setup(r => r.LoadAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess(new List<GameProfile> { existingProfile }));

        // Act
        var inspectResult = await _service.InspectSharedProfileAsync(uri);

        // Assert
        Assert.True(inspectResult.Success);
        var inspection = inspectResult.Data;
        Assert.NotNull(inspection);
        Assert.Equal(1, inspection.CachedManifestCount);
        Assert.Equal(1, inspection.MissingManifestCount);
        Assert.Equal(50 * 1024 * 1024, inspection.TotalDownloadBytesRequired);
        Assert.True(inspection.HasValidGameInstallation);
        Assert.Equal("inst-1", inspection.MatchedGameInstallationId);
        Assert.True(inspection.HasNameConflict);
        Assert.Equal("Generals Online Ranked (1)", inspection.SuggestedProfileName);
        Assert.NotEmpty(inspection.SecurityWarnings);
    }

    /// <summary>
    /// Verifies that importing a profile creates and saves the new GameProfile.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task ImportSharedProfileAsync_Should_CreateAndSaveProfileAsync()
    {
        // Arrange
        var package = new SharedGameProfilePackage
        {
            SchemaVersion = 1,
            Profile = new SharedProfileMetadata
            {
                Name = "Shared Profile",
                GameType = GameType.ZeroHour,
                GameVersion = "1.04",
                CommandLineArguments = "-win",
            },
            RequiredManifests =
            [
                new SharedManifestDependency
                {
                    ManifestId = "1.0.community.mod.testmod",
                    DisplayName = "Mod 1",
                    Version = "1.0",
                    ContentType = ContentType.Mod,
                },
            ],
        };

        var installation = new GameInstallation("/games/zh", GameInstallationType.Retail)
        {
            Id = "inst-1",
            HasZeroHour = true,
            AvailableGameClients =
            [
                new GameClient { Id = "client-zh", Name = "Zero Hour", GameType = GameType.ZeroHour },
            ],
        };

        _installationServiceMock.Setup(i => i.GetInstallationAsync("inst-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<GameInstallation>.CreateSuccess(installation));

        _manifestPoolMock.Setup(m => m.IsManifestAcquiredAsync("1.0.community.mod.testmod", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        _profileRepositoryMock.Setup(r => r.SaveProfileAsync(It.IsAny<GameProfile>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GameProfile p, CancellationToken ct) => ProfileOperationResult<GameProfile>.CreateSuccess(p));

        var request = new SharedProfileImportRequest
        {
            Package = package,
            ProfileName = "My Imported Profile",
            GameInstallationId = "inst-1",
            IncludeGameSettings = true,
        };

        // Act
        var importResult = await _service.ImportSharedProfileAsync(request);

        // Assert
        Assert.True(importResult.Success);
        Assert.NotNull(importResult.Data);
        Assert.Equal("My Imported Profile", importResult.Data.Name);
        Assert.Equal("inst-1", importResult.Data.GameInstallationId);
        _profileRepositoryMock.Verify(r => r.SaveProfileAsync(It.Is<GameProfile>(p => p.Name == "My Imported Profile"), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that inspecting a package with unsupported schema version returns failure.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task InspectSharedProfileAsync_Should_ReturnFailure_WhenSchemaVersionIsUnsupportedAsync()
    {
        // Arrange
        var package = new SharedGameProfilePackage
        {
            SchemaVersion = 999,
            Profile = new SharedProfileMetadata { Name = "Future Profile", GameType = GameType.ZeroHour },
            RequiredManifests = [],
        };
        var json = JsonSerializer.Serialize(package, TestJsonOptions);

        // Act
        var result = await _service.InspectSharedProfileAsync(json);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Unsupported package schema version", result.FirstError);
    }

    /// <summary>
    /// Verifies that inspecting a package with invalid manifest ID returns failure.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task InspectSharedProfileAsync_Should_ReturnFailure_WhenManifestIdIsInvalidAsync()
    {
        // Arrange
        var package = new SharedGameProfilePackage
        {
            SchemaVersion = ProfileSharingConstants.DefaultSchemaVersion,
            Profile = new SharedProfileMetadata { Name = "Invalid Manifest Profile", GameType = GameType.ZeroHour },
            RequiredManifests =
            [
                new SharedManifestDependency { ManifestId = "invalid-manifest-id-without-dots", DisplayName = "Invalid", Version = "1.0", ContentType = ContentType.Mod }
            ],
        };
        var json = JsonSerializer.Serialize(package, TestJsonOptions);

        // Act
        var result = await _service.InspectSharedProfileAsync(json);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Invalid manifest identifier", result.FirstError);
    }

    /// <summary>
    /// Verifies that importing with a profile name exceeding 100 characters returns failure.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task ImportSharedProfileAsync_Should_ReturnFailure_WhenProfileNameExceedsMaxLengthAsync()
    {
        // Arrange
        var package = new SharedGameProfilePackage
        {
            SchemaVersion = ProfileSharingConstants.DefaultSchemaVersion,
            Profile = new SharedProfileMetadata { Name = "Valid Name", GameType = GameType.ZeroHour },
            RequiredManifests = [],
        };
        var request = new SharedProfileImportRequest
        {
            Package = package,
            ProfileName = new string('A', 101),
            GameInstallationId = "inst-1",
        };

        // Act
        var result = await _service.ImportSharedProfileAsync(request);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Profile name must be between 1 and 100 characters", result.FirstError);
    }

    /// <summary>
    /// Verifies that GameInstallation manifests are excluded from exported package RequiredManifests.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task ExportProfile_Should_FilterOutGameInstallationManifestsAsync()
    {
        // Arrange
        var profile = CreateTestProfile("profile-export-filter", "Filter Test");
        profile.EnabledContentIds =
        [
            "1.0.generalsonline.gameclient.generalsonline",
            "1.104.steam.gameinstallation.zerohour",
        ];

        _profileRepositoryMock.Setup(r => r.LoadProfileAsync("profile-export-filter", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));

        var clientManifest = CreateTestManifest("1.0.generalsonline.gameclient.generalsonline", "Generals Online", ContentType.GameClient);
        var installManifest = CreateTestManifest("1.104.steam.gameinstallation.zerohour", "Zero Hour", ContentType.GameInstallation);

        _manifestPoolMock.Setup(m => m.GetManifestAsync("1.0.generalsonline.gameclient.generalsonline", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(clientManifest));
        _manifestPoolMock.Setup(m => m.GetManifestAsync("1.104.steam.gameinstallation.zerohour", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(installManifest));

        // Act
        var uriResult = await _service.ExportProfileToUriAsync("profile-export-filter");

        // Assert
        Assert.True(uriResult.Success);
        Assert.NotNull(uriResult.Data);

        var dataParam = uriResult.Data.Replace($"{CommandLineConstants.ProfileImportUriPrefix}?{CommandLineConstants.DataQueryParam}", string.Empty);
        var json = ProfileSharingCompressionHelper.DecodeAndDecompress(dataParam);
        var package = JsonSerializer.Deserialize<SharedGameProfilePackage>(json, TestJsonOptions);

        Assert.NotNull(package);
        Assert.Single(package.RequiredManifests);

        Assert.Equal("1.0.generalsonline.gameclient.generalsonline", package.RequiredManifests[0].ManifestId);
        Assert.DoesNotContain(package.RequiredManifests, m => m.ManifestId == "1.104.steam.gameinstallation.zerohour");
    }

    /// <summary>
    /// Verifies that inspecting a package ignores any legacy GameInstallation manifests so they do not count towards download sizes.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task InspectSharedProfile_Should_IgnoreGameInstallationDependencies_FromDownloadTotalsAsync()
    {
        // Arrange
        var package = new SharedGameProfilePackage
        {
            SchemaVersion = ProfileSharingConstants.DefaultSchemaVersion,
            Profile = new SharedProfileMetadata
            {
                Name = "Zero Hour Package",
                GameType = GameType.ZeroHour,
                GameVersion = "1.04",
            },
            RequiredManifests =
            [
                new SharedManifestDependency
                {
                    ManifestId = "1.104.steam.gameinstallation.zerohour",
                    DisplayName = "Zero Hour Steam Installation",
                    Version = "1.04",
                    ContentType = ContentType.GameInstallation,
                    DownloadSize = 2_750_000_000L,
                },
                new SharedManifestDependency
                {
                    ManifestId = "1.0.community.mod.testmod",
                    DisplayName = "Test Mod",
                    Version = "1.0",
                    ContentType = ContentType.Mod,
                    DownloadSize = 1_000_000L,
                }
            ],
        };

        var json = JsonSerializer.Serialize(package, TestJsonOptions);
        _manifestPoolMock.Setup(m => m.IsManifestAcquiredAsync("1.0.community.mod.testmod", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(false));

        var installation = new GameInstallation("/games/zh", GameInstallationType.EaApp)
        {
            Id = "inst-ea",
            HasZeroHour = true,
            AvailableGameClients = [new GameClient { Id = "c1", Name = "Zero Hour", GameType = GameType.ZeroHour }],
        };

        _installationServiceMock.Setup(i => i.GetAllInstallationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<GameInstallation>>.CreateSuccess(new List<GameInstallation> { installation }));

        // Act
        var result = await _service.InspectSharedProfileAsync(json);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Single(result.Data.Manifests);
        Assert.Equal("1.0.community.mod.testmod", result.Data.Manifests[0].ManifestId);
        Assert.Equal(1_000_000L, result.Data.TotalDownloadBytesRequired);
        Assert.Equal(1, result.Data.MissingManifestCount);
    }

    /// <summary>
    /// Verifies that importing a profile attaches the target user's local GameInstallation manifest ID to EnabledContentIds.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task ImportSharedProfile_Should_AttachTargetGameInstallationManifestId_ToImportedProfileAsync()
    {
        // Arrange
        var package = new SharedGameProfilePackage
        {
            SchemaVersion = ProfileSharingConstants.DefaultSchemaVersion,
            Profile = new SharedProfileMetadata
            {
                Name = "Shared Mod Profile",
                GameType = GameType.ZeroHour,
                GameVersion = "1.04",
            },
            RequiredManifests =
            [
                new SharedManifestDependency
                {
                    ManifestId = "1.0.community.mod.testmod",
                    DisplayName = "Test Mod",
                    Version = "1.0",
                    ContentType = ContentType.Mod,
                }
            ],
        };

        var installation = new GameInstallation("/games/steam/zh", GameInstallationType.Steam)
        {
            Id = "inst-steam",
            HasZeroHour = true,
            AvailableGameClients =
            [
                new GameClient { Id = "zh-client", Name = "Zero Hour", Version = "1.04", GameType = GameType.ZeroHour }
            ],
        };

        _installationServiceMock.Setup(i => i.GetInstallationAsync("inst-steam", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<GameInstallation>.CreateSuccess(installation));

        _manifestPoolMock.Setup(m => m.IsManifestAcquiredAsync("1.0.community.mod.testmod", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        GameProfile? savedProfile = null;
        _profileRepositoryMock.Setup(r => r.SaveProfileAsync(It.IsAny<GameProfile>(), It.IsAny<CancellationToken>()))
            .Callback<GameProfile, CancellationToken>((p, _) => savedProfile = p)
            .ReturnsAsync((GameProfile p, CancellationToken _) => ProfileOperationResult<GameProfile>.CreateSuccess(p));

        var request = new SharedProfileImportRequest
        {
            Package = package,
            ProfileName = "Imported Steam ZH Profile",
            GameInstallationId = "inst-steam",
        };

        // Act
        var result = await _service.ImportSharedProfileAsync(request);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(savedProfile);
        Assert.Contains("1.0.community.mod.testmod", savedProfile.EnabledContentIds);
        Assert.Contains(savedProfile.EnabledContentIds, id => id.Contains(".steam.gameinstallation.zerohour", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that importing fails when no game installation can be resolved for the request.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task ImportSharedProfileAsync_Should_ReturnFailure_WhenNoInstallationResolvedAsync()
    {
        // Arrange
        var package = new SharedGameProfilePackage
        {
            SchemaVersion = ProfileSharingConstants.DefaultSchemaVersion,
            Profile = new SharedProfileMetadata { Name = "No Install Profile", GameType = GameType.ZeroHour },
            RequiredManifests = [],
        };
        var request = new SharedProfileImportRequest
        {
            Package = package,
            ProfileName = "My Imported Profile",
            GameInstallationId = null!,
        };

        // Act
        var result = await _service.ImportSharedProfileAsync(request);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("No compatible game installation", result.FirstError);
    }

    /// <summary>
    /// Verifies that acquiring a missing dependency with no embedded files and no matching
    /// remote content fails instead of silently registering an empty placeholder manifest.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task ImportSharedProfileAsync_Should_Fail_WhenDependencyCannotBeAcquiredAsync()
    {
        // Arrange
        var package = new SharedGameProfilePackage
        {
            SchemaVersion = ProfileSharingConstants.DefaultSchemaVersion,
            Profile = new SharedProfileMetadata { Name = "Missing Dep Profile", GameType = GameType.ZeroHour },
            RequiredManifests =
            [
                new SharedManifestDependency
                {
                    ManifestId = "1.0.community.mod.ghostmod",
                    DisplayName = "Ghost Mod",
                    Version = "1.0",
                    ContentType = ContentType.Mod,
                },
            ],
        };

        var installation = new GameInstallation("/games/zh", GameInstallationType.Retail)
        {
            Id = "inst-1",
            HasZeroHour = true,
            AvailableGameClients = [new GameClient { Id = "c1", Name = "Zero Hour", GameType = GameType.ZeroHour }],
        };

        _installationServiceMock.Setup(i => i.GetInstallationAsync("inst-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<GameInstallation>.CreateSuccess(installation));
        _manifestPoolMock.Setup(m => m.IsManifestAcquiredAsync("1.0.community.mod.ghostmod", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(false));
        _contentOrchestratorMock.Setup(o => o.SearchAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess([]));

        var request = new SharedProfileImportRequest
        {
            Package = package,
            ProfileName = "My Imported Profile",
            GameInstallationId = "inst-1",
        };

        // Act
        var result = await _service.ImportSharedProfileAsync(request);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Ghost Mod", result.FirstError);
        _manifestPoolMock.Verify(m => m.AddManifestAsync(It.IsAny<ContentManifest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies that when a shared manifest dependency has unpacked files without direct download URLs
    /// (e.g. ModDB, CnCLabs, AoDMaps), the service invokes ContentOrchestrator to discover and acquire the package.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task ImportSharedProfileAsync_Should_AcquireViaContentOrchestrator_WhenFilesLackDownloadUrlsAsync()
    {
        // Arrange
        const string manifestId = "1.0.moddb.mod.contra009";
        var package = new SharedGameProfilePackage
        {
            SchemaVersion = ProfileSharingConstants.DefaultSchemaVersion,
            Profile = new SharedProfileMetadata { Name = "Contra Profile", GameType = GameType.ZeroHour },
            RequiredManifests =
            [
                new SharedManifestDependency
                {
                    ManifestId = manifestId,
                    DisplayName = "Contra 009 Final",
                    Version = "0.09",
                    ContentType = ContentType.Mod,
                    Publisher = "ModDB",
                    PublisherType = PublisherTypeConstants.ModDB,
                    Files =
                    [
                        new ManifestFile { RelativePath = "Contra.big", Size = 500_000_000, Hash = "abc123" },
                        new ManifestFile { RelativePath = "!contra009.ini", Size = 100_000, Hash = "def456" },
                    ],
                },
            ],
        };

        var installation = new GameInstallation("/games/zh", GameInstallationType.Retail)
        {
            Id = "inst-1",
            HasZeroHour = true,
            AvailableGameClients = [new GameClient { Id = "c1", Name = "Zero Hour", GameType = GameType.ZeroHour }],
        };

        var searchResult = new ContentSearchResult
        {
            Id = manifestId,
            Name = "Contra 009 Final",
            ProviderName = "ModDB",
            ContentType = ContentType.Mod,
        };

        _installationServiceMock.Setup(i => i.GetInstallationAsync("inst-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<GameInstallation>.CreateSuccess(installation));
        _manifestPoolMock.Setup(m => m.IsManifestAcquiredAsync(manifestId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(false));
        _contentOrchestratorMock.Setup(o => o.SearchAsync(It.Is<ContentSearchQuery>(q => q.SearchTerm == "Contra 009 Final"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess([searchResult]));
        _contentOrchestratorMock.Setup(o => o.AcquireContentAsync(searchResult, It.IsAny<IProgress<ContentAcquisitionProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest>.CreateSuccess(new ContentManifest { Id = ManifestId.Create(manifestId) }));
        _profileRepositoryMock.Setup(r => r.SaveProfileAsync(It.IsAny<GameProfile>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GameProfile p, CancellationToken _) => ProfileOperationResult<GameProfile>.CreateSuccess(p));

        var request = new SharedProfileImportRequest
        {
            Package = package,
            ProfileName = "Contra Imported",
            GameInstallationId = "inst-1",
        };

        // Act
        var result = await _service.ImportSharedProfileAsync(request);

        // Assert
        Assert.True(result.Success);
        _contentOrchestratorMock.Verify(o => o.SearchAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()), Times.Once);
        _contentOrchestratorMock.Verify(o => o.AcquireContentAsync(searchResult, It.IsAny<IProgress<ContentAcquisitionProgress>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that machine-specific artwork paths are stripped when a profile is packaged for sharing.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task ExportProfile_Should_StripLocalArtworkPaths_FromSharedPackageAsync()
    {
        // Arrange
        var profile = CreateTestProfile("profile-artwork", "Artwork Profile");
        profile.IconPath = @"C:\Users\someone\Pictures\icon.png";
        profile.CoverPath = "covers/relative-cover.png";

        _profileRepositoryMock.Setup(r => r.LoadProfileAsync("profile-artwork", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));

        // Act
        var uriResult = await _service.ExportProfileToUriAsync("profile-artwork");

        // Assert
        Assert.True(uriResult.Success);
        Assert.NotNull(uriResult.Data);

        var dataParam = uriResult.Data.Replace($"{CommandLineConstants.ProfileImportUriPrefix}?{CommandLineConstants.DataQueryParam}", string.Empty);
        var json = ProfileSharingCompressionHelper.DecodeAndDecompress(dataParam);
        var package = JsonSerializer.Deserialize<SharedGameProfilePackage>(json, TestJsonOptions);

        Assert.NotNull(package);
        Assert.Null(package.Profile.IconPath);
        Assert.Equal("covers/relative-cover.png", package.Profile.CoverPath);
    }

    /// <summary>
    /// Verifies that Unix absolute, UNC, and traversal artwork paths are stripped from shared packages.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task ExportProfile_Should_StripUnixAndTraversalArtworkPaths_FromSharedPackageAsync()
    {
        // Arrange
        var profile = CreateTestProfile("profile-artwork-unix", "Artwork Profile Unix");
        profile.IconPath = "/home/user/pictures/icon.png";
        profile.CoverPath = "../traversal/cover.png";

        _profileRepositoryMock.Setup(r => r.LoadProfileAsync("profile-artwork-unix", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));

        // Act
        var uriResult = await _service.ExportProfileToUriAsync("profile-artwork-unix");

        // Assert
        Assert.True(uriResult.Success);
        Assert.NotNull(uriResult.Data);

        var dataParam = uriResult.Data.Replace($"{CommandLineConstants.ProfileImportUriPrefix}?{CommandLineConstants.DataQueryParam}", string.Empty);
        var json = ProfileSharingCompressionHelper.DecodeAndDecompress(dataParam);
        var package = JsonSerializer.Deserialize<SharedGameProfilePackage>(json, TestJsonOptions);

        Assert.NotNull(package);
        Assert.Null(package.Profile.IconPath);
        Assert.Null(package.Profile.CoverPath);
    }

    /// <summary>
    /// Verifies that inspecting a remote URI targeting loopback or private addresses is rejected by SSRF protection.
    /// </summary>
    /// <param name="blockedUrl">The blocked URL to test.</param>
    /// <returns>A task representing the test.</returns>
    [Theory]
    [InlineData("http://127.0.0.1/profile.json")]
    [InlineData("http://localhost/profile.json")]
    [InlineData("http://169.254.169.254/latest/meta-data")]
    [InlineData("http://10.0.0.1/profile.json")]
    [InlineData("http://192.168.1.1/profile.json")]
    [InlineData("http://172.16.0.1/profile.json")]
    public async Task InspectSharedProfileAsync_Should_ReturnFailure_WhenUriTargetsBlockedIpAddressAsync(string blockedUrl)
    {
        // Arrange
        var shareUri = $"{CommandLineConstants.ProfileImportUriPrefix}?url={Uri.EscapeDataString(blockedUrl)}";

        // Act
        var result = await _service.InspectSharedProfileAsync(shareUri);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("blocked", result.FirstError, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that importing a package with a null required manifests list returns failure.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task ImportSharedProfileAsync_Should_ReturnFailure_WhenManifestsListIsNullAsync()
    {
        // Arrange
        var package = new SharedGameProfilePackage
        {
            SchemaVersion = 1,
            Profile = new SharedProfileMetadata
            {
                Name = "Null Manifests Profile",
                GameType = GameType.ZeroHour,
                GameVersion = "1.04",
            },
            RequiredManifests = null!,
        };

        var request = new SharedProfileImportRequest
        {
            Package = package,
            ProfileName = "Imported Null Manifests",
            GameInstallationId = "inst-1",
        };

        // Act
        var result = await _service.ImportSharedProfileAsync(request);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("manifest", result.FirstError, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that exporting a profile containing local content packages and uploads the component via UploadThing.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task ExportProfileToUriAsync_Should_UploadLocalContentToUploadThing_WhenLocalManifestExistsAsync()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, "sample mod content");
        var localProfile = CreateTestProfile("local-profile-1", "Local Modded Setup");
        localProfile.EnabledContentIds = ["1.0.local.mod.custommod"];

        var casMock = new Mock<ICasService>();
        var uploadThingMock = new Mock<IUploadThingService>();
        var uploadHistoryMock = new Mock<IUploadHistoryService>();

        casMock.Setup(c => c.GetContentPathAsync(It.IsAny<string>(), It.IsAny<ContentType>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<string>.CreateSuccess(tempFile));

        uploadThingMock.Setup(u => u.UploadFileAsync(It.IsAny<string>(), It.IsAny<IProgress<double>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<UploadResult>.CreateSuccess(new UploadResult("https://utfs.io/f/testupload.zip", "key123", "token123")));

        var localManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.local.mod.custommod"),
            Name = "Custom Mod",
            Version = "1.0",
            ContentType = ContentType.Mod,
            Publisher = new PublisherInfo
            {
                Name = "GenHub (Local)",
                PublisherType = PublisherTypeConstants.Local,
            },
            Files =
            [
                new ManifestFile
                {
                    RelativePath = "Data/INI/GameData.ini",
                    Hash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
                    Size = 100,
                },
            ],
        };

        _profileRepositoryMock.Setup(r => r.LoadProfileAsync("local-profile-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(localProfile));

        _manifestPoolMock.Setup(m => m.GetManifestAsync("1.0.local.mod.custommod", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(localManifest));

        var serviceWithUpload = new ProfileSharingService(
            _profileRepositoryMock.Object,
            _manifestPoolMock.Object,
            _installationServiceMock.Object,
            _contentOrchestratorMock.Object,
            _factoryResolver,
            NullLogger<ProfileSharingService>.Instance,
            casMock.Object,
            uploadThingMock.Object,
            uploadHistoryMock.Object);

        try
        {
            // Act
            var result = await serviceWithUpload.ExportProfileToUriAsync("local-profile-1");

            // Assert
            Assert.True(result.Success);
            Assert.NotNull(result.Data);
            uploadThingMock.Verify(u => u.UploadFileAsync(It.IsAny<string>(), It.IsAny<IProgress<double>>(), It.IsAny<CancellationToken>()), Times.Once);
            uploadHistoryMock.Verify(h => h.RecordUpload(It.IsAny<long>(), "https://utfs.io/f/testupload.zip", It.IsAny<string>(), "key123", "token123", It.IsAny<string>(), ProfileSharingConstants.UploadCategoryProfiles), Times.Once);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    /// <summary>
    /// Verifies that inspecting a URI with modern UploadThing UFS URL correctly identifies cloud package dependencies.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task InspectSharedProfileAsync_Should_RecognizeModernUfsCloudPackagesAsync()
    {
        // Arrange
        var package = new SharedGameProfilePackage
        {
            SchemaVersion = 1,
            Profile = new SharedProfileMetadata
            {
                Name = "UFS Profile",
                GameType = GameType.ZeroHour,
                GameVersion = "1.04",
            },
            RequiredManifests =
            [
                new SharedManifestDependency
                {
                    ManifestId = "1.0.local.map.custommap",
                    DisplayName = "Custom Map",
                    Version = "1.0",
                    ContentType = ContentType.Map,
                    PackageUrl = "https://50ea2z8yuk.ufs.sh/f/testkey12345",
                    DownloadSize = 500000,
                },
            ],
        };

        var json = JsonSerializer.Serialize(package, TestJsonOptions);
        var encoded = ProfileSharingCompressionHelper.CompressAndEncode(json);
        var uri = $"{CommandLineConstants.ProfileImportUriPrefix}?{CommandLineConstants.DataQueryParam}{encoded}";

        _manifestPoolMock.Setup(m => m.IsManifestAcquiredAsync("1.0.local.map.custommap", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(false));

        // Act
        var result = await _service.InspectSharedProfileAsync(uri);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Single(result.Data.Manifests);
        Assert.Equal("https://50ea2z8yuk.ufs.sh/f/testkey12345", result.Data.Manifests[0].PackageUrl);
    }

    /// <summary>
    /// Verifies that provider manifests (like GeneralsOnline) are never uploaded to UploadThing even if they lack individual file download URLs.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ExportProfileToUriAsync_Should_NotUploadProviderContentToUploadThingAsync()
    {
        // Arrange
        var profile = CreateTestProfile("provider-profile-1", "Generals Online Profile");
        profile.EnabledContentIds = ["1.82826.generalsonline.gameclient.60hz"];

        var casMock = new Mock<ICasService>();
        var uploadThingMock = new Mock<IUploadThingService>();
        var uploadHistoryMock = new Mock<IUploadHistoryService>();

        var providerManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.82826.generalsonline.gameclient.60hz"),
            Name = "Generals Online 60Hz",
            Version = "1.82826",
            ContentType = ContentType.GameClient,
            Publisher = new PublisherInfo
            {
                Name = "Generals Online",
                PublisherType = PublisherTypeConstants.GeneralsOnline,
            },
            Files =
            [
                new ManifestFile
                {
                    RelativePath = "generals.exe",
                    Hash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
                    Size = 5000000,
                    DownloadUrl = null, // Provider manifests in CAS don't store per-file download URLs
                },
            ],
        };

        _profileRepositoryMock.Setup(r => r.LoadProfileAsync("provider-profile-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));

        _manifestPoolMock.Setup(m => m.GetManifestAsync("1.82826.generalsonline.gameclient.60hz", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(providerManifest));

        var service = new ProfileSharingService(
            _profileRepositoryMock.Object,
            _manifestPoolMock.Object,
            _installationServiceMock.Object,
            _contentOrchestratorMock.Object,
            _factoryResolver,
            NullLogger<ProfileSharingService>.Instance,
            casMock.Object,
            uploadThingMock.Object,
            uploadHistoryMock.Object);

        // Act
        var result = await service.ExportProfileToUriAsync("provider-profile-1");

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        uploadThingMock.Verify(u => u.UploadFileAsync(It.IsAny<string>(), It.IsAny<IProgress<double>>(), It.IsAny<CancellationToken>()), Times.Never);
        uploadHistoryMock.Verify(h => h.RecordUpload(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    /// <summary>
    /// Verifies that import reconstructs manifest from existing CAS objects without downloading.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ImportSharedProfileAsync_Should_ReuseExistingCasBlobs_WithoutDownloadingAsync()
    {
        // Arrange
        var tempCasFile = Path.GetTempFileName();
        File.WriteAllText(tempCasFile, "cas content");

        var manifestId = ManifestId.Create("1.0.community.mod.testmod");
        var hash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

        var package = new SharedGameProfilePackage
        {
            Profile = new SharedProfileMetadata
            {
                Name = "Shared CAS Profile",
                GameType = GameType.ZeroHour,
                GameVersion = "1.04",
            },
            RequiredManifests =
            [
                new SharedManifestDependency
                {
                    ManifestId = manifestId.Value,
                    DisplayName = "Test Mod",
                    Version = "1.0",
                    ContentType = ContentType.Mod,
                    Files =
                    [
                        new ManifestFile
                        {
                            RelativePath = "Data/test.ini",
                            Hash = hash,
                            Size = 100,
                        },
                    ],
                },
            ],
        };

        var casMock = new Mock<ICasService>();
        casMock.Setup(c => c.GetContentPathAsync(hash, ContentType.Mod, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<string>.CreateSuccess(tempCasFile));

        // IsManifestAcquired returns false initially
        _manifestPoolMock.Setup(m => m.IsManifestAcquiredAsync(manifestId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(false));

        _manifestPoolMock.Setup(m => m.AddManifestAsync(It.IsAny<ContentManifest>(), It.IsAny<string>(), It.IsAny<IProgress<ContentStorageProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        _installationServiceMock.Setup(i => i.GetInstallationAsync("inst-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<GameInstallation>.CreateSuccess(new GameInstallation("/games/zh", GameInstallationType.Retail)
            {
                Id = "inst-1",
                HasZeroHour = true,
                AvailableGameClients =
                [
                    new GameClient { Id = "client-zh", Name = "Zero Hour", GameType = GameType.ZeroHour },
                ],
            }));

        _profileRepositoryMock.Setup(r => r.SaveProfileAsync(It.IsAny<GameProfile>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(new GameProfile { Id = "prof-new", Name = "Shared CAS Profile" }));

        var service = new ProfileSharingService(
            _profileRepositoryMock.Object,
            _manifestPoolMock.Object,
            _installationServiceMock.Object,
            _contentOrchestratorMock.Object,
            _factoryResolver,
            NullLogger<ProfileSharingService>.Instance,
            casMock.Object);

        try
        {
            // Act
            var request = new SharedProfileImportRequest
            {
                Package = package,
                ProfileName = "Shared CAS Profile",
                GameInstallationId = "inst-1",
            };
            var result = await service.ImportSharedProfileAsync(request);

            // Assert
            Assert.True(result.Success);
            _manifestPoolMock.Verify(
                m => m.AddManifestAsync(
                    It.Is<ContentManifest>(cm => cm.Id == manifestId && cm.Files.All(f => f.SourceType == ContentSourceType.ContentAddressable)),
                    It.IsAny<string>(),
                    It.IsAny<IProgress<ContentStorageProgress>>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
            _contentOrchestratorMock.Verify(c => c.AcquireContentAsync(It.IsAny<ContentSearchResult>(), It.IsAny<IProgress<ContentAcquisitionProgress>>(), It.IsAny<CancellationToken>()), Times.Never);
        }
        finally
        {
            if (File.Exists(tempCasFile))
            {
                File.Delete(tempCasFile);
            }
        }
    }

    /// <summary>
    /// Verifies that importing local content with Windows-style backslashes and subdirectories normalizes paths and preserves file properties.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ImportSharedProfileAsync_Should_NormalizeBackslashPathsAndPreserveFilePropertiesAsync()
    {
        // Arrange
        var tempCasFile = Path.GetTempFileName();
        File.WriteAllText(tempCasFile, "map content");

        var manifestId = ManifestId.Create("1.0.local.mod.bsm-defcon-51-v3");
        var hash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

        var package = new SharedGameProfilePackage
        {
            Profile = new SharedProfileMetadata
            {
                Name = "Local Map Profile",
                GameType = GameType.ZeroHour,
                GameVersion = "1.04",
            },
            RequiredManifests =
            [
                new SharedManifestDependency
                {
                    ManifestId = manifestId.Value,
                    DisplayName = "[BSM] Defcon 51 V3",
                    Version = "1.0",
                    ContentType = ContentType.Mod,
                    Files =
                    [
                        new ManifestFile
                        {
                            RelativePath = @"[BSM] Defcon 51 V3\map.ini",
                            Hash = hash,
                            Size = 100,
                            InstallTarget = ContentInstallTarget.UserMapsDirectory,
                            IsRequired = true,
                        },
                        new ManifestFile
                        {
                            RelativePath = @"[BSM] Defcon 51 V3\map.str",
                            Hash = hash,
                            Size = 50,
                            InstallTarget = ContentInstallTarget.UserMapsDirectory,
                        },
                    ],
                },
            ],
        };

        var casMock = new Mock<ICasService>();
        casMock.Setup(c => c.GetContentPathAsync(hash, ContentType.Mod, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<string>.CreateSuccess(tempCasFile));

        _manifestPoolMock.Setup(m => m.IsManifestAcquiredAsync(manifestId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(false));

        ContentManifest? capturedManifest = null;
        _manifestPoolMock.Setup(m => m.AddManifestAsync(It.IsAny<ContentManifest>(), It.IsAny<string>(), It.IsAny<IProgress<ContentStorageProgress>>(), It.IsAny<CancellationToken>()))
            .Callback<ContentManifest, string?, IProgress<ContentStorageProgress>?, CancellationToken>((cm, _, _, _) => capturedManifest = cm)
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        _installationServiceMock.Setup(i => i.GetInstallationAsync("inst-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<GameInstallation>.CreateSuccess(new GameInstallation("/games/zh", GameInstallationType.Retail)
            {
                Id = "inst-1",
                HasZeroHour = true,
                AvailableGameClients =
                [
                    new GameClient { Id = "client-zh", Name = "Zero Hour", GameType = GameType.ZeroHour },
                ],
            }));

        _profileRepositoryMock.Setup(r => r.SaveProfileAsync(It.IsAny<GameProfile>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(new GameProfile { Id = "prof-new", Name = "Local Map Profile" }));

        var service = new ProfileSharingService(
            _profileRepositoryMock.Object,
            _manifestPoolMock.Object,
            _installationServiceMock.Object,
            _contentOrchestratorMock.Object,
            _factoryResolver,
            NullLogger<ProfileSharingService>.Instance,
            casMock.Object);

        try
        {
            // Act
            var request = new SharedProfileImportRequest
            {
                Package = package,
                ProfileName = "Local Map Profile",
                GameInstallationId = "inst-1",
            };
            var result = await service.ImportSharedProfileAsync(request);

            // Assert
            Assert.True(result.Success);
            Assert.NotNull(capturedManifest);
            Assert.Equal(2, capturedManifest.Files.Count);
            Assert.All(capturedManifest.Files, f =>
            {
                Assert.NotEqual(ContentSourceType.Unknown, f.SourceType);
                Assert.Equal(ContentSourceType.ContentAddressable, f.SourceType);
                Assert.Equal(ContentInstallTarget.UserMapsDirectory, f.InstallTarget);
            });
        }
        finally
        {
            if (File.Exists(tempCasFile))
            {
                File.Delete(tempCasFile);
            }
        }
    }

    /// <summary>
    /// Verifies that exporting a profile containing local files preserves all metadata fields including non-unknown source types.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ExportProfileToUriAsync_Should_PreserveManifestFileProperties_ForLocalContentAsync()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, "sample content");

        var localProfile = CreateTestProfile("local-profile-map", "Map Profile");
        localProfile.EnabledContentIds = ["1.0.local.map.defcon"];

        var casMock = new Mock<ICasService>();
        var uploadThingMock = new Mock<IUploadThingService>();
        var uploadHistoryMock = new Mock<IUploadHistoryService>();

        casMock.Setup(c => c.GetContentPathAsync(It.IsAny<string>(), It.IsAny<ContentType>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<string>.CreateSuccess(tempFile));

        uploadThingMock.Setup(u => u.UploadFileAsync(It.IsAny<string>(), It.IsAny<IProgress<double>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<UploadResult>.CreateSuccess(new UploadResult("https://utfs.io/f/maptest.zip", "key123", "token123")));

        var localManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.local.map.defcon"),
            Name = "Defcon Map",
            Version = "1.0",
            ContentType = ContentType.Map,
            Publisher = new PublisherInfo
            {
                Name = "GenHub (Local)",
                PublisherType = PublisherTypeConstants.Local,
            },
            Files =
            [
                new ManifestFile
                {
                    RelativePath = @"Defcon/map.ini",
                    Hash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
                    Size = 100,
                    InstallTarget = ContentInstallTarget.UserMapsDirectory,
                    SourceType = ContentSourceType.ContentAddressable,
                    IsRequired = true,
                },
            ],
        };

        _profileRepositoryMock.Setup(r => r.LoadProfileAsync("local-profile-map", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(localProfile));

        _manifestPoolMock.Setup(m => m.GetManifestAsync("1.0.local.map.defcon", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(localManifest));

        var service = new ProfileSharingService(
            _profileRepositoryMock.Object,
            _manifestPoolMock.Object,
            _installationServiceMock.Object,
            _contentOrchestratorMock.Object,
            _factoryResolver,
            NullLogger<ProfileSharingService>.Instance,
            casMock.Object,
            uploadThingMock.Object,
            uploadHistoryMock.Object);

        try
        {
            // Act
            var result = await service.ExportProfileToUriAsync("local-profile-map");

            // Assert
            Assert.True(result.Success);
            Assert.NotNull(result.Data);

            var dataParam = result.Data.Replace($"{CommandLineConstants.ProfileImportUriPrefix}?{CommandLineConstants.DataQueryParam}", string.Empty);
            var json = ProfileSharingCompressionHelper.DecodeAndDecompress(dataParam);
            var package = JsonSerializer.Deserialize<SharedGameProfilePackage>(json, TestJsonOptions);

            Assert.NotNull(package);
            var dep = Assert.Single(package.RequiredManifests);
            var file = Assert.Single(dep.Files);
            Assert.Equal(ContentSourceType.ContentAddressable, file.SourceType);
            Assert.Equal(ContentInstallTarget.UserMapsDirectory, file.InstallTarget);
            Assert.True(file.IsRequired);
            Assert.Equal("https://utfs.io/f/maptest.zip", file.DownloadUrl);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    /// <summary>
    /// Verifies that TargetGame is correctly preserved in shared manifest dependencies when exported.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task ExportProfileToPackageAsync_PreservesTargetGame_InSharedDependenciesAsync()
    {
        // Arrange
        var profile = CreateTestProfile("profile-targetgame-1", "ZH Profile");
        _profileRepositoryMock.Setup(r => r.LoadProfileAsync("profile-targetgame-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));

        var manifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.local.map.defcon"),
            Name = "Defcon Map",
            Version = "1.0",
            ContentType = ContentType.Map,
            TargetGame = GameType.ZeroHour,
            Publisher = new PublisherInfo
            {
                Name = "Local",
                PublisherType = PublisherTypeConstants.Local,
            },
        };
        profile.EnabledContentIds = ["1.0.local.map.defcon"];
        _manifestPoolMock.Setup(m => m.GetManifestAsync("1.0.local.map.defcon", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(manifest));

        // Act
        var jsonResult = await _service.ExportProfileToJsonAsync("profile-targetgame-1");
        Assert.True(jsonResult.Success);
        Assert.NotNull(jsonResult.Data);

        var package = JsonSerializer.Deserialize<SharedGameProfilePackage>(jsonResult.Data, TestJsonOptions);

        // Assert
        Assert.NotNull(package);
        var dependency = Assert.Single(package.RequiredManifests);
        Assert.Equal(GameType.ZeroHour, dependency.TargetGame);
    }

    /// <summary>
    /// Verifies that built-in application icons and cover art paths are preserved during profile export.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task ExportProfileToUriAsync_Should_PreserveBuiltInIconsAndCoversAsync()
    {
        // Arrange
        var profile = CreateTestProfile("artwork-profile-1", "Artwork Profile");
        profile.IconPath = "/Assets/Icons/zerohour-icon.png";
        profile.CoverPath = "avares://GenHub/Assets/Covers/zerohour-cover.png";

        _profileRepositoryMock.Setup(r => r.LoadProfileAsync("artwork-profile-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));

        _manifestPoolMock.Setup(m => m.GetManifestAsync(It.IsAny<ManifestId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(CreateTestManifest("1.0.generalsonline.gameclient.generalsonline", "ZH", ContentType.GameClient)));

        // Act
        var result = await _service.ExportProfileToUriAsync("artwork-profile-1");

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);

        var inspectResult = await _service.InspectSharedProfileAsync(result.Data);
        Assert.True(inspectResult.Success);
        Assert.NotNull(inspectResult.Data);
        Assert.Equal("/Assets/Icons/zerohour-icon.png", inspectResult.Data.ProfileMetadata.IconPath);
        Assert.Equal("avares://GenHub/Assets/Covers/zerohour-cover.png", inspectResult.Data.ProfileMetadata.CoverPath);
    }

    /// <summary>
    /// Verifies that exporting multiple profiles with the exact same local content component reuses existing cloud upload without re-uploading.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task ExportProfileToUriAsync_MultipleProfilesWithSameLocalContent_ReusesExistingCloudUploadAsync()
    {
        // Arrange
        var profile1 = CreateTestProfile("profile-1", "Profile 1");
        profile1.EnabledContentIds = ["1.0.local.map.defcon51"];

        var profile2 = CreateTestProfile("profile-2", "Profile 2");
        profile2.EnabledContentIds = ["1.0.local.map.defcon51"];

        var tempDir = Path.Combine(Path.GetTempPath(), "ProfileSharingDedupeTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var tempFile = Path.Combine(tempDir, "map.ini");
        await File.WriteAllTextAsync(tempFile, "Map Content Data");

        var localManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.local.map.defcon51"),
            Name = "[BSM] Defcon 51 V3",
            Version = "1.0",
            ContentType = ContentType.Map,
            Publisher = new PublisherInfo
            {
                Name = PublisherTypeConstants.Local,
                PublisherType = PublisherTypeConstants.Local,
            },
            Files =
            [
                new ManifestFile
                {
                    RelativePath = "Maps/Defcon51/map.ini",
                    Hash = "fakehash123",
                    Size = 16,
                },
            ],
        };

        var casMock = new Mock<ICasService>();
        casMock.Setup(c => c.GetContentPathAsync("fakehash123", ContentType.Map, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<string>.CreateSuccess(tempFile));

        var uploadThingMock = new Mock<IUploadThingService>();
        uploadThingMock.Setup(u => u.UploadFileAsync(It.IsAny<string>(), It.IsAny<IProgress<double>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<UploadResult>.CreateSuccess(new UploadResult("https://utfs.io/f/defcon51.zip", "key123", "token123")));

        string? savedHash = null;
        var uploadHistoryMock = new Mock<IUploadHistoryService>();
        uploadHistoryMock.Setup(h => h.FindExistingUploadAsync(It.IsAny<string>()))
            .ReturnsAsync((string hash) => savedHash != null && savedHash == hash
                ? new UploadRecord { FileHash = hash, Url = "https://utfs.io/f/defcon51.zip" }
                : null);

        uploadHistoryMock.Setup(h => h.RecordUpload(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback<long, string, string, string?, string?, string?, string?>((size, url, name, key, token, hash, cat) =>
            {
                savedHash = hash;
            });

        _profileRepositoryMock.Setup(r => r.LoadProfileAsync("profile-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile1));
        _profileRepositoryMock.Setup(r => r.LoadProfileAsync("profile-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile2));

        _manifestPoolMock.Setup(m => m.GetManifestAsync("1.0.local.map.defcon51", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(localManifest));

        var service = new ProfileSharingService(
            _profileRepositoryMock.Object,
            _manifestPoolMock.Object,
            _installationServiceMock.Object,
            _contentOrchestratorMock.Object,
            _factoryResolver,
            NullLogger<ProfileSharingService>.Instance,
            casMock.Object,
            uploadThingMock.Object,
            uploadHistoryMock.Object);

        try
        {
            // Act: Export Profile 1 -> should upload to cloud once
            var result1 = await service.ExportProfileToUriAsync("profile-1");
            Assert.True(result1.Success);

            // Act: Export Profile 2 with identical map -> should reuse existing upload without uploading
            var result2 = await service.ExportProfileToUriAsync("profile-2");
            Assert.True(result2.Success);

            // Assert: Upload was called exactly ONCE across both profile exports
            uploadThingMock.Verify(u => u.UploadFileAsync(It.IsAny<string>(), It.IsAny<IProgress<double>>(), It.IsAny<CancellationToken>()), Times.Once);
            uploadHistoryMock.Verify(h => h.RecordUpload(It.IsAny<long>(), "https://utfs.io/f/defcon51.zip", "[BSM] Defcon 51 V3.zip", "key123", "token123", It.IsAny<string>(), ProfileSharingConstants.UploadCategoryProfiles), Times.Once);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    private static GameProfile CreateTestProfile(string id, string name)
    {
        return new GameProfile
        {
            Id = id,
            Name = name,
            Description = "Test profile description",
            EnabledContentIds = ["1.0.generalsonline.gameclient.generalsonline"],
            GameClient = new GameClient
            {
                Id = "1.0.generalsonline.gameclient.generalsonline",
                Name = "Generals Online",
                Version = "1.0",
                GameType = GameType.ZeroHour,
            },
            VideoResolutionWidth = 1920,
            VideoResolutionHeight = 1080,
            VideoWindowed = true,
        };
    }

    private static ContentManifest CreateTestManifest(string id, string name, ContentType type)
    {
        return new ContentManifest
        {
            Id = ManifestId.Create(id),
            Name = name,
            Version = "1.0",
            ContentType = type,
            Publisher = new PublisherInfo
            {
                Name = "Community",
                PublisherType = PublisherTypeConstants.GeneralsOnline,
            },
        };
    }
}
