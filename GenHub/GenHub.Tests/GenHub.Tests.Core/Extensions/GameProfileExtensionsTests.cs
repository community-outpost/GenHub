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
