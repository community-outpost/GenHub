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
    /// Verifies that MapCategory maps ModDB category codes correctly, preserving Patch and Skin mappings.
    /// </summary>
    /// <param name="categoryCode">The category code to map.</param>
    /// <param name="expected">The expected content type.</param>
    [Theory]
    [InlineData("2", ContentType.Mod)]
    [InlineData("3", ContentType.Mod)]
    [InlineData("4", ContentType.Patch)]
    [InlineData("28", ContentType.Patch)]
    [InlineData("29", ContentType.Addon)]
    [InlineData("7", ContentType.Video)]
    [InlineData("8", ContentType.Video)]
    [InlineData("101", ContentType.Map)]
    [InlineData("102", ContentType.Map)]
    [InlineData("112", ContentType.Skin)]
    [InlineData("125", ContentType.Skin)]
    [InlineData("126", ContentType.Skin)]
    [InlineData("20", ContentType.ModdingTool)]
    [InlineData("30", ContentType.LanguagePack)]
    public void MapCategory_MapsCategoryCodesCorrectly(string categoryCode, ContentType expected)
    {
        var result = ModDBCategoryMapper.MapCategory(categoryCode);
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Verifies that MapCategoryByName maps category names correctly, preserving Patch and Skin mappings.
    /// </summary>
    /// <param name="categoryName">The category name to map.</param>
    /// <param name="expected">The expected content type.</param>
    [Theory]
    [InlineData("Full Version", ContentType.Mod)]
    [InlineData("Demo", ContentType.Mod)]
    [InlineData("Patch", ContentType.Patch)]
    [InlineData("v1.01 Patch", ContentType.Patch)]
    [InlineData("Script", ContentType.Patch)]
    [InlineData("Multiplayer Map", ContentType.Map)]
    [InlineData("Singleplayer Map", ContentType.Map)]
    [InlineData("Player Skin", ContentType.Skin)]
    [InlineData("GUI", ContentType.Skin)]
    [InlineData("HUD", ContentType.Skin)]
    [InlineData("Mapping Tool", ContentType.ModdingTool)]
    [InlineData("Mod SDK", ContentType.ModdingTool)]
    [InlineData("Mod Tools", ContentType.ModdingTool)]
    [InlineData("2023", ContentType.Addon)]
    [InlineData("Language Pack", ContentType.LanguagePack)]
    [InlineData("Trailer", ContentType.Video)]
    [InlineData("Video", ContentType.Video)]
    [InlineData("Gameplay Video", ContentType.Video)]
    [InlineData("Mapping Tool Video", ContentType.Video)]
    [InlineData("IDE", ContentType.ModdingTool)]
    [InlineData("Modding IDE", ContentType.ModdingTool)]
    [InlineData("GameClient", ContentType.GameClient)]
    [InlineData("Game Client", ContentType.GameClient)]
    [InlineData("gameclient", ContentType.GameClient)]
    [InlineData("Game Installation", ContentType.GameInstallation)]
    [InlineData("Content Bundle", ContentType.ContentBundle)]
    [InlineData("Executable", ContentType.Executable)]
    [InlineData("Map Pack", ContentType.MapPack)]
    [InlineData("maps", ContentType.MapPack)]
    [InlineData("Mission", ContentType.Mission)]
    [InlineData("language mod", ContentType.Mod)]
    [InlineData("map mission", ContentType.Map)]
    public void MapCategoryByName_MapsNamesCorrectly(string categoryName, ContentType expected)
    {
        var result = ModDBCategoryMapper.MapCategoryByName(categoryName);
        Assert.Equal(expected, result);
    }
}
