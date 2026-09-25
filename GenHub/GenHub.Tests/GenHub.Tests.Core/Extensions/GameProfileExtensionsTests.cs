using GenHub.Core.Constants;
using GenHub.Core.Extensions;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameProfile;

namespace GenHub.Tests.Core.Extensions;

/// <summary>
/// Tests for <see cref="GameProfileExtensions"/>.
/// </summary>
public class GameProfileExtensionsTests
{
    /// <summary>
    /// Verifies that the publisher type identifies a GeneralsOnline profile regardless of casing.
    /// </summary>
    /// <param name="publisherType">The publisher type recorded on the profile's client.</param>
    [Theory]
    [InlineData("generalsonline")]
    [InlineData("GeneralsOnline")]
    [InlineData("GENERALSONLINE")]
    public void IsGeneralsOnlineProfile_WithGeneralsOnlinePublisher_ReturnsTrue(string publisherType)
    {
        // Arrange
        var profile = CreateZeroHourProfile(publisherType, "Zero Hour", []);

        // Act & Assert
        Assert.True(profile.IsGeneralsOnlineProfile());
    }

    /// <summary>
    /// Verifies that other Zero Hour publishers are not mistaken for GeneralsOnline, which is what
    /// kept their launches from overwriting the GeneralsOnline client's settings.json.
    /// </summary>
    /// <param name="publisherType">The publisher type recorded on the profile's client.</param>
    [Theory]
    [InlineData(PublisherTypeConstants.TheSuperHackers)]
    [InlineData(CommunityOutpostConstants.PublisherType)]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsGeneralsOnlineProfile_WithOtherPublisher_ReturnsFalse(string? publisherType)
    {
        // Arrange
        var profile = CreateZeroHourProfile(publisherType, "Zero Hour", ["1.0.genhub.mod.test"]);

        // Act & Assert
        Assert.False(profile.IsGeneralsOnlineProfile());
    }

    /// <summary>
    /// Verifies that a recorded publisher settles the question, so a profile belonging to another
    /// client is not reclassified by content it happens to enable or by its client name. Answering
    /// otherwise would let it rewrite the GeneralsOnline client's global settings.
    /// </summary>
    /// <param name="publisherType">The publisher type recorded on the profile's client.</param>
    [Theory]
    [InlineData(PublisherTypeConstants.TheSuperHackers)]
    [InlineData(CommunityOutpostConstants.PublisherType)]
    public void IsGeneralsOnlineProfile_WithOtherPublisherAndGeneralsOnlineHints_ReturnsFalse(string publisherType)
    {
        // Arrange
        var profile = CreateZeroHourProfile(
            publisherType,
            "GeneralsOnline Compatible",
            ["1.9.generalsonline.gameclient.30hz"]);

        // Act & Assert
        Assert.False(profile.IsGeneralsOnlineProfile());
    }

    /// <summary>
    /// Verifies that a profile predating the recorded publisher type is still recognised by its
    /// client name. Such a profile records no publisher at all, so null is its real shape.
    /// </summary>
    /// <param name="publisherType">The publisher type recorded on the profile's client.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void IsGeneralsOnlineProfile_WithGeneralsOnlineClientName_ReturnsTrue(string? publisherType)
    {
        // Arrange
        var profile = CreateZeroHourProfile(publisherType, "GeneralsOnline 30Hz", []);

        // Act & Assert
        Assert.True(profile.IsGeneralsOnlineProfile());
    }

    /// <summary>
    /// Verifies that a profile predating the recorded publisher type is still recognised by its
    /// enabled content. Such a profile records no publisher at all, so null is its real shape.
    /// </summary>
    /// <param name="publisherType">The publisher type recorded on the profile's client.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void IsGeneralsOnlineProfile_WithGeneralsOnlineContent_ReturnsTrue(string? publisherType)
    {
        // Arrange
        var profile = CreateZeroHourProfile(publisherType, "Zero Hour", ["1.9.generalsonline.gameclient.30hz"]);

        // Act & Assert
        Assert.True(profile.IsGeneralsOnlineProfile());
    }

    /// <summary>
    /// Verifies that a profile with no client at all, which is the shape the settings editor sees
    /// while a profile is being created, falls back to its enabled content.
    /// </summary>
    /// <param name="contentId">The content the profile enables.</param>
    /// <param name="expected">Whether that content makes it a GeneralsOnline profile.</param>
    [Theory]
    [InlineData("1.9.generalsonline.gameclient.30hz", true)]
    [InlineData("1.0.genhub.mod.test", false)]
    public void IsGeneralsOnlineProfile_WithoutGameClient_FallsBackToContent(string contentId, bool expected)
    {
        // Arrange
        var profile = new GameProfile
        {
            Id = "profile-1",
            Name = "Test Profile",
            EnabledContentIds = [contentId],
        };

        // Act & Assert
        Assert.Equal(expected, profile.IsGeneralsOnlineProfile());
    }

    /// <summary>
    /// Verifies that Community Patch profiles and clients are recognized as Community Outpost profiles,
    /// even when the build name contains "TheSuperHackers".
    /// </summary>
    /// <param name="clientName">The client name.</param>
    [Theory]
    [InlineData("Community Patch")]
    [InlineData("Community Patch (TheSuperHackers Build)")]
    [InlineData("CommunityPatch")]
    [InlineData("Community Outpost")]
    public void IsCommunityOutpostProfile_WithCommunityPatchOrOutpostClientName_ReturnsTrue(string clientName)
    {
        // Arrange
        var profile = CreateZeroHourProfile(null, clientName, []);

        // Act & Assert
        Assert.True(profile.IsCommunityOutpostProfile());
    }

    /// <summary>
    /// Verifies that a profile named "community patch (thesuperhackers)" is identified as Community Outpost
    /// rather than TheSuperHackers, preventing false-positive branding override.
    /// </summary>
    [Fact]
    public void IsTheSuperHackersProfile_WithCommunityPatchTheSuperHackers_ReturnsFalse()
    {
        // Arrange
        var profile = new GameProfile
        {
            Id = "profile-cp",
            Name = "community patch (thesuperhackers)",
            GameClient = new GameClient
            {
                Id = "client-cp",
                Name = "Community Patch (TheSuperHackers Build)",
                GameType = GameType.ZeroHour,
            },
        };

        // Act & Assert
        Assert.True(profile.IsCommunityOutpostProfile());
        Assert.False(profile.IsTheSuperHackersProfile());
    }

    /// <summary>
    /// Verifies that genuine TheSuperHackers profiles are correctly identified.
    /// </summary>
    /// <param name="publisherType">The publisher type string.</param>
    [Theory]
    [InlineData("TheSuperHackers")]
    [InlineData("SuperHackers")]
    [InlineData("thesuperhackers")]
    public void IsTheSuperHackersProfile_WithGenuineSuperHackers_ReturnsTrue(string publisherType)
    {
        // Arrange
        var profile = CreateZeroHourProfile(publisherType, "Generals Zero Hour", []);

        // Act & Assert
        Assert.True(profile.IsTheSuperHackersProfile());
        Assert.False(profile.IsCommunityOutpostProfile());
    }

    /// <summary>
    /// Verifies that Community Patch keywords resolve to Community Outpost covers and logos,
    /// even if "thesuperhackers" appears in the identifier.
    /// </summary>
    /// <param name="key">The publisher key or profile/client name.</param>
    [Theory]
    [InlineData("community patch (thesuperhackers)")]
    [InlineData("Community Patch (TheSuperHackers Build)")]
    [InlineData("community-patch")]
    public void PublisherInfoConstants_CommunityPatch_ResolvesToCommunityOutpost(string key)
    {
        var cover = PublisherInfoConstants.GetPublisherCover(key);
        var logo = PublisherInfoConstants.GetPublisherLogo(key);

        Assert.Equal(CommunityOutpostConstants.CoverSource, cover);
        Assert.Equal(CommunityOutpostConstants.LogoSource, logo);
    }

    private static GameProfile CreateZeroHourProfile(string? publisherType, string clientName, List<string> enabledContentIds)
    {
        return new GameProfile
        {
            Id = "profile-1",
            Name = "Test Profile",
            GameClient = new GameClient
            {
                Id = "client-1",
                Name = clientName,
                GameType = GameType.ZeroHour,
                PublisherType = publisherType,
            },
            EnabledContentIds = enabledContentIds,
        };
    }
}
