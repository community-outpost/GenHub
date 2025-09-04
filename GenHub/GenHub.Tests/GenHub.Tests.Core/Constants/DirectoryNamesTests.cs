using GenHub.Core.Constants;
using Xunit;

namespace GenHub.Tests.Core.Constants;

/// <summary>
/// Tests for <see cref="DirectoryNames"/> constants.
/// </summary>
public class DirectoryNamesTests
{
    /// <summary>
    /// Tests that all directory name constants have expected values.
    /// </summary>
    [Fact]
    public void DirectoryNames_Constants_ShouldHaveExpectedValues()
    {
        // Arrange & Act & Assert
        Assert.Multiple(() =>
        {
            Assert.Equal("Data", DirectoryNames.Data);
            Assert.Equal("Cache", DirectoryNames.Cache);
            Assert.Equal("Temp", DirectoryNames.Temp);
            Assert.Equal("Logs", DirectoryNames.Logs);
            Assert.Equal("Backups", DirectoryNames.Backups);
        });
    }

    /// <summary>
    /// Tests that directory name constants are not null or empty.
    /// </summary>
    [Fact]
    public void DirectoryNames_Constants_ShouldNotBeNullOrEmpty()
    {
        // Arrange & Act & Assert
        Assert.Multiple(() =>
        {
            Assert.NotNull(DirectoryNames.Data);
            Assert.NotEmpty(DirectoryNames.Data);
            Assert.NotNull(DirectoryNames.Cache);
            Assert.NotEmpty(DirectoryNames.Cache);
            Assert.NotNull(DirectoryNames.Temp);
            Assert.NotEmpty(DirectoryNames.Temp);
            Assert.NotNull(DirectoryNames.Logs);
            Assert.NotEmpty(DirectoryNames.Logs);
            Assert.NotNull(DirectoryNames.Backups);
            Assert.NotEmpty(DirectoryNames.Backups);
        });
    }

    /// <summary>
    /// Tests that directory name constants are unique.
    /// </summary>
    [Fact]
    public void DirectoryNames_Constants_ShouldBeUnique()
    {
        // Arrange
        var directoryNames = new[]
        {
            DirectoryNames.Data,
            DirectoryNames.Cache,
            DirectoryNames.Temp,
            DirectoryNames.Logs,
            DirectoryNames.Backups,
        };

        // Act & Assert
        Assert.Distinct(directoryNames);
    }

    /// <summary>
    /// Tests that directory name constants follow proper naming conventions.
    /// </summary>
    [Fact]
    public void DirectoryNames_Constants_ShouldFollowNamingConventions()
    {
        // Arrange & Act & Assert
        Assert.Multiple(() =>
        {
            // Should start with uppercase letter
            Assert.True(char.IsUpper(DirectoryNames.Data[0]));
            Assert.True(char.IsUpper(DirectoryNames.Cache[0]));
            Assert.True(char.IsUpper(DirectoryNames.Temp[0]));
            Assert.True(char.IsUpper(DirectoryNames.Logs[0]));
            Assert.True(char.IsUpper(DirectoryNames.Backups[0]));

            // Should not contain spaces or special characters
            Assert.DoesNotContain(" ", DirectoryNames.Data);
            Assert.DoesNotContain(" ", DirectoryNames.Cache);
            Assert.DoesNotContain(" ", DirectoryNames.Temp);
            Assert.DoesNotContain(" ", DirectoryNames.Logs);
            Assert.DoesNotContain(" ", DirectoryNames.Backups);
        });
    }
}
