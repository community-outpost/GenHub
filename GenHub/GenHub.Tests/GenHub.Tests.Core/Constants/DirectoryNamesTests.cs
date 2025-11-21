using GenHub.Core.Constants;

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
            Assert.Equal("cas-pool", DirectoryNames.CasPool);
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
            Assert.NotNull(DirectoryNames.CasPool);
            Assert.NotEmpty(DirectoryNames.CasPool);
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
            DirectoryNames.CasPool,
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
            // Should start with uppercase letter or lowercase for special cases
            Assert.True(char.IsUpper(DirectoryNames.Data[0]));
            Assert.True(char.IsUpper(DirectoryNames.Cache[0]));
            Assert.True(char.IsLower(DirectoryNames.CasPool[0]));

            // Should not contain spaces
            Assert.DoesNotContain(" ", DirectoryNames.Data);
            Assert.DoesNotContain(" ", DirectoryNames.Cache);
            Assert.DoesNotContain(" ", DirectoryNames.CasPool);
        });
    }
}