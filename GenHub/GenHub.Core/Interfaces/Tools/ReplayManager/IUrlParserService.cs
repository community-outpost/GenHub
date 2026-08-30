using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Models.Tools.ReplayManager;

namespace GenHub.Core.Interfaces.Tools.ReplayManager;

/// <summary>
/// Parses and identifies replay source URLs.
/// </summary>
public interface IUrlParserService
{
    /// <summary>
    /// Identifies the source of a replay URL.
    /// </summary>
    /// <param name="url">The URL to identify.</param>
    /// <returns>The identified source.</returns>
    ReplaySource IdentifySource(string url);

    /// <summary>
    /// Validates if the URL is a supported replay source.
    /// </summary>
    /// <param name="url">The URL to validate.</param>
    /// <returns>True if the URL is supported.</returns>
    bool IsValidReplayUrl(string url);

    /// <summary>
    /// Extracts the direct download URL from a source-specific URL.
    /// </summary>
    /// <param name="url">The source URL.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The direct download URL if successful, otherwise null.</returns>
    Task<string?> GetDirectDownloadUrlAsync(string url, CancellationToken ct = default);

    /// <summary>
    /// Extracts all direct download URLs from a source-specific URL (e.g., all player replays from a match page).
    /// </summary>
    /// <param name="url">The source URL.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A list of direct download URLs extracted from the source.</returns>
    Task<IReadOnlyList<string>> GetDirectDownloadUrlsAsync(string url, CancellationToken ct = default);
}
