#pragma warning disable CS0618 // Type or member is obsolete

using GenHub.Core.Models.Common;
using Xunit;

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
        settings.SkippedVersion = "2.0.0";

        // Assert
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
}
