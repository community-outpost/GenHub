using GenHub.Core.Constants;
using Xunit;

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
            // CAS maintenance constants
            Assert.Equal(1, StorageConstants.AutoGcIntervalDays);
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
            Assert.IsType<int>(StorageConstants.AutoGcIntervalDays);
        });
    }
}
