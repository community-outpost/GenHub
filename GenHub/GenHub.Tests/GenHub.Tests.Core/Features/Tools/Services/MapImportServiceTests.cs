using GenHub.Core.Constants;
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
using System.Security.Cryptography;
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

    /// <summary>Companions without a map report a useful import failure.</summary>
    /// <param name="archive">Whether to use the archive route.</param>
    /// <returns>The asynchronous test.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImportFromFilesOrArchiveAsync_CompanionsOnly_ReportsMissingMapAsync(bool archive)
    {
        var source = Path.Combine(_workingDirectory, "companions");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "map.str"), "strings");
        await File.WriteAllTextAsync(Path.Combine(source, "preview.tga"), "preview");

        var zipPath = Path.Combine(_workingDirectory, "companions.zip");
        ZipFile.CreateFromDirectory(source, zipPath);
        var result = archive
            ? await _service.ImportFromZipAsync(zipPath, GameType.ZeroHour)
            : await _service.ImportFromFilesAsync([source], GameType.ZeroHour);

        Assert.False(result.Success);
        Assert.Equal(0, result.FilesImported);
        Assert.Contains("No map files were found to import.", result.Errors);
    }

    /// <summary>Empty imports use the configured translation in the displayed error.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ImportFromFilesAsync_NoMaps_UsesLocalizedErrorAsync()
    {
        var directoryService = new Mock<IMapDirectoryService>();
        directoryService.Setup(d => d.GetMapDirectory(It.IsAny<GameType>())).Returns(_mapDirectory);
        var localization = new Mock<ILocalizationService>();
        localization.Setup(l => l.GetString("Maps.Import.Notification.NoMapsFound")).Returns("localized no maps");
        var service = new MapImportService(
            directoryService.Object,
            new HttpClient(),
            new MapNameParser(NullLogger<MapNameParser>.Instance),
            NullLogger<MapImportService>.Instance,
            localizationService: localization.Object);

        var result = await service.ImportFromFilesAsync([], GameType.ZeroHour);

        Assert.False(result.Success);
        Assert.Contains("localized no maps", result.Errors);
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

    /// <summary>
    /// Imports every standard file of a zipped map folder byte for byte, including the .wak,
    /// map.ini and map.str files real map folders carry.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ImportFromZipAsync_MapFolderWithWakIniAndStr_ImportsEveryFileUnchangedAsync()
    {
        var source = CreateStandardMapFolder("Vendetta");
        var zipPath = Path.Combine(_workingDirectory, "vendetta.zip");
        ZipFile.CreateFromDirectory(source, zipPath, CompressionLevel.Optimal, includeBaseDirectory: true);

        var result = await _service.ImportFromZipAsync(zipPath, GameType.ZeroHour);

        Assert.True(result.Success, string.Join(" ", result.Errors));
        Assert.Empty(result.Errors);
        Assert.Single(result.ImportedMaps);
        AssertFolderMatches(source, Path.Combine(_mapDirectory, "Vendetta"));
    }

    /// <summary>
    /// Imports every standard file of a map folder byte for byte when the folder itself is imported.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ImportFromFilesAsync_MapFolder_ImportsEveryFileUnchangedAsync()
    {
        var source = CreateStandardMapFolder("Vendetta");

        var result = await _service.ImportFromFilesAsync([source], GameType.ZeroHour);

        Assert.True(result.Success, string.Join(" ", result.Errors));
        Assert.Empty(result.Errors);
        var imported = Assert.Single(result.ImportedMaps);
        Assert.Equal(4, imported.AssetFiles.Count);
        Assert.Equal(Path.Combine(_mapDirectory, "Vendetta", "Vendetta.tga"), imported.ThumbnailPath);
        AssertFolderMatches(source, Path.Combine(_mapDirectory, "Vendetta"));
    }

    /// <summary>
    /// Imports the companion files of a map folder when only its .map file is selected.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ImportFromFilesAsync_MapFileInsideMapFolder_ImportsEveryFileUnchangedAsync()
    {
        var source = CreateStandardMapFolder("Vendetta");

        var result = await _service.ImportFromFilesAsync([Path.Combine(source, "Vendetta.map")], GameType.ZeroHour);

        Assert.True(result.Success, string.Join(" ", result.Errors));
        AssertFolderMatches(source, Path.Combine(_mapDirectory, "Vendetta"));
    }

    /// <summary>
    /// Keeps named assets apart while distributing shared companions when a folder holds several maps.
    /// </summary>
    /// <param name="importFormat">The import route to exercise.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Theory]
    [InlineData("folder")]
    [InlineData("zip")]
    [InlineData("tar")]
    public async Task ImportFromFilesOrArchiveAsync_FolderWithTwoMaps_CopiesNamedAndSharedAssetsAsync(string importFormat)
    {
        var source = Path.Combine(_workingDirectory, "Source", "Pack");
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "Alpha.map"), "alpha");
        await File.WriteAllTextAsync(Path.Combine(source, "Alpha.wak"), "alpha wak");
        await File.WriteAllTextAsync(Path.Combine(source, "Beta.map"), "beta");
        await File.WriteAllTextAsync(Path.Combine(source, "Beta.tga"), "beta tga");
        await File.WriteAllTextAsync(Path.Combine(source, "map.ini"), "shared ini");
        await File.WriteAllTextAsync(Path.Combine(source, "MAP.STR"), "shared strings");
        await File.WriteAllTextAsync(Path.Combine(source, "map.tga"), "shared preview");

        var importPath = source;
        if (importFormat == "zip")
        {
            importPath = Path.Combine(_workingDirectory, "pack.zip");
            ZipFile.CreateFromDirectory(source, importPath);
        }
        else if (importFormat == "tar")
        {
            importPath = Path.Combine(_workingDirectory, "pack.tar");
            using var archiveStream = File.Create(importPath);
            using var writer = new System.Formats.Tar.TarWriter(archiveStream, System.Formats.Tar.TarEntryFormat.Ustar);
            foreach (var file in Directory.GetFiles(source))
            {
                writer.WriteEntry(file, Path.GetFileName(file));
            }
        }

        Directory.CreateDirectory(Path.Combine(_mapDirectory, "Alpha"));
        Directory.CreateDirectory(Path.Combine(_mapDirectory, "Beta"));
        var result = importFormat == "folder"
            ? await _service.ImportFromFilesAsync([source], GameType.ZeroHour)
            : await _service.ImportFromZipAsync(importPath, GameType.ZeroHour);

        Assert.True(result.Success, string.Join(" ", result.Errors));
        Assert.Equal("shared preview", await File.ReadAllTextAsync(Path.Combine(_mapDirectory, "Alpha (1)", "Alpha.tga")));
        Assert.Equal("beta tga", await File.ReadAllTextAsync(Path.Combine(_mapDirectory, "Beta (1)", "Beta.tga")));
        Assert.Equal(2, result.ImportedMaps.Count);
        Assert.Contains(result.ImportedMaps, map => map.ThumbnailPath == Path.Combine(_mapDirectory, "Alpha (1)", "Alpha.tga"));
        Assert.Contains(result.ImportedMaps, map => map.ThumbnailPath == Path.Combine(_mapDirectory, "Beta (1)", "Beta.tga"));
        Assert.Equal(
            ["Alpha.map", "Alpha.tga", "Alpha.wak", "MAP.STR", "map.ini", "map.tga"],
            Directory.GetFiles(Path.Combine(_mapDirectory, "Alpha (1)")).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.Equal(
            ["Beta.map", "Beta.tga", "MAP.STR", "map.ini", "map.tga"],
            Directory.GetFiles(Path.Combine(_mapDirectory, "Beta (1)")).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// Leaves a disallowed file type behind when a map folder is imported.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ImportFromFilesAsync_MapFolderWithDisallowedFile_SkipsThatFileAsync()
    {
        var source = CreateStandardMapFolder("Vendetta");
        await File.WriteAllTextAsync(Path.Combine(source, "payload.exe"), "not a map file");

        var result = await _service.ImportFromFilesAsync([source], GameType.ZeroHour);

        Assert.True(result.Success, string.Join(" ", result.Errors));
        Assert.Contains(result.Errors, e => e.Contains("payload.exe", StringComparison.Ordinal));
        Assert.False(File.Exists(Path.Combine(_mapDirectory, "Vendetta", "payload.exe")));
    }

    /// <summary>
    /// Leaves a companion file over the asset size limit behind, as zip import does.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ImportFromFilesAsync_MapFolderWithOversizedAsset_SkipsThatAssetAsync()
    {
        var source = CreateStandardMapFolder("Vendetta");
        await using (var oversized = File.Create(Path.Combine(source, "huge.tga")))
        {
            oversized.SetLength(MapManagerConstants.MaxAssetSizeBytes + 1);
        }

        var result = await _service.ImportFromFilesAsync([source], GameType.ZeroHour);

        Assert.True(result.Success, string.Join(" ", result.Errors));
        Assert.Contains(result.Errors, e => e.Contains("huge.tga", StringComparison.Ordinal));
        Assert.False(File.Exists(Path.Combine(_mapDirectory, "Vendetta", "huge.tga")));
        Assert.True(File.Exists(Path.Combine(_mapDirectory, "Vendetta", "map.str")));
    }

    /// <summary>
    /// Keeps rejecting a zip that carries a file type outside the map allowlist.
    /// </summary>
    [Fact]
    public void ValidateZip_RejectsDisallowedFileType()
    {
        var zipPath = Path.Combine(_workingDirectory, "disallowed.zip");
        CreateZip(
            zipPath,
            ("Vendetta/Vendetta.map", "map"),
            ("Vendetta/payload.exe", "binary"));

        var (isValid, errorMessage) = _service.ValidateZip(zipPath);

        Assert.False(isValid);
        Assert.Contains(".exe", errorMessage, StringComparison.Ordinal);
    }

    private static void AssertFolderMatches(string expectedDirectory, string actualDirectory)
    {
        var expected = Directory.GetFiles(expectedDirectory).ToDictionary(f => Path.GetFileName(f)!, HashFile, StringComparer.Ordinal);
        var actual = Directory.GetFiles(actualDirectory).ToDictionary(f => Path.GetFileName(f)!, HashFile, StringComparer.Ordinal);
        Assert.Equal(expected.OrderBy(p => p.Key, StringComparer.Ordinal), actual.OrderBy(p => p.Key, StringComparer.Ordinal));
    }

    private static string HashFile(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

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

    private string CreateStandardMapFolder(string mapName)
    {
        var folder = Path.Combine(_workingDirectory, "Source", mapName);
        Directory.CreateDirectory(folder);
        var files = new[] { $"{mapName}.map", $"{mapName}.tga", $"{mapName}.wak", "map.ini", "map.str" };
        for (var i = 0; i < files.Length; i++)
        {
            var seed = i;
            var content = Enumerable.Range(0, 256 + (i * 97)).Select(j => (byte)((j * 7) + (seed * 31))).ToArray();
            File.WriteAllBytes(Path.Combine(folder, files[i]), content);
        }

        return folder;
    }

    private sealed class CancelOnFirstReport(CancellationTokenSource cancellation) : IProgress<double>
    {
        public void Report(double value) => cancellation.Cancel();
    }
}
