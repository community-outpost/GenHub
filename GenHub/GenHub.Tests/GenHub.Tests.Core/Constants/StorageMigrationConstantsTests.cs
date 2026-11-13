using GenHub.Core.Constants;
using System;
using Xunit;

namespace GenHub.Tests.Core.Constants;

/// <summary>
/// Tests for <see cref="StorageMigrationConstants"/>.
/// </summary>
public class StorageMigrationConstantsTests
{
    /// <summary>
    /// Verifies that all storage migration constants have valid and expected values.
    /// </summary>
    [Fact]
    public void StorageMigrationConstants_HaveExpectedValues()
    {
        Assert.Multiple(() =>
        {
            Assert.Equal("update_genhub.ps1", StorageMigrationConstants.WindowsUpdateScriptName);
            Assert.Equal("update_genhub.sh", StorageMigrationConstants.LinuxUpdateScriptName);
            Assert.Equal("migration.log", StorageMigrationConstants.MigrationLogFileName);
            Assert.Equal("backup", StorageMigrationConstants.MigrationBackupDirectoryName);
            Assert.Equal("MapPacks", StorageMigrationConstants.MapPacksCapitalizedDirectoryName);
            Assert.Equal("mappacks", StorageMigrationConstants.MapPacksLowercaseDirectoryName);
            Assert.True(StorageMigrationConstants.DiskSpaceSafetyMarginBytes > 0);
            Assert.Equal("Preflight Validation", StorageMigrationConstants.StagePreflight);
            Assert.Equal("Relocating Application Data", StorageMigrationConstants.StageStagingData);
            Assert.Equal("Relocating CAS and Workspaces", StorageMigrationConstants.StageRelocatingStorage);
            Assert.Equal("Preparing Binary Migration", StorageMigrationConstants.StagePreparingBinaries);
            Assert.Equal("Launching Migration Assistant", StorageMigrationConstants.StageLaunchingAssistant);
            Assert.Equal("Finalizing Migration", StorageMigrationConstants.StageFinalizing);
            Assert.Equal(".app", StorageMigrationConstants.MacAppBundleExtension);
            Assert.Equal("Contents", StorageMigrationConstants.MacContentsDirectoryName);
            Assert.Equal("Info.plist", StorageMigrationConstants.MacInfoPlistFileName);
            Assert.Equal("MacOS", StorageMigrationConstants.MacOsDirectoryName);
            Assert.Equal(".adoption-pending", StorageMigrationConstants.AdoptionPendingMarkerFileName);
            Assert.Equal("GENHUB_GenHub__AppDataPath", StorageMigrationConstants.AppDataPathEnvVar);
            Assert.Equal("install-location", StorageMigrationConstants.CustomInstallPathFileName);
            Assert.Equal(".genhub", StorageMigrationConstants.GenHubConfigDirectoryName);
        });
    }

    /// <summary>
    /// Verifies that the safety margin is reasonable (at least 50MB).
    /// </summary>
    [Fact]
    public void StorageMigrationConstants_SafetyMargin_IsReasonable()
    {
        Assert.True(StorageMigrationConstants.DiskSpaceSafetyMarginBytes >= 50 * 1024 * 1024L);
    }

    /// <summary>
    /// Verifies that the duplicate installation notification copy names the running location,
    /// the detected path placeholder, and the reinstall guidance.
    /// </summary>
    [Fact]
    public void StorageMigrationConstants_DuplicateInstallationCopy_NamesLocationsAndGuidance()
    {
        Assert.Multiple(() =>
        {
            Assert.Equal("Duplicate Installation Detected", StorageMigrationConstants.DuplicateInstallationDetectedTitle);
            Assert.Contains("{0}", StorageMigrationConstants.DuplicateInstallationAdoptedMessageFormat);
            Assert.Contains("default folder", StorageMigrationConstants.DuplicateInstallationAdoptedMessageFormat);
            Assert.Contains("copied to this installation", StorageMigrationConstants.DuplicateInstallationAdoptedMessageFormat);
            Assert.Contains("{0}", StorageMigrationConstants.DuplicateInstallationDetectedMessageFormat);
            Assert.Contains("default folder", StorageMigrationConstants.DuplicateInstallationDetectedMessageFormat);
            Assert.Contains("{0}", StorageMigrationConstants.DuplicateInstallationWindowsReinstallGuidanceFormat);
            Assert.Contains("--installto", StorageMigrationConstants.DuplicateInstallationWindowsReinstallGuidanceFormat);
            Assert.Contains("duplicate copy", StorageMigrationConstants.DuplicateInstallationWindowsReinstallGuidanceFormat);
            Assert.Contains("Migrate Installation", StorageMigrationConstants.DuplicateInstallationGenericReinstallGuidance);
            Assert.Contains("duplicate copy", StorageMigrationConstants.DuplicateInstallationGenericReinstallGuidance);
        });
    }

    /// <summary>
    /// Verifies that the Windows reinstall guidance instructs uninstalling the duplicate copy
    /// before reinstalling. Both copies share one Velopack uninstall entry pointing at the most
    /// recently installed location, so reinstalling first would retarget Add or Remove Programs
    /// at the kept install.
    /// </summary>
    [Fact]
    public void StorageMigrationConstants_WindowsReinstallGuidance_UninstallsDuplicateBeforeReinstalling()
    {
        var guidance = StorageMigrationConstants.DuplicateInstallationWindowsReinstallGuidanceFormat;

        Assert.True(
            guidance.IndexOf("uninstall", StringComparison.OrdinalIgnoreCase) >= 0 &&
            guidance.IndexOf("uninstall", StringComparison.OrdinalIgnoreCase) < guidance.IndexOf("--installto", StringComparison.Ordinal),
            "Windows guidance must instruct uninstalling the duplicate copy before reinstalling with --installto.");
    }

    /// <summary>
    /// Verifies that the duplicate installation localization keys use the expected hierarchical names.
    /// </summary>
    [Fact]
    public void StorageMigrationConstants_DuplicateInstallationKeys_HaveExpectedValues()
    {
        Assert.Multiple(() =>
        {
            Assert.Equal("Storage.DuplicateInstallation.Title", StorageMigrationConstants.DuplicateInstallationDetectedTitleKey);
            Assert.Equal("Storage.DuplicateInstallation.AdoptedMessage", StorageMigrationConstants.DuplicateInstallationAdoptedMessageKey);
            Assert.Equal("Storage.DuplicateInstallation.DetectedMessage", StorageMigrationConstants.DuplicateInstallationDetectedMessageKey);
            Assert.Equal("Storage.DuplicateInstallation.WindowsReinstallGuidance", StorageMigrationConstants.DuplicateInstallationWindowsReinstallGuidanceKey);
            Assert.Equal("Storage.DuplicateInstallation.GenericReinstallGuidance", StorageMigrationConstants.DuplicateInstallationGenericReinstallGuidanceKey);
        });
    }
}
