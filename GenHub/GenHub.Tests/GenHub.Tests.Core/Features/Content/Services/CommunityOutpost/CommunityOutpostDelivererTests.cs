using System.IO.Compression;
using System.Reflection;
using System.Text;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Features.Content.Services.CommunityOutpost;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace GenHub.Tests.Core.Features.Content.Services.CommunityOutpost;

/// <summary>
/// Tests the containment and expansion bounds applied to Community Outpost archives, which arrive
/// from a third-party catalog and are therefore untrusted input.
/// </summary>
public sealed class CommunityOutpostDelivererTests : IDisposable
{
    private readonly string _workingDirectory = Path.Combine(
        Path.GetTempPath(),
        "GenHubCommunityOutpost",
        Guid.NewGuid().ToString("N"));

    private readonly string _extractDirectory;

    /// <summary>
    /// Initializes a new instance of the <see cref="CommunityOutpostDelivererTests"/> class.
    /// </summary>
    public CommunityOutpostDelivererTests()
    {
        _extractDirectory = Path.Combine(_workingDirectory, "extracted");
        Directory.CreateDirectory(_extractDirectory);
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
    /// Extracts entries that stay inside the target directory.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ExtractArchiveAsync_ExtractsEntriesWithinBudgetAsync()
    {
        var archivePath = Path.Combine(_workingDirectory, "content.zip");
        CreateArchive(archivePath, "patch/readme.txt", "generals.big");

        await InvokeExtractArchiveAsync(archivePath, _extractDirectory);

        Assert.True(File.Exists(Path.Combine(_extractDirectory, "patch", "readme.txt")));
        Assert.True(File.Exists(Path.Combine(_extractDirectory, "generals.big")));
    }

    /// <summary>
    /// Refuses an entry whose key climbs out of the extract directory, rather than depending on the
    /// archive library to block it.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ExtractArchiveAsync_RejectsEntryEscapingTheExtractDirectoryAsync()
    {
        var archivePath = Path.Combine(_workingDirectory, "traversal.zip");
        CreateArchive(archivePath, "../escaped.big");

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            InvokeExtractArchiveAsync(archivePath, _extractDirectory));

        Assert.Contains("outside target directory", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(_workingDirectory, "escaped.big")));
    }

    /// <summary>
    /// Refuses an archive that declares more entries than the extraction budget allows.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ExtractArchiveAsync_RejectsArchiveOverTheEntryBudgetAsync()
    {
        var archivePath = Path.Combine(_workingDirectory, "swarm.zip");
        var entryNames = Enumerable
            .Range(0, CommunityOutpostConstants.MaxArchiveEntries + 1)
            .Select(index => $"entry{index}.dat")
            .ToArray();
        CreateArchive(archivePath, entryNames);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            InvokeExtractArchiveAsync(archivePath, _extractDirectory));

        Assert.Contains("too many entries", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.GetFileSystemEntries(_extractDirectory));
    }

    /// <summary>
    /// Refuses an entry whose name cannot name a file before that name is turned into a path. A
    /// name that resolves to the extract directory itself would otherwise stage the write beside
    /// that directory rather than inside it, and a colon names an NTFS alternate data stream.
    /// </summary>
    /// <param name="entryName">The entry name the archive declares.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Theory]
    [InlineData(".")]
    [InlineData("patch/..")]
    [InlineData(" ")]
    [InlineData("payload.big:stream")]
    public async Task ExtractArchiveAsync_RejectsEntryWithAnUnusableNameAsync(string entryName)
    {
        var archivePath = Path.Combine(_workingDirectory, "unusable.zip");
        CreateArchive(archivePath, entryName);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            InvokeExtractArchiveAsync(archivePath, _extractDirectory));

        Assert.Contains("cannot be extracted to a file", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.GetFileSystemEntries(_workingDirectory, "*.genhub-staging*"));
    }

    /// <summary>
    /// Surfaces a cancellation that lands part-way through extraction as a cancellation rather than
    /// as an ordinary extraction failure, so callers can tell a user who changed their mind from a
    /// hostile or broken archive. The cancellation is triggered once an early entry has landed on
    /// disk and while a much larger one is still being written, which is what puts it inside the
    /// entry loop rather than in front of it. The downloaded archive is the only complete copy of
    /// the content, so it must survive, and the truncated file set must never reach the manifest
    /// pool.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task DeliverContentAsync_CancelledMidExtraction_KeepsArchiveAndRegistersNothingAsync()
    {
        const int largeEntryBytes = 32 * 1024 * 1024;
        var targetDirectory = Path.Combine(_workingDirectory, "target");
        Directory.CreateDirectory(targetDirectory);

        var downloadService = new Mock<IDownloadService>();
        downloadService
            .Setup(d => d.DownloadFileAsync(
                It.IsAny<Uri>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<IProgress<DownloadProgress>?>(),
                It.IsAny<CancellationToken>()))
            .Returns((Uri _, string destination, string? _, IProgress<DownloadProgress>? _, CancellationToken _) =>
            {
                CreateArchive(destination, ("first.dat", 16), ("marker.dat", 16), ("large.dat", largeEntryBytes));
                return Task.FromResult(DownloadResult.CreateSuccess(destination, 1, TimeSpan.FromSeconds(1)));
            });

        var manifestPool = new Mock<IContentManifestPool>();
        var deliverer = CreateDeliverer(downloadService.Object, manifestPool.Object);
        var manifest = new ContentManifest
        {
            Files =
            [
                new ManifestFile
                {
                    RelativePath = "content.zip",
                    DownloadUrl = "https://legi.cc/gp2/f/cbpr.zip",
                },
            ],
        };

        var extractDirectory = Path.Combine(targetDirectory, "extracted");
        using var cancellation = new CancellationTokenSource();
        var cancelWhenMarkerLands = CancelWhenFileAppearsAsync(
            Path.Combine(extractDirectory, "marker.dat"),
            cancellation);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            deliverer.DeliverContentAsync(manifest, targetDirectory, null, cancellation.Token));

        await cancelWhenMarkerLands;

        Assert.True(
            File.Exists(Path.Combine(targetDirectory, "content.zip")),
            "the archive is the only recoverable copy of the content");
        Assert.True(
            File.Exists(Path.Combine(extractDirectory, "first.dat")),
            "the cancellation has to land after extraction started, not in front of it");
        Assert.False(
            File.Exists(Path.Combine(extractDirectory, "large.dat")),
            "the entry being written when the cancellation landed must not be left behind");
        Assert.Empty(Directory.GetFileSystemEntries(extractDirectory, "*.genhub-staging*"));

        manifestPool.Verify(
            p => p.AddManifestAsync(
                It.IsAny<ContentManifest>(),
                It.IsAny<string>(),
                It.IsAny<IProgress<ContentStorageProgress>?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies that multi-variant hotkeys packages like hlei repack all language/game subdirectories.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task RepackContentIfNeededAsync_WithHleiPackage_RepacksAllVariantBigFilesAsync()
    {
        var hleiManifest = new ContentManifest
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

        var zhEnDir = Path.Combine(_extractDirectory, "ZH", "BIG EN", "Data", "English");
        var zhDeDir = Path.Combine(_extractDirectory, "ZH", "BIG DE", "Data", "English");
        var zhRuDir = Path.Combine(_extractDirectory, "ZH", "BIG RU", "Data", "English");
        var ccgEnDir = Path.Combine(_extractDirectory, "CCG", "BIG EN", "Data", "English");

        Directory.CreateDirectory(zhEnDir);
        Directory.CreateDirectory(zhDeDir);
        Directory.CreateDirectory(zhRuDir);
        Directory.CreateDirectory(ccgEnDir);

        File.WriteAllText(Path.Combine(zhEnDir, "generals.csf"), "EN CSF");
        File.WriteAllText(Path.Combine(zhDeDir, "generals.csf"), "DE CSF");
        File.WriteAllText(Path.Combine(zhRuDir, "generals.csf"), "RU CSF");
        File.WriteAllText(Path.Combine(ccgEnDir, "generals.csf"), "CCG CSF");

        var deliverer = CreateDeliverer(new Mock<IDownloadService>().Object, new Mock<IContentManifestPool>().Object);
        var repackMethod = typeof(CommunityOutpostDeliverer).GetMethod(
            "RepackContentIfNeededAsync",
            BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("RepackContentIfNeededAsync method not found.");

        await (Task)repackMethod.Invoke(deliverer, [hleiManifest, _extractDirectory, CancellationToken.None])!;

        Assert.True(File.Exists(Path.Combine(_extractDirectory, "!HotkeysLeikezeENZH.big")));
        Assert.True(File.Exists(Path.Combine(_extractDirectory, "!HotkeysLeikezeDEZH.big")));
        Assert.True(File.Exists(Path.Combine(_extractDirectory, "!HotkeysLeikezeRUZH.big")));
        Assert.True(File.Exists(Path.Combine(_extractDirectory, "!HotkeysLeikezeEN.big")));
        Assert.False(Directory.Exists(Path.Combine(_extractDirectory, "ZH")));
        Assert.False(Directory.Exists(Path.Combine(_extractDirectory, "CCG")));
    }

    /// <summary>
    /// Verifies that pre-existing BIG files inside a variant directory are copied to the resolved variant destination filename.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task RepackContentIfNeededAsync_WithPreExistingBigInVariant_CopiesToResolvedVariantFileNameAsync()
    {
        var hleiManifest = new ContentManifest
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

        var zhEnDir = Path.Combine(_extractDirectory, "ZH", "BIG EN");
        Directory.CreateDirectory(zhEnDir);
        File.WriteAllText(Path.Combine(zhEnDir, "arbitrary_name.big"), "PRE-PACKED BIG CONTENT");

        var deliverer = CreateDeliverer(new Mock<IDownloadService>().Object, new Mock<IContentManifestPool>().Object);
        var repackMethod = typeof(CommunityOutpostDeliverer).GetMethod(
            "RepackContentIfNeededAsync",
            BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("RepackContentIfNeededAsync method not found.");

        await (Task)repackMethod.Invoke(deliverer, [hleiManifest, _extractDirectory, CancellationToken.None])!;

        var destFile = Path.Combine(_extractDirectory, "!HotkeysLeikezeENZH.big");
        Assert.True(File.Exists(destFile));
        Assert.Equal("PRE-PACKED BIG CONTENT", File.ReadAllText(destFile));
    }

    /// <summary>
    /// Verifies that repacking is skipped for content metadata that does not require repacking.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task RepackContentIfNeededAsync_WithNonRepackingContent_SkipsRepackAsync()
    {
        var manifest = new ContentManifest
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

        var dummyFile = Path.Combine(_extractDirectory, "d3d8.dll");
        File.WriteAllText(dummyFile, "DLL CONTENT");

        var deliverer = CreateDeliverer(new Mock<IDownloadService>().Object, new Mock<IContentManifestPool>().Object);
        var repackMethod = typeof(CommunityOutpostDeliverer).GetMethod(
            "RepackContentIfNeededAsync",
            BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("RepackContentIfNeededAsync method not found.");

        await (Task)repackMethod.Invoke(deliverer, [manifest, _extractDirectory, CancellationToken.None])!;

        Assert.True(File.Exists(dummyFile));
    }

    private static CommunityOutpostDeliverer CreateDeliverer(
        IDownloadService downloadService,
        IContentManifestPool manifestPool)
    {
        var converter = new CompressedImageToTgaConverter(NullLogger<CompressedImageToTgaConverter>.Instance);
        return new CommunityOutpostDeliverer(
            downloadService,
            converter,
            NullLogger<CommunityOutpostDeliverer>.Instance);
    }

    private static void CreateArchive(string archivePath, params string[] entryNames)
    {
        using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create);
        foreach (var entryName in entryNames)
        {
            var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
            using var stream = entry.Open();
            stream.Write(Encoding.UTF8.GetBytes(entryName));
        }
    }

    private static void CreateArchive(string archivePath, params (string EntryName, int ByteCount)[] entries)
    {
        using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create);
        foreach (var (entryName, byteCount) in entries)
        {
            var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
            using var stream = entry.Open();
            stream.Write(new byte[byteCount]);
        }
    }

    private static Task CancelWhenFileAppearsAsync(string path, CancellationTokenSource cancellation)
    {
        return Task.Factory.StartNew(
            () =>
            {
                var deadline = DateTime.UtcNow.AddSeconds(30);
                while (!File.Exists(path) && DateTime.UtcNow < deadline)
                {
                }

                cancellation.Cancel();
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    private static async Task InvokeExtractArchiveAsync(string archivePath, string extractPath)
    {
        var extract = typeof(CommunityOutpostDeliverer).GetMethod(
            "ExtractArchiveAsync",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("CommunityOutpostDeliverer.ExtractArchiveAsync was not found.");

        await (Task)extract.Invoke(null, [archivePath, extractPath, CancellationToken.None])!;
    }
}
