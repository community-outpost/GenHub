using System;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Interfaces.Common;

/// <summary>
/// Validates that download targets are public HTTP(S) destinations.
/// </summary>
public interface IDownloadUrlValidator
{
    /// <summary>
    /// Determines whether the URI is safe to connect to: an absolute HTTP or HTTPS
    /// URI whose host resolves only to public internet addresses.
    /// </summary>
    /// <param name="uri">The download target to validate.</param>
    /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
    /// <returns>
    /// A task representing the asynchronous operation, with <see langword="true"/> when the
    /// target is safe; otherwise, <see langword="false"/>. Resolution failures fail closed.
    /// </returns>
    Task<bool> IsSafeAsync(Uri uri, CancellationToken cancellationToken);
}
