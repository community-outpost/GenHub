using GenHub.Core.Models.Content;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;

namespace GenHub.Core.Interfaces.Content;

/// <summary>
/// Defines a contract for a service that can resolve a <see cref="ContentSearchResult"/>
/// object into a full <see cref="ContentManifest"/>.
/// </summary>
public interface IContentResolver
{
    /// <summary>
    /// Gets the unique identifier for this resolver, which matches the ResolverId in a <see cref="ContentSearchResult"/>.
    /// </summary>
    string ResolverId { get; }

    /// <summary>
    /// Resolves a discovered content item into a full ContentManifest.
    /// </summary>
    /// <param name="discoveredItem">The discovered content to resolve.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A <see cref="ContentManifest"/> wrapped in <see cref="ContentOperationResult{ContentManifest}"/>.</returns>
    Task<ContentOperationResult<ContentManifest>> ResolveAsync(ContentSearchResult discoveredItem, CancellationToken cancellationToken = default);
}
