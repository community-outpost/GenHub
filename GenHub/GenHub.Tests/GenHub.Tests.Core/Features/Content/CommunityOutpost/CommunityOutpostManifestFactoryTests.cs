using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.CommunityOutpost;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Features.Content.Services.CommunityOutpost;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.Content.CommunityOutpost;

/// <summary>
/// Tests for CommunityOutpostManifestFactory.
/// </summary>
public class CommunityOutpostManifestFactoryTests : IDisposable
{
    private readonly Mock<ILogger<CommunityOutpostManifestFactory>> _loggerMock;
    private readonly Mock<IFileHashProvider> _hashProviderMock;
    private readonly CommunityOutpostManifestFactory _factory;
    private readonly string _tempDir;

    /// <summary>
    /// Initializes a new instance of the <see cref="CommunityOutpostManifestFactoryTests"/> class.
    /// </summary>
    public CommunityOutpostManifestFactoryTests()
    {
        _loggerMock = new Mock<ILogger<CommunityOutpostManifestFactory>>();
        _hashProviderMock = new Mock<IFileHashProvider>();

        _hashProviderMock.Setup(x => x.ComputeFileHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("abc123hash");

        _factory = new CommunityOutpostManifestFactory(_loggerMock.Object, _hashProviderMock.Object, null!);
        _tempDir = Path.Combine(Path.GetTempPath(), "GenHubTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    /// <summary>
    /// Disposes of the test directory.
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Verifies that multiple variants are correctly split into manifests.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task CreateManifestsFromExtractedContentAsync_WithHleiPackage_ShouldSplitIntoMultipleManifests()
    {
        // Arrange
        var zhEnDir = Path.Combine(_tempDir, "ZH", "BIG EN");
        var zhDeDir = Path.Combine(_tempDir, "ZH", "BIG DE");
        var ccgEnDir = Path.Combine(_tempDir, "CCG", "BIG EN");

        Directory.CreateDirectory(zhEnDir);
        Directory.CreateDirectory(zhDeDir);
        Directory.CreateDirectory(ccgEnDir);

        File.WriteAllText(Path.Combine(zhEnDir, "!HotkeysLeikezeENZH.big"), "mock content");
        File.WriteAllText(Path.Combine(zhDeDir, "!HotkeysLeikezeDEZH.big"), "mock content");
        File.WriteAllText(Path.Combine(ccgEnDir, "!HotkeysLeikezeEN.big"), "mock content");

        var originalManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.communityoutpost.addon.hlei"),
            Name = "Leikeze's Hotkeys",
            ContentType = GenHub.Core.Models.Enums.ContentType.Addon,
            Publisher = new PublisherInfo { PublisherType = "communityoutpost" },
            Metadata = new ContentMetadata
            {
                Tags = ["contentCode:hlei"],
            },
        };

        // Act
        var manifests = await _factory.CreateManifestsFromExtractedContentAsync(originalManifest, _tempDir);

        // Assert
        Assert.Equal(3, manifests.Count);

        var zhEnManifest = manifests.FirstOrDefault(m => m.Id.Value.Contains("-zerohour-en"));
        Assert.NotNull(zhEnManifest);
        Assert.Equal(GameType.ZeroHour, zhEnManifest.TargetGame);
        Assert.Contains("(EN)", zhEnManifest.Name);
        Assert.Single(zhEnManifest.Files);

        var zhDeManifest = manifests.FirstOrDefault(m => m.Id.Value.Contains("-zerohour-de"));
        Assert.NotNull(zhDeManifest);
        Assert.Equal(GameType.ZeroHour, zhDeManifest.TargetGame);
        Assert.Contains("(DE)", zhDeManifest.Name);

        var ccgEnManifest = manifests.FirstOrDefault(m => m.Id.Value.Contains("-generals-en"));
        Assert.NotNull(ccgEnManifest);
        Assert.Equal(GameType.Generals, ccgEnManifest.TargetGame);
        Assert.Contains("[Generals]", ccgEnManifest.Name);
    }

    /// <summary>
    /// Verifies that content with no variants returns a single manifest.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task CreateManifestsFromExtractedContentAsync_WithNoVariants_ShouldReturnSingleManifest()
    {
        // Arrange
        File.WriteAllText(Path.Combine(_tempDir, "mod.big"), "mock content");

        var originalManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.communityoutpost.addon.gent"),
            Name = "GenTool",
            ContentType = GenHub.Core.Models.Enums.ContentType.Addon,
            Publisher = new PublisherInfo { PublisherType = "communityoutpost" },
            Metadata = new ContentMetadata
            {
                Tags = ["contentCode:gent"],
            },
        };

        // Act
        var manifests = await _factory.CreateManifestsFromExtractedContentAsync(originalManifest, _tempDir);

        // Assert
        Assert.Single(manifests);
        Assert.Equal("1.0.communityoutpost.addon.gent", manifests[0].Id.Value);
        Assert.Single(manifests[0].Files);
    }
}
