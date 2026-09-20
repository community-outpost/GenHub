using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GitHub;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GenHub.Features.Content.Services.Helpers;

/// <summary>
/// Matches SuperHackers release assets to game types by filename convention.
/// Zero Hour archives carry a "zh" marker (generalszh, zerohour, zero-hour, _zh) while
/// Generals archives carry "generals" without any Zero Hour marker.
/// </summary>
public static class SuperHackersAssetMatcher
{
    /// <summary>
    /// Finds the release asset for the specified game type.
    /// </summary>
    /// <param name="assets">The release assets to search.</param>
    /// <param name="gameType">The game type to match.</param>
    /// <returns>The matching asset, or null when no asset matches.</returns>
    public static GitHubReleaseAsset? FindAsset(IEnumerable<GitHubReleaseAsset>? assets, GameType gameType)
    {
        if (assets == null)
        {
            return null;
        }

        var candidates = assets
            .Where(asset => !string.IsNullOrWhiteSpace(asset.Name))
            .ToList();

        return gameType switch
        {
            GameType.ZeroHour => candidates.FirstOrDefault(IsZeroHourAsset),
            GameType.Generals => candidates.FirstOrDefault(IsGeneralsAsset),
            _ => null,
        };
    }

    /// <summary>
    /// Finds the release asset filename for the specified game type.
    /// </summary>
    /// <param name="assets">The release assets to search.</param>
    /// <param name="gameType">The game type to match.</param>
    /// <returns>The matching asset filename, or null when no asset matches.</returns>
    public static string? FindAssetName(IEnumerable<GitHubReleaseAsset>? assets, GameType gameType)
    {
        return FindAsset(assets, gameType)?.Name;
    }

    private static bool IsZeroHourAsset(GitHubReleaseAsset asset)
    {
        return asset.Name.Contains(SuperHackersConstants.GeneralsZhAssetMarker, StringComparison.OrdinalIgnoreCase)
            || asset.Name.Contains(SuperHackersConstants.ZeroHourHyphenAssetMarker, StringComparison.OrdinalIgnoreCase)
            || asset.Name.Contains(SuperHackersConstants.ZeroHourAssetMarker, StringComparison.OrdinalIgnoreCase)
            || asset.Name.Contains(SuperHackersConstants.ZeroHourShortAssetMarker, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsGeneralsAsset(GitHubReleaseAsset asset)
    {
        return asset.Name.Contains(SuperHackersConstants.GeneralsAssetMarker, StringComparison.OrdinalIgnoreCase)
            && !IsZeroHourAsset(asset);
    }
}
