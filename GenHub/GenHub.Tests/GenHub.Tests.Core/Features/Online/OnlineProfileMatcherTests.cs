using GenHub.Core.Helpers;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Online;
using ContentType = GenHub.Core.Models.Enums.ContentType;
using GameType = GenHub.Core.Models.Enums.GameType;
using OnlineProfileMatch = GenHub.Core.Models.Online.OnlineProfileMatch;

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
    /// Tests that the same client with different mods without matching INI CRC compares as mismatch.
    /// </summary>
    [Fact]
    public void Compare_WithSameClientDifferentMods_ShouldBeMismatch()
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
        Assert.Equal(OnlineProfileMatch.Mismatch, match);
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
    [InlineData("opf3|ZeroHour|1.04|zerohour-client|0123456789abcdef")]
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
        Assert.StartsWith("opf3|", fingerprint, StringComparison.Ordinal);
    }

    /// <summary>
    /// Tests that a newline smuggled across the client/content boundary hashes distinctly.
    /// </summary>
    [Fact]
    public void ComputeFingerprint_WithNewlineAcrossBoundary_ShouldDiffer()
    {
        // Arrange: content id "A\nB" under client "Generals|v|i" versus content
        // "B" under client "Generals|v|i\nA" share one naive joined string.
        var plain = new GameProfile
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Plain",
            GameClient = new GameClient { Id = "i", Name = "Generals", Version = "v", GameType = GameType.Generals },
            EnabledContentIds = ["A\nB"],
        };
        var smuggled = new GameProfile
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Smuggled",
            GameClient = new GameClient { Id = "i\nA", Name = "Generals", Version = "v", GameType = GameType.Generals },
            EnabledContentIds = ["B"],
        };

        // Act
        var first = OnlineProfileMatcher.ComputeFingerprint(plain, contentTypes: null);
        var second = OnlineProfileMatcher.ComputeFingerprint(smuggled, contentTypes: null);

        // Assert
        Assert.NotEqual(first, second);
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

    /// <summary>
    /// Tests that a disputed registration resolves to gameplay, so duplicates
    /// surface as a mismatch instead of hiding a mod.
    /// </summary>
    [Fact]
    public void ResolveDeclaredType_WithGameplayDispute_ShouldPreferGameplay()
    {
        // Act
        var type = OnlineProfileMatcher.ResolveDeclaredType([ContentType.Addon, ContentType.Mod]);

        // Assert
        Assert.Equal(ContentType.Mod, type);
    }

    /// <summary>
    /// Tests that duplicate registrations resolve deterministically regardless
    /// of enumeration order, so two machines never classify the same id
    /// differently and report a phantom mod mismatch.
    /// </summary>
    [Fact]
    public void ResolveDeclaredType_WithDuplicates_ShouldBeOrderIndependent()
    {
        // Act
        var forward = OnlineProfileMatcher.ResolveDeclaredType([ContentType.Map, ContentType.Skin]);
        var reverse = OnlineProfileMatcher.ResolveDeclaredType([ContentType.Skin, ContentType.Map]);

        // Assert
        Assert.Equal(forward, reverse);
    }

    /// <summary>
    /// Tests that fingerprints without CRCs keep the previous version and
    /// shape, so id-only setups match exactly as before.
    /// </summary>
    [Fact]
    public void CreateFingerprint_WithoutCrcs_ShouldUseLegacyPrefix()
    {
        // Act
        var fingerprint = OnlineProfileMatcher.CreateFingerprint("ZeroHour|1.04|client-1", ["mod-a"]);

        // Assert
        Assert.StartsWith("opf3|ZeroHour|1.04|client-1|", fingerprint, StringComparison.Ordinal);
        Assert.Equal(5, fingerprint.Split('|').Length);
    }

    /// <summary>
    /// Tests that fingerprints with CRCs carry the versioned segments the
    /// compatibility verdict reads.
    /// </summary>
    [Fact]
    public void CreateFingerprint_WithCrcs_ShouldUseCompatibilityPrefix()
    {
        // Act
        var fingerprint = OnlineProfileMatcher.CreateFingerprint("ZeroHour|1.04|client-1", ["mod-a"], "0x11111111", "0x22222222");

        // Assert
        Assert.StartsWith("opf4|ZeroHour|1.04|client-1|", fingerprint, StringComparison.Ordinal);
        Assert.EndsWith("|0x11111111|0x22222222", fingerprint, StringComparison.Ordinal);
        Assert.Equal(7, fingerprint.Split('|').Length);
    }

    /// <summary>
    /// Tests that equal engine CRCs upgrade a same-client verdict to exact:
    /// the setups produce the same game data, so they can play together.
    /// </summary>
    [Fact]
    public void Compare_SameClientWithEqualIniCrc_ShouldUpgradeToExact()
    {
        // Arrange: same client and CRCs, but id-level noise in the content hash.
        var local = OnlineProfileMatcher.CreateFingerprint("ZeroHour|1.04|client-1", ["mod-a"], "0x11111111", "0x22222222");
        var expected = OnlineProfileMatcher.CreateFingerprint("ZeroHour|1.04|client-1", ["mod-b"], "0x11111111", "0x22222222");

        // Act
        var match = OnlineProfileMatcher.Compare(expected, "ZeroHour|1.04|client-1", local, "ZeroHour|1.04|client-1");

        // Assert
        Assert.Equal(OnlineProfileMatch.Exact, match);
    }

    /// <summary>
    /// Tests that differing engine INI CRCs result in a mismatch.
    /// </summary>
    [Fact]
    public void Compare_WithDifferingIniCrc_ShouldBeMismatch()
    {
        // Arrange
        var local = OnlineProfileMatcher.CreateFingerprint("ZeroHour|1.04|client-1", ["mod-a"], "0x11111111", "0x22222222");
        var expected = OnlineProfileMatcher.CreateFingerprint("ZeroHour|1.04|client-1", ["mod-a"], "0x33333333", "0x22222222");

        // Act
        var match = OnlineProfileMatcher.Compare(expected, "ZeroHour|1.04|client-1", local, "ZeroHour|1.04|client-1");

        // Assert
        Assert.Equal(OnlineProfileMatch.Mismatch, match);
    }

    /// <summary>
    /// Tests that matching INI CRC confirms compatibility even when the
    /// exeCRC differs (e.g. retail vs community patch retail builds).
    /// </summary>
    [Fact]
    public void Compare_MatchingIniCrcWithDifferingExeCrc_ShouldBeExact()
    {
        // Arrange
        var local = OnlineProfileMatcher.CreateFingerprint("ZeroHour|1.04|client-1", ["mod-a"], "0x11111111", "0x22222222");
        var expected = OnlineProfileMatcher.CreateFingerprint("ZeroHour|1.04|client-1", ["mod-a"], "0x11111111", "0x44444444");

        // Act
        var match = OnlineProfileMatcher.Compare(expected, "ZeroHour|1.04|client-1", local, "ZeroHour|1.04|client-1");

        // Assert
        Assert.Equal(OnlineProfileMatch.Exact, match);
    }

    /// <summary>
    /// Tests that a mixed-version lobby without CRCs on both sides results in mismatch
    /// when content fingerprints do not match identically.
    /// </summary>
    [Fact]
    public void Compare_MixedVersionsWithoutCrcs_ShouldBeMismatch()
    {
        // Arrange
        var local = OnlineProfileMatcher.CreateFingerprint("ZeroHour|1.04|client-1", ["mod-a"]);
        var expected = OnlineProfileMatcher.CreateFingerprint("ZeroHour|1.04|client-1", ["mod-b"], "0x11111111", "0x22222222");

        // Act
        var match = OnlineProfileMatcher.Compare(expected, "ZeroHour|1.04|client-1", local, "ZeroHour|1.04|client-1");

        // Assert
        Assert.Equal(OnlineProfileMatch.Mismatch, match);
    }

    /// <summary>
    /// Tests that roster badges upgrade the same way as the local verdict.
    /// </summary>
    [Fact]
    public void CompareMember_WithEqualIniCrc_ShouldUpgradeToExact()
    {
        // Arrange
        var member = OnlineProfileMatcher.CreateFingerprint("ZeroHour|1.04|client-1", ["mod-a"], "0x11111111", string.Empty);
        var expected = OnlineProfileMatcher.CreateFingerprint("ZeroHour|1.04|client-1", ["mod-b"], "0x11111111", string.Empty);

        // Act
        var match = OnlineProfileMatcher.CompareMember(member, expected, "ZeroHour|1.04|client-1");

        // Assert
        Assert.Equal(OnlineProfileMatch.Exact, match);
    }

    /// <summary>
    /// Tests that the client key extracts from the versioned fingerprint.
    /// </summary>
    [Fact]
    public void TryGetGameClientKey_CompatibilityFingerprint_ShouldExtractClientKey()
    {
        // Arrange
        var fingerprint = OnlineProfileMatcher.CreateFingerprint("ZeroHour|1.04|client-1", ["mod-a"], "0x11111111", "0x22222222");

        // Act
        var parsed = OnlineProfileMatcher.TryGetGameClientKey(fingerprint, out var clientKey);

        // Assert
        Assert.True(parsed);
        Assert.Equal("ZeroHour|1.04|client-1", clientKey);
    }

    /// <summary>
    /// Tests that id-only fingerprints report no compatibility CRCs.
    /// </summary>
    [Fact]
    public void TryGetCompatibilityCrcs_LegacyFingerprint_ShouldReturnFalse()
    {
        // Arrange
        var fingerprint = OnlineProfileMatcher.CreateFingerprint("ZeroHour|1.04|client-1", ["mod-a"]);

        // Act
        var parsed = OnlineProfileMatcher.TryGetCompatibilityCrcs(fingerprint, out var iniCrc, out var exeCrc);

        // Assert
        Assert.False(parsed);
        Assert.Equal(string.Empty, iniCrc);
        Assert.Equal(string.Empty, exeCrc);
    }

    /// <summary>
    /// Tests that profiles with different client keys (e.g. Steam vs EA App) upgrade to Exact
    /// when their engine iniCRC matches.
    /// </summary>
    [Fact]
    public void Compare_DifferentClientsWithMatchingIniCrc_ShouldUpgradeToExact()
    {
        // Arrange
        var steamFingerprint = OnlineProfileMatcher.CreateFingerprint("ZeroHour|1.04|steam-client", ["mod-a"], "0xFEAAE3F3", "0xDA2B4B18");
        var eaFingerprint = OnlineProfileMatcher.CreateFingerprint("ZeroHour|1.04|ea-client", ["mod-b"], "0xFEAAE3F3", "0xDA2B4B18");

        // Act
        var match = OnlineProfileMatcher.Compare(steamFingerprint, "ZeroHour|1.04|steam-client", eaFingerprint, "ZeroHour|1.04|ea-client");

        // Assert
        Assert.Equal(OnlineProfileMatch.Exact, match);
    }

    /// <summary>
    /// Tests that hex casing and 0x prefix differences are normalized when matching CRCs.
    /// </summary>
    [Fact]
    public void Compare_WithDifferentCrcCasingAndPrefixes_ShouldMatchExact()
    {
        // Arrange
        var first = OnlineProfileMatcher.CreateFingerprint("ZeroHour|1.04|client-1", ["mod-a"], "feaaE3f3", "0xDA2B4B18");
        var second = OnlineProfileMatcher.CreateFingerprint("ZeroHour|1.04|client-2", ["mod-b"], "0xFEAAE3F3", "da2b4b18");

        // Act
        var match = OnlineProfileMatcher.Compare(first, "ZeroHour|1.04|client-1", second, "ZeroHour|1.04|client-2");

        // Assert
        Assert.Equal(OnlineProfileMatch.Exact, match);
    }

    /// <summary>
    /// Tests that different game types (e.g. Generals vs Zero Hour) do not match even with identical CRC.
    /// </summary>
    [Fact]
    public void Compare_DifferentGamesWithMatchingIniCrc_ShouldMismatch()
    {
        // Arrange
        var generals = OnlineProfileMatcher.CreateFingerprint("Generals|1.08|client-1", ["mod-a"], "0xFEAAE3F3", "0xDA2B4B18");
        var zh = OnlineProfileMatcher.CreateFingerprint("ZeroHour|1.04|client-2", ["mod-a"], "0xFEAAE3F3", "0xDA2B4B18");

        // Act
        var match = OnlineProfileMatcher.Compare(generals, "Generals|1.08|client-1", zh, "ZeroHour|1.04|client-2");

        // Assert
        Assert.Equal(OnlineProfileMatch.Mismatch, match);
    }

    /// <summary>
    /// Tests that roster member comparison also upgrades to Exact across different clients with matching iniCRC.
    /// </summary>
    [Fact]
    public void CompareMember_DifferentClientsWithMatchingIniCrc_ShouldUpgradeToExact()
    {
        // Arrange
        var member = OnlineProfileMatcher.CreateFingerprint("ZeroHour|1.04|member-client", ["mod-a"], "FEAAE3F3", "DA2B4B18");
        var expected = OnlineProfileMatcher.CreateFingerprint("ZeroHour|1.04|host-client", ["mod-b"], "0xFEAAE3F3", "0xDA2B4B18");

        // Act
        var match = OnlineProfileMatcher.CompareMember(member, expected, "ZeroHour|1.04|host-client");

        // Assert
        Assert.Equal(OnlineProfileMatch.Exact, match);
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
