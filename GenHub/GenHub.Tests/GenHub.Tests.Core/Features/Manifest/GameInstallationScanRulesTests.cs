using GenHub.Core.Constants;
using GenHub.Features.Manifest;
using Xunit;

namespace GenHub.Tests.Core.Features.Manifest;

/// <summary>
/// Tests for <see cref="GameInstallationScanRules"/> backup handling.
/// </summary>
public class GameInstallationScanRulesTests
{
    private const string TargetFileName = GameClientConstants.DbgHelpDll;
    private const string NumberedInfix = ".7";

    /// <summary>
    /// Verifies GenPatcher rename backups, first and numbered, are skipped by installation scans.
    /// </summary>
    /// <param name="infix">The sequence part between the GenPatcher infix and the backup extension.</param>
    [Theory]
    [InlineData("")]
    [InlineData(NumberedInfix)]
    public void ShouldSkipFile_GenPatcherBackup_ReturnsTrue(string infix)
    {
        var name = TargetFileName + FileTypes.GenPatcherBackupInfix + infix + FileTypes.BackupExtension;

        Assert.True(GameInstallationScanRules.ShouldSkipFile(name));
    }

    /// <summary>
    /// Verifies the live file itself is scanned.
    /// </summary>
    [Fact]
    public void ShouldSkipFile_LiveFile_ReturnsFalse()
    {
        Assert.False(GameInstallationScanRules.ShouldSkipFile(TargetFileName));
    }
}
