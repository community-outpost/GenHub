using GenHub.Core.Models.AppUpdate;

namespace GenHub.Tests.Core.Models.AppUpdate;

/// <summary>
/// Tests for <see cref="UpdateProgress"/> percentage clamping.
/// </summary>
public class UpdateProgressTests
{
    /// <summary>
    /// Verifies that completion percent is clamped to 0-100.
    /// </summary>
    /// <param name="input">The raw percentage value.</param>
    /// <param name="expected">The expected clamped percentage.</param>
    [Theory]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(50, 50)]
    [InlineData(100, 100)]
    [InlineData(150, 100)]
    public void PercentComplete_ClampsToValidRange(int input, int expected)
    {
        var progress = new UpdateProgress { PercentComplete = input };

        Assert.Equal(expected, progress.PercentComplete);
    }
}
