using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Features.Content.Services.Catalog;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Content.Services.Catalog;

/// <summary>
/// Entry-point baking coverage for <see cref="GenericCatalogManifestFactory"/>: the shared
/// helper must behave identically here as in the GitHub factory.
/// </summary>
public sealed class GenericCatalogManifestFactoryEntryTests : IDisposable
{
    private readonly Mock<IFileHashProvider> _hashProviderMock;
    private readonly Mock<IArchivePayloadProcessor> _archiveProcessorMock;
    private readonly GenericCatalogManifestFactory _factory;
    private readonly string _tempDirectory;

    /// <summary>
    /// Initializes a new instance of the <see cref="GenericCatalogManifestFactoryEntryTests"/> class.
    /// </summary>
    public GenericCatalogManifestFactoryEntryTests()
    {
        _hashProviderMock = new Mock<IFileHashProvider>();
        _archiveProcessorMock = new Mock<IArchivePayloadProcessor>();

        _hashProviderMock
            .Setup(h => h.ComputeFileHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("dummy-sha256");
        _archiveProcessorMock
            .Setup(a => a.ProcessPayloadAsync(It.IsAny<string>(), It.IsAny<ContentType>(), It.IsAny<GameType>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _factory = new GenericCatalogManifestFactory(
            _hashProviderMock.Object,
            NullLogger<GenericCatalogManifestFactory>.Instance,
            _archiveProcessorMock.Object);

        _tempDirectory = Path.Combine(Path.GetTempPath(), "GenHub_CatalogEntryTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    /// <inheritdoc/>
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
    /// A mac client payload gets its Mach-O binary marked executable by magic bytes and
    /// baked as the declared entry.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task CreateManifests_GameClientWithSingleNative_BakesEntryAndMarksExecutableAsync()
    {
        File.WriteAllBytes(Path.Combine(_tempDirectory, "GeneralsOnlineZH"), [0xFE, 0xED, 0xFA, 0xCE, 0x00, 0x00, 0x00, 0x00]);
        await File.WriteAllTextAsync(Path.Combine(_tempDirectory, "README"), "documentation without magic bytes");

        var result = await _factory.CreateManifestsFromExtractedContentAsync(ClientManifest(), _tempDirectory);

        Assert.True(result.Success);
        var manifest = result.Data!.Single();
        Assert.Equal("GeneralsOnlineZH", manifest.EntryPoint);
        Assert.True(manifest.Files.Single(f => f.RelativePath == "GeneralsOnlineZH").IsExecutable);
        Assert.False(manifest.Files.Single(f => f.RelativePath == "README").IsExecutable);
    }

    /// <summary>
    /// An ambiguous client payload fails instead of producing a manifest that cannot launch.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task CreateManifests_AmbiguousGameClient_FailsAsync()
    {
        File.WriteAllBytes(Path.Combine(_tempDirectory, "alpha"), [0xFE, 0xED, 0xFA, 0xCE, 0x00, 0x00, 0x00, 0x00]);
        File.WriteAllBytes(Path.Combine(_tempDirectory, "beta"), [0x7F, 0x45, 0x4C, 0x46, 0x02, 0x01, 0x01, 0x00]);

        var result = await _factory.CreateManifestsFromExtractedContentAsync(ClientManifest(), _tempDirectory);

        Assert.False(result.Success);
    }

    /// <summary>
    /// Non-client content is unaffected by entry baking.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task CreateManifests_ModContent_SucceedsWithoutEntryAsync()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDirectory, "patch.big"), "archive");

        var original = ClientManifest();
        original.ContentType = ContentType.Mod;

        var result = await _factory.CreateManifestsFromExtractedContentAsync(original, _tempDirectory);

        Assert.True(result.Success);
        Assert.Null(result.Data!.Single().EntryPoint);
    }

    private static ContentManifest ClientManifest()
    {
        return new ContentManifest
        {
            Id = ManifestId.Create("1.0.test.gameclient.sample"),
            Name = "Sample Client",
            Version = "1.0",
            ContentType = ContentType.GameClient,
            TargetGame = GameType.ZeroHour,
            Publisher = new PublisherInfo { Name = "Test", PublisherType = "catalog" },
        };
    }
}
