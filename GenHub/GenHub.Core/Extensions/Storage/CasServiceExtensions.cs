using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Models.Enums;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Extensions.Storage;

/// <summary>
/// Extension methods for <see cref="ICasService"/>.
/// </summary>
public static class CasServiceExtensions
{
    /// <summary>
    /// Checks whether content with the given hash exists in CAS, trying the content-type pool
    /// first and falling back to a pool-agnostic lookup.
    /// </summary>
    /// <param name="casService">The CAS service.</param>
    /// <param name="hash">The content hash to check.</param>
    /// <param name="contentType">The content type for pool routing.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when the object exists in either lookup; otherwise, false.</returns>
    public static async Task<bool> ExistsInAnyPoolAsync(
        this ICasService casService,
        string hash,
        ContentType contentType,
        CancellationToken cancellationToken = default)
    {
        var existsResult = await casService.ExistsAsync(hash, contentType, cancellationToken).ConfigureAwait(false);
        if (existsResult is { Success: true, Data: true })
        {
            return true;
        }

        var fallbackResult = await casService.ExistsAsync(hash, cancellationToken).ConfigureAwait(false);
        return fallbackResult is { Success: true, Data: true };
    }
}
