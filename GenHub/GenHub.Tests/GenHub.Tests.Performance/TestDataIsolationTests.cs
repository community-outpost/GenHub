using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Tests.Shared;

namespace GenHub.Tests.Performance;

/// <summary>
/// Verifies the performance test host cannot use the user's application data by default.
/// </summary>
public class TestDataIsolationTests
{
    /// <summary>
    /// Verifies assembly initialization redirects storage and disables real legacy migration.
    /// </summary>
    [Fact]
    public void TestHost_UsesIsolatedApplicationData()
    {
        var root = AppDataPathHelper.GetDataRoot();

        Assert.Equal(TestDataRootInitializer.DataRoot, root);
        Assert.Equal(root, Environment.GetEnvironmentVariable(StorageMigrationConstants.AppDataPathEnvVar));
        Assert.True(PathHelper.IsPathWithinDirectory(Path.GetTempPath(), root));
        Assert.Null(AppDataPathHelper.GetLegacyRoamingRoot());
    }
}
