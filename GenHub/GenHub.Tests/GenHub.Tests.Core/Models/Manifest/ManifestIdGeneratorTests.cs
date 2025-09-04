using System;
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
        var manifestSchemaVersion = "1.0";

        // Act
        var result = ManifestIdGenerator.GeneratePublisherContentId(publisherId, contentName, manifestSchemaVersion);

        // Assert
        Assert.Equal("test.publisher.test.content.1.0", result);
    }

    /// <summary>
    /// Tests that GeneratePublisherContentId correctly normalizes special characters.
    /// </summary>
    /// <param name="input">The input string to normalize.</param>
    /// <param name="expected">The expected normalized output.</param>
    [Theory]
    [InlineData("C&C Generals", "c.c.generals")]
    [InlineData("Zero Hour!!!", "zero.hour")]
    [InlineData("Test@Content#123", "test.content.123")]
    [InlineData("Multi  Word  Name", "multi.word.name")]
    [InlineData("UPPERCASE", "uppercase")]
    public void GeneratePublisherContentId_WithSpecialCharacters_NormalizesCorrectly(string input, string expected)
    {
        // Arrange
        var publisherId = "test";
        var manifestSchemaVersion = "1.0";

        // Act
        var result = ManifestIdGenerator.GeneratePublisherContentId(publisherId, input, manifestSchemaVersion);

        // Assert
        Assert.Equal($"test.{expected}.1.0", result);
    }

    /// <summary>
    /// Tests that GeneratePublisherContentId correctly normalizes complex version strings.
    /// </summary>
    /// <param name="version">The version string to normalize.</param>
    /// <param name="expectedVersion">The expected normalized version.</param>
    [Theory]
    [InlineData("1.0.0-beta", "1.0.0.beta")]
    [InlineData("2.1.3-alpha.1", "2.1.3.alpha.1")]
    [InlineData("1.0.0+build.123", "1.0.0.build.123")]
    [InlineData("0.1.0-rc.1", "0.1.0.rc.1")]
    public void GeneratePublisherContentId_WithComplexVersions_NormalizesCorrectly(string version, string expectedVersion)
    {
        // Arrange
        var publisherId = "test";
        var contentName = "content";

        // Act
        var result = ManifestIdGenerator.GeneratePublisherContentId(publisherId, contentName, version);

        // Assert
        Assert.Equal($"test.content.{expectedVersion}", result);
    }

    /// <summary>
    /// Tests that GeneratePublisherContentId throws ArgumentException for invalid inputs.
    /// </summary>
    /// <param name="publisherId">The publisher ID to test.</param>
    /// <param name="contentName">The content name to test.</param>
    /// <param name="manifestSchemaVersion">The manifest schema version to test.</param>
    /// <param name="expectedMessage">The expected error message.</param>
    [Theory]
    [InlineData("", "content", "1.0", "Publisher ID cannot be empty")]
    [InlineData(" ", "content", "1.0", "Publisher ID cannot be empty")]
    [InlineData("publisher", "", "1.0", "Content name cannot be empty")]
    [InlineData("publisher", " ", "1.0", "Content name cannot be empty")]
    [InlineData("publisher", "content", "", "Manifest schema version cannot be empty")]
    [InlineData("publisher", "content", " ", "Manifest schema version cannot be empty")]
    public void GeneratePublisherContentId_WithInvalidInputs_ThrowsArgumentException(string publisherId, string contentName, string manifestSchemaVersion, string expectedMessage)
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            ManifestIdGenerator.GeneratePublisherContentId(publisherId, contentName, manifestSchemaVersion));

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
        var manifestSchemaVersion = "1.0";

        // Act
        var result1 = ManifestIdGenerator.GeneratePublisherContentId(publisherId, contentName, manifestSchemaVersion);
        var result2 = ManifestIdGenerator.GeneratePublisherContentId(publisherId, contentName, manifestSchemaVersion);

        // Assert
        Assert.Equal(result1, result2);
    }

    /// <summary>
    /// Tests that GenerateBaseGameId returns the expected format with valid inputs.
    /// </summary>
    [Fact]
    public void GenerateBaseGameId_WithValidInputs_ReturnsExpectedFormat()
    {
        // Arrange
        var installation = new GameInstallation("C:\\Games", GameInstallationType.Steam);
        var gameType = GameType.Generals;
        var manifestSchemaVersion = "1.0";

        // Act
        var result = ManifestIdGenerator.GenerateBaseGameId(installation, gameType, manifestSchemaVersion);

        // Assert
        Assert.Equal("steam.generals.1.0", result);
    }

    /// <summary>
    /// Tests that GenerateBaseGameId returns correct format for all installation types.
    /// </summary>
    /// <param name="installationType">The installation type to test.</param>
    /// <param name="gameType">The game type to test.</param>
    /// <param name="expected">The expected result string.</param>
    [Theory]
    [InlineData(GameInstallationType.Steam, GameType.Generals, "steam.generals.1.0")]
    [InlineData(GameInstallationType.EaApp, GameType.ZeroHour, "eaapp.zerohour.1.0")]
    [InlineData(GameInstallationType.Origin, GameType.Generals, "origin.generals.1.0")]
    [InlineData(GameInstallationType.TheFirstDecade, GameType.ZeroHour, "thefirstdecade.zerohour.1.0")]
    [InlineData(GameInstallationType.RGMechanics, GameType.Generals, "rgmechanics.generals.1.0")]
    [InlineData(GameInstallationType.CDISO, GameType.ZeroHour, "cdiso.zerohour.1.0")]
    [InlineData(GameInstallationType.Wine, GameType.Generals, "wine.generals.1.0")]
    [InlineData(GameInstallationType.Retail, GameType.ZeroHour, "retail.zerohour.1.0")]
    [InlineData(GameInstallationType.Unknown, GameType.Generals, "unknown.generals.1.0")]
    public void GenerateBaseGameId_WithAllInstallationTypes_ReturnsCorrectFormat(GameInstallationType installationType, GameType gameType, string expected)
    {
        // Arrange
        var installation = new GameInstallation("C:\\Games", installationType);
        var manifestSchemaVersion = "1.0";

        // Act
        var result = ManifestIdGenerator.GenerateBaseGameId(installation, gameType, manifestSchemaVersion);

        // Assert
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Tests that GenerateBaseGameId correctly normalizes complex version strings.
    /// </summary>
    /// <param name="version">The version string to normalize.</param>
    /// <param name="expectedVersion">The expected normalized version.</param>
    [Theory]
    [InlineData("1.0.0-beta", "1.0.0.beta")]
    [InlineData("2.1.3-alpha.1", "2.1.3.alpha.1")]
    [InlineData("1.0.0+build.123", "1.0.0.build.123")]
    public void GenerateBaseGameId_WithComplexVersions_NormalizesCorrectly(string version, string expectedVersion)
    {
        // Arrange
        var installation = new GameInstallation("C:\\Games", GameInstallationType.Steam);
        var gameType = GameType.Generals;

        // Act
        var result = ManifestIdGenerator.GenerateBaseGameId(installation, gameType, version);

        // Assert
        Assert.Equal($"steam.generals.{expectedVersion}", result);
    }

    /// <summary>
    /// Tests that GenerateBaseGameId throws ArgumentNullException for null installation.
    /// </summary>
    [Fact]
    public void GenerateBaseGameId_WithNullInstallation_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            ManifestIdGenerator.GenerateBaseGameId(null!, GameType.Generals, "1.0"));
    }

    /// <summary>
    /// Tests that GenerateBaseGameId throws ArgumentException for invalid manifest schema versions.
    /// </summary>
    /// <param name="manifestSchemaVersion">The manifest schema version to test.</param>
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void GenerateBaseGameId_WithInvalidManifestSchemaVersion_ThrowsArgumentException(string manifestSchemaVersion)
    {
        // Arrange
        var installation = new GameInstallation("C:\\Games", GameInstallationType.Steam);

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            ManifestIdGenerator.GenerateBaseGameId(installation, GameType.Generals, manifestSchemaVersion));

        Assert.Contains("Manifest schema version cannot be empty", exception.Message);
    }

    /// <summary>
    /// Tests that GenerateBaseGameId produces deterministic results.
    /// </summary>
    [Fact]
    public void GenerateBaseGameId_IsDeterministic()
    {
        // Arrange
        var installation = new GameInstallation("C:\\Games", GameInstallationType.Steam);
        var gameType = GameType.Generals;
        var manifestSchemaVersion = "1.0";

        // Act
        var result1 = ManifestIdGenerator.GenerateBaseGameId(installation, gameType, manifestSchemaVersion);
        var result2 = ManifestIdGenerator.GenerateBaseGameId(installation, gameType, manifestSchemaVersion);

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
        var manifestSchemaVersion = "1.0";

        // Act
        var result = ManifestIdGenerator.GeneratePublisherContentId(publisherId, input, manifestSchemaVersion);

        // Assert
        Assert.Equal($"test.{expected}.1.0", result);
        Assert.False(result.StartsWith("."));
        Assert.False(result.EndsWith("."));
        Assert.DoesNotContain("..", result);
    }

    /// <summary>
    /// Tests that GeneratePublisherContentId handles version normalization with leading and trailing dots.
    /// </summary>
    /// <param name="version">The version string to test.</param>
    /// <param name="expectedVersion">The expected normalized version.</param>
    [Theory]
    [InlineData("  1.0  ", "1.0")]
    [InlineData("1.0.", "1.0")]
    [InlineData(".1.0", "1.0")]
    [InlineData("1..0", "1.0")]
    [InlineData("1...0", "1.0")]
    public void GeneratePublisherContentId_VersionNormalization_NoLeadingTrailingDots(string version, string expectedVersion)
    {
        // Arrange
        var publisherId = "test";
        var contentName = "content";

        // Act
        var result = ManifestIdGenerator.GeneratePublisherContentId(publisherId, contentName, version);

        // Assert
        Assert.Equal($"test.content.{expectedVersion}", result);
        Assert.False(result.StartsWith("."));
        Assert.False(result.EndsWith("."));
        Assert.DoesNotContain("..", result);
    }

    /// <summary>
    /// Tests that GenerateBaseGameId handles version normalization with leading and trailing dots.
    /// </summary>
    /// <param name="version">The version string to test.</param>
    /// <param name="expectedVersion">The expected normalized version.</param>
    [Theory]
    [InlineData("  1.0  ", "1.0")]
    [InlineData("1.0.", "1.0")]
    [InlineData(".1.0", "1.0")]
    [InlineData("1..0", "1.0")]
    public void GenerateBaseGameId_VersionNormalization_NoLeadingTrailingDots(string version, string expectedVersion)
    {
        // Arrange
        var installation = new GameInstallation("C:\\Games", GameInstallationType.Steam);
        var gameType = GameType.Generals;

        // Act
        var result = ManifestIdGenerator.GenerateBaseGameId(installation, gameType, version);

        // Assert
        Assert.Equal($"steam.generals.{expectedVersion}", result);
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
        var contentName = "!!!"; // Normalizes to empty
        var manifestSchemaVersion = "1.0";

        // Act
        var result = ManifestIdGenerator.GeneratePublisherContentId(publisherId, contentName, manifestSchemaVersion);

        // Assert
        Assert.Equal("test.unknown.1.0", result); // Should handle empty segments gracefully with placeholder
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
        var manifestSchemaVersion = "1.0.0-beta.1";

        // Act
        var result = ManifestIdGenerator.GeneratePublisherContentId(publisherId, contentName, manifestSchemaVersion);

        // Assert - Should always produce the same result regardless of platform
        Assert.Equal("test.publisher.123.c.c.generals.1.0.0.beta.1", result);
    }

    /// <summary>
    /// Tests that GenerateBaseGameId produces deterministic results across platforms.
    /// </summary>
    [Fact]
    public void GenerateBaseGameId_CrossPlatformDeterministic()
    {
        // Arrange
        var installation = new GameInstallation("C:\\Games\\Test", GameInstallationType.TheFirstDecade);
        var gameType = GameType.ZeroHour;
        var manifestSchemaVersion = "2.1.3-alpha.1";

        // Act
        var result = ManifestIdGenerator.GenerateBaseGameId(installation, gameType, manifestSchemaVersion);

        // Assert - Should always produce the same result regardless of platform
        Assert.Equal("thefirstdecade.zerohour.2.1.3.alpha.1", result);
    }
}
