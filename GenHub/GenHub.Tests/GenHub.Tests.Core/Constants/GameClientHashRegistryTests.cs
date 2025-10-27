using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Features.GameClients;

namespace GenHub.Tests.Core.Constants;

/// <summary>
/// Unit tests for <see cref="GameClientHashRegistry"/>.
/// </summary>
public class GameClientHashRegistryTests
{
    private readonly GameClientHashRegistry _registry = new();

    /// <summary>
    /// Verifies that known hash constants are properly registered.
    /// </summary>
    [Fact]
    public void KnownHashes_AreRegistered()
    {
        // Test Generals 1.08 hash
        var generalsInfo = _registry.TryGetInfo("1c96366ff6a99f40863f6bbcfa8bf7622e8df1f80a474201e0e95e37c6416255", out var info);
        Assert.True(generalsInfo);
        Assert.NotNull(info);
        Assert.Equal(GameType.Generals, info.Value.GameType);
        Assert.Equal("1.08", info.Value.Version);
        Assert.Equal("EA/Steam", info.Value.Publisher);
        Assert.True(info.Value.IsOfficial);

        // Test Zero Hour 1.04 hash
        var zh104Info = _registry.TryGetInfo("f37a4929f8d697104e99c2bcf46f8d833122c943afcd87fd077df641d344495b", out var zh104);
        Assert.True(zh104Info);
        Assert.NotNull(zh104);
        Assert.Equal(GameType.ZeroHour, zh104.Value.GameType);
        Assert.Equal("1.04", zh104.Value.Version);
        Assert.Equal("EA/Steam", zh104.Value.Publisher);
        Assert.True(zh104.Value.IsOfficial);

        // Test Zero Hour 1.05 hash
        var zh105Info = _registry.TryGetInfo("420fba1dbdc4c14e2418c2b0d3010b9fac6f314eafa1f3a101805b8d98883ea1", out var zh105);
        Assert.True(zh105Info);
        Assert.NotNull(zh105);
        Assert.Equal(GameType.ZeroHour, zh105.Value.GameType);
        Assert.Equal("1.05", zh105.Value.Version);
        Assert.Equal("Community", zh105.Value.Publisher);
        Assert.False(zh105.Value.IsOfficial);
    }

    /// <summary>
    /// Verifies that GetVersionFromHash returns correct versions.
    /// </summary>
    [Fact]
    public void GetVersionFromHash_ReturnsCorrectVersions()
    {
        // Test Generals versions
        var generalsVersion = _registry.GetVersionFromHash("1c96366ff6a99f40863f6bbcfa8bf7622e8df1f80a474201e0e95e37c6416255", GameType.Generals);
        Assert.Equal("1.08", generalsVersion);

        // Test Zero Hour versions
        var zh104Version = _registry.GetVersionFromHash("f37a4929f8d697104e99c2bcf46f8d833122c943afcd87fd077df641d344495b", GameType.ZeroHour);
        Assert.Equal("1.04", zh104Version);

        var zh105Version = _registry.GetVersionFromHash("420fba1dbdc4c14e2418c2b0d3010b9fac6f314eafa1f3a101805b8d98883ea1", GameType.ZeroHour);
        Assert.Equal("1.05", zh105Version);

        // Test unknown hash
        var unknownVersion = _registry.GetVersionFromHash("unknownhash", GameType.Generals);
        Assert.Equal("Unknown", unknownVersion);
    }

    /// <summary>
    /// Verifies that possible executable names are configured.
    /// </summary>
    [Fact]
    public void PossibleExecutableNames_AreConfigured()
    {
        var names = _registry.PossibleExecutableNames;
        Assert.NotNull(names);
        Assert.NotEmpty(names);
        Assert.Contains("generals.exe", names);
        Assert.Contains("generalsv.exe", names);
        Assert.Contains("generalszh.exe", names);
        Assert.Contains("generalsonlinezh_30.exe", names);
        Assert.Contains("generalsonlinezh_60.exe", names);
    }

    /// <summary>
    /// Verifies that GameClientInfo.Validate() works correctly.
    /// </summary>
    [Fact]
    public void GameClientInfo_Validate_WorksCorrectly()
    {
        // Valid info
        var validInfo = new GameClientInfo(GameType.Generals, "1.08", "EA", "Test", true);
        Assert.True(validInfo.Validate());

        // Invalid - empty version
        var invalidVersion = new GameClientInfo(GameType.Generals, string.Empty, "EA", "Test", true);
        Assert.False(invalidVersion.Validate());

        // Invalid - unknown game type
        var invalidGameType = new GameClientInfo(GameType.Unknown, "1.08", "EA", "Test", true);
        Assert.False(invalidGameType.Validate());

        // Invalid - empty publisher
        var invalidPublisher = new GameClientInfo(GameType.Generals, "1.08", string.Empty, "Test", true);
        Assert.False(invalidPublisher.Validate());
    }
}
