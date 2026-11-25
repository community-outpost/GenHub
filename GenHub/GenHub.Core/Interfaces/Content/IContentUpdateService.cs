using GenHub.Core.Models.Results.Content;

namespace GenHub.Core.Interfaces.Content;

/// <summary>
/// Defines the contract for content update checking services.
/// </summary>
public interface IContentUpdateService
{
    /// <summary>
    /// Checks for available content updates.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result containing update availability information.</returns>
    Task<ContentUpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default);
}
