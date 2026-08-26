namespace GenHub.Tests.Windows.Features.ActionSets;

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GenHub.Core.Exceptions;
using GenHub.Core.Features.ActionSets;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using GenHub.Windows.Features.ActionSets.Fixes;
using Microsoft.Extensions.Logging;
using Moq;
using SharpCompress.Archives;
using Xunit;

/// <summary>
/// Unit tests for transactional safety, rollback retention, and undo behavior in <see cref="BasePackageDeploymentFix"/>.
/// </summary>
public sealed class BasePackageDeploymentFixTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly Mock<ILogger> _loggerMock;
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock;

    /// <summary>
    /// Initializes a new instance of the <see cref="BasePackageDeploymentFixTests"/> class.
    /// </summary>
    public BasePackageDeploymentFixTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"GenHub_PkgDeployTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDirectory);
        _loggerMock = new Mock<ILogger>();
        _httpClientFactoryMock = new Mock<IHttpClientFactory>();
    }

    /// <summary>
    /// Disposes the temporary test directory.
    /// </summary>
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, true);
            }
        }
        catch (IOException)
        {
            // Ignored on test cleanup
        }
        catch (UnauthorizedAccessException)
        {
            // Ignored on test cleanup
        }
    }

    /// <summary>
    /// Verifies that when undo encounters a missing recorded backup file,
    /// it does not delete the destination file (to prevent data loss) and returns failure.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the test operation.</returns>
    [Fact]
    public async Task Undo_WhenRecordedBackupIsMissing_RetainsDestinationFileAndFailsSafely()
    {
        var fix = new TestPackageDeploymentFix(_loggerMock.Object, _httpClientFactoryMock.Object);
        var installationPath = Path.Combine(_testDirectory, "GameInstall");
        Directory.CreateDirectory(installationPath);
        var installation = new GameInstallation(installationPath, GameInstallationType.Steam);

        var destFile = Path.Combine(installationPath, "game_asset.dll");
        await File.WriteAllTextAsync(destFile, "ImportantOriginalOrModifiedContent");

        var missingBackupFile = Path.Combine(_testDirectory, "Backups", "missing_backup.bak");
        var markerPath = fix.PublicGetMarkerPath(installation);
        var markerDir = Path.GetDirectoryName(markerPath);
        if (!string.IsNullOrEmpty(markerDir))
        {
            Directory.CreateDirectory(markerDir);
        }

        await File.WriteAllLinesAsync(markerPath, [$"{destFile}|{missingBackupFile}"]);

        try
        {
            var result = await fix.UndoAsync(installation);

            result.Success.Should().BeFalse();
            File.Exists(destFile).Should().BeTrue("Destination file must be preserved when backup is missing");
            var content = await File.ReadAllTextAsync(destFile);
            content.Should().Be("ImportantOriginalOrModifiedContent");
        }
        finally
        {
            if (File.Exists(markerPath))
            {
                File.Delete(markerPath);
            }
        }
    }

    /// <summary>
    /// Verifies that when all recorded files are restored successfully,
    /// the backup directory and marker are removed and original content is restored.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the test operation.</returns>
    [Fact]
    public async Task Undo_WhenRestorationSucceeds_RestoresOriginalsAndCleansUp()
    {
        var fix = new TestPackageDeploymentFix(_loggerMock.Object, _httpClientFactoryMock.Object);
        var installationPath = Path.Combine(_testDirectory, "GameInstallSuccess");
        Directory.CreateDirectory(installationPath);
        var installation = new GameInstallation(installationPath, GameInstallationType.Steam);

        var backupDir = fix.PublicGetBackupDirectory(installation);
        Directory.CreateDirectory(backupDir);

        var destFile = Path.Combine(installationPath, "original.ini");
        var backupFile = Path.Combine(backupDir, "original.ini.bak");

        await File.WriteAllTextAsync(destFile, "ModifiedByPatch");
        await File.WriteAllTextAsync(backupFile, "OriginalCleanGameContent");

        var markerPath = fix.PublicGetMarkerPath(installation);
        var markerDir = Path.GetDirectoryName(markerPath);
        if (!string.IsNullOrEmpty(markerDir))
        {
            Directory.CreateDirectory(markerDir);
        }

        await File.WriteAllLinesAsync(markerPath, [$"{destFile}|{backupFile}"]);

        try
        {
            var result = await fix.UndoAsync(installation);

            result.Success.Should().BeTrue();
            File.Exists(destFile).Should().BeTrue();
            var restoredContent = await File.ReadAllTextAsync(destFile);
            restoredContent.Should().Be("OriginalCleanGameContent");
            File.Exists(backupFile).Should().BeFalse();
            File.Exists(markerPath).Should().BeFalse();
        }
        finally
        {
            if (File.Exists(markerPath))
            {
                File.Delete(markerPath);
            }

            if (Directory.Exists(backupDir))
            {
                Directory.Delete(backupDir, true);
            }
        }
    }

    /// <summary>
    /// Verifies that ExtractArchiveEntriesAsync successfully extracts multiple entries when their
    /// cumulative decompressed size is within the allowed aggregate package size budget.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the test operation.</returns>
    [Fact]
    public async Task ExtractArchiveEntriesAsync_WhenEntriesAreWithinAggregateBudget_ExtractsAllEntriesSuccessfullyAsync()
    {
        var archivePath = Path.Combine(_testDirectory, "valid_multi_entry.zip");
        var extractDir = Path.Combine(_testDirectory, "extract_valid");
        Directory.CreateDirectory(extractDir);

        await CreateValidMultiEntryZipAsync(archivePath);

        using var archive = ArchiveFactory.OpenArchive(archivePath);
        var extracted = await TestPackageDeploymentFix.PublicExtractArchiveEntriesAsync(archive, extractDir);

        extracted.Should().HaveCount(2);
        File.Exists(Path.Combine(extractDir, "file1.dat")).Should().BeTrue();
        File.Exists(Path.Combine(extractDir, "file2.dat")).Should().BeTrue();
    }

    /// <summary>
    /// Verifies that ExtractArchiveEntriesAsync tracks cumulative extracted bytes across entries and throws
    /// <see cref="ArchiveExpansionLimitExceededException"/> when the multi-entry total exceeds the aggregate budget.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the test operation.</returns>
    [Fact]
    public async Task ExtractArchiveEntriesAsync_WhenCumulativeSizeExceedsAggregateBudget_ThrowsArchiveExpansionLimitExceededExceptionAsync()
    {
        var archivePath = Path.Combine(_testDirectory, "multi_entry_exceeding_budget.zip");
        var extractDir = Path.Combine(_testDirectory, "extract_exceeded");
        Directory.CreateDirectory(extractDir);

        await CreateOversizedMultiEntryZipAsync(archivePath);

        using var archive = ArchiveFactory.OpenArchive(archivePath);
        var act = () => TestPackageDeploymentFix.PublicExtractArchiveEntriesAsync(archive, extractDir);

        await act.Should().ThrowAsync<ArchiveExpansionLimitExceededException>();

        // Entry 1 was within the remaining budget and completed, whereas entry 2 exceeded the budget and was cleaned up.
        File.Exists(Path.Combine(extractDir, "entry1.dat")).Should().BeTrue();
        File.Exists(Path.Combine(extractDir, "entry2.dat")).Should().BeFalse();
    }

    private static async Task CreateValidMultiEntryZipAsync(string archivePath)
    {
        using var zipArchive = ZipFile.Open(archivePath, ZipArchiveMode.Create);
        var entry1 = zipArchive.CreateEntry("file1.dat");
        await using (var stream1 = entry1.Open())
        {
            await stream1.WriteAsync(new byte[1024]);
        }

        var entry2 = zipArchive.CreateEntry("file2.dat");
        await using (var stream2 = entry2.Open())
        {
            await stream2.WriteAsync(new byte[2048]);
        }
    }

    private static async Task CreateOversizedMultiEntryZipAsync(string archivePath)
    {
        var chunk = new byte[1024 * 1024];
        using var zipArchive = ZipFile.Open(archivePath, ZipArchiveMode.Create);
        var entry1 = zipArchive.CreateEntry("entry1.dat", CompressionLevel.Optimal);
        await using (var stream1 = entry1.Open())
        {
            for (var i = 0; i < 110; i++)
            {
                await stream1.WriteAsync(chunk);
            }
        }

        var entry2 = zipArchive.CreateEntry("entry2.dat", CompressionLevel.Optimal);
        await using (var stream2 = entry2.Open())
        {
            for (var i = 0; i < 110; i++)
            {
                await stream2.WriteAsync(chunk);
            }
        }
    }

    private sealed class TestPackageDeploymentFix(
        ILogger logger,
        IHttpClientFactory httpClientFactory,
        string customId = "TestPackageDeploymentFix")
        : BasePackageDeploymentFix(httpClientFactory, logger, $"{customId}.done")
    {
        public override string Id => customId;

        public override string Title => "Test Package Fix";

        public override string Description => "Test description";

        public override bool IsCoreFix => false;

        public override bool IsCrucialFix => false;

        protected override string PackageDisplayName => "Test Package";

        protected override string TempFilePrefix => "test_pkg";

        protected override string ExpectedSha256 => "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

        protected override IReadOnlyList<string> DownloadUrls => ["https://example.com/test.zip"];

        public static Task<Dictionary<string, string>> PublicExtractArchiveEntriesAsync(
            IArchive archive,
            string extractDir,
            CancellationToken ct = default) => ExtractArchiveEntriesAsync(archive, extractDir, ct);

        public override Task<bool> IsApplicableAsync(GameInstallation installation, CancellationToken ct = default) => Task.FromResult(true);

        public override Task<bool> IsAppliedAsync(GameInstallation installation, CancellationToken ct = default) => Task.FromResult(false);

        public string PublicGetMarkerPath(GameInstallation installation) => GetMarkerPath(installation);

        public string PublicGetBackupDirectory(GameInstallation installation) => GetBackupDirectory(installation);

        protected override bool AreAssetsPresent(GameInstallation installation) => false;

        protected override List<string> GetLegacyFilePaths(GameInstallation installation) => [];

        protected override Task<(int ExtractedCount, List<string>? DeployedFiles)> ExtractAndDeployAssetsAsync(
            string archivePath,
            DeploymentContext context,
            GameInstallation installation,
            CancellationToken ct)
        {
            return Task.FromResult<(int, List<string>?)>((0, []));
        }
    }
}
