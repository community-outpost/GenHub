using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.GameProfiles;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
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
    private readonly Mock<IGameProfileRepository> _profileRepositoryMock = new();
    private readonly Mock<IContentManifestPool> _manifestPoolMock = new();
    private readonly Mock<IGameInstallationService> _installationServiceMock = new();
    private readonly Mock<IContentOrchestrator> _contentOrchestratorMock = new();
    private readonly PublisherManifestFactoryResolver _factoryResolver;
    private readonly HttpClient _httpClient = new();
    private readonly ProfileSharingService _service;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProfileSharingServiceTests"/> class.
    /// </summary>
    public ProfileSharingServiceTests()
    {
        _factoryResolver = new PublisherManifestFactoryResolver([], NullLogger<PublisherManifestFactoryResolver>.Instance);
        _service = new ProfileSharingService(
            _profileRepositoryMock.Object,
            _manifestPoolMock.Object,
            _installationServiceMock.Object,
            _contentOrchestratorMock.Object,
            _factoryResolver,
            _httpClient,
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
        Assert.StartsWith("genhub://profile/import?data=", result.Data, StringComparison.OrdinalIgnoreCase);
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

        var json = JsonSerializer.Serialize(package);
        var encoded = ProfileSharingCompressionHelper.CompressAndEncode(json);
        var uri = $"genhub://profile/import?data={encoded}";

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
        Assert.Equal("Generals Online Ranked (Imported)", inspection.SuggestedProfileName);
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
    /// Verifies Discord markdown template generation with title and URI.
    /// </summary>
    [Fact]
    public void GenerateDiscordMarkdown_Should_ProduceFormattedMarkdown()
    {
        // Arrange
        var profile = new GameProfile
        {
            Id = "p-1",
            Name = "Generals Pro Match",
            Description = "Play with 144Hz limit and widescreen support",
            GameClient = new GameClient { Name = "Generals", Version = "1.08", GameType = GameType.Generals },
        };
        var shareUri = "genhub://profile/import?data=TEST_PAYLOAD";

        // Act
        var markdown = _service.GenerateDiscordMarkdown(profile, shareUri);

        // Assert
        Assert.Contains("Generals Pro Match", markdown);
        Assert.Contains("Generals 1.08", markdown);
        Assert.Contains("Play with 144Hz limit", markdown);
        Assert.Contains(shareUri, markdown);
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
