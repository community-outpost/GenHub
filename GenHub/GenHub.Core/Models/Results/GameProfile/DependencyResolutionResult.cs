using System.Collections.Generic;
using GenHub.Core.Models.Manifest;

namespace GenHub.Core.Models.Results;

/// <summary>
/// Represents the result of a dependency resolution operation.
/// </summary>
public class DependencyResolutionResult : ResultBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DependencyResolutionResult"/> class.
    /// </summary>
    /// <param name="success">Whether the resolution succeeded.</param>
    /// <param name="resolvedContentIds">The resolved content IDs.</param>
    /// <param name="resolvedManifests">The resolved manifests.</param>
    /// <param name="missingContentIds">The missing content IDs.</param>
    /// <param name="warnings">The warnings, if any.</param>
    /// <param name="errors">The errors, if any.</param>
    /// <param name="elapsed">The elapsed time.</param>
    protected DependencyResolutionResult(
        bool success,
        IReadOnlyList<string> resolvedContentIds,
        IReadOnlyList<ContentManifest> resolvedManifests,
        IReadOnlyList<string> missingContentIds,
        IReadOnlyList<string>? warnings = null,
        IEnumerable<string>? errors = null,
        TimeSpan elapsed = default)
        : base(success, errors, elapsed)
    {
        ResolvedContentIds = resolvedContentIds;
        ResolvedManifests = resolvedManifests;
        MissingContentIds = missingContentIds;
        Warnings = warnings ?? Array.Empty<string>();
    }

    /// <summary>
    /// Gets the resolved content IDs.
    /// </summary>
    public IReadOnlyList<string> ResolvedContentIds { get; }

    /// <summary>
    /// Gets the resolved manifests.
    /// </summary>
    public IReadOnlyList<ContentManifest> ResolvedManifests { get; }

    /// <summary>
    /// Gets the missing content IDs.
    /// </summary>
    public IReadOnlyList<string> MissingContentIds { get; }

    /// <summary>
    /// Gets the warnings generated during resolution.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; }

    /// <summary>
    /// Creates a successful dependency resolution result.
    /// </summary>
    /// <param name="resolvedContentIds">The resolved content IDs.</param>
    /// <param name="resolvedManifests">The resolved manifests.</param>
    /// <param name="missingContentIds">The missing content IDs.</param>
    /// <param name="elapsed">The elapsed time.</param>
    /// <returns>A successful <see cref="DependencyResolutionResult"/>.</returns>
    public static DependencyResolutionResult CreateSuccess(
        IReadOnlyList<string> resolvedContentIds,
        IReadOnlyList<ContentManifest> resolvedManifests,
        IReadOnlyList<string> missingContentIds,
        TimeSpan elapsed = default)
    {
        return new DependencyResolutionResult(true, resolvedContentIds, resolvedManifests, missingContentIds, null, null, elapsed);
    }

    /// <summary>
    /// Creates a successful dependency resolution result with warnings.
    /// </summary>
    /// <param name="resolvedContentIds">The resolved content IDs.</param>
    /// <param name="resolvedManifests">The resolved manifests.</param>
    /// <param name="missingContentIds">The missing content IDs.</param>
    /// <param name="warnings">The warnings generated during resolution.</param>
    /// <param name="elapsed">The elapsed time.</param>
    /// <returns>A successful <see cref="DependencyResolutionResult"/> with warnings.</returns>
    public static DependencyResolutionResult CreateSuccessWithWarnings(
        IReadOnlyList<string> resolvedContentIds,
        IReadOnlyList<ContentManifest> resolvedManifests,
        IReadOnlyList<string> missingContentIds,
        IReadOnlyList<string> warnings,
        TimeSpan elapsed = default)
    {
        return new DependencyResolutionResult(true, resolvedContentIds, resolvedManifests, missingContentIds, warnings, null, elapsed);
    }

    /// <summary>
    /// Creates a failed dependency resolution result with a single error message.
    /// </summary>
    /// <param name="error">The error message.</param>
    /// <param name="elapsed">The elapsed time.</param>
    /// <returns>A failed <see cref="DependencyResolutionResult"/>.</returns>
    public static DependencyResolutionResult CreateFailure(string error, TimeSpan elapsed = default)
    {
        return new DependencyResolutionResult(false, Array.Empty<string>(), Array.Empty<ContentManifest>(), Array.Empty<string>(), null, new[] { error }, elapsed);
    }

    /// <summary>
    /// Creates a failed dependency resolution result with multiple error messages.
    /// </summary>
    /// <param name="errors">The error messages.</param>
    /// <param name="elapsed">The elapsed time.</param>
    /// <returns>A failed <see cref="DependencyResolutionResult"/>.</returns>
    public static DependencyResolutionResult CreateFailure(IEnumerable<string> errors, TimeSpan elapsed = default)
    {
        ArgumentNullException.ThrowIfNull(errors, nameof(errors));
        if (!errors.Any())
            throw new ArgumentException("Errors collection cannot be empty.", nameof(errors));

        return new DependencyResolutionResult(false, Array.Empty<string>(), Array.Empty<ContentManifest>(), Array.Empty<string>(), null, errors, elapsed);
    }
}
