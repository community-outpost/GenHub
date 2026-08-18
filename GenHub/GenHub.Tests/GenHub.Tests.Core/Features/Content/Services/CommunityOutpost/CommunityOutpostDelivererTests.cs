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
    /// Surfaces a cancellation that lands part-way through extraction as a cancellation rather than
    /// as an ordinary extraction failure, so callers can tell a user who changed their mind from a
    /// hostile or broken archive. The downloaded archive is the only complete copy of the content,
    /// so it must survive, and the truncated file set must never reach the manifest pool.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task DeliverContentAsync_CancelledDuringExtraction_KeepsArchiveAndRegistersNothingAsync()
    {
        const int entryCount = 6;
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
                CreateArchive(
                    destination,
                    Enumerable.Range(0, entryCount).Select(index => $"entry{index}.dat").ToArray());
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

        using var cancellation = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            deliverer.DeliverContentAsync(
                manifest,
                targetDirectory,
                new CancelOnExtractingReport(cancellation),
                cancellation.Token));

        Assert.True(
            File.Exists(Path.Combine(targetDirectory, "content.zip")),
            "the archive is the only recoverable copy of the content");
        Assert.True(Directory.GetFiles(targetDirectory, "entry*.dat", SearchOption.AllDirectories).Length < entryCount);

        manifestPool.Verify(
            p => p.AddManifestAsync(
                It.IsAny<ContentManifest>(),
                It.IsAny<string>(),
                It.IsAny<IProgress<ContentStorageProgress>?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static CommunityOutpostDeliverer CreateDeliverer(
        IDownloadService downloadService,
        IContentManifestPool manifestPool)
    {
        var converter = new CompressedImageToTgaConverter(NullLogger<CompressedImageToTgaConverter>.Instance);
        var manifestFactory = new CommunityOutpostManifestFactory(
            NullLogger<CommunityOutpostManifestFactory>.Instance,
            new Mock<IFileHashProvider>().Object,
            converter);

        return new CommunityOutpostDeliverer(
            downloadService,
            manifestPool,
            manifestFactory,
            new Mock<IGameInstallationService>().Object,
            new Mock<IInstallationCasPoolService>().Object,
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

    private static async Task InvokeExtractArchiveAsync(string archivePath, string extractPath)
    {
        var extract = typeof(CommunityOutpostDeliverer).GetMethod(
            "ExtractArchiveAsync",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("CommunityOutpostDeliverer.ExtractArchiveAsync was not found.");

        await (Task)extract.Invoke(null, [archivePath, extractPath, CancellationToken.None])!;
    }

    private sealed class CancelOnExtractingReport(CancellationTokenSource cancellation) : IProgress<ContentAcquisitionProgress>
    {
        public void Report(ContentAcquisitionProgress value)
        {
            if (value.Phase == ContentAcquisitionPhase.Extracting)
            {
                cancellation.Cancel();
            }
        }
    }
}
