using GenHub.Core.Models.Content;

namespace GenHub.Tests.Core.Models.Content;

/// <summary>
/// Tests for <see cref="ContentAcquisitionProgress"/> percentage clamping.
/// </summary>
public class ContentAcquisitionProgressTests
{
    /// <summary>
    /// Verifies that overall progress is clamped to 0-100.
    /// </summary>
    /// <param name="input">The raw percentage value.</param>
    /// <param name="expected">The expected clamped percentage.</param>
    [Theory]
    [InlineData(-25, 0)]
    [InlineData(0, 0)]
    [InlineData(42.5, 42.5)]
    [InlineData(100, 100)]
    [InlineData(250, 100)]
    public void ProgressPercentage_ClampsToValidRange(double input, double expected)
    {
        var progress = new ContentAcquisitionProgress { ProgressPercentage = input };

        Assert.Equal(expected, progress.ProgressPercentage);
    }

    /// <summary>
    /// Verifies that stage progress is clamped to 0-100.
    /// </summary>
    /// <param name="input">The raw percentage value.</param>
    /// <param name="expected">The expected clamped percentage.</param>
    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(55, 55)]
    [InlineData(100, 100)]
    [InlineData(140, 100)]
    public void StageProgress_ClampsToValidRange(double input, double expected)
    {
        var progress = new ContentAcquisitionProgress { StageProgress = input };

        Assert.Equal(expected, progress.StageProgress);
    }

    /// <summary>
    /// Verifies that NaN progress collapses to zero instead of poisoning progress bars.
    /// </summary>
    [Fact]
    public void ProgressPercentage_WithNaN_CollapsesToZero()
    {
        var progress = new ContentAcquisitionProgress
        {
            ProgressPercentage = double.NaN,
            StageProgress = double.NaN,
        };

        Assert.Equal(0, progress.ProgressPercentage);
        Assert.Equal(0, progress.StageProgress);
    }
}
