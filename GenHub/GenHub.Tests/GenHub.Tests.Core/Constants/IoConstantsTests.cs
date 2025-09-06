using GenHub.Core.Constants;
using Xunit;

namespace GenHub.Tests.Core.Constants;

/// <summary>
/// Tests for <see cref="IoConstants"/> constants.
/// </summary>
public class IoConstantsTests
{
    /// <summary>
    /// Tests that IO constants have expected values.
    /// </summary>
    [Fact]
    public void IoConstants_ShouldHaveExpectedValues()
    {
        // Arrange & Act & Assert
        Assert.Equal(4096, IoConstants.DefaultFileBufferSize);
    }

    /// <summary>
    /// Tests that IO constants are positive.
    /// </summary>
    [Fact]
    public void IoConstants_ShouldBePositive()
    {
        // Arrange & Act & Assert
        Assert.True(IoConstants.DefaultFileBufferSize > 0);
    }

    /// <summary>
    /// Tests that default file buffer size is a power of 2.
    /// </summary>
    [Fact]
    public void DefaultFileBufferSize_ShouldBePowerOfTwo()
    {
        // Arrange
        var size = IoConstants.DefaultFileBufferSize;

        // Act & Assert
        Assert.True((size & (size - 1)) == 0, "DefaultFileBufferSize should be a power of 2");
    }
}
