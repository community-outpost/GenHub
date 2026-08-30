using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Content.Services.CommunityOutpost;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Content.CommunityOutpost;

/// <summary>
/// Tests for CommunityOutpostResolver to verify manifest generation and ingestion gate compatibility.
/// </summary>
public class CommunityOutpostResolverTests
{
    private readonly Mock<IProviderDefinitionLoader> _providerLoaderMock;

    /// <summary>
    /// Initializes a new instance of the <see cref="CommunityOutpostResolverTests"/> class.
    /// </summary>
    public CommunityOutpostResolverTests()
    {
        _providerLoaderMock = new Mock<IProviderDefinitionLoader>();

        var providerDefinition = new ProviderDefinition
        {
            ProviderId = CommunityOutpostConstants.PublisherId,
            PublisherType = CommunityOutpostConstants.PublisherType,
            DisplayName = CommunityOutpostConstants.PublisherName,
            Endpoints = new ProviderEndpoints
            {
                WebsiteUrl = "https://legi.cc",
            },
        };

        _providerLoaderMock
            .Setup(l => l.GetProvider(CommunityOutpostConstants.PublisherId))
            .Returns(providerDefinition);
    }

    /// <summary>
    /// Verifies that Community Patch resolution creates a manifest with format version 1 that passes ManifestIngestionGate.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ResolveAsync_CommunityPatch_GeneratesManifestAcceptedByIngestionGateAsync()
    {
        // Arrange
        var builderMock = CreateBuilderMock(
            ManifestId.Create("1.20260827.communityoutpost.gameclient.communitypatch"),
            "Community Patch (TheSuperHackers Build)",
            "27-08-2026",
            ContentType.GameClient,
            GameType.ZeroHour);

        var resolver = new CommunityOutpostResolver(
            () => builderMock.Object,
            _providerLoaderMock.Object,
            NullLogger<CommunityOutpostResolver>.Instance);

        var searchResult = new ContentSearchResult
        {
            Id = "1.20260827.communityoutpost.gameclient.community-patch",
            Name = "Community Patch (TheSuperHackers Build)",
            Version = "27-08-2026",
            SourceUrl = "https://legi.cc/patch/generalszh_27-08-2026_NonRet.zip",
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
        };
        searchResult.ResolverMetadata["contentCode"] = "community-patch";

        // Act
        var result = await resolver.ResolveAsync(searchResult);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);

        builderMock.Verify(
            m => m.WithBasicInfo(
                CommunityOutpostConstants.PublisherType,
                "community-patch",
                "20260827"),
            Times.Once);

        var manifest = result.Data;
        Assert.Equal(ManifestConstants.DefaultManifestVersion, manifest.ManifestVersion);
        Assert.Equal("27-08-2026", manifest.Version);
        Assert.Equal(ContentType.GameClient, manifest.ContentType);
        Assert.Equal(GameType.ZeroHour, manifest.TargetGame);

        // Ingestion gate must accept the manifest
        var accepted = ManifestIngestionGate.TryAccept(manifest, out var rejectionReason);
        Assert.True(accepted, $"Manifest should be accepted by ManifestIngestionGate, but was rejected with: {rejectionReason}");
        Assert.Null(rejectionReason);
    }

    /// <summary>
    /// Verifies that resolving base game clients produces manifests with DefaultManifestVersion format.
    /// </summary>
    /// <param name="contentCode">The content code under test.</param>
    /// <param name="expectedName">The expected display name.</param>
    /// <param name="version">The patch version string.</param>
    /// <param name="expectedNumericVersion">The expected numeric version for manifest ID.</param>
    /// <param name="expectedContentName">The expected content name for manifest ID.</param>
    /// <param name="expectedGame">The expected game type.</param>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Theory]
    [InlineData("104e", "Zero Hour 1.04 (English)", "1.04", "104", "patch104english", GameType.ZeroHour)]
    [InlineData("108e", "Generals 1.08 (English)", "1.08", "108", "patch108english", GameType.Generals)]
    public async Task ResolveAsync_BaseGamePatch_GeneratesManifestAcceptedByIngestionGateAsync(
        string contentCode,
        string expectedName,
        string version,
        string expectedNumericVersion,
        string expectedContentName,
        GameType expectedGame)
    {
        // Arrange
        var builderMock = CreateBuilderMock(
            ManifestId.Create($"1.{expectedNumericVersion}.communityoutpost.gameclient.{expectedContentName}"),
            expectedName,
            version,
            ContentType.GameClient,
            expectedGame);

        var resolver = new CommunityOutpostResolver(
            () => builderMock.Object,
            _providerLoaderMock.Object,
            NullLogger<CommunityOutpostResolver>.Instance);

        var searchResult = new ContentSearchResult
        {
            Id = $"1.{expectedNumericVersion}.communityoutpost.gameclient.{expectedContentName}",
            Name = expectedName,
            Version = version,
            SourceUrl = $"https://legi.cc/gp2/files/{contentCode}.dat",
            ContentType = ContentType.GameClient,
            TargetGame = expectedGame,
        };
        searchResult.ResolverMetadata["contentCode"] = contentCode;

        // Act
        var result = await resolver.ResolveAsync(searchResult);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);

        builderMock.Verify(
            m => m.WithBasicInfo(
                CommunityOutpostConstants.PublisherType,
                expectedContentName,
                expectedNumericVersion),
            Times.Once);

        var manifest = result.Data;
        Assert.Equal(ManifestConstants.DefaultManifestVersion, manifest.ManifestVersion);
        Assert.Equal(version, manifest.Version);

        var accepted = ManifestIngestionGate.TryAccept(manifest, out var rejectionReason);
        Assert.True(accepted, $"Manifest should be accepted by ManifestIngestionGate, but was rejected with: {rejectionReason}");
        Assert.Null(rejectionReason);
    }

    /// <summary>
    /// Verifies that resolving addons like GenTool produces valid manifests that pass ManifestIngestionGate.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ResolveAsync_AddonContent_GeneratesManifestAcceptedByIngestionGateAsync()
    {
        // Arrange
        var builderMock = CreateBuilderMock(
            ManifestId.Create("1.1.communityoutpost.addon.gent"),
            "GenTool",
            "8.8",
            ContentType.Addon,
            GameType.ZeroHour);

        var resolver = new CommunityOutpostResolver(
            () => builderMock.Object,
            _providerLoaderMock.Object,
            NullLogger<CommunityOutpostResolver>.Instance);

        var searchResult = new ContentSearchResult
        {
            Id = "1.1.communityoutpost.addon.gent",
            Name = "GenTool",
            Version = "8.8",
            SourceUrl = "https://legi.cc/gp2/files/gent.dat",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
        };
        searchResult.ResolverMetadata["contentCode"] = "gent";

        // Act
        var result = await resolver.ResolveAsync(searchResult);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);

        var manifest = result.Data;
        Assert.Equal(ManifestConstants.DefaultManifestVersion, manifest.ManifestVersion);

        var accepted = ManifestIngestionGate.TryAccept(manifest, out var rejectionReason);
        Assert.True(accepted, $"Manifest should be accepted by ManifestIngestionGate, but was rejected with: {rejectionReason}");
        Assert.Null(rejectionReason);
    }

    private static Mock<IContentManifestBuilder> CreateBuilderMock(
        ManifestId manifestId,
        string name,
        string version,
        ContentType contentType,
        GameType targetGame)
    {
        var manifest = new ContentManifest
        {
            Id = manifestId,
            Name = name,
            Version = version,
            ContentType = contentType,
            TargetGame = targetGame,
            ManifestVersion = ManifestConstants.DefaultManifestVersion,
        };

        var builderMock = new Mock<IContentManifestBuilder>();
        builderMock.Setup(m => m.WithBasicInfo(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>())).Returns(builderMock.Object);
        builderMock.Setup(m => m.WithContentType(It.IsAny<ContentType>(), It.IsAny<GameType>())).Returns(builderMock.Object);
        builderMock.Setup(m => m.WithPublisher(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).Returns(builderMock.Object);
        builderMock.Setup(m => m.WithMetadata(It.IsAny<string>(), It.IsAny<List<string>?>(), It.IsAny<string>(), It.IsAny<List<string>?>(), It.IsAny<string>())).Returns(builderMock.Object);
        builderMock.Setup(m => m.WithInstallationInstructions(It.IsAny<WorkspaceStrategy>())).Returns(builderMock.Object);
        builderMock.Setup(m => m.AddDependency(
            It.IsAny<ManifestId>(),
            It.IsAny<string>(),
            It.IsAny<ContentType>(),
            It.IsAny<DependencyInstallBehavior>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<List<string>?>(),
            It.IsAny<bool>(),
            It.IsAny<List<ManifestId>?>())).Returns(builderMock.Object);
        builderMock.Setup(m => m.AddRemoteFileAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<ContentSourceType>(),
            It.IsAny<bool>(),
            It.IsAny<FilePermissions?>())).ReturnsAsync(builderMock.Object);
        builderMock.Setup(m => m.Build()).Returns(manifest);

        return builderMock;
    }
}
