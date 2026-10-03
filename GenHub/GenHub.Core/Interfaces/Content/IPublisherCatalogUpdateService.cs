using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Interfaces.Content;

/// <summary>
/// Service that monitors subscribed publisher catalogs for updates to downloaded content
/// and alerts the user with persistent notifications until dismissed.
/// </summary>
public interface IPublisherCatalogUpdateService : IContentUpdateService
{
    /// <summary>
    /// Marks an update as dismissed by the user for a specific content version.
    /// </summary>
    /// <param name="publisherId">The publisher ID.</param>
    /// <param name="contentId">The content ID.</param>
    /// <param name="version">The version string.</param>
    void DismissUpdate(string publisherId, string contentId, string version);

    /// <summary>
    /// Checks if an update was dismissed by the user.
    /// </summary>
    /// <param name="publisherId">The publisher ID.</param>
    /// <param name="contentId">The content ID.</param>
    /// <param name="version">The version string.</param>
    /// <returns>True if dismissed, false otherwise.</returns>
    bool IsUpdateDismissed(string publisherId, string contentId, string version);

    /// <summary>
    /// Clears all dismissed update records.
    /// </summary>
    void ClearDismissedUpdates();
}
