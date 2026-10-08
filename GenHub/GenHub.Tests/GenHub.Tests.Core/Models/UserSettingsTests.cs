using GenHub.Core.Models.Common;
using Xunit;

#pragma warning disable CS0618 // Type or member is obsolete

namespace GenHub.Tests.Core.Models;

/// <summary>
/// Unit tests for <see cref="UserSettings"/> to verify backward compatibility.
/// </summary>
public class UserSettingsTests
{
    /// <summary>
    /// Verifies that the legacy SkippedVersion property still works for backward compatibility.
    /// </summary>
    [Fact]
    public void SkippedVersion_Getter_ReturnsFirstItemFromSkippedVersions()
    {
        // Arrange
        UserSettings settings = new()
        {
            SkippedVersions = ["1.0.0", "1.1.0"],
        };

        // Act & Assert
        Assert.Equal("1.0.0", settings.SkippedVersion);
    }

    /// <summary>
    /// Verifies that setting the legacy SkippedVersion property adds it to SkippedVersions.
    /// </summary>
    [Fact]
    public void SkippedVersion_Setter_AddsItemToSkippedVersions()
    {
        // Arrange
        UserSettings settings = new();

        // Act
        settings.SkippedVersion = "2.0.0";

        // Assert
        Assert.Contains("2.0.0", settings.SkippedVersions);
        Assert.Equal("2.0.0", settings.SkippedVersion);
    }

    /// <summary>
    /// Verifies that setting SkippedVersion to an existing value does not duplicate it in the list.
    /// </summary>
    [Fact]
    public void SkippedVersion_Setter_IsIdempotent()
    {
        // Arrange
        UserSettings settings = new();

        // Act
        settings.SkippedVersion = "2.0.0";
        var firstCount = settings.SkippedVersions.Count;
        settings.SkippedVersion = "2.0.0";

        // Assert
        Assert.Equal(1, firstCount);
        Assert.Single(settings.SkippedVersions);
        Assert.Equal("2.0.0", settings.SkippedVersion);
    }

    /// <summary>
    /// Verifies that SkippedVersion returns null if SkippedVersions is empty.
    /// </summary>
    [Fact]
    public void SkippedVersion_ReturnsNull_WhenSkippedVersionsIsEmpty()
    {
        // Arrange
        var settings = new UserSettings();

        // Act & Assert
        Assert.Null(settings.SkippedVersion);
    }

    /// <summary>
    /// Verifies that Language defaults to the default culture name.
    /// </summary>
    [Fact]
    public void Language_DefaultsToLocalizationConstantsDefaultCultureName()
    {
        // Arrange & Act
        var settings = new UserSettings();

        // Assert
        Assert.Equal(GenHub.Core.Constants.LocalizationConstants.DefaultCultureName, settings.Language);
    }

    /// <summary>
    /// Verifies that Clone copies the Language property.
    /// </summary>
    [Fact]
    public void Clone_CopiesLanguageProperty()
    {
        // Arrange
        var settings = new UserSettings
        {
            Language = "ar-SA",
        };

        // Act
        var clone = settings.Clone();

        // Assert
        Assert.Equal("ar-SA", clone.Language);
    }

    /// <summary>
    /// Verifies that Language survives JSON serialization round-trip.
    /// </summary>
    [Fact]
    public void Language_SerializesAndDeserializesCorrectly()
    {
        // Arrange
        var settings = new UserSettings
        {
            Language = "ru-RU",
        };

        // Act
        var json = System.Text.Json.JsonSerializer.Serialize(settings);
        var deserialized = System.Text.Json.JsonSerializer.Deserialize<UserSettings>(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal("ru-RU", deserialized.Language);
    }
}
