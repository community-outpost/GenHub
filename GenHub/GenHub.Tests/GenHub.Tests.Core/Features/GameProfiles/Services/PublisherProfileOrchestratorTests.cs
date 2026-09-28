using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.GameProfiles.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.GameProfiles.Services;

/// <summary>
/// Unit tests for <see cref="PublisherProfileOrchestrator"/>.
/// </summary>
public sealed class PublisherProfileOrchestratorTests
{
    private readonly Mock<IContentOrchestrator> _contentOrchestratorMock;
    private readonly Mock<IContentManifestPool> _manifestPoolMock;
    private readonly Mock<IGameClientProfileService> _gameClientProfileServiceMock;
    private readonly Mock<INotificationService> _notificationServiceMock;
    private readonly Mock<IContentVersionComparer> _versionComparerMock;
    private readonly PublisherProfileOrchestrator _orchestrator;

    /// <summary>
    /// Initializes a new instance of the <see cref="PublisherProfileOrchestratorTests"/> class.
    /// </summary>
    public PublisherProfileOrchestratorTests()
    {
        _contentOrchestratorMock = new Mock<IContentOrchestrator>();
        _manifestPoolMock = new Mock<IContentManifestPool>();
        _gameClientProfileServiceMock = new Mock<IGameClientProfileService>();
        _notificationServiceMock = new Mock<INotificationService>();
        _versionComparerMock = new Mock<IContentVersionComparer>();

        _orchestrator = new PublisherProfileOrchestrator(
            _contentOrchestratorMock.Object,
            _manifestPoolMock.Object,
            _gameClientProfileServiceMock.Object,
            _notificationServiceMock.Object,
            _versionComparerMock.Object,
            NullLogger<PublisherProfileOrchestrator>.Instance);
    }

    /// <summary>
    /// Verifies that passing a null client returns a failure result.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task CreateProfilesForPublisherClientAsync_WhenClientNull_ReturnsFailureAsync()
    {
        // Arrange
        var installation = CreateInstallation();

        // Act
        var result = await _orchestrator.CreateProfilesForPublisherClientAsync(installation, null!);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Game client cannot be null", string.Join(" ", result.Errors));
    }

    /// <summary>
    /// Verifies that passing a client without a publisher type returns a failure result.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task CreateProfilesForPublisherClientAsync_WhenPublisherTypeNullOrEmpty_ReturnsFailureAsync()
    {
        // Arrange
        var installation = CreateInstallation();
        var client = new GameClient { Id = "test-client", PublisherType = null };

        // Act
        var result = await _orchestrator.CreateProfilesForPublisherClientAsync(installation, client);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Publisher type unknown", string.Join(" ", result.Errors));
    }

    /// <summary>
    /// Verifies that when skipAcquisition is true and manifests exist in the pool,
    /// acquisition is skipped and profiles are created directly from the pool manifests.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task CreateProfilesForPublisherClientAsync_WhenSkipAcquisitionTrueAndPoolHasManifests_DoesNotAcquireContentAndCreatesProfilesAsync()
    {
        // Arrange
        var installation = CreateInstallation();
        var client = CreateGameClient();
        var manifest = CreateDownloadedManifest();

        _manifestPoolMock
            .Setup(p => p.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([manifest]));

        _gameClientProfileServiceMock
            .Setup(s => s.CreateProfileFromManifestAsync(manifest, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(new GameProfile { Id = "p1", Name = "Profile 1" }));

        // Act
        var result = await _orchestrator.CreateProfilesForPublisherClientAsync(
            installation,
            client,
            skipAcquisition: true);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(1, result.Data);

        _contentOrchestratorMock.Verify(
            o => o.SearchAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _contentOrchestratorMock.Verify(
            o => o.AcquireContentAsync(It.IsAny<ContentSearchResult>(), It.IsAny<IProgress<ContentAcquisitionProgress>>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _gameClientProfileServiceMock.Verify(
            s => s.CreateProfileFromManifestAsync(manifest, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that when skipAcquisition is true but the pool is empty,
    /// the orchestrator falls back to acquisition so profiles can be created.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task CreateProfilesForPublisherClientAsync_WhenSkipAcquisitionTrueAndPoolIsEmpty_FallsBackToAcquisitionAsync()
    {
        // Arrange
        var installation = CreateInstallation();
        var client = CreateGameClient();
        var manifest = CreateDownloadedManifest();

        var searchResultItem = new ContentSearchResult
        {
            Id = "search-go",
            Name = "GeneralsOnline 60Hz",
            Version = "060526_QFE1",
            ContentType = ContentType.GameClient,
            ProviderName = PublisherTypeConstants.GeneralsOnline,
        };

        // First call returns empty pool, second call (after acquisition) returns the acquired manifest
        var invocationCount = 0;
        _manifestPoolMock
            .Setup(p => p.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                invocationCount++;
                return OperationResult<IEnumerable<ContentManifest>>.CreateSuccess(
                    invocationCount == 1 ? [] : [manifest]);
            });

        _contentOrchestratorMock
            .Setup(o => o.SearchAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess([searchResultItem]));

        _contentOrchestratorMock
            .Setup(o => o.AcquireContentAsync(It.IsAny<ContentSearchResult>(), It.IsAny<IProgress<ContentAcquisitionProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest>.CreateSuccess(manifest));

        _gameClientProfileServiceMock
            .Setup(s => s.CreateProfileFromManifestAsync(manifest, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(new GameProfile { Id = "p1", Name = "Profile 1" }));

        // Act
        var result = await _orchestrator.CreateProfilesForPublisherClientAsync(
            installation,
            client,
            skipAcquisition: true);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(1, result.Data);

        _contentOrchestratorMock.Verify(
            o => o.SearchAsync(
                It.Is<ContentSearchQuery>(q => q.ProviderName == PublisherTypeConstants.GeneralsOnline && q.ContentType == ContentType.GameClient),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _contentOrchestratorMock.Verify(
            o => o.AcquireContentAsync(
                searchResultItem,
                It.IsAny<IProgress<ContentAcquisitionProgress>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        _gameClientProfileServiceMock.Verify(
            s => s.CreateProfileFromManifestAsync(manifest, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that when a non-retail Community Patch client is passed,
    /// the orchestrator selects and acquires the non-retail search result.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task CreateProfilesForPublisherClientAsync_WhenNonRetailCommunityPatch_AcquiresNonRetailCandidateAsync()
    {
        // Arrange
        var installation = CreateInstallation();
        var client = new GameClient
        {
            Id = GameClientConstants.SyntheticClientIds.CommunityPatchNonRet,
            Name = "Community Patch (Non-Retail)",
            PublisherType = CommunityOutpostConstants.PublisherType,
        };

        var retailItem = new ContentSearchResult
        {
            Id = "generalszh_23-07-2026.zip",
            Name = "Community Patch 23-07-2026",
            Version = "23-07-2026",
            ContentType = ContentType.GameClient,
            ProviderName = CommunityOutpostConstants.PublisherType,
        };

        var nonRetItem = new ContentSearchResult
        {
            Id = "generalszh_11-09-2026_NonRet.zip",
            Name = "Community Patch 11-09-2026 (Non-Retail)",
            Version = "11-09-2026",
            Tags = { "nonretail" },
            ContentType = ContentType.GameClient,
            ProviderName = CommunityOutpostConstants.PublisherType,
        };

        var nonRetManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.communityoutpost.gameclient.cp-nonret"),
            Name = "Community Patch (Non-Retail)",
            Version = "11-09-2026",
            Metadata = new ContentMetadata { Tags = ["nonretail"] },
            ContentType = ContentType.GameClient,
            Publisher = new PublisherInfo { PublisherType = CommunityOutpostConstants.PublisherType },
            Files = [new ManifestFile { RelativePath = "generals.exe", Hash = "hash" }],
        };

        var invocationCount = 0;
        _manifestPoolMock
            .Setup(p => p.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                invocationCount++;
                return OperationResult<IEnumerable<ContentManifest>>.CreateSuccess(
                    invocationCount == 1 ? [] : [nonRetManifest]);
            });

        _contentOrchestratorMock
            .Setup(o => o.SearchAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess([nonRetItem, retailItem]));

        _contentOrchestratorMock
            .Setup(o => o.AcquireContentAsync(It.IsAny<ContentSearchResult>(), It.IsAny<IProgress<ContentAcquisitionProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest>.CreateSuccess(nonRetManifest));

        _gameClientProfileServiceMock
            .Setup(s => s.CreateProfileFromManifestAsync(nonRetManifest, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(new GameProfile { Id = "p-nonret", Name = "CP Non-Ret Profile" }));

        // Act
        var result = await _orchestrator.CreateProfilesForPublisherClientAsync(installation, client);

        // Assert: Orchestrator selected the nonRetItem for acquisition, not retailItem
        Assert.True(result.Success);
        _contentOrchestratorMock.Verify(
            o => o.AcquireContentAsync(
                nonRetItem,
                It.IsAny<IProgress<ContentAcquisitionProgress>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that when a retail Community Patch client is passed,
    /// the orchestrator selects and acquires the retail search result even if non-retail appears first.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task CreateProfilesForPublisherClientAsync_WhenRetailCommunityPatch_AcquiresRetailCandidateAsync()
    {
        // Arrange
        var installation = CreateInstallation();
        var client = new GameClient
        {
            Id = GameClientConstants.SyntheticClientIds.CommunityPatch,
            Name = "Community Patch",
            PublisherType = CommunityOutpostConstants.PublisherType,
        };

        var nonRetItem = new ContentSearchResult
        {
            Id = "generalszh_11-09-2026_NonRet.zip",
            Name = "Community Patch 11-09-2026 (Non-Retail)",
            Version = "11-09-2026",
            Tags = { "nonretail" },
            ContentType = ContentType.GameClient,
            ProviderName = CommunityOutpostConstants.PublisherType,
        };

        var retailItem = new ContentSearchResult
        {
            Id = "generalszh_23-07-2026.zip",
            Name = "Community Patch 23-07-2026",
            Version = "23-07-2026",
            ContentType = ContentType.GameClient,
            ProviderName = CommunityOutpostConstants.PublisherType,
        };

        var retailManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.communityoutpost.gameclient.cp-retail"),
            Name = "Community Patch",
            Version = "23-07-2026",
            ContentType = ContentType.GameClient,
            Publisher = new PublisherInfo { PublisherType = CommunityOutpostConstants.PublisherType },
            Files = [new ManifestFile { RelativePath = "generals.exe", Hash = "hash" }],
        };

        var invocationCount = 0;
        _manifestPoolMock
            .Setup(p => p.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                invocationCount++;
                return OperationResult<IEnumerable<ContentManifest>>.CreateSuccess(
                    invocationCount == 1 ? [] : [retailManifest]);
            });

        // Even though nonRetItem appears first in search results:
        _contentOrchestratorMock
            .Setup(o => o.SearchAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess([nonRetItem, retailItem]));

        _contentOrchestratorMock
            .Setup(o => o.AcquireContentAsync(It.IsAny<ContentSearchResult>(), It.IsAny<IProgress<ContentAcquisitionProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest>.CreateSuccess(retailManifest));

        _gameClientProfileServiceMock
            .Setup(s => s.CreateProfileFromManifestAsync(retailManifest, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(new GameProfile { Id = "p-retail", Name = "CP Retail Profile" }));

        // Act
        var result = await _orchestrator.CreateProfilesForPublisherClientAsync(installation, client);

        // Assert: Orchestrator selected the retailItem for acquisition
        Assert.True(result.Success);
        _contentOrchestratorMock.Verify(
            o => o.AcquireContentAsync(
                retailItem,
                It.IsAny<IProgress<ContentAcquisitionProgress>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that when a non-retail Community Patch client is requested but only retail candidates
    /// are discovered, the orchestrator does not acquire the incompatible retail variant.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task CreateProfilesForPublisherClientAsync_WhenNonRetailRequestedButOnlyRetailFound_DoesNotAcquireOppositeVariantAsync()
    {
        // Arrange
        var installation = CreateInstallation();
        var client = new GameClient
        {
            Id = GameClientConstants.SyntheticClientIds.CommunityPatchNonRet,
            Name = "Community Patch (Non-Retail)",
            PublisherType = CommunityOutpostConstants.PublisherType,
        };

        var retailItem = new ContentSearchResult
        {
            Id = "generalszh_23-07-2026.zip",
            Name = "Community Patch 23-07-2026",
            Version = "23-07-2026",
            ContentType = ContentType.GameClient,
            ProviderName = CommunityOutpostConstants.PublisherType,
        };

        _manifestPoolMock
            .Setup(p => p.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([]));

        _contentOrchestratorMock
            .Setup(o => o.SearchAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess([retailItem]));

        // Act
        var result = await _orchestrator.CreateProfilesForPublisherClientAsync(installation, client);

        // Assert: Never acquires retailItem
        Assert.True(result.Success);
        Assert.Equal(0, result.Data);
        _contentOrchestratorMock.Verify(
            o => o.AcquireContentAsync(
                It.IsAny<ContentSearchResult>(),
                It.IsAny<IProgress<ContentAcquisitionProgress>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies that skipAcquisition with an empty pool creates the profile from the detected
    /// client on disk instead of downloading the publisher package.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task CreateProfilesForPublisherClientAsync_WhenSkipAcquisitionAndDetectedClientOnDisk_CreatesProfileFromClientWithoutAcquiringAsync()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var executablePath = Path.Combine(directory, GameClientConstants.SuperHackersZeroHourExecutable);
            await File.WriteAllBytesAsync(executablePath, [0x4D, 0x5A, 0x90, 0x00]);
            var installation = new GameInstallation(directory, GameInstallationType.Retail, null);
            var client = CreateSuperHackersClient(executablePath);
            SetupPool([]);
            SetupDetectedClientProfile(installation, client);

            var result = await _orchestrator.CreateProfilesForPublisherClientAsync(installation, client, skipAcquisition: true);

            Assert.True(result.Success);
            Assert.Equal(1, result.Data);
            VerifyNoAcquisition();
            _gameClientProfileServiceMock.Verify(
                s => s.CreateProfileForGameClientAsync(installation, client, null, null, null, It.IsAny<CancellationToken>()),
                Times.Once);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Verifies that on macOS and Linux a native client never pulls the Windows publisher
    /// package or profiles a pooled Windows manifest, whatever the wizard decided.
    /// </summary>
    /// <param name="skipAcquisition">Whether the wizard asked to create a profile only.</param>
    /// <param name="forceReacquireContent">Whether the wizard asked to update.</param>
    /// <param name="poolHasWindowsManifest">Whether a Windows package is already pooled.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, true)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    public async Task CreateProfilesForPublisherClientAsync_WhenNativeBuildOnNonWindowsHost_CreatesNativeProfileWithoutAcquiringAsync(
        bool skipAcquisition,
        bool forceReacquireContent,
        bool poolHasWindowsManifest)
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var executablePath = Path.Combine(directory, Path.GetFileNameWithoutExtension(GameClientConstants.SuperHackersZeroHourExecutable));
            await File.WriteAllBytesAsync(executablePath, HostNativeExecutableHeader());
            var client = CreateSuperHackersClient(executablePath);

            // Windows keeps the publisher package flow; a Mach-O binary is not a client there.
            if (OperatingSystem.IsWindows())
            {
                return;
            }

            var installation = new GameInstallation(directory, GameInstallationType.Retail, null);
            var windowsManifest = CreateSuperHackersWindowsManifest();
            SetupPool(poolHasWindowsManifest ? [windowsManifest] : []);
            SetupSearchAndAcquire(windowsManifest);
            SetupDetectedClientProfile(installation, client);

            var result = await _orchestrator.CreateProfilesForPublisherClientAsync(
                installation,
                client,
                forceReacquireContent: forceReacquireContent,
                skipAcquisition: skipAcquisition);

            Assert.True(result.Success);
            Assert.Equal(1, result.Data);
            VerifyNoAcquisition();
            _gameClientProfileServiceMock.Verify(
                s => s.CreateProfileFromManifestAsync(It.IsAny<ContentManifest>(), It.IsAny<CancellationToken>()),
                Times.Never);
            _gameClientProfileServiceMock.Verify(
                s => s.CreateProfileForGameClientAsync(installation, client, null, null, null, It.IsAny<CancellationToken>()),
                Times.Once);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Verifies that every native client of the publisher in the installation gets a profile,
    /// so a deployment with both Generals and Zero Hour engines yields both.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task CreateProfilesForPublisherClientAsync_WhenInstallationHasNativeGeneralsAndZeroHour_CreatesBothProfilesAsync()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var directory = CreateTemporaryDirectory();
        try
        {
            var zeroHourPath = Path.Combine(directory, Path.GetFileNameWithoutExtension(GameClientConstants.SuperHackersZeroHourExecutable));
            var generalsPath = Path.Combine(directory, Path.GetFileNameWithoutExtension(GameClientConstants.SuperHackersGeneralsExecutable));
            await File.WriteAllBytesAsync(zeroHourPath, HostNativeExecutableHeader());
            await File.WriteAllBytesAsync(generalsPath, HostNativeExecutableHeader());
            var zeroHour = CreateSuperHackersClient(zeroHourPath);
            var generals = CreateSuperHackersClient(generalsPath);
            generals.Id = "1.108.retail.gameclient.generals";
            generals.GameType = GameType.Generals;
            var installation = new GameInstallation(directory, GameInstallationType.Retail, null)
            {
                AvailableGameClients = [zeroHour, generals],
            };
            SetupPool([]);
            SetupDetectedClientProfile(installation, zeroHour);
            SetupDetectedClientProfile(installation, generals);

            var result = await _orchestrator.CreateProfilesForPublisherClientAsync(installation, zeroHour);

            Assert.True(result.Success);
            Assert.Equal(2, result.Data);
            VerifyNoAcquisition();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Verifies how detected-client profile outcomes combine: real failures are propagated
    /// and an already existing profile is not an error.
    /// </summary>
    /// <param name="firstOutcome">The outcome for the Zero Hour client.</param>
    /// <param name="secondOutcome">The outcome for the Generals client.</param>
    /// <param name="expectSuccess">Whether the orchestrator should report success.</param>
    /// <param name="expectedCount">The expected number of created profiles on success.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Theory]
    [InlineData("fail", "fail", false, 0)]
    [InlineData("exists", "fail", false, 0)]
    [InlineData("exists", "exists", true, 0)]
    [InlineData("created", "fail", true, 1)]
    public async Task CreateProfilesForPublisherClientAsync_WithDetectedClientOutcomes_PropagatesRealFailuresAsync(
        string firstOutcome,
        string secondOutcome,
        bool expectSuccess,
        int expectedCount)
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var zeroHourPath = Path.Combine(directory, GameClientConstants.SuperHackersZeroHourExecutable);
            var generalsPath = Path.Combine(directory, GameClientConstants.SuperHackersGeneralsExecutable);
            await File.WriteAllBytesAsync(zeroHourPath, [0x4D, 0x5A, 0x90, 0x00]);
            await File.WriteAllBytesAsync(generalsPath, [0x4D, 0x5A, 0x90, 0x00]);
            var zeroHour = CreateSuperHackersClient(zeroHourPath);
            var generals = CreateSuperHackersClient(generalsPath);
            generals.Id = "1.108.retail.gameclient.generals";
            generals.GameType = GameType.Generals;
            var installation = new GameInstallation(directory, GameInstallationType.Retail, null);
            var clients = new[] { zeroHour, generals };
            SetupPool([]);
            SetupProfileOutcome(installation, zeroHour, firstOutcome);
            SetupProfileOutcome(installation, generals, secondOutcome);

            var result = await _orchestrator.CreateProfilesFromDetectedClientsAsync(
                installation,
                clients,
                PublisherTypeConstants.TheSuperHackers,
                false,
                CancellationToken.None);

            Assert.Equal(expectSuccess, result.Success);
            if (expectSuccess)
            {
                Assert.Equal(expectedCount, result.Data);
            }
            else
            {
                Assert.Contains("disk full", string.Join(" ", result.Errors));
                Assert.DoesNotContain("already exists", string.Join(" ", result.Errors), StringComparison.OrdinalIgnoreCase);
            }

            var anyFailure = firstOutcome == "fail" || secondOutcome == "fail";
            _notificationServiceMock.Verify(
                n => n.ShowWarning(It.IsAny<string>(), It.Is<string>(m => m.Contains("disk full")), It.IsAny<int?>(), It.IsAny<bool>()),
                anyFailure ? Times.Once() : Times.Never());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Verifies that skipAcquisition with a failing profile creation returns a failure with the reason.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task CreateProfilesForPublisherClientAsync_WhenSkipAcquisitionAndProfileCreationFails_ReturnsFailureAsync()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var executablePath = Path.Combine(directory, GameClientConstants.SuperHackersZeroHourExecutable);
            await File.WriteAllBytesAsync(executablePath, [0x4D, 0x5A, 0x90, 0x00]);
            var installation = new GameInstallation(directory, GameInstallationType.Retail, null);
            var client = CreateSuperHackersClient(executablePath);
            SetupPool([]);
            SetupProfileOutcome(installation, client, "fail");

            var result = await _orchestrator.CreateProfilesForPublisherClientAsync(installation, client, skipAcquisition: true);

            Assert.False(result.Success);
            Assert.Contains("disk full", string.Join(" ", result.Errors));
            VerifyNoAcquisition();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Verifies which detected clients count as native on this host. Flatpak bundles, AppImages
    /// and ELF binaries are native on Linux; app bundles and Mach-O binaries are native on macOS.
    /// Windows binaries, non-executables and missing files are not, and nothing is native on Windows.
    /// </summary>
    /// <param name="relativePath">The client path under a temporary directory.</param>
    /// <param name="kind">What to create at that path.</param>
    /// <param name="nativeOn">The host it is native to: "linux", "macos" or "none".</param>
    [Theory]
    [InlineData("generalszh.flatpak", "text", "linux")]
    [InlineData("GeneralsZH.AppImage", "elf", "linux")]
    [InlineData("generalszh", "elf", "linux")]
    [InlineData("generalszh", "macho", "macos")]
    [InlineData("Zero Hour.app", "directory", "macos")]
    [InlineData("generalszh.exe", "pe", "none")]
    [InlineData("generalszh", "pe", "none")]
    [InlineData("generalszh", "text", "none")]
    [InlineData("generalszh", "missing", "none")]
    [InlineData("Data", "directory", "none")]
    public void HostPlatformCheck_ClassifiesClientsByPlatform(string relativePath, string kind, string nativeOn)
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, relativePath);
            switch (kind)
            {
                case "directory":
                    Directory.CreateDirectory(path);
                    break;
                case "text":
                    File.WriteAllText(path, "not a binary");
                    break;
                case "elf":
                    File.WriteAllBytes(path, [0x7F, 0x45, 0x4C, 0x46, 0x02, 0x01, 0x01, 0x00]);
                    break;
                case "macho":
                    File.WriteAllBytes(path, [0xCF, 0xFA, 0xED, 0xFE, 0x0C, 0x00, 0x00, 0x01]);
                    break;
                case "pe":
                    File.WriteAllBytes(path, [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00]);
                    break;
                default:
                    break;
            }

            var client = CreateSuperHackersClient(path);

            var expected = nativeOn switch
            {
                "linux" => OperatingSystem.IsLinux(),
                "macos" => OperatingSystem.IsMacOS(),
                _ => false,
            };
            Assert.Equal(expected, PublisherProfileOrchestrator.IsHostNativeClient(client));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static byte[] HostNativeExecutableHeader() => OperatingSystem.IsLinux()
        ? [0x7F, 0x45, 0x4C, 0x46, 0x02, 0x01, 0x01, 0x00]
        : [0xCF, 0xFA, 0xED, 0xFE, 0x0C, 0x00, 0x00, 0x01];

    private static GameInstallation CreateInstallation() => new(@"C:\Games\ZeroHour", GameInstallationType.Steam, null);

    private static GameClient CreateGameClient() => new()
    {
        Id = "test-client",
        Name = "GeneralsOnline",
        PublisherType = PublisherTypeConstants.GeneralsOnline,
        ExecutablePath = @"C:\Games\ZeroHour\GeneralsOnlineZH_60.exe",
    };

    private static ContentManifest CreateDownloadedManifest() => new()
    {
        Id = ManifestId.Create("1.0.generalsonline.gameclient.zerohour-generalsonline-60hz"),
        Name = "GeneralsOnline 60Hz",
        Version = "060526_QFE1",
        ContentType = ContentType.GameClient,
        Publisher = new PublisherInfo
        {
            PublisherType = PublisherTypeConstants.GeneralsOnline,
            Name = "Generals Online",
        },
        Files =
        [
            new ManifestFile
            {
                RelativePath = "GeneralsOnlineZH_60.exe",
                SourceType = ContentSourceType.ContentAddressable,
                Hash = "hash123",
            },
        ],
    };

    private static string CreateTemporaryDirectory() =>
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"GenHub.PublisherProfile.{Guid.NewGuid():N}")).FullName;

    private static GameClient CreateSuperHackersClient(string executablePath) => new()
    {
        Id = "1.104.retail.gameclient.zerohour",
        Name = $"{SuperHackersConstants.PublisherName} - {SuperHackersConstants.ZeroHourDisplayName}",
        PublisherType = PublisherTypeConstants.TheSuperHackers,
        GameType = GameType.ZeroHour,
        ExecutablePath = executablePath,
        WorkingDirectory = Path.GetDirectoryName(executablePath)!,
    };

    private static ContentManifest CreateSuperHackersWindowsManifest() => new()
    {
        Id = ManifestId.Create("1.20260925.thesuperhackers.gameclient.generalszh"),
        Name = "TheSuperHackers - Zero Hour",
        Version = "weekly-2026-09-25",
        ContentType = ContentType.GameClient,
        TargetGame = GameType.ZeroHour,
        Publisher = new PublisherInfo { PublisherType = PublisherTypeConstants.TheSuperHackers },
        Files =
        [
            new ManifestFile
            {
                RelativePath = GameClientConstants.SuperHackersZeroHourExecutable,
                SourceType = ContentSourceType.ContentAddressable,
                Hash = "hash",
            },
        ],
    };

    private void SetupPool(IEnumerable<ContentManifest> manifests)
    {
        _manifestPoolMock
            .Setup(p => p.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess(manifests));
    }

    private void SetupSearchAndAcquire(ContentManifest acquiredManifest)
    {
        var searchResult = new ContentSearchResult
        {
            Id = "generalszh-weekly-2026-09-25.zip",
            Name = "TheSuperHackers weekly-2026-09-25",
            Version = "weekly-2026-09-26",
            ContentType = ContentType.GameClient,
            ProviderName = PublisherTypeConstants.TheSuperHackers,
        };
        _contentOrchestratorMock
            .Setup(o => o.SearchAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess([searchResult]));
        _contentOrchestratorMock
            .Setup(o => o.AcquireContentAsync(It.IsAny<ContentSearchResult>(), It.IsAny<IProgress<ContentAcquisitionProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest>.CreateSuccess(acquiredManifest));
        _versionComparerMock
            .Setup(c => c.Compare(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(1);
        _gameClientProfileServiceMock
            .Setup(s => s.CreateProfileFromManifestAsync(It.IsAny<ContentManifest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(new GameProfile { Id = "windows", Name = "Windows" }));
    }

    private void SetupDetectedClientProfile(GameInstallation installation, GameClient client)
    {
        _gameClientProfileServiceMock
            .Setup(s => s.CreateProfileForGameClientAsync(installation, client, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(new GameProfile { Id = "native", Name = client.Name, GameClient = client }));
    }

    private void VerifyNoAcquisition()
    {
        _contentOrchestratorMock.Verify(
            o => o.SearchAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _contentOrchestratorMock.Verify(
            o => o.AcquireContentAsync(It.IsAny<ContentSearchResult>(), It.IsAny<IProgress<ContentAcquisitionProgress>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private void SetupProfileOutcome(GameInstallation installation, GameClient client, string outcome)
    {
        var result = outcome switch
        {
            "created" => ProfileOperationResult<GameProfile>.CreateSuccess(new GameProfile { Id = client.ExecutablePath, Name = client.Name, GameClient = client }),
            "exists" => ProfileOperationResult<GameProfile>.CreateFailure("Profile already exists", ProfileConstants.ProfileAlreadyExistsErrorCode),
            _ => ProfileOperationResult<GameProfile>.CreateFailure("disk full"),
        };
        _gameClientProfileServiceMock
            .Setup(s => s.CreateProfileForGameClientAsync(installation, client, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
    }
}
