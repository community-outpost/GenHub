using GenHub.Core.Models.Content;
using Xunit;

namespace GenHub.Tests.Core.Models.Content;

/// <summary>
/// Unit tests for <see cref="ContentSearchQuery"/>.
/// </summary>
public class ContentSearchQueryTests
{
    /// <summary>
    /// Verifies that Language property accepts null value.
    /// </summary>
    [Fact]
    public void Language_AcceptsNullValue()
    {
        // Arrange & Act
        var query = new ContentSearchQuery { Language = null };

        // Assert
        Assert.Null(query.Language);
    }

    /// <summary>
    /// Verifies that Language property accepts lowercase input.
    /// </summary>
    [Fact]
    public void Language_AcceptsLowercaseInput()
    {
        // Arrange & Act
        var query = new ContentSearchQuery { Language = "en" };

        // Assert
        Assert.Equal("EN", query.Language);
    }

    /// <summary>
    /// Verifies that Language property accepts uppercase input.
    /// </summary>
    [Fact]
    public void Language_AcceptsUppercaseInput()
    {
        // Arrange & Act
        var query = new ContentSearchQuery { Language = "EN" };

        // Assert
        Assert.Equal("EN", query.Language);
    }

    /// <summary>
    /// Verifies that Language property accepts supported language codes.
    /// </summary>
    /// <param name="input">The input language code.</param>
    /// <param name="expected">The expected normalized language code.</param>
    [Theory]
    [InlineData("All", "All")]
    [InlineData("EN", "EN")]
    [InlineData("DE", "DE")]
    [InlineData("FR", "FR")]
    [InlineData("ES", "ES")]
    [InlineData("IT", "IT")]
    [InlineData("KO", "KO")]
    [InlineData("PL", "PL")]
    [InlineData("PT-BR", "PT-BR")]
    [InlineData("ZH-CN", "ZH-CN")]
    [InlineData("ZH-TW", "ZH-TW")]
    [InlineData("pt", "PT-BR")]
    [InlineData("zh", "ZH-CN")]
    [InlineData("all", "All")]
    [InlineData(" en ", "EN")]
    [InlineData("PT_BR", "PT-BR")]
    public void Language_AcceptsSupportedLanguageCodes(string input, string expected)
    {
        // Arrange & Act
        var query = new ContentSearchQuery { Language = input };

        // Assert
        Assert.Equal(expected, query.Language);
    }

    /// <summary>
    /// Verifies that default Language property is null.
    /// </summary>
    [Fact]
    public void Language_DefaultValueIsNull()
    {
        // Arrange & Act
        var query = new ContentSearchQuery();

        // Assert
        Assert.Null(query.Language);
    }
}
