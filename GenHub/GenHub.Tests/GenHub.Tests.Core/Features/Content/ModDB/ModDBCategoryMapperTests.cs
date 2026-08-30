using GenHub.Core.Models.Enums;
using GenHub.Core.Models.ModDB;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Content.ModDB;

/// <summary>
/// Unit tests for <see cref="ModDBCategoryMapper"/>.
/// </summary>
public class ModDBCategoryMapperTests
{
    /// <summary>
    /// Verifies that MapCategory maps ModDB category codes correctly, especially mapping patches and scripts to Mod.
    /// </summary>
    /// <param name="categoryCode">The category code to map.</param>
    /// <param name="expected">The expected content type.</param>
    [Theory]
    [InlineData("2", ContentType.Mod)]
    [InlineData("3", ContentType.Mod)]
    [InlineData("4", ContentType.Mod)]
    [InlineData("28", ContentType.Mod)]
    [InlineData("29", ContentType.Addon)]
    [InlineData("7", ContentType.Video)]
    [InlineData("8", ContentType.Video)]
    [InlineData("101", ContentType.Map)]
    [InlineData("102", ContentType.Map)]
    [InlineData("112", ContentType.Addon)]
    [InlineData("125", ContentType.Addon)]
    [InlineData("126", ContentType.Addon)]
    [InlineData("20", ContentType.ModdingTool)]
    [InlineData("30", ContentType.LanguagePack)]
    public void MapCategory_MapsCategoryCodesCorrectly(string categoryCode, ContentType expected)
    {
        var result = ModDBCategoryMapper.MapCategory(categoryCode);
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Verifies that MapCategoryByName maps category names correctly, mapping patch and script names to Mod.
    /// </summary>
    /// <param name="categoryName">The category name to map.</param>
    /// <param name="expected">The expected content type.</param>
    [Theory]
    [InlineData("Full Version", ContentType.Mod)]
    [InlineData("Demo", ContentType.Mod)]
    [InlineData("Patch", ContentType.Mod)]
    [InlineData("v1.01 Patch", ContentType.Mod)]
    [InlineData("Script", ContentType.Mod)]
    [InlineData("Multiplayer Map", ContentType.Map)]
    [InlineData("Singleplayer Map", ContentType.Map)]
    [InlineData("Player Skin", ContentType.Addon)]
    [InlineData("GUI", ContentType.Addon)]
    [InlineData("HUD", ContentType.Addon)]
    [InlineData("Mapping Tool", ContentType.ModdingTool)]
    [InlineData("Language Pack", ContentType.LanguagePack)]
    [InlineData("Trailer", ContentType.Video)]
    public void MapCategoryByName_MapsNamesCorrectly(string categoryName, ContentType expected)
    {
        var result = ModDBCategoryMapper.MapCategoryByName(categoryName);
        Assert.Equal(expected, result);
    }
}
