using GenHub.Core.Models.Manifest;

namespace GenHub.Core.Interfaces.Content;

/// <summary>
/// Publisher-specific factory for post-extraction manifest processing.
/// </summary>
/// <remarks>
/// <para>
/// Factories receive an already-resolved manifest and extracted files on disk. They compute
/// file hashes for CAS storage, update the manifest with actual file entries, and optionally
/// split a single package into multiple manifests (e.g., Generals + Zero Hour variants).
/// </para>
/// <para>
/// Factories should not create initial manifests with download URLs (that's the resolver's job),
/// download files (deliverer), or parse catalogs (parser).
/// </para>
/// <para>
/// Pipeline: Discoverer → Parser → Resolver → Deliverer → Factory.
/// </para>
/// </remarks>
public interface IPublisherManifestFactory
{
    /// <summary>
    /// Gets the publisher identifier this factory handles.
    /// Examples: "thesuperhackers", "generalsonline", "communityoutpost".
    /// </summary>
    string PublisherId { get; }

    /// <summary>
    /// Determines if this factory can handle the given manifest.
    /// Typically checks Publisher.PublisherType and ContentType.
    /// </summary>
    /// <param name="manifest">The manifest to check.</param>
    /// <returns>True if this factory can process the manifest.</returns>
    bool CanHandle(ContentManifest manifest);

    /// <summary>
    /// Creates enriched manifests from extracted content.
    /// </summary>
    /// <param name="originalManifest">
    /// The manifest from the resolver, containing download URLs but no file hashes.
    /// </param>
    /// <param name="extractedDirectory">
    /// Directory where the deliverer extracted the package files.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// One or more manifests with file hashes and sizes. Multi-variant content
    /// (e.g., separate Generals and Zero Hour executables) may return multiple manifests.
    /// </returns>
    Task<List<ContentManifest>> CreateManifestsFromExtractedContentAsync(
        ContentManifest originalManifest,
        string extractedDirectory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the subdirectory for a specific manifest's files.
    /// Used when multi-variant content has files in different subdirectories.
    /// </summary>
    /// <param name="manifest">The manifest to get the directory for.</param>
    /// <param name="extractedDirectory">The root extracted directory.</param>
    /// <returns>The subdirectory path for this manifest's files.</returns>
    string GetManifestDirectory(ContentManifest manifest, string extractedDirectory);
}
