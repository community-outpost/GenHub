using System;
using System.Linq;
using GenHub.Core.Models.CommunityOutpost;
using GenHub.Core.Models.Providers;

namespace GenHub.Features.Content.Services.CommunityOutpost;

/// <summary>
/// Provides extension methods for working with provider endpoints.
/// </summary>
public static class ProviderEndpointsExtensions
{
    /// <summary>
    /// Gets the preferred download URL from provider endpoints based on mirror priority.
    /// </summary>
    /// <param name="endpoints">The provider endpoints.</param>
    /// <param name="item">The GenPatcher content item.</param>
    /// <returns>The preferred download URL, or null if no mirrors are available.</returns>
    public static string? GetPreferredDownloadUrl(this ProviderEndpoints endpoints, GenPatcherContentItem item)
    {
        if (item.Mirrors.Count == 0) return null;

        // Check mirror preference from endpoints mirrors priority
        // NOTE: ProviderDefinition has MirrorPreference list, but ProviderEndpoints has Mirrors (list of EndpointMirror).
        // This extension simplifies access.
        if (endpoints.Mirrors != null && endpoints.Mirrors.Count > 0)
        {
            var orderedMirrors = endpoints.Mirrors.OrderBy(m => m.Priority).ToList();
            foreach (var mirrorEndpoint in orderedMirrors)
            {
                var mirror = item.Mirrors.FirstOrDefault(m =>
                    m.Name.Contains(mirrorEndpoint.Name, StringComparison.OrdinalIgnoreCase));

                if (mirror != null)
                {
                    return mirror.Url;
                }
            }
        }

        // Fallback to first
        return item.Mirrors.First().Url;
    }
}
