using System.IO.Compression;
using System.Text;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Tools.ReplayManager;
using GenHub.Core.Models.Enums;
using GenHub.Features.Tools.ReplayManager.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace GenHub.Tests.Core.Features.Tools.Services;

/// <summary>
/// Tests how a replay archive import behaves when it is interrupted, which decides whether the
/// caller is told the archive was imported in full.
/// </summary>
public sealed class ReplayImportServiceTests : IDisposable
{
    private readonly string _workingDirectory = Path.Combine(
        Path.GetTempPath(),
        "GenHubReplayImport",
        Guid.NewGuid().ToString("N"));

    private readonly string _replayDirectory;
    private readonly ReplayImportService _service;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReplayImportServiceTests"/> class.
    /// </summary>
    public ReplayImportServiceTests()
    {
        _replayDirectory = Path.Combine(_workingDirectory, "Replays");
        Directory.CreateDirectory(_replayDirectory);

        var directoryService = new Mock<IReplayDirectoryService>();
        directoryService.Setup(d => d.GetReplayDirectory(It.IsAny<GameType>())).Returns(_replayDirectory);

        var zipValidationService = new Mock<IZipValidationService>();
        zipValidationService.Setup(z => z.ValidateZip(It.IsAny<string>())).Returns((true, null));

        _service = new ReplayImportService(
            new Mock<IDownloadService>().Object,
            directoryService.Object,
            new Mock<IUrlParserService>().Object,
            zipValidationService.Object,
            NullLogger<ReplayImportService>.Instance);
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
    /// Imports every entry of an archive that is never interrupted.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ImportFromZipAsync_ImportsEveryEntryAsync()
    {
        var zipPath = Path.Combine(_workingDirectory, "replays.zip");
        CreateZip(zipPath, "first.rep", "second.rep");

        var result = await _service.ImportFromZipAsync(zipPath, GameType.ZeroHour);

        Assert.True(result.Success, string.Join(" ", result.Errors));
        Assert.Equal(2, result.FilesImported);
    }

    /// <summary>
    /// Surfaces a cancellation that lands part-way through an archive as a cancellation. Entries
    /// imported before the cancellation must not be reported as a successful import, because the
    /// caller would otherwise treat a truncated set of replays as the whole archive.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ImportFromZipAsync_CancelledMidArchive_DoesNotReportSuccessAsync()
    {
        var zipPath = Path.Combine(_workingDirectory, "cancelled.zip");
        CreateZip(zipPath, "first.rep", "second.rep");

        using var cancellation = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _service.ImportFromZipAsync(
                zipPath,
                GameType.ZeroHour,
                new CancelOnceAnEntryIsImported(cancellation),
                cancellation.Token));

        Assert.Single(Directory.GetFiles(_replayDirectory));
    }

    private static void CreateZip(string zipPath, params string[] entryNames)
    {
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (var entryName in entryNames)
        {
            var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
            using var stream = entry.Open();
            stream.Write(Encoding.UTF8.GetBytes(entryName));
        }
    }

    private sealed class CancelOnceAnEntryIsImported(CancellationTokenSource cancellation) : IProgress<double>
    {
        private int _reports;

        public void Report(double value)
        {
            if (++_reports > 1)
            {
                cancellation.Cancel();
            }
        }
    }
}
