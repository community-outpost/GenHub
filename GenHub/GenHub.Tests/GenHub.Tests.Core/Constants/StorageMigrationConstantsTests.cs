using GenHub.Core.Constants;
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
            Assert.True(StorageMigrationConstants.DiskSpaceSafetyMarginBytes > 0);
            Assert.Equal("Preflight Validation", StorageMigrationConstants.StagePreflight);
            Assert.Equal("Relocating Application Data", StorageMigrationConstants.StageStagingData);
            Assert.Equal("Relocating CAS and Workspaces", StorageMigrationConstants.StageRelocatingStorage);
            Assert.Equal("Preparing Binary Migration", StorageMigrationConstants.StagePreparingBinaries);
            Assert.Equal("Launching Migration Assistant", StorageMigrationConstants.StageLaunchingAssistant);
            Assert.Equal("Finalizing Migration", StorageMigrationConstants.StageFinalizing);
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
}
