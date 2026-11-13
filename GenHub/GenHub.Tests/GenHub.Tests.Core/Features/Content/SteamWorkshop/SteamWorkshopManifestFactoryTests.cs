using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Features.Content.Services.SteamWorkshop;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;
using ParsedContentDetails = GenHub.Core.Models.Content.ParsedContentDetails;

namespace GenHub.Tests.Core.Features.Content.SteamWorkshop;

/// <summary>
/// Unit tests for <see cref="SteamWorkshopManifestFactory"/>.
/// </summary>
public sealed class SteamWorkshopManifestFactoryTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "SteamWorkshopFactoryTests", Guid.NewGuid().ToString("N"));
    private readonly Mock<IFileHashProvider> _hashProviderMock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="SteamWorkshopManifestFactoryTests"/> class.
    /// </summary>
    public SteamWorkshopManifestFactoryTests()
    {
        Directory.CreateDirectory(_tempDir);
        _hashProviderMock
            .Setup(provider => provider.ComputeFileHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("0123456789abcdef0123456789abcdef");
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
    /// Verifies manifests carry workshop identity, game, type, and publisher metadata.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task CreateManifestAsync_BuildsWorkshopManifestAsync()
    {
        var factory = CreateFactory();
        var details = new ParsedContentDetails(
            Name: "Desert Duel",
            Description: "Great skirmish map.",
            Author: "Mapper",
            PreviewImage: "https://example.com/preview.jpg",
            Screenshots: ["https://example.com/shot.jpg"],
            FileSize: 1024,
            DownloadCount: 10,
            SubmissionDate: new DateTime(2024, 8, 26),
            DownloadUrl: SteamWorkshopHelper.BuildFileDetailsUrl("3790356853"),
            TargetGame: GameType.ZeroHour,
            ContentType: ContentType.Map);

        var manifest = await factory.CreateManifestAsync(details);

        Assert.Equal(ContentType.Map, manifest.ContentType);
        Assert.Equal(GameType.ZeroHour, manifest.TargetGame);
        Assert.Equal(SteamWorkshopConstants.PublisherType, manifest.Publisher.PublisherType);
        Assert.Contains("steamworkshop", manifest.Metadata!.Tags);
        Assert.Equal("https://example.com/preview.jpg", manifest.Metadata.IconUrl);
        Assert.Contains("https://example.com/shot.jpg", manifest.Metadata.ScreenshotUrls!);
        Assert.Equal("desert-duel", manifest.Name);
        Assert.Equal("20240826", manifest.Version);
    }

    /// <summary>
    /// Verifies wrong detail types are rejected.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task CreateManifestAsync_WrongType_ThrowsAsync()
    {
        var factory = CreateFactory();

        await Assert.ThrowsAsync<ArgumentException>(() => factory.CreateManifestAsync(new object()));
    }

    /// <summary>
    /// Verifies workshop tags flow into the manifest tags.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task CreateManifestAsync_WorkshopTags_IncludedAsync()
    {
        var factory = CreateFactory();
        var details = new ParsedContentDetails(
            Name: "Desert Duel",
            Description: "Great skirmish map.",
            Author: "Mapper",
            PreviewImage: string.Empty,
            Screenshots: null,
            FileSize: 64,
            DownloadCount: 1,
            SubmissionDate: new DateTime(2024, 1, 2),
            DownloadUrl: SteamWorkshopHelper.BuildFileDetailsUrl("3790356853"),
            TargetGame: GameType.ZeroHour,
            ContentType: ContentType.Map,
            Tags: ["Multiplayer", "1v1"]);

        var manifest = await factory.CreateManifestAsync(details);

        Assert.Contains("Multiplayer", manifest.Metadata!.Tags);
        Assert.Contains("1v1", manifest.Metadata!.Tags);
        Assert.Contains("steamworkshop", manifest.Metadata!.Tags);
    }

    /// <summary>
    /// Verifies missing dates fall back to the default version.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task CreateManifestAsync_MissingDate_UsesDefaultVersionAsync()
    {
        var factory = CreateFactory();
        var details = new ParsedContentDetails(
            Name: "Desert Duel",
            Description: "Great skirmish map.",
            Author: "Mapper",
            PreviewImage: string.Empty,
            Screenshots: null,
            FileSize: 64,
            DownloadCount: 1,
            SubmissionDate: DateTime.MinValue,
            DownloadUrl: SteamWorkshopHelper.BuildFileDetailsUrl("3790356853"),
            TargetGame: GameType.ZeroHour,
            ContentType: ContentType.Map);

        var manifest = await factory.CreateManifestAsync(details);

        Assert.Equal("1", manifest.Version);
    }

    /// <summary>
    /// Verifies versions stay Gregorian under non-Gregorian cultures.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task CreateManifestAsync_NonGregorianCulture_UsesInvariantVersionAsync()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("th-TH");
            var factory = CreateFactory();
            var details = new ParsedContentDetails(
                Name: "Desert Duel",
                Description: "Great skirmish map.",
                Author: "Mapper",
                PreviewImage: string.Empty,
                Screenshots: null,
                FileSize: 64,
                DownloadCount: 1,
                SubmissionDate: new DateTime(2024, 8, 26),
                DownloadUrl: SteamWorkshopHelper.BuildFileDetailsUrl("3790356853"),
                TargetGame: GameType.ZeroHour,
                ContentType: ContentType.Map);

            var manifest = await factory.CreateManifestAsync(details);

            Assert.Equal("20240826", manifest.Version);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    /// <summary>
    /// Verifies delivered workshop files are scanned into hashed manifest entries.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task CreateManifestsFromExtractedContentAsync_ScansDeliveredFilesAsync()
    {
        var factory = CreateFactory();
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "duel.map"), "map-bytes");
        Directory.CreateDirectory(Path.Combine(_tempDir, "art"));
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "art", "preview.tga"), "preview-bytes");
        var original = new ContentManifest
        {
            Id = "1.0.steamworkshop.map.duel",
            Name = "Desert Duel",
            ContentType = ContentType.Map,
            TargetGame = GameType.ZeroHour,
        };

        var result = await factory.CreateManifestsFromExtractedContentAsync(original, _tempDir);

        Assert.True(result.Success);
        var manifest = Assert.Single(result.Data!);
        Assert.Equal(2, manifest.Files.Count);
        Assert.All(manifest.Files, file =>
        {
            Assert.Equal("0123456789abcdef0123456789abcdef", file.Hash);
            Assert.Equal(ContentInstallTarget.UserMapsDirectory, file.InstallTarget);
        });
    }

    /// <summary>
    /// Verifies maps, map packs, and missions install into the user maps directory.
    /// </summary>
    /// <param name="contentType">The content type.</param>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Theory]
    [InlineData(ContentType.Map)]
    [InlineData(ContentType.MapPack)]
    [InlineData(ContentType.Mission)]
    public async Task CreateManifestsFromExtractedContentAsync_MapTypes_UseUserMapsDirectoryAsync(ContentType contentType)
    {
        var factory = CreateFactory();
        var contentDir = Path.Combine(_tempDir, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(contentDir);
        await File.WriteAllTextAsync(Path.Combine(contentDir, "item.map"), "item");
        var original = new ContentManifest
        {
            Id = $"1.0.steamworkshop.{contentType.ToString().ToLowerInvariant()}.item",
            Name = "Item",
            ContentType = contentType,
            TargetGame = GameType.ZeroHour,
        };

        var result = await factory.CreateManifestsFromExtractedContentAsync(original, contentDir);

        Assert.True(result.Success);
        Assert.All(
            Assert.Single(result.Data!).Files,
            file => Assert.Equal(ContentInstallTarget.UserMapsDirectory, file.InstallTarget));
    }

    /// <summary>
    /// Verifies staging leftovers are excluded from the manifest.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task CreateManifestsFromExtractedContentAsync_StagingFile_ExcludedAsync()
    {
        var factory = CreateFactory();
        var stagingDir = Path.Combine(_tempDir, "staging");
        Directory.CreateDirectory(stagingDir);
        await File.WriteAllTextAsync(Path.Combine(stagingDir, "duel.map"), "map-bytes");
        await File.WriteAllTextAsync(Path.Combine(stagingDir, $"duel.map.{Guid.NewGuid():N}.tmp"), "partial");
        var original = new ContentManifest
        {
            Id = "1.0.steamworkshop.map.duel",
            Name = "Desert Duel",
            ContentType = ContentType.Map,
            TargetGame = GameType.ZeroHour,
        };

        var result = await factory.CreateManifestsFromExtractedContentAsync(original, stagingDir);

        Assert.True(result.Success);
        var manifest = Assert.Single(result.Data!);
        var file = Assert.Single(manifest.Files);
        Assert.Equal("duel.map", file.RelativePath);
    }

    /// <summary>
    /// Verifies per-file progress completes at 100 percent.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task CreateManifestsFromExtractedContentAsync_ReportsProgressAsync()
    {
        var factory = CreateFactory();
        var progressDir = Path.Combine(_tempDir, "progress");
        Directory.CreateDirectory(progressDir);
        await File.WriteAllTextAsync(Path.Combine(progressDir, "one.map"), "one");
        await File.WriteAllTextAsync(Path.Combine(progressDir, "two.map"), "two");
        var original = new ContentManifest
        {
            Id = "1.0.steamworkshop.map.duel",
            Name = "Desert Duel",
            ContentType = ContentType.Map,
            TargetGame = GameType.ZeroHour,
        };
        var progress = new SynchronousProgress<ContentAcquisitionProgress>();

        var result = await factory.CreateManifestsFromExtractedContentAsync(original, progressDir, progress);

        Assert.True(result.Success);
        Assert.Equal(2, progress.Reports.Count);
        Assert.All(progress.Reports, report => Assert.Equal(2, report.TotalFiles));
        Assert.Equal(100, progress.Reports[^1].ProgressPercentage);
        Assert.Equal(2, progress.Reports[^1].FilesProcessed);
    }

    private sealed class SynchronousProgress<T> : IProgress<T>
    {
        private readonly object _lock = new();

        /// <summary>
        /// Gets the captured progress reports.
        /// </summary>
        public List<T> Reports { get; } = [];

        /// <inheritdoc/>
        public void Report(T value)
        {
            lock (_lock)
            {
                Reports.Add(value);
            }
        }
    }

    /// <summary>
    /// Verifies missing directories fail instead of reporting an empty install.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task CreateManifestsFromExtractedContentAsync_MissingDirectory_ReturnsFailureAsync()
    {
        var factory = CreateFactory();
        var original = new ContentManifest
        {
            Id = "1.0.steamworkshop.map.duel",
            Name = "Desert Duel",
            ContentType = ContentType.Map,
        };

        var result = await factory.CreateManifestsFromExtractedContentAsync(original, Path.Combine(_tempDir, "missing"));

        Assert.False(result.Success);
    }

    /// <summary>
    /// Verifies empty deliveries fail instead of reporting an empty install.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task CreateManifestsFromExtractedContentAsync_EmptyDirectory_ReturnsFailureAsync()
    {
        var factory = CreateFactory();
        var emptyDir = Path.Combine(_tempDir, "empty");
        Directory.CreateDirectory(emptyDir);
        var original = new ContentManifest
        {
            Id = "1.0.steamworkshop.map.duel",
            Name = "Desert Duel",
            ContentType = ContentType.Map,
        };

        var result = await factory.CreateManifestsFromExtractedContentAsync(original, emptyDir);

        Assert.False(result.Success);
    }

    private SteamWorkshopManifestFactory CreateFactory()
    {
        return new SteamWorkshopManifestFactory(
            SteamWorkshopTestBuilders.CreateBuilder,
            Mock.Of<IProviderDefinitionLoader>(),
            _hashProviderMock.Object,
            Mock.Of<ILogger<SteamWorkshopManifestFactory>>());
    }
}
