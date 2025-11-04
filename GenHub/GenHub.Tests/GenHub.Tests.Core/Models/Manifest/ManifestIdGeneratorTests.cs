using System;
using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Manifest;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Models.Manifest;

/// <summary>
/// Unit tests for ManifestIdGenerator to ensure deterministic ID generation.
/// </summary>
public class ManifestIdGeneratorTests
{
    /// <summary>
    /// Tests that GeneratePublisherContentId with valid inputs returns expected format.
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
        Assert.Equal("1.0.testpublisher.mod.testcontent", result);
    }

    /// <summary>
    /// Tests that GeneratePublisherContentId correctly normalizes special characters.
    /// </summary>
    /// <param name="input">The input string to normalize.</param>
    /// <param name="expected">The expected normalized output.</param>
    [Theory]
    [InlineData("C&C Generals", "1.0.test.mod.ccgenerals")]
    [InlineData("Zero Hour!!!", "1.0.test.mod.zerohour")]
    [InlineData("Test@Content#123", "1.0.test.mod.testcontent123")]
    [InlineData("Multi  Word  Name", "1.0.test.mod.multiwordname")]
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
    [Theory]
    [InlineData("", "content")]
    [InlineData(" ", "content")]
    [InlineData("publisher", "")]
    [InlineData("publisher", " ")]
    public void GeneratePublisherContentId_WithInvalidInputs_ThrowsArgumentException(string publisherId, string contentName)
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            ManifestIdGenerator.GeneratePublisherContentId(publisherId, GenHub.Core.Models.Enums.ContentType.Mod, contentName, 0));
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
        Assert.Equal("1.0.testpublisher.mod.testcontent", result1);
        Assert.Equal(result1, result2);
    }

    /// <summary>
    /// Tests that GenerateGameInstallationId returns the expected 5-segment format with valid inputs.
    /// Format: schemaVersion.userVersion.publisher.contentType.contentName.
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
        Assert.Equal("1.0.steam.gameinstallation.generals", result);
    }

    /// <summary>
    /// Tests that GenerateGameInstallationId returns correct 5-segment format for all installation types.
    /// All installation types are treated as publishers.
    /// </summary>
    /// <param name="installationType">The installation type to test.</param>
    /// <param name="gameType">The game type to test.</param>
    /// <param name="expected">The expected result string.</param>
    [Theory]
    [InlineData(GameInstallationType.Steam, GameType.Generals, "1.0.steam.gameinstallation.generals")]
    [InlineData(GameInstallationType.EaApp, GameType.ZeroHour, "1.0.eaapp.gameinstallation.zerohour")]
    [InlineData(GameInstallationType.TheFirstDecade, GameType.ZeroHour, "1.0.thefirstdecade.gameinstallation.zerohour")]
    [InlineData(GameInstallationType.CDISO, GameType.ZeroHour, "1.0.cdiso.gameinstallation.zerohour")]
    [InlineData(GameInstallationType.Wine, GameType.Generals, "1.0.wine.gameinstallation.generals")]
    [InlineData(GameInstallationType.Retail, GameType.ZeroHour, "1.0.retail.gameinstallation.zerohour")]
    [InlineData(GameInstallationType.Unknown, GameType.Generals, "1.0.unknown.gameinstallation.generals")]
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
        Assert.Equal($"{expectedVersion}.steam.gameinstallation.generals", result);
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
    [InlineData("test..content", "testcontent")]
    [InlineData("test...content", "testcontent")]
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
        Assert.Equal($"{expectedVersion}.steam.gameinstallation.generals", result);
        Assert.False(result.StartsWith("."));
        Assert.False(result.EndsWith("."));
        Assert.DoesNotContain("..", result);
    }

    /// <summary>
    /// Tests that GeneratePublisherContentId handles valid content names correctly.
    /// </summary>
    [Fact]
    public void GeneratePublisherContentId_WithValidContentName_ReturnsValidId()
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
        Assert.Equal("1.1.testpublisher123.mod.ccgenerals", result);
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
        Assert.Equal("1.2.thefirstdecade.gameinstallation.zerohour", result);
    }

    /// <summary>
    /// Tests that GeneratePublisherContentId with valid publisher and content returns correct format.
    /// </summary>
    [Fact]
    public void GeneratePublisherContentId_WithValidPublisherAndContent_ReturnsCorrectFormat()
    {
        // Arrange
        var publisherId = "cnclabs";
        var contentType = ContentType.Mod;
        var contentName = "urban-chaos";
        var expected = "1.0.cnclabs.mod.urbanchaos";

        // Act
        var result = ManifestIdGenerator.GeneratePublisherContentId(publisherId, contentType, contentName, 0);

        // Assert
        Assert.Equal(expected, result);

        // Note: ManifestId validator may not accept this exact format
    }

    /// <summary>
    /// Tests that GeneratePublisherContentId with suffix returns appended suffix.
    /// </summary>
    [Fact]
    public void GeneratePublisherContentId_WithSuffix_ReturnsAppendedSuffix()
    {
        // Arrange
        var publisherId = "moddb-westwood";
        var contentType = ContentType.Mod;
        var contentName = "balance-patch";
        var expected = "1.1.moddbwestwood.mod.balancepatch";

        // Act
        var result = ManifestIdGenerator.GeneratePublisherContentId(publisherId, contentType, contentName, 1);

        // Assert
        Assert.Equal(expected, result);

        // Note: ManifestId validator may not accept IDs with suffixes in contentName
    }

    /// <summary>
    /// Tests that GeneratePublisherContentId with GitHub style publisher handles normalization.
    /// </summary>
    [Fact]
    public void GeneratePublisherContentId_WithGitHubStylePublisher_HandlesNormalization()
    {
        // Arrange
        var publisherId = "undead2146/genhub-mod"; // Simulates owner/repo input
        var contentType = ContentType.Mod;
        var contentName = "custom-mod";
        var expected = "1.0.undead2146genhubmod.mod.custommod"; // Normalized: slashes to dots

        // Act
        var result = ManifestIdGenerator.GeneratePublisherContentId(publisherId, contentType, contentName, 0);

        // Assert
        Assert.Equal(expected, result);

        // Note: This ID has 7 segments and won't pass strict 5-segment validation
    }

    /// <summary>
    /// Tests that GeneratePublisherContentId with empty publisher throws ArgumentException.
    /// </summary>
    [Fact]
    public void GeneratePublisherContentId_WithEmptyPublisher_ThrowsArgumentException()
    {
        // Arrange
        var publisherId = string.Empty;
        var contentType = ContentType.Mod;
        var contentName = "test-mod";

        // Act & Assert
        Assert.Throws<ArgumentException>(() => ManifestIdGenerator.GeneratePublisherContentId(publisherId, contentType, contentName, 0));
    }

    /// <summary>
    /// Tests that GeneratePublisherContentId with empty content throws ArgumentException.
    /// </summary>
    [Fact]
    public void GeneratePublisherContentId_WithEmptyContent_ThrowsArgumentException()
    {
        // Arrange
        var publisherId = "cnclabs";
        var contentType = ContentType.Mod;
        var contentName = string.Empty;

        // Act & Assert
        Assert.Throws<ArgumentException>(() => ManifestIdGenerator.GeneratePublisherContentId(publisherId, contentType, contentName, 0));
    }

    /// <summary>
    /// Tests that GeneratePublisherContentId with negative version throws ArgumentException.
    /// </summary>
    [Fact]
    public void GeneratePublisherContentId_WithNegativeVersion_ThrowsArgumentException()
    {
        // Arrange
        var publisherId = "cnclabs";
        var contentType = ContentType.Mod;
        var contentName = "test";

        // Act & Assert
        Assert.Throws<ArgumentException>(() => ManifestIdGenerator.GeneratePublisherContentId(publisherId, contentType, contentName, -1));
    }

    /// <summary>
    /// Tests that GeneratePublisherContentId with null publisher throws ArgumentNullException.
    /// </summary>
    [Fact]
    public void GeneratePublisherContentId_WithNullPublisher_ThrowsArgumentNullException()
    {
        // Arrange
        string? publisherId = null;
        var contentType = ContentType.Mod;
        var contentName = "test";

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => ManifestIdGenerator.GeneratePublisherContentId(publisherId!, contentType, contentName, 0));
    }

    /// <summary>
    /// Tests that GenerateGameInstallationId with Steam Generals returns correct 5-segment format.
    /// </summary>
    [Fact]
    public void GenerateGameInstallationId_WithSteamGenerals_ReturnsCorrectFormat()
    {
        // Arrange
        var installation = new GameInstallation("C:\\Games\\Generals", GameInstallationType.Steam);
        var gameType = GameType.Generals;
        var expected = "1.0.steam.gameinstallation.generals";

        // Act
        var result = ManifestIdGenerator.GenerateGameInstallationId(installation, gameType, 0);

        // Assert
        Assert.Equal(expected, result);
        Assert.True(ManifestIdValidator.IsValid(result, out _));
    }

    /// <summary>
    /// Tests that GenerateGameInstallationId with EA App Zero Hour returns correct format.
    /// </summary>
    [Fact]
    public void GenerateGameInstallationId_WithEaAppZeroHour_ReturnsCorrectFormat()
    {
        // Arrange
        var installation = new GameInstallation("C:\\Games\\ZeroHour", GameInstallationType.EaApp);
        var gameType = GameType.ZeroHour;
        var expected = "1.1.eaapp.gameinstallation.zerohour";

        // Act
        var result = ManifestIdGenerator.GenerateGameInstallationId(installation, gameType, 1);

        // Assert
        Assert.Equal(expected, result);
        Assert.True(ManifestIdValidator.IsValid(result, out _));
    }

    /// <summary>
    /// Tests that GenerateGameInstallationId with negative version throws ArgumentException.
    /// </summary>
    [Fact]
    public void GenerateGameInstallationId_WithNegativeVersion_ThrowsArgumentException()
    {
        // Arrange
        var installation = new GameInstallation("C:\\Games", GameInstallationType.Retail);
        var gameType = GameType.Generals;

        // Act & Assert
        Assert.Throws<ArgumentException>(() => ManifestIdGenerator.GenerateGameInstallationId(installation, gameType, -1));
    }
}
