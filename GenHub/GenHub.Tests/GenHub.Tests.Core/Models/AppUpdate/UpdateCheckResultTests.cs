using GenHub.Core.Models.Results;

namespace GenHub.Tests.Core.Models.AppUpdate;

/// <summary>
/// Unit tests for UpdateCheckResult.
/// </summary>
public class UpdateCheckResultTests
{
    /// <summary>
    /// Verifies default construction sets expected defaults.
    /// </summary>
    [Fact]
    public void UpdateCheckResult_DefaultConstruction_SetsExpectedDefaults()
    {
        var result = UpdateCheckResult.CreateInitial();
        Assert.False(result.IsUpdateAvailable);
        Assert.Equal(string.Empty, result.CurrentVersion);
        Assert.NotNull(result.ErrorMessages);
        Assert.Empty(result.ErrorMessages);
    }
}