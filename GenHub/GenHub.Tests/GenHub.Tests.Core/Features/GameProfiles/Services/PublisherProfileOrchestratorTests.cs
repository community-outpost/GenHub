using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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

    private static GameInstallation CreateInstallation() => new("C:\\Games\\ZeroHour", GameInstallationType.Steam, null);

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
}
