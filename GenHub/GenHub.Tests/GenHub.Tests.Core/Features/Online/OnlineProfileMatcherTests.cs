using GenHub.Core.Helpers;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Online;
using ContentType = GenHub.Core.Models.Enums.ContentType;
using GameType = GenHub.Core.Models.Enums.GameType;

namespace GenHub.Tests.Core.Features.Online;

/// <summary>
/// Unit tests for <see cref="OnlineProfileMatcher"/>.
/// </summary>
public class OnlineProfileMatcherTests
{
    /// <summary>
    /// Tests that cosmetics never enter the fingerprint or match outcome.
    /// </summary>
    [Fact]
    public void ComputeFingerprint_WithCosmeticOnlyDifference_ShouldMatch()
    {
        // Arrange
        var plain = ProfileWith("1.0.0.steam.mod.generals-plus", "1.0.0.steam.skin.dark-ui");
        var types = new Dictionary<string, ContentType>(StringComparer.Ordinal)
        {
            ["1.0.0.steam.mod.generals-plus"] = ContentType.Mod,
            ["1.0.0.steam.skin.dark-ui"] = ContentType.Skin,
        };

        // Act
        var fingerprint = OnlineProfileMatcher.ComputeFingerprint(plain, types);
        var bare = OnlineProfileMatcher.ComputeFingerprint(
            ProfileWith("1.0.0.steam.mod.generals-plus"), types);

        // Assert
        Assert.Equal(bare, fingerprint);
        Assert.DoesNotContain("dark-ui", fingerprint, StringComparison.Ordinal);
    }

    /// <summary>
    /// Tests that unknown content types count as gameplay content.
    /// </summary>
    [Fact]
    public void GetGameplayContentIds_WithUnknownType_ShouldInclude()
    {
        // Arrange
        var profile = ProfileWith("1.0.0.steam.mod.generals-plus", "mystery-content");

        // Act
        var ids = OnlineProfileMatcher.GetGameplayContentIds(profile, new Dictionary<string, ContentType>());

        // Assert
        Assert.Contains("mystery-content", ids);
    }

    /// <summary>
    /// Tests that identical setups compare as exact.
    /// </summary>
    [Fact]
    public void Compare_WithIdenticalFingerprints_ShouldBeExact()
    {
        // Arrange
        var profile = ProfileWith("1.0.0.steam.mod.generals-plus");
        var fingerprint = OnlineProfileMatcher.ComputeFingerprint(profile, contentTypes: null);
        var client = OnlineProfileMatcher.GetGameClientKey(profile);

        // Act
        var match = OnlineProfileMatcher.Compare(fingerprint, client, fingerprint, client);

        // Assert
        Assert.Equal(OnlineProfileMatch.Exact, match);
    }

    /// <summary>
    /// Tests that the same client with different mods compares as same-client.
    /// </summary>
    [Fact]
    public void Compare_WithSameClientDifferentMods_ShouldBeSameClient()
    {
        // Arrange
        var first = ProfileWith("1.0.0.steam.mod.generals-plus");
        var second = ProfileWith("1.0.0.steam.mod.other-mod");
        var expected = OnlineProfileMatcher.ComputeFingerprint(first, contentTypes: null);
        var local = OnlineProfileMatcher.ComputeFingerprint(second, contentTypes: null);
        var client = OnlineProfileMatcher.GetGameClientKey(first);

        // Act
        var match = OnlineProfileMatcher.Compare(expected, client, local, client);

        // Assert
        Assert.Equal(OnlineProfileMatch.SameClient, match);
    }

    /// <summary>
    /// Tests that different clients compare as mismatch.
    /// </summary>
    [Fact]
    public void Compare_WithDifferentClients_ShouldBeMismatch()
    {
        // Arrange
        var zeroHour = ProfileWith("1.0.0.steam.mod.generals-plus");
        var generals = ProfileWith("1.0.0.steam.mod.generals-plus");
        generals.GameClient = new GameClient
        {
            Id = "generals-client",
            Name = "Generals",
            Version = "1.08",
            GameType = GameType.Generals,
        };
        var expected = OnlineProfileMatcher.ComputeFingerprint(zeroHour, contentTypes: null);
        var local = OnlineProfileMatcher.ComputeFingerprint(generals, contentTypes: null);

        // Act
        var match = OnlineProfileMatcher.Compare(
            expected,
            OnlineProfileMatcher.GetGameClientKey(zeroHour),
            local,
            OnlineProfileMatcher.GetGameClientKey(generals));

        // Assert
        Assert.Equal(OnlineProfileMatch.Mismatch, match);
    }

    /// <summary>
    /// Tests that empty fingerprints compare as unknown.
    /// </summary>
    [Fact]
    public void Compare_WithEmptyFingerprint_ShouldBeUnknown()
    {
        // Act
        var match = OnlineProfileMatcher.Compare("opf1|x|y", "x", string.Empty, "x");

        // Assert
        Assert.Equal(OnlineProfileMatch.Unknown, match);
    }

    /// <summary>
    /// Tests that the client key extracts from current and previous fingerprints.
    /// </summary>
    /// <param name="fingerprint">The fingerprint to parse.</param>
    [Theory]
    [InlineData("opf2|ZeroHour|1.04|zerohour-client|0123456789abcdef")]
    [InlineData("opf1|ZeroHour|1.04|zerohour-client|some-mod")]
    public void TryGetGameClientKey_WithValidFingerprint_ShouldExtract(string fingerprint)
    {
        // Act
        var parsed = OnlineProfileMatcher.TryGetGameClientKey(fingerprint, out var client);

        // Assert
        Assert.True(parsed);
        Assert.Equal("ZeroHour|1.04|zerohour-client", client);
    }

    /// <summary>
    /// Tests that malformed fingerprints fail parsing.
    /// </summary>
    /// <param name="fingerprint">The malformed fingerprint.</param>
    [Theory]
    [InlineData("")]
    [InlineData("not-a-fingerprint")]
    [InlineData("opf2|only-client")]
    public void TryGetGameClientKey_WithMalformedInput_ShouldFail(string fingerprint)
    {
        // Act
        var parsed = OnlineProfileMatcher.TryGetGameClientKey(fingerprint, out _);

        // Assert
        Assert.False(parsed);
    }

    /// <summary>
    /// Tests that fingerprints stay bounded no matter the content count.
    /// </summary>
    [Fact]
    public void ComputeFingerprint_WithManyLongIds_ShouldStayBounded()
    {
        // Arrange
        var ids = Enumerable.Range(0, 100).Select(i => $"1.0.0.steam.mod.very-long-mod-name-number-{i:D3}-padding").ToArray();
        var profile = ProfileWith(ids);

        // Act
        var fingerprint = OnlineProfileMatcher.ComputeFingerprint(profile, contentTypes: null);

        // Assert
        Assert.True(fingerprint.Length < 256);
        Assert.StartsWith("opf2|", fingerprint, StringComparison.Ordinal);
    }

    /// <summary>
    /// Tests that content id lists bound to the edge publish limit.
    /// </summary>
    [Fact]
    public void BoundContentIds_WithOversizedList_ShouldCap()
    {
        // Arrange
        var ids = Enumerable.Range(0, 50).Select(i => $"id-{i:D2}").ToList();

        // Act
        var bounded = OnlineProfileMatcher.BoundContentIds(ids);

        // Assert
        Assert.Equal(32, bounded.Count);
        Assert.Equal("id-00", bounded[0]);
    }

    /// <summary>
    /// Tests that overlap scoring counts shared ids.
    /// </summary>
    [Fact]
    public void ScoreOverlap_WithSharedIds_ShouldCountThem()
    {
        // Act
        var score = OnlineProfileMatcher.ScoreOverlap(["a", "b", "c"], ["b", "c", "d"]);

        // Assert
        Assert.Equal(2, score);
    }

    private static GameProfile ProfileWith(params string[] contentIds)
    {
        return new GameProfile
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Test",
            GameClient = new GameClient
            {
                Id = "zerohour-client",
                Name = "Zero Hour",
                Version = "1.04",
                GameType = GameType.ZeroHour,
            },
            EnabledContentIds = [.. contentIds],
        };
    }
}
