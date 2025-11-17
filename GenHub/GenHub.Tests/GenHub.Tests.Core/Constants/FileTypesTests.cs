using GenHub.Core.Constants;

namespace GenHub.Tests.Core.Constants;

/// <summary>
/// Tests for <see cref="FileTypes"/> constants.
/// </summary>
public class FileTypesTests
{
    /// <summary>
    /// Tests that all file type constants have expected values.
    /// </summary>
    [Fact]
    public void FileTypes_Constants_ShouldHaveExpectedValues()
    {
        // Arrange & Act & Assert
        Assert.Multiple(() =>
        {
            Assert.Equal("Manifests", FileTypes.ManifestsDirectory);
            Assert.Equal("*.manifest.json", FileTypes.ManifestFilePattern);
            Assert.Equal(".manifest.json", FileTypes.ManifestFileExtension);
            Assert.Equal(".json", FileTypes.JsonFileExtension);
            Assert.Equal("*.json", FileTypes.JsonFilePattern);
            Assert.Equal("settings.json", FileTypes.SettingsFileName);
        });
    }

    /// <summary>
    /// Tests that file type constants are not null or empty.
    /// </summary>
    [Fact]
    public void FileTypes_Constants_ShouldNotBeNullOrEmpty()
    {
        // Arrange & Act & Assert
        Assert.Multiple(() =>
        {
            Assert.NotNull(FileTypes.ManifestsDirectory);
            Assert.NotEmpty(FileTypes.ManifestsDirectory);
            Assert.NotNull(FileTypes.ManifestFilePattern);
            Assert.NotEmpty(FileTypes.ManifestFilePattern);
            Assert.NotNull(FileTypes.ManifestFileExtension);
            Assert.NotEmpty(FileTypes.ManifestFileExtension);
            Assert.NotNull(FileTypes.JsonFileExtension);
            Assert.NotEmpty(FileTypes.JsonFileExtension);
            Assert.NotNull(FileTypes.JsonFilePattern);
            Assert.NotEmpty(FileTypes.JsonFilePattern);
            Assert.NotNull(FileTypes.SettingsFileName);
            Assert.NotEmpty(FileTypes.SettingsFileName);
        });
    }

    /// <summary>
    /// Tests that file extension constants start with a dot.
    /// </summary>
    [Fact]
    public void FileTypes_Extensions_ShouldStartWithDot()
    {
        // Arrange & Act & Assert
        Assert.Multiple(() =>
        {
            Assert.StartsWith(".", FileTypes.ManifestFileExtension);
            Assert.StartsWith(".", FileTypes.JsonFileExtension);
        });
    }

    /// <summary>
    /// Tests that file pattern constants contain wildcards.
    /// </summary>
    [Fact]
    public void FileTypes_Patterns_ShouldContainWildcards()
    {
        // Arrange & Act & Assert
        Assert.Multiple(() =>
        {
            Assert.Contains("*", FileTypes.ManifestFilePattern);
            Assert.Contains("*", FileTypes.JsonFilePattern);
        });
    }

    /// <summary>
    /// Tests that manifest-related constants are consistent.
    /// </summary>
    [Fact]
    public void FileTypes_ManifestConstants_ShouldBeConsistent()
    {
        // Arrange & Act & Assert
        Assert.Multiple(() =>
        {
            // Manifest file extension should be part of the pattern
            Assert.Contains(FileTypes.ManifestFileExtension.TrimStart('.'), FileTypes.ManifestFilePattern);

            // Manifest file extension should end with .json
            Assert.EndsWith(FileTypes.JsonFileExtension, FileTypes.ManifestFileExtension);
        });
    }

    /// <summary>
    /// Tests that settings file name uses JSON extension.
    /// </summary>
    [Fact]
    public void FileTypes_SettingsFileName_ShouldUseJsonExtension()
    {
        // Arrange & Act & Assert
        Assert.EndsWith(FileTypes.JsonFileExtension, FileTypes.SettingsFileName);
    }

    /// <summary>
    /// Tests that file type constants follow proper naming conventions.
    /// </summary>
    [Fact]
    public void FileTypes_Constants_ShouldFollowNamingConventions()
    {
        // Arrange & Act & Assert
        Assert.Multiple(() =>
        {
            // Directory names should start with uppercase
            Assert.True(char.IsUpper(FileTypes.ManifestsDirectory[0]));

            // Extensions should start with dot and be lowercase
            Assert.Equal('.', FileTypes.ManifestFileExtension[0]);
            Assert.Equal('.', FileTypes.JsonFileExtension[0]);
            Assert.Equal(FileTypes.ManifestFileExtension, FileTypes.ManifestFileExtension.ToLower());
            Assert.Equal(FileTypes.JsonFileExtension, FileTypes.JsonFileExtension.ToLower());
        });
    }
}