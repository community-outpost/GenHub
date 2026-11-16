using GenHub.Core.Models.Enums;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GenHub.Core.Helpers;

/// <summary>
/// Applies the shared game-profile sort order used by the launcher tab and every profile
/// picker. Callers project their freshest display values so all profile lists stay consistent
/// when sort modes are added or refined.
/// </summary>
public static class ProfileSortHelper
{
    /// <summary>
    /// Gets the display values a profile list is sorted by.
    /// </summary>
    /// <param name="Name">The profile name.</param>
    /// <param name="LastPlayedAt">When the profile was last played. The default value (which equals <see cref="DateTime.MinValue"/>) means never played, so callers projecting nullable timestamps must normalize null to default.</param>
    /// <param name="CreatedAt">When the profile was created.</param>
    /// <param name="DisplayOrder">The custom display order for free sort mode.</param>
    public sealed record ProfileSortKeys(string Name, DateTime LastPlayedAt, DateTime CreatedAt, int DisplayOrder);

    /// <summary>
    /// Sorts items using the specified profile sort mode.
    /// </summary>
    /// <typeparam name="T">The item type carrying sortable profile values.</typeparam>
    /// <param name="items">The items to sort.</param>
    /// <param name="keysSelector">Selects the sort keys each item represents.</param>
    /// <param name="sortMode">The sort mode to apply.</param>
    /// <returns>The items in sorted order.</returns>
    public static List<T> SortByProfile<T>(IEnumerable<T> items, Func<T, ProfileSortKeys> keysSelector, ProfileSortMode sortMode)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(keysSelector);

        var keyedItems = items.Select(item => (Item: item, Keys: keysSelector(item))).ToList();

        return sortMode switch
        {
            ProfileSortMode.LastPlayed => keyedItems
                .OrderByDescending(keyed => HasPlayed(keyed.Keys.LastPlayedAt))
                .ThenByDescending(keyed => keyed.Keys.LastPlayedAt)
                .ThenByDescending(keyed => keyed.Keys.CreatedAt)
                .Select(keyed => keyed.Item)
                .ToList(),
            ProfileSortMode.DateCreated => keyedItems
                .OrderByDescending(keyed => keyed.Keys.CreatedAt)
                .Select(keyed => keyed.Item)
                .ToList(),
            ProfileSortMode.Alphabetical => keyedItems
                .OrderBy(keyed => keyed.Keys.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(keyed => keyed.Item)
                .ToList(),
            ProfileSortMode.AlphabeticalDesc => keyedItems
                .OrderByDescending(keyed => keyed.Keys.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(keyed => keyed.Item)
                .ToList(),
            _ => keyedItems
                .OrderBy(keyed => keyed.Keys.DisplayOrder)
                .ThenByDescending(keyed => keyed.Keys.CreatedAt)
                .Select(keyed => keyed.Item)
                .ToList(),
        };
    }

    /// <summary>
    /// Normalizes a persisted sort mode, falling back to last-played order for out-of-range values.
    /// </summary>
    /// <param name="sortMode">The persisted sort mode.</param>
    /// <returns>The sort mode when defined; otherwise <see cref="ProfileSortMode.LastPlayed"/>.</returns>
    public static ProfileSortMode NormalizeSortMode(ProfileSortMode sortMode)
    {
        return Enum.IsDefined(sortMode) ? sortMode : ProfileSortMode.LastPlayed;
    }

    private static bool HasPlayed(DateTime lastPlayedAt)
    {
        return lastPlayedAt != default;
    }
}
