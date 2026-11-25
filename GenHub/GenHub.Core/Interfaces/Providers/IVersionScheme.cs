using GenHub.Core.Models.Content;

namespace GenHub.Core.Interfaces.Providers;

/// <summary>
/// Parses and orders version strings for one versioning convention.
/// </summary>
/// <remarks>
/// A provider definition names its scheme by <see cref="SchemeId"/>, so adding a publisher
/// with a new version format means shipping a scheme, not editing a comparison routine.
/// Implementing <see cref="IComparer{T}"/> lets a scheme be handed straight to LINQ ordering.
/// </remarks>
public interface IVersionScheme : IComparer<string?>
{
    /// <summary>
    /// Gets the identifier that provider definitions use to select this scheme.
    /// </summary>
    string SchemeId { get; }

    /// <summary>
    /// Parses a version string into its ordered components.
    /// </summary>
    /// <param name="version">The raw version string.</param>
    /// <param name="result">The parsed version, or empty when parsing fails.</param>
    /// <returns><c>true</c> if the version matched this scheme.</returns>
    bool TryParse(string? version, out ContentVersion result);
}
