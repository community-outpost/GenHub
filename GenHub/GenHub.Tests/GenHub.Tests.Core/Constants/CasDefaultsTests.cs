using GenHub.Core.Constants;
using Xunit;

namespace GenHub.Tests.Core.Constants;

/// <summary>
/// Tests for <see cref="CasDefaults"/> constants.
/// </summary>
public class CasDefaultsTests
{
    /// <summary>
    /// Tests that all CAS default constants have expected values.
    /// </summary>
    [Fact]
    public void CasDefaults_Constants_ShouldHaveExpectedValues()
    {
        // Arrange & Act & Assert
        Assert.Multiple(() =>
        {
            // Cache size should be 50GB
            Assert.Equal(50L * 1024 * 1024 * 1024, CasDefaults.MaxCacheSizeBytes);

            // Concurrent operations should be 4
            Assert.Equal(4, CasDefaults.MaxConcurrentOperations);
        });
    }

    /// <summary>
    /// Tests that cache size constant has reasonable value.
    /// </summary>
    [Fact]
    public void CasDefaults_MaxCacheSizeBytes_ShouldHaveReasonableValue()
    {
        // Arrange
        var expected50GB = 50L * 1024 * 1024 * 1024;

        // Act & Assert
        Assert.Multiple(() =>
        {
            Assert.Equal(CasDefaults.MaxCacheSizeBytes, expected50GB);
            Assert.True(CasDefaults.MaxCacheSizeBytes > 0);
            Assert.True(CasDefaults.MaxCacheSizeBytes >= 1024L * 1024L * 1024L); // At least 1GB
        });
    }

    /// <summary>
    /// Tests that concurrent operations constant has reasonable value.
    /// </summary>
    [Fact]
    public void CasDefaults_MaxConcurrentOperations_ShouldHaveReasonableValue()
    {
        // Arrange & Act & Assert
        Assert.Multiple(() =>
        {
            Assert.True(CasDefaults.MaxConcurrentOperations > 0);
            Assert.True(CasDefaults.MaxConcurrentOperations <= 100); // Not too high
            Assert.True(CasDefaults.MaxConcurrentOperations >= 1); // At least 1
        });
    }

    /// <summary>
    /// Tests that integer constants are of correct type.
    /// </summary>
    [Fact]
    public void CasDefaults_IntegerConstants_ShouldBeCorrectType()
    {
        // Arrange & Act & Assert
        Assert.IsType<int>(CasDefaults.MaxConcurrentOperations);
    }

    /// <summary>
    /// Tests that long constants are of correct type.
    /// </summary>
    [Fact]
    public void CasDefaults_LongConstants_ShouldBeCorrectType()
    {
        // Arrange & Act & Assert
        Assert.IsType<long>(CasDefaults.MaxCacheSizeBytes);
    }
}
