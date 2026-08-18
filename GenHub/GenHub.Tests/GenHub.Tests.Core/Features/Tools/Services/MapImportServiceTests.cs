using System.IO.Compression;
using System.Net.Http;
using System.Text;
using GenHub.Core.Interfaces.Tools.MapManager;
using GenHub.Core.Models.Enums;
using GenHub.Features.Tools.MapManager.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

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
}
