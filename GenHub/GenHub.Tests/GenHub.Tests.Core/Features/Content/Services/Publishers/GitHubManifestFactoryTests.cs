using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Features.Content.Services.Publishers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Content.Services.Publishers;

/// <summary>
/// Unit tests for <see cref="GitHubManifestFactory"/>.
/// </summary>
public sealed class GitHubManifestFactoryTests : IDisposable
{
    private readonly Mock<IFileHashProvider> _hashProviderMock;
    private readonly Mock<IArchivePayloadProcessor> _archiveProcessorMock;
    private readonly Mock<IControlBarPackageProcessor> _controlBarProcessorMock;
    private readonly GitHubManifestFactory _factory;
    private readonly string _tempDirectory;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitHubManifestFactoryTests"/> class.
    /// </summary>
    public GitHubManifestFactoryTests()
    {
        _hashProviderMock = new Mock<IFileHashProvider>();
        _archiveProcessorMock = new Mock<IArchivePayloadProcessor>();
        _controlBarProcessorMock = new Mock<IControlBarPackageProcessor>();

        _hashProviderMock
            .Setup(h => h.ComputeFileHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("dummy-sha256");

        _factory = new GitHubManifestFactory(
            NullLogger<GitHubManifestFactory>.Instance,
            _hashProviderMock.Object,
            _archiveProcessorMock.Object,
            _controlBarProcessorMock.Object);

        _tempDirectory = Path.Combine(Path.GetTempPath(), "GenHub_GHFactoryTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            try
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
            catch
            {
                // Best-effort cleanup
            }
        }
    }

    /// <summary>
    /// Verifies CanHandle correctly identifies github publisher types.
    /// </summary>
    /// <param name="publisherType">The publisher type string.</param>
    /// <param name="expected">Expected match result.</param>
    [Theory]
    [InlineData("github", true)]
    [InlineData("GitHub", true)]
    [InlineData("GITHUB", true)]
    [InlineData("communityoutpost", false)]
    [InlineData("moddb", false)]
    [InlineData(null, false)]
    public void CanHandle_MatchesPublisherType(string? publisherType, bool expected)
    {
        var manifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.test.mod.item"),
            Publisher = publisherType != null ? new PublisherInfo { Name = "Test", PublisherType = publisherType } : null!,
        };

        var result = _factory.CanHandle(manifest);
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Verifies that CreateManifestsFromExtractedContentAsync invokes archive processing before hashing files.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task CreateManifestsFromExtractedContentAsync_ProcessesArchivesBeforeHashing()
    {
        // Arrange
        var testFilePath = Path.Combine(_tempDirectory, "test.big");
        await File.WriteAllTextAsync(testFilePath, "big-content");

        var manifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.test.mod.sample"),
            Name = "Sample Mod",
            Version = "1.0",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            Publisher = new PublisherInfo { Name = "Test", PublisherType = "github" },
        };

        var executionOrder = new System.Collections.Generic.List<string>();

        _archiveProcessorMock
            .Setup(a => a.ProcessPayloadAsync(It.IsAny<string>(), It.IsAny<ContentType>(), It.IsAny<GameType>(), It.IsAny<CancellationToken>()))
            .Callback(() => executionOrder.Add("ProcessPayload"))
            .Returns(Task.CompletedTask);

        _hashProviderMock
            .Setup(h => h.ComputeFileHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => executionOrder.Add("ComputeHash"))
            .ReturnsAsync("sha256-hash");

        // Act
        var manifests = await _factory.CreateManifestsFromExtractedContentAsync(manifest, _tempDirectory);

        // Assert
        Assert.Single(manifests);
        var created = manifests[0];
        Assert.Single(created.Files);
        Assert.Equal("test.big", created.Files[0].RelativePath);
        Assert.Equal("sha256-hash", created.Files[0].Hash);

        Assert.Equal(2, executionOrder.Count);
        Assert.Equal("ProcessPayload", executionOrder[0]);
        Assert.Equal("ComputeHash", executionOrder[1]);

        _archiveProcessorMock.Verify(
            a => a.ProcessPayloadAsync(_tempDirectory, ContentType.Mod, GameType.ZeroHour, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that if Control Bar content is detected, repack is invoked.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task CreateManifestsFromExtractedContentAsync_RepacksControlBarWhenDetected()
    {
        // Arrange
        var testFilePath = Path.Combine(_tempDirectory, "340_ControlBarProZH.big");
        await File.WriteAllTextAsync(testFilePath, "metadata-content");

        var manifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.test.controlbar.sample"),
            Name = "Sample Control Bar",
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            Publisher = new PublisherInfo { Name = "Test", PublisherType = "github" },
        };

        _controlBarProcessorMock
            .Setup(c => c.IsControlBarContent(_tempDirectory, manifest))
            .Returns(true);

        _controlBarProcessorMock
            .Setup(c => c.ProcessAndRepackControlBarAsync(_tempDirectory, manifest, null, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "340_ControlBarProZH.big" });

        // Act
        var manifests = await _factory.CreateManifestsFromExtractedContentAsync(manifest, _tempDirectory);

        // Assert
        Assert.Single(manifests);
        _controlBarProcessorMock.Verify(
            c => c.ProcessAndRepackControlBarAsync(_tempDirectory, manifest, null, true, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
