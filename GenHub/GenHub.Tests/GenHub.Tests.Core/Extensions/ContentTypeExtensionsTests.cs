using System;
using GenHub.Core.Extensions;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Extensions;

/// <summary>
/// Unit tests for <see cref="ContentTypeExtensions"/>.
/// </summary>
public class ContentTypeExtensionsTests
{
    /// <summary>
    /// Tests that GetDisplayName returns expected user-friendly display names for all content types.
    /// </summary>
    /// <param name="contentType">The content type.</param>
    /// <param name="expectedDisplayName">The expected display name.</param>
    [Theory]
    [InlineData(ContentType.GameInstallation, "Game Installation")]
    [InlineData(ContentType.GameClient, "GameClient")]
    [InlineData(ContentType.Mod, "Mods")]
    [InlineData(ContentType.Patch, "Patch")]
    [InlineData(ContentType.Addon, "Addons")]
    [InlineData(ContentType.MapPack, "Maps")]
    [InlineData(ContentType.Map, "Map")]
    [InlineData(ContentType.Mission, "Mission")]
    [InlineData(ContentType.LanguagePack, "Language Pack")]
    [InlineData(ContentType.ContentBundle, "Content Bundle")]
    [InlineData(ContentType.PublisherReferral, "Publisher Referral")]
    [InlineData(ContentType.ContentReferral, "Content Referral")]
    [InlineData(ContentType.ModdingTool, "Tool")]
    [InlineData(ContentType.Executable, "Executable")]
    [InlineData(ContentType.Skin, "Skin")]
    [InlineData(ContentType.Video, "Video")]
    [InlineData(ContentType.Replay, "Replay")]
    [InlineData(ContentType.Screensaver, "Screensaver")]
    [InlineData(ContentType.UnknownContentType, "Unknown")]
    public void GetDisplayName_ReturnsExpectedDisplayName(ContentType contentType, string expectedDisplayName)
    {
        var result = contentType.GetDisplayName();
        Assert.Equal(expectedDisplayName, result);
    }

    /// <summary>
    /// Tests that ToManifestIdString returns stable lowercase string representations for all content types.
    /// </summary>
    /// <param name="contentType">The content type.</param>
    /// <param name="expectedManifestString">The expected manifest ID segment string.</param>
    [Theory]
    [InlineData(ContentType.GameInstallation, "gameinstallation")]
    [InlineData(ContentType.GameClient, "gameclient")]
    [InlineData(ContentType.Mod, "mod")]
    [InlineData(ContentType.Patch, "patch")]
    [InlineData(ContentType.Addon, "addon")]
    [InlineData(ContentType.MapPack, "mappack")]
    [InlineData(ContentType.LanguagePack, "languagepack")]
    [InlineData(ContentType.ContentBundle, "contentbundle")]
    [InlineData(ContentType.PublisherReferral, "publisherreferral")]
    [InlineData(ContentType.ContentReferral, "contentreferral")]
    [InlineData(ContentType.Mission, "mission")]
    [InlineData(ContentType.Map, "map")]
    [InlineData(ContentType.Skin, "skin")]
    [InlineData(ContentType.Video, "video")]
    [InlineData(ContentType.Replay, "replay")]
    [InlineData(ContentType.Screensaver, "screensaver")]
    [InlineData(ContentType.ModdingTool, "moddingtool")]
    [InlineData(ContentType.Executable, "executable")]
    [InlineData(ContentType.UnknownContentType, "unknown")]
    public void ToManifestIdString_ReturnsExpectedManifestIdString(ContentType contentType, string expectedManifestString)
    {
        var result = contentType.ToManifestIdString();
        Assert.Equal(expectedManifestString, result);
    }

    /// <summary>
    /// Tests that no valid ContentType enum value produces 'unknown' except UnknownContentType.
    /// </summary>
    [Fact]
    public void ToManifestIdString_AllValidEnums_DoNotReturnUnknown()
    {
        foreach (ContentType contentType in Enum.GetValues<ContentType>())
        {
            if (contentType == ContentType.UnknownContentType)
            {
                continue;
            }

            var manifestString = contentType.ToManifestIdString();
            Assert.NotEqual("unknown", manifestString);
        }
    }

    /// <summary>
    /// Tests that IsStandalone returns true only for standalone tools and executables.
    /// </summary>
    /// <param name="contentType">The content type.</param>
    /// <param name="expectedStandalone">The expected boolean value.</param>
    [Theory]
    [InlineData(ContentType.ModdingTool, true)]
    [InlineData(ContentType.Executable, true)]
    [InlineData(ContentType.Mod, false)]
    [InlineData(ContentType.Addon, false)]
    [InlineData(ContentType.GameClient, false)]
    public void IsStandalone_ReturnsExpectedValue(ContentType contentType, bool expectedStandalone)
    {
        var result = contentType.IsStandalone();
        Assert.Equal(expectedStandalone, result);
    }
}
