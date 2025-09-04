using GenHub.Core.Constants;
using Xunit;

namespace GenHub.Tests.Core.Constants;

/// <summary>
/// Tests for <see cref="ConversionConstants"/> constants.
/// </summary>
public class ConversionConstantsTests
{
    /// <summary>
    /// Tests that conversion constants have expected values.
    /// </summary>
    [Fact]
    public void ConversionConstants_ShouldHaveExpectedValues()
    {
        // Arrange & Act & Assert
        Assert.Multiple(() =>
        {
            Assert.Equal(1024, ConversionConstants.BytesPerKilobyte);
            Assert.Equal(1024 * 1024, ConversionConstants.BytesPerMegabyte);
            Assert.Equal(1024L * 1024 * 1024, ConversionConstants.BytesPerGigabyte);
        });
    }

    /// <summary>
    /// Tests that conversion constants are positive.
    /// </summary>
    [Fact]
    public void ConversionConstants_ShouldBePositive()
    {
        // Arrange & Act & Assert
        Assert.Multiple(() =>
        {
            Assert.True(ConversionConstants.BytesPerKilobyte > 0);
            Assert.True(ConversionConstants.BytesPerMegabyte > 0);
            Assert.True(ConversionConstants.BytesPerGigabyte > 0);
        });
    }

    /// <summary>
    /// Tests that conversion constants are powers of 1024.
    /// </summary>
    [Fact]
    public void ConversionConstants_ShouldBePowersOf1024()
    {
        // Arrange & Act & Assert
        Assert.Multiple(() =>
        {
            Assert.Equal(1024, ConversionConstants.BytesPerKilobyte);
            Assert.Equal(ConversionConstants.BytesPerKilobyte * ConversionConstants.BytesPerKilobyte, ConversionConstants.BytesPerMegabyte);
            Assert.Equal((long)ConversionConstants.BytesPerMegabyte * ConversionConstants.BytesPerKilobyte, ConversionConstants.BytesPerGigabyte);
        });
    }
}
