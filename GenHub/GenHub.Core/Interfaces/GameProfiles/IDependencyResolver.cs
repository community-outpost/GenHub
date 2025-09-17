using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Models.Results;

namespace GenHub.Core.Interfaces.GameProfiles;

/// <summary>
/// Interface for resolving content dependencies.
/// </summary>
public interface IDependencyResolver
{
    /// <summary>
    /// Resolves dependencies recursively for the given content IDs.
    /// </summary>
    /// <param name="contentIds">The initial content IDs.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A set of all resolved content IDs including dependencies.</returns>
    Task<HashSet<string>> ResolveDependenciesAsync(IEnumerable<string> contentIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves dependencies recursively for the given content IDs, returning detailed results.
    /// </summary>
    /// <param name="contentIds">The initial content IDs.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A <see cref="DependencyResolutionResult"/> with resolved content, manifests, and missing IDs.</returns>
    Task<DependencyResolutionResult> ResolveDependenciesWithManifestsAsync(IEnumerable<string> contentIds, CancellationToken cancellationToken = default);
}
