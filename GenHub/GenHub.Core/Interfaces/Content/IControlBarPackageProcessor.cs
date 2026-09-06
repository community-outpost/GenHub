using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Models.Manifest;

namespace GenHub.Core.Interfaces.Content;

/// <summary>
/// Service for detecting, isolating, converting, and packaging Control Bar content into SAGE-compatible .big archives.
/// </summary>
public interface IControlBarPackageProcessor
{
    /// <summary>
    /// Checks whether the extracted directory or manifest represents a Control Bar mod or UI addon that needs repacking.
    /// </summary>
    /// <param name="extractedDirectory">The directory containing extracted files.</param>
    /// <param name="manifest">The content manifest.</param>
    /// <returns>True if the content is a Control Bar that requires processing.</returns>
    bool IsControlBarContent(string extractedDirectory, ContentManifest manifest);

    /// <summary>
    /// Processes extracted Control Bar content: isolates the requested resolution variant, converts AVIF/WebP textures to TGA,
    /// repacks Art/Data folders into .big archives, ensures metadata BIG is present, and optionally cleans up raw sources.
    /// </summary>
    /// <param name="extractedDirectory">The directory containing extracted files.</param>
    /// <param name="manifest">The content manifest.</param>
    /// <param name="requestedVariant">Optional explicit variant identifier (e.g. "1080p").</param>
    /// <param name="cleanupSources">Whether to clean up source directories after packaging. Set to false when processing multiple variants against the same directory.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of generated or included .big file names.</returns>
    Task<IReadOnlyList<string>> ProcessAndRepackControlBarAsync(
        string extractedDirectory,
        ContentManifest manifest,
        string? requestedVariant = null,
        bool cleanupSources = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cleans up raw source directories and unneeded loose files in the extracted directory after all variants have been packaged.
    /// </summary>
    /// <param name="extractedDirectory">The directory containing extracted files.</param>
    /// <param name="repackedOutputs">The set of repacked output BIG file names to preserve.</param>
    void CleanupSourceDirectories(string extractedDirectory, IEnumerable<string> repackedOutputs);

    /// <summary>
    /// Finds the variant BIG root directory within extracted content.
    /// </summary>
    /// <param name="extractedDirectory">The extracted root directory.</param>
    /// <param name="variantId">The variant identifier (e.g. "1080p").</param>
    /// <returns>The path to the variant root directory, or null if not found.</returns>
    string? FindControlBarVariantBigRoot(string extractedDirectory, string variantId);

    /// <summary>
    /// Gets the normalized suffix for a variant identifier (e.g. "1080p" -> "1080", "2160p" -> "4K").
    /// </summary>
    /// <param name="variantId">The variant identifier.</param>
    /// <returns>The normalized variant suffix.</returns>
    string GetControlBarVariantSuffix(string variantId);

    /// <summary>
    /// Checks if a file is an allowed Control Bar .big archive for the given variant suffix.
    /// </summary>
    /// <param name="fileName">The file name.</param>
    /// <param name="variantSuffix">The variant suffix.</param>
    /// <returns>True if the file is allowed.</returns>
    bool IsAllowedControlBarBig(string fileName, string variantSuffix);

    /// <summary>
    /// Checks if a file name corresponds to a metadata-only Control Bar .big archive.
    /// </summary>
    /// <param name="fileName">The file name.</param>
    /// <returns>True if the file is a metadata-only .big archive.</returns>
    bool IsMetadataOnlyBig(string fileName);
}
