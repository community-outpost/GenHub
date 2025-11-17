using GenHub.Core.Constants;

namespace GenHub.Tests.Core.Constants;

/// <summary>
/// Tests for <see cref="StorageConstants"/> constants.
/// </summary>
public class StorageConstantsTests
{
    /// <summary>
    /// Tests that all storage constants have expected values.
    /// </summary>
    [Fact]
    public void StorageConstants_Constants_ShouldHaveExpectedValues()
    {
        // Arrange & Act & Assert
        Assert.Multiple(() =>
        {
            // CAS retry constants
            Assert.Equal(10, StorageConstants.MaxRetries);

            // CAS maintenance constants
            Assert.Equal(1, StorageConstants.AutoGcIntervalDays);
        });
    }

    /// <summary>
    /// Tests that retry constants have reasonable values.
    /// </summary>
    [Fact]
    public void StorageConstants_RetryConstants_ShouldHaveReasonableValues()
    {
        // Arrange & Act & Assert
        Assert.Multiple(() =>
        {
            // MaxRetries should be positive
            Assert.True(StorageConstants.MaxRetries > 0);

            // MaxRetries should be reasonable (not too low or too high)
            Assert.True(StorageConstants.MaxRetries >= 3);
            Assert.True(StorageConstants.MaxRetries <= 20);
        });
    }

    /// <summary>
    /// Tests that maintenance constants have reasonable values.
    /// </summary>
    [Fact]
    public void StorageConstants_MaintenanceConstants_ShouldHaveReasonableValues()
    {
        // Arrange & Act & Assert
        Assert.Multiple(() =>
        {
            // AutoGcIntervalDays should be positive
            Assert.True(StorageConstants.AutoGcIntervalDays > 0);

            // AutoGcIntervalDays should be reasonable (not too frequent or too infrequent)
            Assert.True(StorageConstants.AutoGcIntervalDays >= 1);
            Assert.True(StorageConstants.AutoGcIntervalDays <= 30);
        });
    }

    /// <summary>
    /// Tests that integer constants are of correct type.
    /// </summary>
    [Fact]
    public void StorageConstants_IntegerConstants_ShouldBeCorrectType()
    {
        // Arrange & Act & Assert
        Assert.Multiple(() =>
        {
            Assert.IsType<int>(StorageConstants.MaxRetries);
            Assert.IsType<int>(StorageConstants.AutoGcIntervalDays);
        });
    }
}