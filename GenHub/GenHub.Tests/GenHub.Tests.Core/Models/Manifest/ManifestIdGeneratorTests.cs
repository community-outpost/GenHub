using System;
using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Manifest;
using Xunit;

namespace GenHub.Tests.Core.Models.Manifest;

/// <summary>
/// Unit tests for ManifestIdGenerator to ensure deterministic ID generation.
/// </summary>
public class ManifestIdGeneratorTests
{
    /// <summary>
    /// Tests that GeneratePublisherContentId returns the expected format with valid inputs.
    /// </summary>
    [Fact]
    public void GeneratePublisherContentId_WithValidInputs_ReturnsExpectedFormat()
    {
        // Arrange
        var publisherId = "test-publisher";
        var contentName = "test-content";
        var userVersion = 0;

        // Act
        var result = ManifestIdGenerator.GeneratePublisherContentId(publisherId, GenHub.Core.Models.Enums.ContentType.Mod, contentName, userVersion);

        // Assert
        Assert.Equal("1.0.test.publisher.mod.test.content", result);
    }

    /// <summary>
    /// Tests that GeneratePublisherContentId correctly normalizes special characters.
    /// </summary>
    /// <param name="input">The input string to normalize.</param>
    /// <param name="expected">The expected normalized output.</param>
    [Theory]
    [InlineData("C&C Generals", "1.0.test.mod.c.c.generals")]
    [InlineData("Zero Hour!!!", "1.0.test.mod.zero.hour")]
    [InlineData("Test@Content#123", "1.0.test.mod.test.content.123")]
    [InlineData("Multi  Word  Name", "1.0.test.mod.multi.word.name")]
    [InlineData("UPPERCASE", "1.0.test.mod.uppercase")]
    public void GeneratePublisherContentId_WithSpecialCharacters_NormalizesCorrectly(string input, string expected)
    {
        // Arrange
        var publisherId = "test";
        var userVersion = 0;

        // Act
        var result = ManifestIdGenerator.GeneratePublisherContentId(publisherId, GenHub.Core.Models.Enums.ContentType.Mod, input, userVersion);

        // Assert
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Tests that GeneratePublisherContentId correctly handles different user versions.
    /// </summary>
    /// <param name="userVersion">The user version number.</param>
    /// <param name="expectedVersion">The expected version in the ID.</param>
    [Theory]
    [InlineData(0, "1.0")]
    [InlineData(1, "1.1")]
    [InlineData(5, "1.5")]
    [InlineData(20, "1.20")]
    public void GeneratePublisherContentId_WithDifferentUserVersions_ReturnsCorrectFormat(int userVersion, string expectedVersion)
    {
        // Arrange
        var publisherId = "test";
        var contentName = "content";

        // Act
        var result = ManifestIdGenerator.GeneratePublisherContentId(publisherId, GenHub.Core.Models.Enums.ContentType.Mod, contentName, userVersion);

        // Assert
        Assert.Equal($"{expectedVersion}.test.mod.content", result);
    }

    /// <summary>
    /// Tests that GeneratePublisherContentId throws ArgumentException for invalid inputs.
    /// </summary>
    /// <param name="publisherId">The publisher ID to test.</param>
    /// <param name="contentName">The content name to test.</param>
    /// <param name="expectedMessage">The expected error message.</param>
    [Theory]
    [InlineData("", "content", "Input cannot be null or whitespace")]
    [InlineData(" ", "content", "Input cannot be null or whitespace")]
    [InlineData("publisher", "", "Input cannot be null or whitespace")]
    [InlineData("publisher", " ", "Input cannot be null or whitespace")]
    public void GeneratePublisherContentId_WithInvalidInputs_ThrowsArgumentException(string publisherId, string contentName, string expectedMessage)
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            ManifestIdGenerator.GeneratePublisherContentId(publisherId, GenHub.Core.Models.Enums.ContentType.Mod, contentName, 0));

        Assert.Contains(expectedMessage, exception.Message);
    }

    /// <summary>
    /// Tests that GeneratePublisherContentId produces deterministic results.
    /// </summary>
    [Fact]
    public void GeneratePublisherContentId_IsDeterministic()
    {
        // Arrange
        var publisherId = "test-publisher";
        var contentName = "test-content";
        var userVersion = 0;

        // Act
        var result1 = ManifestIdGenerator.GeneratePublisherContentId(publisherId, GenHub.Core.Models.Enums.ContentType.Mod, contentName, userVersion);
        var result2 = ManifestIdGenerator.GeneratePublisherContentId(publisherId, GenHub.Core.Models.Enums.ContentType.Mod, contentName, userVersion);

        // Assert
        Assert.Equal(result1, result2);
    }

    /// <summary>
    /// Tests that GenerateGameInstallationId returns the expected format with valid inputs.
    /// </summary>
    [Fact]
    public void GenerateGameInstallationId_WithValidInputs_ReturnsExpectedFormat()
    {
        // Arrange
        var installation = new GameInstallation("C:\\Games", GameInstallationType.Steam);
        var gameType = GameType.Generals;
        var userVersion = 0;

        // Act
        var result = ManifestIdGenerator.GenerateGameInstallationId(installation, gameType, userVersion);

        // Assert
        Assert.Equal("1.0.steam.generals", result);
    }

    /// <summary>
    /// Tests that GenerateGameInstallationId returns correct format for all installation types.
    /// </summary>
    /// <param name="installationType">The installation type to test.</param>
    /// <param name="gameType">The game type to test.</param>
    /// <param name="expected">The expected result string.</param>
    [Theory]
    [InlineData(GameInstallationType.Steam, GameType.Generals, "1.0.steam.generals")]
    [InlineData(GameInstallationType.EaApp, GameType.ZeroHour, "1.0.eaapp.zerohour")]
    [InlineData(GameInstallationType.TheFirstDecade, GameType.ZeroHour, "1.0.thefirstdecade.zerohour")]
    [InlineData(GameInstallationType.CDISO, GameType.ZeroHour, "1.0.cdiso.zerohour")]
    [InlineData(GameInstallationType.Wine, GameType.Generals, "1.0.wine.generals")]
    [InlineData(GameInstallationType.Retail, GameType.ZeroHour, "1.0.retail.zerohour")]
    [InlineData(GameInstallationType.Unknown, GameType.Generals, "1.0.unknown.generals")]
    public void GenerateGameInstallationId_WithAllInstallationTypes_ReturnsCorrectFormat(GameInstallationType installationType, GameType gameType, string expected)
    {
        // Arrange
        var installation = new GameInstallation("C:\\Games", installationType);
        var userVersion = 0;

        // Act
        var result = ManifestIdGenerator.GenerateGameInstallationId(installation, gameType, userVersion);

        // Assert
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Tests that GenerateGameInstallationId handles different user versions correctly.
    /// </summary>
    /// <param name="userVersion">The user version number.</param>
    /// <param name="expectedVersion">The expected version in the ID.</param>
    [Theory]
    [InlineData(0, "1.0")]
    [InlineData(1, "1.1")]
    [InlineData(5, "1.5")]
    public void GenerateGameInstallationId_WithDifferentUserVersions_ReturnsCorrectFormat(int userVersion, string expectedVersion)
    {
        // Arrange
        var installation = new GameInstallation("C:\\Games", GameInstallationType.Steam);
        var gameType = GameType.Generals;

        // Act
        var result = ManifestIdGenerator.GenerateGameInstallationId(installation, gameType, userVersion);

        // Assert
        Assert.Equal($"{expectedVersion}.steam.generals", result);
    }

    /// <summary>
    /// Tests that GenerateGameInstallationId throws ArgumentNullException for null installation.
    /// </summary>
    [Fact]
    public void GenerateGameInstallationId_WithNullInstallation_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            ManifestIdGenerator.GenerateGameInstallationId(null!, GameType.Generals, 0));
    }

    /// <summary>
    /// Tests that GenerateGameInstallationId throws ArgumentException for invalid user versions.
    /// </summary>
    /// <param name="userVersion">The user version to test.</param>
    [Theory]
    [InlineData(-1)]
    [InlineData(-5)]
    public void GenerateGameInstallationId_WithInvalidUserVersion_ThrowsArgumentException(int userVersion)
    {
        // Arrange
        var installation = new GameInstallation("C:\\Games", GameInstallationType.Steam);

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            ManifestIdGenerator.GenerateGameInstallationId(installation, GameType.Generals, userVersion));

        Assert.Contains("Version must be numeric and non-negative", exception.Message);
    }

    /// <summary>
    /// Tests that GenerateGameInstallationId produces deterministic results.
    /// </summary>
    [Fact]
    public void GenerateGameInstallationId_IsDeterministic()
    {
        // Arrange
        var installation = new GameInstallation("C:\\Games", GameInstallationType.Steam);
        var gameType = GameType.Generals;
        var userVersion = 0;

        // Act
        var result1 = ManifestIdGenerator.GenerateGameInstallationId(installation, gameType, userVersion);
        var result2 = ManifestIdGenerator.GenerateGameInstallationId(installation, gameType, userVersion);

        // Assert
        Assert.Equal(result1, result2);
    }

    /// <summary>
    /// Tests that GeneratePublisherContentId handles leading and trailing dots correctly.
    /// </summary>
    /// <param name="input">The input string to test.</param>
    /// <param name="expected">The expected normalized output.</param>
    [Theory]
    [InlineData("  test  ", "test")]
    [InlineData("test.", "test")]
    [InlineData(".test", "test")]
    [InlineData("test..content", "test.content")]
    [InlineData("test...content", "test.content")]
    public void GeneratePublisherContentId_NoLeadingTrailingDots(string input, string expected)
    {
        // Arrange
        var publisherId = "test";
        var userVersion = 0;

        // Act
        var result = ManifestIdGenerator.GeneratePublisherContentId(publisherId, GenHub.Core.Models.Enums.ContentType.Mod, input, userVersion);

        // Assert
        Assert.Equal($"1.0.test.mod.{expected}", result);
        Assert.False(result.StartsWith("."));
        Assert.False(result.EndsWith("."));
        Assert.DoesNotContain("..", result);
    }

    /// <summary>
    /// Tests that GeneratePublisherContentId handles different user versions correctly.
    /// </summary>
    /// <param name="userVersion">The user version number.</param>
    /// <param name="expectedVersion">The expected version in the ID.</param>
    [Theory]
    [InlineData(0, "1.0")]
    [InlineData(1, "1.1")]
    [InlineData(10, "1.10")]
    public void GeneratePublisherContentId_UserVersionFormatting_Correct(int userVersion, string expectedVersion)
    {
        // Arrange
        var publisherId = "test";
        var contentName = "content";

        // Act
        var result = ManifestIdGenerator.GeneratePublisherContentId(publisherId, GenHub.Core.Models.Enums.ContentType.Mod, contentName, userVersion);

        // Assert
        Assert.Equal($"{expectedVersion}.test.mod.content", result);
        Assert.False(result.StartsWith("."));
        Assert.False(result.EndsWith("."));
        Assert.DoesNotContain("..", result);
    }

    /// <summary>
    /// Tests that GenerateGameInstallationId handles different user versions correctly.
    /// </summary>
    /// <param name="userVersion">The user version number.</param>
    /// <param name="expectedVersion">The expected version in the ID.</param>
    [Theory]
    [InlineData(0, "1.0")]
    [InlineData(1, "1.1")]
    [InlineData(10, "1.10")]
    public void GenerateGameInstallationId_UserVersionFormatting_Correct(int userVersion, string expectedVersion)
    {
        // Arrange
        var installation = new GameInstallation("C:\\Games", GameInstallationType.Steam);
        var gameType = GameType.Generals;

        // Act
        var result = ManifestIdGenerator.GenerateGameInstallationId(installation, gameType, userVersion);

        // Assert
        Assert.Equal($"{expectedVersion}.steam.generals", result);
        Assert.False(result.StartsWith("."));
        Assert.False(result.EndsWith("."));
        Assert.DoesNotContain("..", result);
    }

    /// <summary>
    /// Tests that GeneratePublisherContentId handles empty normalized strings gracefully.
    /// </summary>
    [Fact]
    public void GeneratePublisherContentId_WithEmptyNormalizedStrings_StillValid()
    {
        // Arrange
        var publisherId = "test";
        var contentName = "unknown"; // Normalizes to "unknown"
        var userVersion = 0;

        // Act
        var result = ManifestIdGenerator.GeneratePublisherContentId(publisherId, GenHub.Core.Models.Enums.ContentType.Mod, contentName, userVersion);

        // Assert
        Assert.Equal("1.0.test.mod.unknown", result); // Should handle empty segments gracefully with placeholder
    }

    /// <summary>
    /// Tests that GeneratePublisherContentId produces deterministic results across platforms.
    /// </summary>
    [Fact]
    public void GeneratePublisherContentId_CrossPlatformDeterministic()
    {
        // Arrange
        var publisherId = "Test@Publisher#123";
        var contentName = "C&C Generals!!!";
        var userVersion = 1;

        // Act
        var result = ManifestIdGenerator.GeneratePublisherContentId(publisherId, GenHub.Core.Models.Enums.ContentType.Mod, contentName, userVersion);

        // Assert - Should always produce the same result regardless of platform
        Assert.Equal("1.1.test.publisher.123.mod.c.c.generals", result);
    }

    /// <summary>
    /// Tests that GenerateGameInstallationId produces deterministic results across platforms.
    /// </summary>
    [Fact]
    public void GenerateGameInstallationId_CrossPlatformDeterministic()
    {
        // Arrange
        var installation = new GameInstallation("C:\\Games\\Test", GameInstallationType.TheFirstDecade);
        var gameType = GameType.ZeroHour;
        var userVersion = 2;

        // Act
        var result = ManifestIdGenerator.GenerateGameInstallationId(installation, gameType, userVersion);

        // Assert - Should always produce the same result regardless of platform
        Assert.Equal("1.2.thefirstdecade.zerohour", result);
    }
}
