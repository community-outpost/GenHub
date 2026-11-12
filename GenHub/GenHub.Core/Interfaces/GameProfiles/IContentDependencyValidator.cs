using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Manifest;

namespace GenHub.Core.Interfaces.GameProfiles;

/// <summary>
/// Finds the required dependencies missing from a set of enabled profile content.
/// Every path that adds content to a profile, saves a profile, or launches one uses the same rules.
/// </summary>
public interface IContentDependencyValidator
{
    /// <summary>
    /// Finds required dependencies that no enabled content satisfies.
    /// </summary>
    /// <param name="enabledContentIds">The manifest IDs that the profile would enable.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>One gap per missing item. The list is empty when nothing is missing.</returns>
    Task<IReadOnlyList<ContentDependencyGap>> FindMissingDependenciesAsync(
        IEnumerable<string> enabledContentIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds required dependencies that no manifest in <paramref name="enabledManifests"/> satisfies.
    /// </summary>
    /// <param name="enabledManifests">The manifests that the profile would enable.</param>
    /// <returns>One gap per missing item. The list is empty when nothing is missing.</returns>
    IReadOnlyList<ContentDependencyGap> FindMissingDependencies(IReadOnlyCollection<ContentManifest> enabledManifests);
}
