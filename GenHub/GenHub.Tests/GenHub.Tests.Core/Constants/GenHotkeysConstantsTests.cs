using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using Xunit;

namespace GenHub.Tests.Core.Constants;

/// <summary>
/// Unit tests for <see cref="GenHotkeysConstants"/>.
/// </summary>
public class GenHotkeysConstantsTests
{
    /// <summary>
    /// Verifies that <see cref="GenHotkeysConstants.BigFileNamePattern"/> has the expected format string.
    /// </summary>
    [Fact]
    public void BigFileNamePattern_ShouldMatchExpectedFormat()
    {
        Assert.Equal("!Hotkeys_{0}{1}_{2}.big", GenHotkeysConstants.BigFileNamePattern);
    }

    /// <summary>
    /// Verifies that <see cref="GenHotkeysConstants.GetBigFileName(string?, GameType)"/> formats the filename
    /// correctly without a profile ID suffix.
    /// </summary>
    [Fact]
    public void GetBigFileName_WithoutProfileId_FormatsCorrectly()
    {
        var generalsFileName = GenHotkeysConstants.GetBigFileName("MyProfile", GameType.Generals);
        var zhFileName = GenHotkeysConstants.GetBigFileName("MyProfile", GameType.ZeroHour);

        Assert.Equal("!Hotkeys_MyProfile_Gen.big", generalsFileName);
        Assert.Equal("!Hotkeys_MyProfile_ZH.big", zhFileName);
    }

    /// <summary>
    /// Verifies that <see cref="GenHotkeysConstants.GetBigFileName(string?, GameType, string?)"/> includes
    /// the first 8 characters of the profile ID as a suffix.
    /// </summary>
    [Fact]
    public void GetBigFileName_WithProfileId_FormatsWithIdSuffix()
    {
        var fileName = GenHotkeysConstants.GetBigFileName("Competitive", GameType.ZeroHour, "12345678abcdef");

        Assert.Equal("!Hotkeys_Competitive_12345678_ZH.big", fileName);
    }

    /// <summary>
    /// Verifies that <see cref="GenHotkeysConstants.GetBigFileName(string?, GameType, string?)"/> sanitizes
    /// special characters in the profile name.
    /// </summary>
    [Fact]
    public void GetBigFileName_WithSpecialCharacters_SanitizesName()
    {
        var fileName = GenHotkeysConstants.GetBigFileName("My Cool Hotkeys!@#", GameType.ZeroHour);

        Assert.Equal("!Hotkeys_My_Cool_Hotkeys____ZH.big", fileName);
    }

    /// <summary>
    /// Verifies that <see cref="GenHotkeysConstants.GetBigFileName(string?, GameType, string?)"/> defaults to
    /// "Hotkeys" when the profile name is null or whitespace.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GetBigFileName_WhenNullOrWhitespace_DefaultsToHotkeys(string? profileName)
    {
        var fileName = GenHotkeysConstants.GetBigFileName(profileName, GameType.ZeroHour);

        Assert.Equal("!Hotkeys_Hotkeys_ZH.big", fileName);
    }
}
