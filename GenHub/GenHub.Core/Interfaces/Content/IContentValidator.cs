using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;

namespace GenHub.Core.Interfaces.Content;

/// <summary>
/// Interface for validating content manifests and their integrity.
/// This is the core validation service used by the content system.
/// </summary>
public interface IContentValidator
{
    /// <summary>
    /// Validates a manifest's structure and metadata.
    /// </summary>
    /// <param name="manifest">The manifest to validate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Validation result containing any issues found.</returns>
    Task<ValidationResult> ValidateManifestAsync(ContentManifest manifest, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates the integrity of content files against their manifest.
    /// </summary>
    /// <param name="contentPath">Path to the content directory.</param>
    /// <param name="manifest">The manifest describing expected files.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Validation result containing any integrity issues.</returns>
    Task<ValidationResult> ValidateContentIntegrityAsync(string contentPath, ContentManifest manifest, CancellationToken cancellationToken = default);

    /// <summary>
    /// Detects extraneous files in the content directory that are not specified in the manifest.
    /// This is critical for symlinked directories to ensure clean content isolation.
    /// </summary>
    /// <param name="contentPath">Path to the content directory to scan.</param>
    /// <param name="manifest">The manifest describing expected files.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Validation result containing any extraneous files found.</returns>
    Task<ValidationResult> DetectExtraneousFilesAsync(string contentPath, ContentManifest manifest, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs full validation for a manifest and its files (manifest structure, file integrity and extraneous files).
    /// </summary>
    /// <param name="contentPath">Path to the content directory to validate.</param>
    /// <param name="manifest">The manifest to validate against.</param>
    /// <param name="progress">Optional progress reporter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Validation result containing any issues found.</returns>
    Task<ValidationResult> ValidateAllAsync(string contentPath, ContentManifest manifest, IProgress<GenHub.Core.Models.Validation.ValidationProgress>? progress = null, CancellationToken cancellationToken = default);
}