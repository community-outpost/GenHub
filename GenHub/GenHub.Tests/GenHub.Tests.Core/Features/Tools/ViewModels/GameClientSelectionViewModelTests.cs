using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Manifest;
using GenHub.Features.Tools.ReplayManager.ViewModels;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.ViewModels;

/// <summary>
/// Unit tests for <see cref="GameClientSelectionViewModel"/>.
/// </summary>
public sealed class GameClientSelectionViewModelTests
{
    /// <summary>
    /// Verifies that ManifestMatchesGame matches manifests with explicit TargetGame.
    /// </summary>
    /// <param name="manifestGame">The GameType assigned to the manifest.</param>
    /// <param name="targetGame">The target GameType being filtered for.</param>
    /// <param name="expected">The expected match result.</param>
    [Theory]
    [InlineData(GameType.ZeroHour, GameType.ZeroHour, true)]
    [InlineData(GameType.Generals, GameType.Generals, true)]
    [InlineData(GameType.ZeroHour, GameType.Generals, false)]
    [InlineData(GameType.Generals, GameType.ZeroHour, false)]
    public void ManifestMatchesGame_ExplicitTargetGame_ReturnsExpected(GameType manifestGame, GameType targetGame, bool expected)
    {
        var manifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.test.gameclient.zh"),
            TargetGame = manifestGame,
        };

        var result = GameClientSelectionViewModel.ManifestMatchesGame(manifest, targetGame);

        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Verifies that ManifestMatchesGame matches unknown target game manifests by ID token prefix, suffix, or exact match.
    /// </summary>
    /// <param name="manifestId">The manifest ID string to evaluate.</param>
    /// <param name="targetGame">The target GameType being filtered for.</param>
    /// <param name="expected">The expected match result.</param>
    [Theory]
    [InlineData("1.0.generalsonline.gameclient.zerohour", GameType.ZeroHour, true)]
    [InlineData("1.0.generalsonline.gameclient.zh", GameType.ZeroHour, true)]
    [InlineData("1.0.generalsonline.gameclient.zerohour-generalsonline-60hz", GameType.ZeroHour, true)]
    [InlineData("1.0.generalsonline.gameclient.zh-generalsonline-60hz", GameType.ZeroHour, true)]
    [InlineData("1.0.community.gameclient.custom-zerohour", GameType.ZeroHour, true)]
    [InlineData("1.0.community.gameclient.custom-zh", GameType.ZeroHour, true)]
    [InlineData("1.0.generalsonline.gameclient.generals", GameType.Generals, true)]
    [InlineData("1.0.generalsonline.gameclient.generals-generalsonline-60hz", GameType.Generals, true)]
    [InlineData("1.0.community.gameclient.custom-generals", GameType.Generals, true)]
    [InlineData("1.0.generalsonline.gameclient.generals-generalsonline-60hz", GameType.ZeroHour, false)]
    [InlineData("1.0.generalsonline.gameclient.zerohour-generalsonline-60hz", GameType.Generals, false)]
    [InlineData("1.0.other.gameclient.unknown", GameType.ZeroHour, false)]
    [InlineData("1.0.other.gameclient.unknown", GameType.Generals, false)]
    public void ManifestMatchesGame_UnknownTargetGame_MatchesTokensCorrectly(string manifestId, GameType targetGame, bool expected)
    {
        var manifest = new ContentManifest
        {
            Id = ManifestId.Create(manifestId),
            TargetGame = GameType.Unknown,
        };

        var result = GameClientSelectionViewModel.ManifestMatchesGame(manifest, targetGame);

        Assert.Equal(expected, result);
    }
}
