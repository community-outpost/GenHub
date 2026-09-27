using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.GameSettings;
using GenHub.Core.Interfaces.Launching;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Storage;
using GenHub.Features.Content.Services.Publishers;
using GenHub.Features.GameProfiles.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.GameProfiles.Services;

/// <summary>
/// Verifies that a shared profile imported onto a local installation references the installation
/// manifest that is actually pooled, so the imported profile passes launch validation.
/// </summary>
public sealed class ProfileSharingImportInstallationManifestTests
{
    private const string InstallationId = "inst-tsh-native";
    private const string PooledInstallationManifestId = "1.104.genhublocal.gameinstallation.zerohour";
    private const string UnknownVersionInstallationManifestId = "1.0.genhublocal.gameinstallation.zerohour";
    private const string ClientManifestId = "1.0.thesuperhackers.gameclient.zerohour";

    private static readonly string InstallationPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "GenHubImportTests", "zerohour");

    private static readonly JsonSerializerOptions PackageJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly Dictionary<string, ContentManifest> _pool = new(StringComparer.OrdinalIgnoreCase);
    private readonly Mock<IContentManifestPool> _manifestPoolMock = new();
    private readonly Mock<IGameProfileRepository> _profileRepositoryMock = new();
    private readonly Mock<IGameInstallationService> _installationServiceMock = new();
    private readonly Mock<IGameProfileManager> _profileManagerMock = new();
    private readonly Mock<ICasService> _casServiceMock = new();
    private readonly Mock<IGameLaunchRunner> _launchRunnerMock = new();
    private GameProfile? _savedProfile;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProfileSharingImportInstallationManifestTests"/> class.
    /// </summary>
    public ProfileSharingImportInstallationManifestTests()
    {
        _manifestPoolMock
            .Setup(m => m.GetManifestAsync(It.IsAny<ManifestId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManifestId id, CancellationToken _) =>
                OperationResult<ContentManifest?>.CreateSuccess(_pool.GetValueOrDefault(id.Value)));
        _manifestPoolMock
            .Setup(m => m.SearchManifestsAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContentSearchQuery query, CancellationToken _) =>
                OperationResult<IEnumerable<ContentManifest>>.CreateSuccess(
                    _pool.Values
                        .Where(m => m.ContentType == query.ContentType && m.TargetGame == query.TargetGame)
                        .ToList()));
        _manifestPoolMock
            .Setup(m => m.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => OperationResult<IEnumerable<ContentManifest>>.CreateSuccess(_pool.Values.ToList()));
        _manifestPoolMock
            .Setup(m => m.IsManifestAcquiredAsync(It.IsAny<ManifestId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManifestId id, CancellationToken _) => OperationResult<bool>.CreateSuccess(_pool.ContainsKey(id.Value)));

        _profileRepositoryMock
            .Setup(r => r.LoadAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([]));
        _profileRepositoryMock
            .Setup(r => r.SaveProfileAsync(It.IsAny<GameProfile>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GameProfile profile, CancellationToken _) =>
            {
                _savedProfile = profile;
                return ProfileOperationResult<GameProfile>.CreateSuccess(profile);
            });
        _profileManagerMock
            .Setup(p => p.GetProfileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, CancellationToken _) => _savedProfile?.Id == id
                ? ProfileOperationResult<GameProfile>.CreateSuccess(_savedProfile)
                : ProfileOperationResult<GameProfile>.CreateFailure("Profile not found"));

        _casServiceMock
            .Setup(c => c.GetStatsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CasStats { ObjectCount = 1, TotalSize = 1, SpaceSaved = 0 });
        _launchRunnerMock.Setup(r => r.CanLaunchWindowsExecutables()).Returns(true);
    }

    /// <summary>
    /// Verifies that importing onto an installation whose client reports an Unknown version references
    /// the pooled default-version installation manifest, and that the imported profile passes launch validation.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task ImportSharedProfileAsync_WithUnknownClientVersion_ProducesLaunchableProfileAsync()
    {
        PoolInstallationManifest(PooledInstallationManifestId, GameInstallationType.Custom);
        PoolClientManifest();
        SetUpInstallation(GameClientConstants.UnknownVersion);

        using var sharingService = CreateSharingService();
        var importResult = await sharingService.ImportSharedProfileAsync(CreateImportRequest());

        Assert.True(importResult.Success, importResult.FirstError);
        var profile = Assert.IsType<GameProfile>(importResult.Data);
        Assert.Contains(PooledInstallationManifestId, profile.EnabledContentIds);
        Assert.DoesNotContain(UnknownVersionInstallationManifestId, profile.EnabledContentIds);

        var validation = await CreateFacade().ValidateLaunchAsync(profile.Id);

        Assert.True(validation.Success, string.Join("; ", validation.Errors));
    }

    /// <summary>
    /// Verifies that when the id derived from the client is not pooled, import uses the pooled
    /// manifest for the same installation type and game instead of a dangling id.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task ImportSharedProfileAsync_WhenDerivedIdIsNotPooled_UsesPooledManifestForSameInstallationTypeAsync()
    {
        PoolInstallationManifest(UnknownVersionInstallationManifestId, GameInstallationType.Custom);
        PoolInstallationManifest("1.104.steam.gameinstallation.zerohour", GameInstallationType.Steam);
        PoolClientManifest();
        SetUpInstallation("1.04");

        using var sharingService = CreateSharingService();
        var importResult = await sharingService.ImportSharedProfileAsync(CreateImportRequest());

        Assert.True(importResult.Success, importResult.FirstError);
        var profile = Assert.IsType<GameProfile>(importResult.Data);
        Assert.Contains(UnknownVersionInstallationManifestId, profile.EnabledContentIds);
        Assert.DoesNotContain("1.104.steam.gameinstallation.zerohour", profile.EnabledContentIds);
        Assert.DoesNotContain(PooledInstallationManifestId, profile.EnabledContentIds);
    }

    /// <summary>A deterministic ID collision cannot attach another installation's manifest.</summary>
    /// <param name="hasMatchingFallback">Whether a verified fallback is available.</param>
    /// <returns>The asynchronous test.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImportSharedProfileAsync_ExpectedIdBelongsToOtherInstallation_RequiresMatchingSourceAsync(bool hasMatchingFallback)
    {
        PoolInstallationManifest(PooledInstallationManifestId, GameInstallationType.Custom, System.IO.Path.Combine(System.IO.Path.GetTempPath(), "other-installation"));
        if (hasMatchingFallback)
        {
            PoolInstallationManifest(UnknownVersionInstallationManifestId, GameInstallationType.Custom);
        }

        PoolClientManifest();
        SetUpInstallation("1.04");
        using var sharingService = CreateSharingService();

        var result = await sharingService.ImportSharedProfileAsync(CreateImportRequest());

        Assert.Equal(hasMatchingFallback, result.Success);
        if (hasMatchingFallback)
        {
            Assert.Contains(UnknownVersionInstallationManifestId, result.Data!.EnabledContentIds);
            Assert.DoesNotContain(PooledInstallationManifestId, result.Data.EnabledContentIds);
        }
        else
        {
            Assert.Null(_savedProfile);
            Assert.Contains("Rescan", result.FirstError);
        }
    }

    /// <summary>A verified expected manifest takes priority over an unrelated or unidentified candidate.</summary>
    /// <param name="sourcePath">The pooled manifest's source directory.</param>
    /// <returns>The asynchronous test.</returns>
    [Theory]
    [InlineData("/games/other-installation")]
    [InlineData(null)]
    [InlineData("relative-installation")]
    public async Task ImportSharedProfileAsync_VerifiedExpectedManifest_TakesPriorityOverUnrelatedCandidateAsync(string? sourcePath)
    {
        if (sourcePath == "/games/other-installation")
        {
            sourcePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "other-installation");
        }

        PoolInstallationManifest(UnknownVersionInstallationManifestId, GameInstallationType.Custom, sourcePath);
        PoolInstallationManifest(PooledInstallationManifestId, GameInstallationType.Custom);
        PoolClientManifest();
        SetUpInstallation("1.04");
        using var sharingService = CreateSharingService();

        var result = await sharingService.ImportSharedProfileAsync(CreateImportRequest());

        Assert.True(result.Success, result.FirstError);
        Assert.Contains(PooledInstallationManifestId, result.Data!.EnabledContentIds);
        Assert.DoesNotContain(UnknownVersionInstallationManifestId, result.Data.EnabledContentIds);
    }

    /// <summary>
    /// Verifies that with no installation manifest pooled, import still uses the default-version id
    /// the installation service registers under rather than a version-zero id.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task ImportSharedProfileAsync_WhenNothingIsPooled_UsesDefaultVersionIdAsync()
    {
        SetUpInstallation(GameClientConstants.UnknownVersion);

        using var sharingService = CreateSharingService();
        var importResult = await sharingService.ImportSharedProfileAsync(CreateImportRequest());

        Assert.True(importResult.Success, importResult.FirstError);
        var profile = Assert.IsType<GameProfile>(importResult.Data);
        Assert.Contains(PooledInstallationManifestId, profile.EnabledContentIds);
        Assert.DoesNotContain(UnknownVersionInstallationManifestId, profile.EnabledContentIds);
    }

    /// <summary>
    /// Verifies that inspection warns only about dependencies import cannot acquire. Curated
    /// dependencies without URLs are resolved through content providers during import.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task InspectSharedProfileAsync_WarnsOnlyAboutDependenciesImportCannotAcquireAsync()
    {
        _installationServiceMock
            .Setup(i => i.GetAllInstallationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<GameInstallation>>.CreateSuccess([]));

        var package = CreatePackage(
        [
            new SharedManifestDependency
            {
                ManifestId = "1.0.aodmaps.mappack.aodmappack",
                DisplayName = "AOD Map Pack",
                Version = "1.0",
                ContentType = ContentType.MapPack,
                PublisherType = PublisherTypeConstants.AODMaps,
            },
            new SharedManifestDependency
            {
                ManifestId = "1.0.thesuperhackers.patch.generalsgamepatch2",
                DisplayName = "Community Patch 2",
                Version = "1.0",
                ContentType = ContentType.Patch,
                PublisherType = PublisherTypeConstants.TheSuperHackers,
            },
            new SharedManifestDependency
            {
                ManifestId = "1.0.community.mod.orphanmod",
                DisplayName = "Orphan Mod",
                Version = "1.0",
                ContentType = ContentType.Mod,
                PackageHash = new string('a', 64),
            },
        ]);

        using var sharingService = CreateSharingService();
        var result = await sharingService.InspectSharedProfileAsync(JsonSerializer.Serialize(package, PackageJsonOptions));

        Assert.True(result.Success, result.FirstError);
        var inspection = Assert.IsType<SharedProfileInspectionResult>(result.Data);
        Assert.Single(inspection.SecurityWarningCodes, code => code == ProfileSecurityWarningCode.MissingDownloadSource);
        var warning = Assert.Single(inspection.SecurityWarnings, w => w.Contains("cannot be acquired", StringComparison.Ordinal));
        Assert.Contains("Orphan Mod", warning, StringComparison.Ordinal);
    }

    private static SharedGameProfilePackage CreatePackage(IEnumerable<SharedManifestDependency>? extraDependencies = null) => new()
    {
        SchemaVersion = ProfileSharingConstants.DefaultSchemaVersion,
        Profile = new SharedProfileMetadata
        {
            Name = "Shared Native Profile",
            GameType = GameType.ZeroHour,
            GameClientManifestId = ClientManifestId,
        },
        RequiredManifests =
        [
            new SharedManifestDependency
            {
                ManifestId = "1.108.steam.gameinstallation.zerohour",
                DisplayName = "Zero Hour",
                Version = "1.08",
                ContentType = ContentType.GameInstallation,
            },
            .. extraDependencies ?? [],
        ],
    };

    private static SharedProfileImportRequest CreateImportRequest() => new()
    {
        Package = CreatePackage(),
        ProfileName = "Imported Native Profile",
        GameInstallationId = InstallationId,
    };

    private void PoolInstallationManifest(string id, GameInstallationType installationType) =>
        PoolInstallationManifest(id, installationType, InstallationPath);

    private void PoolInstallationManifest(string id, GameInstallationType installationType, string? sourcePath)
    {
        _pool[id] = new ContentManifest
        {
            Id = ManifestId.Create(id),
            Name = $"{installationType} Zero Hour",
            Metadata = new ContentMetadata { SourcePath = sourcePath },
            Version = id.Split('.')[1],
            ContentType = ContentType.GameInstallation,
            TargetGame = GameType.ZeroHour,
        };
    }

    private void PoolClientManifest()
    {
        _pool[ClientManifestId] = new ContentManifest
        {
            Id = ManifestId.Create(ClientManifestId),
            Name = "GeneralsX Zero Hour",
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
        };
    }

    private void SetUpInstallation(string clientVersion)
    {
        var installation = new GameInstallation(InstallationPath, GameInstallationType.Custom)
        {
            Id = InstallationId,
            HasZeroHour = true,
            AvailableGameClients =
            [
                new GameClient
                {
                    Id = ClientManifestId,
                    Name = "GeneralsX Zero Hour",
                    GameType = GameType.ZeroHour,
                    Version = clientVersion,
                    PublisherType = PublisherTypeConstants.TheSuperHackers,
                    InstallationId = InstallationId,
                },
            ],
        };

        _installationServiceMock
            .Setup(i => i.GetInstallationAsync(InstallationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<GameInstallation>.CreateSuccess(installation));
    }

    private ProfileSharingService CreateSharingService() => new(
        _profileRepositoryMock.Object,
        _manifestPoolMock.Object,
        _installationServiceMock.Object,
        new Mock<IContentOrchestrator>().Object,
        new PublisherManifestFactoryResolver([], NullLogger<PublisherManifestFactoryResolver>.Instance),
        NullLogger<ProfileSharingService>.Instance);

    private ProfileLauncherFacade CreateFacade() => new(
        _profileManagerMock.Object,
        new Mock<IGameLauncher>().Object,
        new Mock<IWorkspaceManager>().Object,
        new Mock<ILaunchRegistry>().Object,
        _manifestPoolMock.Object,
        _installationServiceMock.Object,
        new Mock<IDependencyResolver>().Object,
        _casServiceMock.Object,
        new Mock<IGameSettingsService>().Object,
        new Mock<IStorageLocationService>().Object,
        new Mock<INotificationService>().Object,
        new Mock<IPublisherReconcilerRegistry>().Object,
        new Mock<IConfigurationProviderService>().Object,
        new Mock<IGameProcessManager>().Object,
        new Mock<ISymlinkCapabilityProvider>().Object,
        NullLogger<ProfileLauncherFacade>.Instance,
        _launchRunnerMock.Object);
}
