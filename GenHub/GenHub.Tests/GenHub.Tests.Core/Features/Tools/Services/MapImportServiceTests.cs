using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Tools.MapManager;
using GenHub.Core.Models.Enums;
using GenHub.Features.Tools.MapManager.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Tests.Core.Features.Tools.Services;

/// <summary>
/// Tests how map ZIP archives are split into path segments, which drives both the traversal
/// check and the grouping of a map with its assets.
/// </summary>
public sealed class MapImportServiceTests : IDisposable
{
    private readonly string _workingDirectory = Path.Combine(
        Path.GetTempPath(),
        "GenHubMapImport",
        Guid.NewGuid().ToString("N"));

    private readonly string _mapDirectory;
    private readonly MapImportService _service;

    /// <summary>
    /// Initializes a new instance of the <see cref="MapImportServiceTests"/> class.
    /// </summary>
    public MapImportServiceTests()
    {
        _mapDirectory = Path.Combine(_workingDirectory, "Maps");
        Directory.CreateDirectory(_mapDirectory);

        var directoryService = new Mock<IMapDirectoryService>();
        directoryService.Setup(d => d.GetMapDirectory(It.IsAny<GameType>())).Returns(_mapDirectory);

        _service = new MapImportService(
            directoryService.Object,
            new HttpClient(),
            new MapNameParser(NullLogger<MapNameParser>.Instance),
            NullLogger<MapImportService>.Instance);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_workingDirectory))
        {
            Directory.Delete(_workingDirectory, recursive: true);
        }
    }

    /// <summary>
    /// Rejects a backslash-separated traversal segment. Splitting on backslashes is what makes the
    /// leading <c>..</c> visible as its own segment.
    /// </summary>
    [Fact]
    public void ValidateZip_RejectsBackslashTraversalSegment()
    {
        var zipPath = Path.Combine(_workingDirectory, "traversal.zip");
        CreateZip(zipPath, ("..\\escaped.map", "map"));

        var (isValid, errorMessage) = _service.ValidateZip(zipPath);

        Assert.False(isValid);
        Assert.Contains("path traversal", errorMessage, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Resolves a map and its asset to the same backslash-separated directory. Without splitting on
    /// backslashes each entry becomes its own directory, and the asset is reported as a directory
    /// holding no map.
    /// </summary>
    [Fact]
    public void ValidateZip_ResolvesBackslashSeparatedEntriesToTheSameDirectory()
    {
        var zipPath = Path.Combine(_workingDirectory, "backslash.zip");
        CreateZip(
            zipPath,
            ("Desert\\desert.map", "map"),
            ("Desert\\map.tga", "thumbnail"));

        var (isValid, errorMessage) = _service.ValidateZip(zipPath);

        Assert.True(isValid, errorMessage);
    }

    /// <summary>
    /// Keeps an apostrophe inside a directory name intact, so the map and its assets stay grouped
    /// under the directory the archive actually declared.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ImportFromZipAsync_KeepsDirectoryNamesContainingApostrophesIntactAsync()
    {
        var zipPath = Path.Combine(_workingDirectory, "apostrophe.zip");
        CreateZip(
            zipPath,
            ("Bob's Map/bob.map", "map"),
            ("Bob's Map/map.tga", "thumbnail"));

        var result = await _service.ImportFromZipAsync(zipPath, GameType.ZeroHour);

        Assert.True(result.Success, string.Join(" ", result.Errors));
        var imported = Assert.Single(result.ImportedMaps);
        Assert.Equal("Bob's Map", imported.DirectoryName);
        Assert.True(File.Exists(Path.Combine(_mapDirectory, "Bob's Map", "bob.map")));
        Assert.True(File.Exists(Path.Combine(_mapDirectory, "Bob's Map", "map.tga")));
    }

    /// <summary>
    /// Surfaces a cancellation that lands part-way through an archive as a cancellation. Maps
    /// extracted before the cancellation must not be reported as a successful import, because the
    /// caller would otherwise treat a truncated map set as the whole archive.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ImportFromZipAsync_CancelledMidArchive_DoesNotReportSuccessAsync()
    {
        var zipPath = Path.Combine(_workingDirectory, "cancelled.zip");
        CreateZip(
            zipPath,
            ("First/first.map", "map"),
            ("Second/second.map", "map"));

        using var cancellation = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _service.ImportFromZipAsync(
                zipPath,
                GameType.ZeroHour,
                new CancelOnFirstReport(cancellation),
                cancellation.Token));

        Assert.Single(Directory.GetDirectories(_mapDirectory));
    }

    /// <summary>
    /// Skips only the map whose directory cannot be created and keeps importing the rest. Creating
    /// that directory is the first thing done for a map and can fail on its own — here a file
    /// already occupies the name — so it belongs inside the per-map handler rather than in front of
    /// it, where one bad name would sink the whole archive.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ImportFromZipAsync_MapDirectoryThatCannotBeCreated_SkipsOnlyThatMapAsync()
    {
        var zipPath = Path.Combine(_workingDirectory, "blocked.zip");
        CreateZip(
            zipPath,
            ("Blocked/blocked.map", "map"),
            ("Second/second.map", "map"));
        await File.WriteAllTextAsync(Path.Combine(_mapDirectory, "Blocked"), "not a directory");

        var result = await _service.ImportFromZipAsync(zipPath, GameType.ZeroHour);

        Assert.True(result.Success, string.Join(" ", result.Errors));
        var imported = Assert.Single(result.ImportedMaps);
        Assert.Equal("Second", imported.DirectoryName);
        Assert.NotEmpty(result.Errors);
        Assert.False(Directory.Exists(Path.Combine(_mapDirectory, "Blocked")));
    }

    /// <summary>
    /// Verifies that ImportFromUrlAsync unwraps a map share URI and downloads the inner URL.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ImportFromUrlAsync_WithMapShareUri_DownloadsInnerUrlAsync()
    {
        const string innerUrl = "https://example.com/cool.map";
        Uri? requestedUri = null;
        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((request, _) => requestedUri = request.RequestUri)
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent("fake-map-bytes"),
            });

        var directoryService = new Mock<IMapDirectoryService>();
        directoryService.Setup(d => d.GetMapDirectory(It.IsAny<GameType>())).Returns(_mapDirectory);
        var service = new MapImportService(
            directoryService.Object,
            new HttpClient(mockHandler.Object),
            new MapNameParser(NullLogger<MapNameParser>.Instance),
            NullLogger<MapImportService>.Instance,
            downloadUrlValidator: CreateValidator(true).Object);

        var result = await service.ImportFromUrlAsync(
            $"genhub://map/import?url={Uri.EscapeDataString(innerUrl)}&game=zerohour",
            GameType.ZeroHour);

        Assert.True(result.Success, string.Join(" ", result.Errors));
        Assert.Equal(innerUrl, requestedUri?.ToString());
        Assert.Equal(1, result.FilesImported);
    }

    /// <summary>
    /// Verifies that ImportFromUrlAsync rejects share URIs targeting the replay manager.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ImportFromUrlAsync_WithReplayShareUri_ReturnsCrossToolErrorAsync()
    {
        var result = await _service.ImportFromUrlAsync(
            "genhub://replay/import?url=https%3A%2F%2Fexample.com%2Freplay.rep",
            GameType.ZeroHour);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("Replay Manager", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies that ImportFromUrlAsync reports cross-tool share URIs with the localized message.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ImportFromUrlAsync_WithReplayShareUri_UsesLocalizedErrorAsync()
    {
        var localizationService = new Mock<ILocalizationService>();
        localizationService
            .Setup(l => l.GetString("Tools.Share.Error.CrossToolReplayLink", It.IsAny<object?[]>()))
            .Returns("LOCALIZED cross-tool replay error");

        var directoryService = new Mock<IMapDirectoryService>();
        directoryService.Setup(d => d.GetMapDirectory(It.IsAny<GameType>())).Returns(_mapDirectory);
        var service = new MapImportService(
            directoryService.Object,
            new HttpClient(),
            new MapNameParser(NullLogger<MapNameParser>.Instance),
            NullLogger<MapImportService>.Instance,
            localizationService.Object);

        var result = await service.ImportFromUrlAsync(
            "genhub://replay/import?url=https%3A%2F%2Fexample.com%2Freplay.rep",
            GameType.ZeroHour);

        Assert.False(result.Success);
        Assert.Contains("LOCALIZED cross-tool replay error", result.Errors);
    }

    /// <summary>
    /// Verifies that ImportFromUrlAsync rejects malformed GenHub links with a clean error instead
    /// of attempting a download.
    /// </summary>
    /// <param name="url">The malformed share URI to import.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Theory]
    [InlineData("genhub://map/import")]
    [InlineData("genhub://map/import?url=not-a-url")]
    [InlineData("genhub://subscribe?url=https%3A%2F%2Fexample.com%2Fcatalog.json")]
    public async Task ImportFromUrlAsync_WithMalformedShareUri_ReturnsInvalidLinkErrorAsync(string url)
    {
        Uri? requestedUri = null;
        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((request, _) => requestedUri = request.RequestUri)
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent("fake-map-bytes"),
            });

        var localizationService = new Mock<ILocalizationService>();
        localizationService
            .Setup(l => l.GetString("Tools.Share.Error.InvalidShareLink", It.IsAny<object?[]>()))
            .Returns("LOCALIZED invalid share link");

        var directoryService = new Mock<IMapDirectoryService>();
        directoryService.Setup(d => d.GetMapDirectory(It.IsAny<GameType>())).Returns(_mapDirectory);
        var service = new MapImportService(
            directoryService.Object,
            new HttpClient(mockHandler.Object),
            new MapNameParser(NullLogger<MapNameParser>.Instance),
            NullLogger<MapImportService>.Instance,
            localizationService.Object,
            CreateValidator(true).Object);

        var result = await service.ImportFromUrlAsync(url, GameType.ZeroHour);

        Assert.False(result.Success);
        Assert.Contains("LOCALIZED invalid share link", result.Errors);
        Assert.Null(requestedUri);
    }

    /// <summary>
    /// Verifies that ImportFromUrlAsync blocks non-public download targets before connecting.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ImportFromUrlAsync_WithBlockedDownloadUrl_ReturnsBlockedErrorAsync()
    {
        Uri? requestedUri = null;
        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((request, _) => requestedUri = request.RequestUri)
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent("fake-map-bytes"),
            });

        var directoryService = new Mock<IMapDirectoryService>();
        directoryService.Setup(d => d.GetMapDirectory(It.IsAny<GameType>())).Returns(_mapDirectory);
        var service = new MapImportService(
            directoryService.Object,
            new HttpClient(mockHandler.Object),
            new MapNameParser(NullLogger<MapNameParser>.Instance),
            NullLogger<MapImportService>.Instance,
            downloadUrlValidator: CreateValidator(false).Object);

        var result = await service.ImportFromUrlAsync("http://192.168.1.9/cool.map", GameType.ZeroHour);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("public internet", StringComparison.OrdinalIgnoreCase));
        Assert.Null(requestedUri);
    }

    /// <summary>
    /// Verifies that flat ZIP archives (containing .map and .tga at the archive root)
    /// extract both the map and the preview thumbnail into a folder matching the map name.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ImportFromZipAsync_FlatZipWithMapAndTga_ExtractsMapAndThumbnailAsync()
    {
        var zipPath = Path.Combine(_workingDirectory, "flat_map.zip");
        CreateZip(
            zipPath,
            ("Last Stand_8.map", "map data"),
            ("Last Stand_8.tga", "tga preview data"));

        var result = await _service.ImportFromZipAsync(zipPath, GameType.ZeroHour);

        Assert.True(result.Success, string.Join(" ", result.Errors));
        var imported = Assert.Single(result.ImportedMaps);
        Assert.Equal("Last Stand_8", imported.DirectoryName);
        Assert.True(File.Exists(Path.Combine(_mapDirectory, "Last Stand_8", "Last Stand_8.map")));
        Assert.True(File.Exists(Path.Combine(_mapDirectory, "Last Stand_8", "Last Stand_8.tga")));
        Assert.NotNull(imported.ThumbnailPath);
        Assert.True(File.Exists(imported.ThumbnailPath));
    }

    /// <summary>
    /// Verifies that archive folders ending in .map have the extension stripped from the directory name.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ImportFromZipAsync_DirectoryEndingInDotMap_StripsDotMapExtensionAsync()
    {
        var zipPath = Path.Combine(_workingDirectory, "dot_map_folder.zip");
        CreateZip(
            zipPath,
            ("Last Stand_8.map/Last Stand_8.map", "map data"),
            ("Last Stand_8.map/Last Stand_8.tga", "tga preview data"));

        var result = await _service.ImportFromZipAsync(zipPath, GameType.ZeroHour);

        Assert.True(result.Success, string.Join(" ", result.Errors));
        var imported = Assert.Single(result.ImportedMaps);
        Assert.Equal("Last Stand_8", imported.DirectoryName);
        Assert.True(File.Exists(Path.Combine(_mapDirectory, "Last Stand_8", "Last Stand_8.map")));
        Assert.True(File.Exists(Path.Combine(_mapDirectory, "Last Stand_8", "Last Stand_8.tga")));
    }

    /// <summary>
    /// Verifies that when an archive contains a generic map.tga thumbnail, an engine-compatible
    /// &lt;MapName&gt;.tga copy is created in the map folder.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ImportFromZipAsync_MapWithGenericMapTga_CreatesMapDirNamedTgaPreviewAsync()
    {
        var zipPath = Path.Combine(_workingDirectory, "generic_thumbnail.zip");
        CreateZip(
            zipPath,
            ("Desert/desert.map", "map data"),
            ("Desert/map.tga", "generic thumbnail data"));

        var result = await _service.ImportFromZipAsync(zipPath, GameType.ZeroHour);

        Assert.True(result.Success, string.Join(" ", result.Errors));
        var imported = Assert.Single(result.ImportedMaps);
        Assert.Equal("Desert", imported.DirectoryName);
        Assert.True(File.Exists(Path.Combine(_mapDirectory, "Desert", "desert.map")));
        Assert.True(File.Exists(Path.Combine(_mapDirectory, "Desert", "map.tga")));
        Assert.True(File.Exists(Path.Combine(_mapDirectory, "Desert", "Desert.tga")));
    }

    private static Mock<IDownloadUrlValidator> CreateValidator(bool result)
    {
        var validator = new Mock<IDownloadUrlValidator>();
        validator.Setup(v => v.IsSafeAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>())).ReturnsAsync(result);
        return validator;
    }

    private static void CreateZip(string zipPath, params (string EntryName, string Content)[] entries)
    {
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (var (entryName, content) in entries)
        {
            var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
            using var stream = entry.Open();
            stream.Write(Encoding.UTF8.GetBytes(content));
        }
    }

    private sealed class CancelOnFirstReport(CancellationTokenSource cancellation) : IProgress<double>
    {
        public void Report(double value) => cancellation.Cancel();
    }
}
