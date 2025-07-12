using System;
using System.Collections.Generic;
using GenHub.Common.Models;

namespace GenHub.Infrastructure.Extensions;

/// <summary>
/// Extension methods for the <see cref="NavigationTab"/> enum.
/// </summary>
public static class NavigationTabExtensions
{
    /// <summary>
    /// Gets the display name for a <see cref="NavigationTab"/> value.
    /// </summary>
    /// <param name="tab">The navigation tab.</param>
    /// <returns>The display name for the tab.</returns>
    public static string ToDisplayString(this NavigationTab tab) => tab switch
    {
        NavigationTab.GameProfiles => "Game Profiles",
        NavigationTab.Downloads => "Downloads",
        NavigationTab.Settings => "Settings",
        _ => tab.ToString(),
    };
}
