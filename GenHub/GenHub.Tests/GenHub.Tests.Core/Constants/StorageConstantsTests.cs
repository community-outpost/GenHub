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
            // CAS retry constants
            Assert.Equal(10, StorageConstants.MaxRetries);
            Assert.Equal(100, StorageConstants.RetryDelayMs);
            Assert.Equal(5000, StorageConstants.MaxRetryDelayMs);

            // CAS directory structure
            Assert.Equal("objects", StorageConstants.ObjectsDirectory);
            Assert.Equal("locks", StorageConstants.LocksDirectory);
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

            // RetryDelayMs should be positive and reasonable
            Assert.True(StorageConstants.RetryDelayMs > 0);
            Assert.True(StorageConstants.RetryDelayMs < 10000); // Less than 10 seconds

            // MaxRetryDelayMs should be greater than RetryDelayMs
            Assert.True(StorageConstants.MaxRetryDelayMs > StorageConstants.RetryDelayMs);
        });
    }

    /// <summary>
    /// Tests that directory name constants are not null or empty.
    /// </summary>
    [Fact]
    public void StorageConstants_DirectoryConstants_ShouldNotBeNullOrEmpty()
    {
        // Arrange & Act & Assert
        Assert.Multiple(() =>
        {
            Assert.NotNull(StorageConstants.ObjectsDirectory);
            Assert.NotEmpty(StorageConstants.ObjectsDirectory);
            Assert.NotNull(StorageConstants.LocksDirectory);
            Assert.NotEmpty(StorageConstants.LocksDirectory);
        });
    }

    /// <summary>
    /// Tests that directory name constants are unique.
    /// </summary>
    [Fact]
    public void StorageConstants_DirectoryConstants_ShouldBeUnique()
    {
        // Arrange
        var directoryNames = new[]
        {
            StorageConstants.ObjectsDirectory,
            StorageConstants.LocksDirectory,
        };

        // Act & Assert
        Assert.Distinct(directoryNames);
    }

    /// <summary>
    /// Tests that directory name constants follow proper naming conventions.
    /// </summary>
    [Fact]
    public void StorageConstants_DirectoryConstants_ShouldFollowNamingConventions()
    {
        // Arrange & Act & Assert
        Assert.Multiple(() =>
        {
            // Should be lowercase
            Assert.Equal(StorageConstants.ObjectsDirectory, StorageConstants.ObjectsDirectory.ToLower());
            Assert.Equal(StorageConstants.LocksDirectory, StorageConstants.LocksDirectory.ToLower());

            // Should not contain spaces or special characters
            Assert.DoesNotContain(" ", StorageConstants.ObjectsDirectory);
            Assert.DoesNotContain(" ", StorageConstants.LocksDirectory);

            // Should not contain uppercase letters
            Assert.DoesNotMatch("[A-Z]", StorageConstants.ObjectsDirectory);
            Assert.DoesNotMatch("[A-Z]", StorageConstants.LocksDirectory);
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
            Assert.IsType<int>(StorageConstants.RetryDelayMs);
            Assert.IsType<int>(StorageConstants.MaxRetryDelayMs);
        });
    }

    /// <summary>
    /// Tests that string constants are of correct type.
    /// </summary>
    [Fact]
    public void StorageConstants_StringConstants_ShouldBeCorrectType()
    {
        // Arrange & Act & Assert
        Assert.Multiple(() =>
        {
            Assert.IsType<string>(StorageConstants.ObjectsDirectory);
            Assert.IsType<string>(StorageConstants.LocksDirectory);
        });
    }

    /// <summary>
    /// Tests that retry delay progression is logical.
    /// </summary>
    [Fact]
    public void StorageConstants_RetryDelayProgression_ShouldBeLogical()
    {
        // Arrange
        var baseDelay = StorageConstants.RetryDelayMs;
        var maxDelay = StorageConstants.MaxRetryDelayMs;

        // Act & Assert
        // Max delay should be significantly larger than base delay for exponential backoff
        Assert.True(maxDelay >= baseDelay * 2);
    }
}
