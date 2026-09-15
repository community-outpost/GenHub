using System.Collections.Generic;
using System.Linq;

namespace GenHub.Core.Models.Content;

/// <summary>
/// Request for content replacement operation.
/// </summary>
public record ContentReplacementRequest
{
    /// <summary>
    /// Gets mapping of old manifest IDs to new manifest IDs.
    /// </summary>
    /// <remarks>
    /// The mapping should typically contain non-empty entries where keys and values are different
    /// (i.e., actually replacing one manifest with another). Self-replacements (key == value)
    /// are allowed but will result in no-ops. Validation fails if the mapping is null or empty.
    /// </remarks>
    public required IReadOnlyDictionary<string, string> ManifestMapping { get; init; }

    /// <summary>
    /// Gets a value indicating whether to remove old manifests after replacement.
    /// </summary>
    public bool RemoveOldManifests { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether to run garbage collection after replacement.
    /// </summary>
    public bool RunGarbageCollection { get; init; } = true;

    /// <summary>
    /// Gets the source that triggered the request.
    /// </summary>
    public string? Source { get; init; }

    /// <summary>
    /// Validates replacement request and returns validation errors if any.
    /// </summary>
    /// <returns>A list of validation error messages, or empty if validation passes.</returns>
    public List<string> Validate()
    {
        var errors = new List<string>();

        if (ManifestMapping == null || ManifestMapping.Count == 0)
        {
            errors.Add("Manifest mapping cannot be empty.");
            return errors;
        }

        if (ManifestMapping.Any(m => string.IsNullOrWhiteSpace(m.Key) || string.IsNullOrWhiteSpace(m.Value)))
        {
            errors.Add("Manifest IDs in mapping cannot be empty or whitespace.");
        }

        // Self-replacements (key == value) are allowed but will result in no-ops.
        // We don't add them to errors since they're not actually invalid - just ineffectual.
        // The operation will still succeed but won't cause any changes.
        return errors;
    }
}
