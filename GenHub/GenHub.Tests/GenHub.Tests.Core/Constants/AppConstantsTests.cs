using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using Xunit;

namespace GenHub.Tests.Core.Constants;

/// <summary>
/// Tests for <see cref="AppConstants"/> constants.
/// </summary>
public class AppConstantsTests
{
    /// <summary>
    /// Tests that all App constants have expected values.
    /// </summary>
    [Fact]
    public void AppConstants_Constants_ShouldHaveExpectedValues()
    {
        // Arrange & Act & Assert
        Assert.Multiple(() =>
        {
            // Application name and version
            Assert.Equal("GenHub", AppConstants.AppName);
            Assert.Equal("1.0", AppConstants.AppVersion);

            // Theme constants
            Assert.Equal(Theme.Dark, AppConstants.DefaultTheme);
            Assert.Equal("Dark", AppConstants.DefaultThemeName);
        });
    }

    /// <summary>
    /// Tests that application name constants are not null or empty.
    /// </summary>
    [Fact]
    public void AppConstants_AppNameConstants_ShouldNotBeNullOrEmpty()
    {
        // Arrange & Act & Assert
        Assert.Multiple(() =>
        {
            Assert.NotNull(AppConstants.AppName);
            Assert.NotEmpty(AppConstants.AppName);
            Assert.NotNull(AppConstants.AppVersion);
            Assert.NotEmpty(AppConstants.AppVersion);
        });
    }

    /// <summary>
    /// Tests that theme constants are not null or empty.
    /// </summary>
    [Fact]
    public void AppConstants_ThemeConstants_ShouldNotBeNullOrEmpty()
    {
        // Arrange & Act & Assert
        Assert.Multiple(() =>
        {
            Assert.NotNull(AppConstants.DefaultThemeName);
            Assert.NotEmpty(AppConstants.DefaultThemeName);
        });
    }

    /// <summary>
    /// Tests that application name follows proper naming conventions.
    /// </summary>
    [Fact]
    public void AppConstants_AppName_ShouldFollowNamingConventions()
    {
        // Arrange & Act & Assert
        Assert.Multiple(() =>

            // Should not contain spaces
            Assert.DoesNotContain(" ", AppConstants.AppName));
    }

    /// <summary>
    /// Tests that application version follows proper version format.
    /// </summary>
    [Fact]
    public void AppConstants_AppVersion_ShouldFollowVersionFormat()
    {
        // Arrange & Act & Assert
        Assert.Multiple(() =>
        {
            // Should not be null or empty
            Assert.NotNull(AppConstants.AppVersion);
            Assert.NotEmpty(AppConstants.AppVersion);

            // Should contain a dot (basic version format check)
            Assert.Contains(".", AppConstants.AppVersion);

            // Should not contain spaces
            Assert.DoesNotContain(" ", AppConstants.AppVersion);
        });
    }

    /// <summary>
    /// Tests that string constants are of correct type.
    /// </summary>
    [Fact]
    public void AppConstants_StringConstants_ShouldBeCorrectType()
    {
        // Arrange & Act & Assert
        Assert.Multiple(() =>
        {
            Assert.IsType<string>(AppConstants.AppName);
            Assert.IsType<string>(AppConstants.AppVersion);
            Assert.IsType<string>(AppConstants.DefaultThemeName);
        });
    }

    /// <summary>
    /// Tests that enum constants are of correct type.
    /// </summary>
    [Fact]
    public void AppConstants_EnumConstants_ShouldBeCorrectType()
    {
        // Arrange & Act & Assert
        Assert.Multiple(() =>
        {
            Assert.IsType<Theme>(AppConstants.DefaultTheme);
        });
    }

    /// <summary>
    /// Tests that theme constants are consistent.
    /// </summary>
    [Fact]
    public void AppConstants_ThemeConstants_ShouldBeConsistent()
    {
        // Arrange & Act & Assert
        Assert.Multiple(() =>
        {
            // Default theme name should match the string representation of default theme
            Assert.Equal(AppConstants.DefaultThemeName, AppConstants.DefaultTheme.ToString());
        });
    }
}
