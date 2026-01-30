using System;
using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;

namespace GenHub.Core.Extensions;

/// <summary>
/// Extension methods for CNCLabs-specific enums.
/// </summary>
public static class CNCLabsExtensions
{
    /// <summary>
    /// Gets the relative page path for a CNCLabs page type.
    /// </summary>
    /// <param name="pageType">The page type.</param>
    /// <returns>The relative URL path.</returns>
    public static string GetPath(this CNCLabsPageType pageType)
    {
        return pageType switch
        {
            CNCLabsPageType.GeneralsMaps => CNCLabsConstants.MapsPagePath,
            CNCLabsPageType.GeneralsMissions => CNCLabsConstants.MissionsPagePath,
            CNCLabsPageType.ZeroHourMaps => CNCLabsConstants.ZeroHourMapsPagePath,
            CNCLabsPageType.ZeroHourMissions => CNCLabsConstants.ZeroHourMissionsPagePath,
            _ => throw new ArgumentOutOfRangeException(nameof(pageType), pageType, "Unhandled CNCLabsPageType"),
        };
    }
}
