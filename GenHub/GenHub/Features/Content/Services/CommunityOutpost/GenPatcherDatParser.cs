using System.Collections.Generic;
using System.Linq;
using GenHub.Core.Models.CommunityOutpost;

namespace GenHub.Features.Content.Services.CommunityOutpost;

/// <summary>
/// Provides parsing utilities for GenPatcher .dat files.
/// </summary>
public static class GenPatcherDatParser
{
    /// <summary>
    /// Gets ordered download URLs from a GenPatcher content item.
    /// </summary>
    /// <param name="item">The GenPatcher content item.</param>
    /// <returns>A list of download URLs in order.</returns>
    public static List<string> GetOrderedDownloadUrls(GenPatcherContentItem item)
    {
        if (item?.Mirrors == null)
        {
            return [];
        }

        var urls = item.Mirrors.Select(m => m.Url).Where(u => !string.IsNullOrEmpty(u)).ToList();

        return [.. urls];
    }
}
