using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Models.GameClients;
using Xunit;

namespace GenHub.Tests.Core.Helpers;

/// <summary>
/// Unit tests for <see cref="GameClientCapabilitiesHelper"/>.
/// </summary>
public class GameClientCapabilitiesHelperTests
{
    /// <summary>
    /// Verifies that IsSuperHackersPublisher identifies SuperHackers publisher variants.
    /// </summary>
    /// <param name="publisher">The publisher identifier.</param>
    /// <param name="expected">Expected result.</param>
    [Theory]
    [InlineData("thesuperhackers", true)]
    [InlineData("superhackers", true)]
    [InlineData("TheSuperHackers", true)]
    [InlineData("SUPERHACKERS", true)]
    [InlineData("ea", false)]
    [InlineData("steam", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsSuperHackersPublisher_IdentifiesCorrectly(string? publisher, bool expected)
    {
        Assert.Equal(expected, GameClientCapabilitiesHelper.IsSuperHackersPublisher(publisher));
    }

    /// <summary>
    /// Verifies that InferCapabilities maps recovery keywords to AllRecoveryFeatures.
    /// </summary>
    /// <param name="publisher">The publisher name or identifier.</param>
    /// <param name="id">The client identifier.</param>
    /// <param name="name">The client display name.</param>
    [Theory]
    [InlineData(null, null, "Zero Hour Recovery Client")]
    [InlineData(null, "client-recovery", null)]
    public void InferCapabilities_WhenRecoveryKeyword_ReturnsAllRecoveryFeatures(string? publisher, string? id, string? name)
    {
        var capabilities = GameClientCapabilitiesHelper.InferCapabilities(publisher, id, name);
        Assert.Equal(GameClientCapabilities.AllRecoveryFeatures, capabilities);
    }

    /// <summary>
    /// Verifies that InferCapabilities does not grant recovery features to SuperHackers clients without recovery keywords.
    /// </summary>
    /// <param name="publisher">The publisher name or identifier.</param>
    /// <param name="id">The client identifier.</param>
    /// <param name="name">The client display name.</param>
    [Theory]
    [InlineData("thesuperhackers", null, null)]
    [InlineData(null, "1.0.local.gameclient.superhackers", null)]
    [InlineData(null, null, "Zero Hour SuperHackers Client")]
    public void InferCapabilities_WhenSuperHackersWithoutRecoveryKeyword_ReturnsNone(string? publisher, string? id, string? name)
    {
        var capabilities = GameClientCapabilitiesHelper.InferCapabilities(publisher, id, name);
        Assert.Equal(GameClientCapabilities.None, capabilities);
    }

    /// <summary>
    /// Verifies that InferCapabilities maps checkpoint keyword to CheckpointSaves.
    /// </summary>
    [Fact]
    public void InferCapabilities_WhenCheckpointKeywordOnly_ReturnsCheckpointSaves()
    {
        var capabilities = GameClientCapabilitiesHelper.InferCapabilities("custom", "client-checkpoint-only", "Custom Client");
        Assert.Equal(GameClientCapabilities.CheckpointSaves, capabilities);
    }

    /// <summary>
    /// Verifies that InferCapabilities maps takeover keyword to PlayerTakeover.
    /// </summary>
    [Fact]
    public void InferCapabilities_WhenTakeoverKeywordOnly_ReturnsPlayerTakeover()
    {
        var capabilities = GameClientCapabilitiesHelper.InferCapabilities("custom", "client-takeover-only", "Custom Client");
        Assert.Equal(GameClientCapabilities.PlayerTakeover, capabilities);
    }

    /// <summary>
    /// Verifies that InferCapabilities combines checkpoint and takeover keywords.
    /// </summary>
    [Fact]
    public void InferCapabilities_WhenBothCheckpointAndTakeoverKeywords_CombinesFlags()
    {
        var capabilities = GameClientCapabilitiesHelper.InferCapabilities("custom", "client-checkpoint-takeover", "Custom Client");
        Assert.Equal(GameClientCapabilities.CheckpointSaves | GameClientCapabilities.PlayerTakeover, capabilities);
        Assert.False((capabilities & GameClientCapabilities.AllRecoveryFeatures) == GameClientCapabilities.AllRecoveryFeatures);
    }

    /// <summary>
    /// Verifies that InferCapabilities returns None for unrelated clients.
    /// </summary>
    [Fact]
    public void InferCapabilities_WhenUnrelatedClient_ReturnsNone()
    {
        var capabilities = GameClientCapabilitiesHelper.InferCapabilities("ea", "1.104.retail.gameclient.zerohour", "Zero Hour 1.04");
        Assert.Equal(GameClientCapabilities.None, capabilities);
    }
}
