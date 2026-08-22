namespace GenHub.Tests.Windows.Features.ActionSets;

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GenHub.Core.Features.ActionSets;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using GenHub.Windows.Features.ActionSets.Fixes;
using Microsoft.Extensions.Logging;
using Moq;
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
