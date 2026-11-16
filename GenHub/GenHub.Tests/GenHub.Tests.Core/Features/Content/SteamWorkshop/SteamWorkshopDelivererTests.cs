using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.Steam;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Features.Content.Services.SteamWorkshop;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.Content.SteamWorkshop;

/// <summary>
/// Unit tests for <see cref="SteamWorkshopDeliverer"/>.
/// </summary>
public sealed class SteamWorkshopDelivererTests : IDisposable
{
    private const string SubscribedFileId = "3790356853";

    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "SteamWorkshopDelivererTests", Guid.NewGuid().ToString("N"));
    private readonly Mock<IGameInstallationService> _installationServiceMock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="SteamWorkshopDelivererTests"/> class.
    /// </summary>
    public SteamWorkshopDelivererTests()
    {
        Directory.CreateDirectory(_tempRoot);
    }

    /// <summary>
    /// Disposes of the test directories.
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, true);
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Verifies Steam Workshop manifests with content IDs are deliverable.
    /// </summary>
    [Fact]
    public void CanDeliver_SteamWorkshopManifest_ReturnsTrue()
    {
        var sut = CreateSut();

        Assert.True(sut.CanDeliver(CreateManifest("steamworkshop.3790356853")));
    }

    /// <summary>
    /// Verifies foreign manifests are not deliverable.
    /// </summary>
    /// <param name="publisherType">The publisher type.</param>
    /// <param name="originalContentId">The original content ID.</param>
    [Theory]
    [InlineData("cnclabs", "steamworkshop.3790356853")]
    [InlineData("steamworkshop", "cnclabs.map.1")]
    [InlineData("steamworkshop", "")]
    public void CanDeliver_ForeignManifest_ReturnsFalse(string publisherType, string originalContentId)
    {
        var sut = CreateSut();
        var manifest = CreateManifest(originalContentId);
        manifest.Publisher = new PublisherInfo { PublisherType = publisherType };

        Assert.False(sut.CanDeliver(manifest));
    }

    /// <summary>
    /// Verifies subscribed content is copied into the target directory.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DeliverContentAsync_SubscribedContent_CopiesFilesAsync()
    {
        await CreateSubscribedFixtureAsync();
        var sut = CreateSut();
        var target = Path.Combine(_tempRoot, "staging");

        var result = await sut.DeliverContentAsync(CreateManifest("steamworkshop.3790356853"), target);

        Assert.True(result.Success);
        Assert.Equal("map-bytes", await File.ReadAllTextAsync(Path.Combine(target, "duel.map")));
    }

    /// <summary>
    /// Verifies missing subscriptions fail with subscribe instructions.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DeliverContentAsync_NotSubscribed_ReturnsFailureAsync()
    {
        const string unsubscribedId = "steamworkshop.100000000000";
        _installationServiceMock.SetupGet(service => service.CachedInstallations).Returns([]);
        var sut = CreateSut();

        var result = await sut.DeliverContentAsync(CreateManifest(unsubscribedId), Path.Combine(_tempRoot, "staging"));

        Assert.False(result.Success);
        Assert.Contains("Subscribe", result.FirstError, StringComparison.Ordinal);
        Assert.Contains("100000000000", result.FirstError, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies mis-tagged manifests still deliver from the alternate workshop AppID.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DeliverContentAsync_AlternateAppId_DeliversAsync()
    {
        await CreateSubscribedFixtureAsync(appId: SteamWorkshopConstants.ZeroHourAppId);
        var sut = CreateSut();
        var target = Path.Combine(_tempRoot, "staging");

        var result = await sut.DeliverContentAsync(CreateManifest("steamworkshop.3790356853", GameType.Generals), target);

        Assert.True(result.Success);
        Assert.Equal("map-bytes", await File.ReadAllTextAsync(Path.Combine(target, "duel.map")));
    }

    /// <summary>
    /// Verifies failed deliveries remove partially copied files.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DeliverContentAsync_CopyFailure_RemovesPartialFilesAsync()
    {
        var subscribed = await CreateSubscribedFixtureAsync();
        await File.WriteAllTextAsync(Path.Combine(subscribed, "second.map"), "more-bytes");
        var target = Path.Combine(_tempRoot, "staging");
        Directory.CreateDirectory(Path.Combine(target, "second.map"));
        var sut = CreateSut();

        var result = await sut.DeliverContentAsync(CreateManifest("steamworkshop.3790356853"), target);

        Assert.False(result.Success);
        Assert.Empty(Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories));
    }

    /// <summary>
    /// Verifies validation reflects subscription state.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ValidateContentAsync_SubscribedContent_ReturnsTrueAsync()
    {
        await CreateSubscribedFixtureAsync();
        var sut = CreateSut();

        var result = await sut.ValidateContentAsync(CreateManifest("steamworkshop.3790356853"));

        Assert.True(result.Success);
        Assert.True(result.Data);
    }

    /// <summary>
    /// Verifies validation reports missing subscriptions.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ValidateContentAsync_NotSubscribed_ReturnsFalseAsync()
    {
        _installationServiceMock.SetupGet(service => service.CachedInstallations).Returns([]);
        var sut = CreateSut();

        var result = await sut.ValidateContentAsync(CreateManifest("steamworkshop.100000000000"));

        Assert.True(result.Success);
        Assert.False(result.Data);
    }

    /// <summary>
    /// Verifies path traversal sequences in relative paths throw an InvalidOperationException.
    /// </summary>
    /// <param name="relativePath">The relative path containing traversal sequences.</param>
    [Theory]
    [InlineData("../escaped.txt")]
    [InlineData("..\\escaped.txt")]
    [InlineData("nested/../../escaped.txt")]
    public void ResolveTargetPath_PathTraversal_ThrowsInvalidOperationException(string relativePath)
    {
        var targetRoot = Path.Combine(_tempRoot, "target");
        Assert.Throws<InvalidOperationException>(() =>
            SteamWorkshopDeliverer.ResolveTargetPath(targetRoot, relativePath));
    }

    /// <summary>
    /// Verifies pre-existing files are preserved if delivery fails.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task DeliverContentAsync_Failure_PreservesPreExistingFilesAsync()
    {
        var subscribed = await CreateSubscribedFixtureAsync();
        await File.WriteAllTextAsync(Path.Combine(subscribed, "second.map"), "new-second");
        var target = Path.Combine(_tempRoot, "staging");
        Directory.CreateDirectory(target);
        var preExistingFile = Path.Combine(target, "duel.map");
        await File.WriteAllTextAsync(preExistingFile, "original-content");

        Directory.CreateDirectory(Path.Combine(target, "second.map"));

        var sut = CreateSut();
        var result = await sut.DeliverContentAsync(CreateManifest("steamworkshop.3790356853"), target);

        Assert.False(result.Success);
        Assert.True(File.Exists(preExistingFile));
        Assert.Equal("original-content", await File.ReadAllTextAsync(preExistingFile));
    }

    /// <summary>
    /// Verifies that when item is not subscribed locally, client downloader is invoked for 1-click download.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task DeliverContentAsync_NotSubscribedLocally_UsesClientDownloaderAsync()
    {
        _installationServiceMock.SetupGet(service => service.CachedInstallations).Returns([]);
        var downloaderMock = new Mock<ISteamWorkshopClientDownloader>();
        downloaderMock.Setup(d => d.DownloadWorkshopItemAsync(
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IProgress<ContentAcquisitionProgress>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        var sut = new TestDeliverer(
            _installationServiceMock.Object,
            Mock.Of<ILogger<SteamWorkshopDeliverer>>(),
            downloaderMock.Object);

        var target = Path.Combine(_tempRoot, "direct_download_staging");
        Directory.CreateDirectory(target);

        var manifest = CreateManifest("steamworkshop.3810909841");
        var result = await sut.DeliverContentAsync(manifest, target);

        Assert.True(result.Success);
        downloaderMock.Verify(
            d => d.DownloadWorkshopItemAsync(
                SteamWorkshopConstants.ZeroHourAppId,
                "3810909841",
                target,
                It.IsAny<IProgress<ContentAcquisitionProgress>?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static ContentManifest CreateManifest(string originalContentId, GameType targetGame = GameType.ZeroHour)
    {
        return new ContentManifest
        {
            Id = "1.0.steamworkshop.map.duel",
            Name = "Desert Duel",
            ContentType = GenHub.Core.Models.Enums.ContentType.Map,
            TargetGame = targetGame,
            OriginalContentId = originalContentId,
            Publisher = new PublisherInfo { PublisherType = SteamWorkshopConstants.PublisherType },
        };
    }

    private async Task<string> CreateSubscribedFixtureAsync(string publishedFileId = SubscribedFileId, int? appId = null)
    {
        var resolvedAppId = appId ?? SteamWorkshopConstants.ZeroHourAppId;
        var steamApps = Path.Combine(_tempRoot, "Steam", "steamapps");
        var subscribed = Path.Combine(steamApps, "workshop", "content", resolvedAppId.ToString(), publishedFileId);
        Directory.CreateDirectory(subscribed);
        await File.WriteAllTextAsync(Path.Combine(subscribed, "duel.map"), "map-bytes");
        var installation = new GameInstallation(Path.Combine(steamApps, "common", "Zero Hour"), GameInstallationType.Steam);
        _installationServiceMock.SetupGet(service => service.CachedInstallations).Returns([installation]);
        return subscribed;
    }

    private SteamWorkshopDeliverer CreateSut()
    {
        return new TestDeliverer(_installationServiceMock.Object, Mock.Of<ILogger<SteamWorkshopDeliverer>>());
    }

    private sealed class TestDeliverer(
        IGameInstallationService installationService,
        ILogger<SteamWorkshopDeliverer> logger,
        ISteamWorkshopClientDownloader? clientDownloader = null)
        : SteamWorkshopDeliverer(installationService, logger, clientDownloader)
    {
        protected override IEnumerable<string> EnumerateWellKnownSteamRoots()
        {
            return [];
        }
    }
}
