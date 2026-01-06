using GenHub.Core.Constants;
using GenHub.Core.Models.CommunityOutpost;
using GenHub.Core.Models.Enums;

using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Content.CommunityOutpost;

/// <summary>
/// Tests for <see cref="GenPatcherContentRegistry"/>.
/// </summary>
public class GenPatcherContentRegistryTests
{
    /// <summary>
    /// Verifies that GetMetadata returns correct metadata for known content codes.
    /// </summary>
    /// <param name="contentCode">The content code to look up.</param>
    /// <param name="expectedName">The expected display name.</param>
    /// <param name="expectedType">The expected content type.</param>
    /// <param name="expectedGame">The expected target game.</param>
    [Theory]
    [InlineData("gent", "GenTool", ContentType.Addon, GameType.ZeroHour)]
    [InlineData("genl", "GenLauncher", ContentType.Addon, GameType.ZeroHour)]
    [InlineData("10gn", "Generals 1.08", ContentType.GameClient, GameType.Generals)]
    [InlineData("10zh", "Zero Hour 1.04", ContentType.GameClient, GameType.ZeroHour)]
    [InlineData("cbbs", "Control Bar - Basic", ContentType.Addon, GameType.ZeroHour)]
    [InlineData("crzh", "Camera Mod - Zero Hour", ContentType.Addon, GameType.ZeroHour)]
    public void GetMetadata_ReturnsCorrectMetadataForKnownCodes(
        string contentCode,
        string expectedName,
        ContentType expectedType,
        GameType expectedGame)
    {
        // Act
        var metadata = GenPatcherContentRegistry.GetMetadata(contentCode);

        // Assert
        Assert.Equal(expectedName, metadata.DisplayName);
        Assert.Equal(expectedType, metadata.ContentType);
        Assert.Equal(expectedGame, metadata.TargetGame);
    }

    /// <summary>
    /// Verifies that GetMetadata correctly parses patch codes with language suffixes.
    /// </summary>
    /// <param name="contentCode">The patch content code to parse.</param>
    /// <param name="expectedLanguage">The expected language code.</param>
    /// <param name="expectedGame">The expected target game.</param>
    /// <param name="expectedVersion">The expected patch version string.</param>
    [Theory]
    [InlineData("108e", "en", GameType.Generals, "1.08")]
    [InlineData("108b", "pt-BR", GameType.Generals, "1.08")]
    [InlineData("108d", "de", GameType.Generals, "1.08")]
    [InlineData("108f", "fr", GameType.Generals, "1.08")]
    [InlineData("104e", "en", GameType.ZeroHour, "1.04")]
    [InlineData("104c", "zh", GameType.ZeroHour, "1.04")]
    [InlineData("104k", "ko", GameType.ZeroHour, "1.04")]
    public void GetMetadata_ParsesPatchCodesCorrectly(
        string contentCode,
        string expectedLanguage,
        GameType expectedGame,
        string expectedVersion)
    {
        // Act
        var metadata = GenPatcherContentRegistry.GetMetadata(contentCode);

        // Assert
        Assert.Equal(ContentType.Patch, metadata.ContentType);
        Assert.Equal(expectedLanguage, metadata.LanguageCode);
        Assert.Equal(expectedGame, metadata.TargetGame);
        Assert.Equal(expectedVersion, metadata.Version);
        Assert.Equal(GenPatcherContentCategory.OfficialPatch, metadata.Category);
    }

    /// <summary>
    /// Verifies that GetMetadata returns unknown metadata for unrecognized codes.
    /// </summary>
    [Fact]
    public void GetMetadata_ReturnsUnknownForUnrecognizedCode()
    {
        // Act
        var metadata = GenPatcherContentRegistry.GetMetadata("zzzz");

        // Assert
        Assert.Contains(GameClientConstants.UnknownVersion, metadata.DisplayName);
        Assert.Equal(ContentType.UnknownContentType, metadata.ContentType);
        Assert.Equal(GenPatcherContentCategory.Other, metadata.Category);
    }

    /// <summary>
    /// Verifies that GetMetadata handles null or empty input gracefully.
    /// </summary>
    /// <param name="contentCode">The null or empty content code to test.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void GetMetadata_HandlesNullOrEmptyInput(string? contentCode)
    {
        // Act
        var metadata = GenPatcherContentRegistry.GetMetadata(contentCode!);

        // Assert
        Assert.NotNull(metadata);
        Assert.Equal(ContentType.UnknownContentType, metadata.ContentType);
    }

    /// <summary>
    /// Verifies that GetMetadata is case-insensitive.
    /// </summary>
    /// <param name="contentCode">The content code in various cases to test.</param>
    [Theory]
    [InlineData("GENT")]
    [InlineData("Gent")]
    [InlineData("gEnT")]
    public void GetMetadata_IsCaseInsensitive(string contentCode)
    {
        // Act
        var metadata = GenPatcherContentRegistry.GetMetadata(contentCode);

        // Assert
        Assert.Equal("GenTool", metadata.DisplayName);
        Assert.Equal(ContentType.Addon, metadata.ContentType);
    }

    /// <summary>
    /// Verifies that IsKnownCode returns true for known codes.
    /// </summary>
    /// <param name="contentCode">The known content code to test.</param>
    [Theory]
    [InlineData("gent")]
    [InlineData("genl")]
    [InlineData("cbbs")]
    [InlineData("10zh")]
    public void IsKnownCode_ReturnsTrueForKnownCodes(string contentCode)
    {
        // Act
        var isKnown = GenPatcherContentRegistry.IsKnownCode(contentCode);

        // Assert
        Assert.True(isKnown);
    }

    /// <summary>
    /// Verifies that IsKnownCode returns false for unknown codes.
    /// </summary>
    /// <param name="contentCode">The unknown content code to test.</param>
    [Theory]
    [InlineData("zzzz")]
    [InlineData("abcd")]
    [InlineData("108e")] // Patch codes are parsed dynamically, not in known list
    public void IsKnownCode_ReturnsFalseForUnknownCodes(string contentCode)
    {
        // Act
        var isKnown = GenPatcherContentRegistry.IsKnownCode(contentCode);

        // Assert
        Assert.False(isKnown);
    }

    /// <summary>
    /// Verifies that GetKnownContentCodes returns non-empty collection.
    /// </summary>
    [Fact]
    public void GetKnownContentCodes_ReturnsNonEmptyCollection()
    {
        // Act
        var codes = GenPatcherContentRegistry.GetKnownContentCodes();

        // Assert
        Assert.NotEmpty(codes);
        Assert.Contains("gent", codes);
        Assert.Contains("genl", codes);
        Assert.Contains("10zh", codes);
    }

    /// <summary>
    /// Verifies that content categories are correctly assigned.
    /// </summary>
    /// <param name="contentCode">The content code to look up.</param>
    /// <param name="expectedCategory">The expected content category.</param>
    [Theory]
    [InlineData("10gn", GenPatcherContentCategory.BaseGame)]
    [InlineData("cbbs", GenPatcherContentCategory.ControlBar)]
    [InlineData("crzh", GenPatcherContentCategory.Camera)]
    [InlineData("hlen", GenPatcherContentCategory.Hotkeys)]
    [InlineData("gent", GenPatcherContentCategory.Tools)]
    [InlineData("maod", GenPatcherContentCategory.Maps)]
    [InlineData("icon", GenPatcherContentCategory.Visuals)]
    [InlineData("vc05", GenPatcherContentCategory.Prerequisites)]
    public void GetMetadata_AssignsCorrectCategory(
        string contentCode,
        GenPatcherContentCategory expectedCategory)
    {
        // Act
        var metadata = GenPatcherContentRegistry.GetMetadata(contentCode);

        // Assert
        Assert.Equal(expectedCategory, metadata.Category);
    }

    /// <summary>
    /// Verifies that all supported language suffixes are recognized in patch codes.
    /// </summary>
    /// <param name="suffix">The language suffix character to test.</param>
    /// <param name="expectedLanguageCode">The expected ISO language code.</param>
    [Theory]
    [InlineData('e', "en")]
    [InlineData('b', "pt-BR")]
    [InlineData('c', "zh")]
    [InlineData('d', "de")]
    [InlineData('f', "fr")]
    [InlineData('i', "it")]
    [InlineData('k', "ko")]
    [InlineData('p', "pl")]
    [InlineData('s', "es")]
    public void GetMetadata_RecognizesAllLanguageSuffixes(char suffix, string expectedLanguageCode)
    {
        // Arrange
        var contentCode = $"108{suffix}";

        // Act
        var metadata = GenPatcherContentRegistry.GetMetadata(contentCode);

        // Assert
        Assert.Equal(expectedLanguageCode, metadata.LanguageCode);
        Assert.Equal(ContentType.Patch, metadata.ContentType);
    }
}
