using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Interfaces.Content;

/// <summary>
/// Interface for content update checking services.
/// </summary>
public interface IContentUpdateService
{
    /// <summary>
    /// Checks for available content updates.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A tuple containing update availability, latest version, and current version.</returns>
    Task<(bool UpdateAvailable, string? LatestVersion, string? CurrentVersion)> CheckForUpdatesAsync(CancellationToken cancellationToken = default);
}
