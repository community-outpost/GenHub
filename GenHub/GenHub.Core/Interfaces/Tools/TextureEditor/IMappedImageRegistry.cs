using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.TextureEditor;

namespace GenHub.Core.Interfaces.Tools.TextureEditor;

/// <summary>
/// Indexes MappedImage entries scanned from workspace, project, and retail INI files.
/// Names are case-insensitive, matching SAGE ImageCollection behavior.
/// </summary>
public interface IMappedImageRegistry
{
    /// <summary>
    /// Gets all indexed entries ordered by name.
    /// </summary>
    IReadOnlyList<MappedImageDefinition> All { get; }

    /// <summary>
    /// Gets the number of indexed entries.
    /// </summary>
    int Count { get; }

    /// <summary>
    /// Scans a directory recursively for MappedImages INI files.
    /// Files are applied in SAGE load order: base files first, then TextureSize_*
    /// overrides, then HandCreated overrides last, alphabetical within each tier.
    /// Unlike the engine, which loads only the active texture size, the registry
    /// indexes every size into one catalog for editing.
    /// </summary>
    /// <param name="directory">The directory to scan.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The scan outcome.</returns>
    Task<OperationResult<MappedImageScanResult>> ScanDirectoryAsync(string directory, CancellationToken cancellationToken = default);

    /// <summary>
    /// Looks up an entry by name using case-insensitive comparison.
    /// </summary>
    /// <param name="name">The mapped image name.</param>
    /// <returns>The entry, or null when unknown.</returns>
    MappedImageDefinition? GetByName(string name);

    /// <summary>
    /// Gets all entries referencing a texture file name using case-insensitive comparison.
    /// </summary>
    /// <param name="textureFileName">The texture file name.</param>
    /// <returns>The matching entries ordered by name.</returns>
    IReadOnlyList<MappedImageDefinition> GetByTexture(string textureFileName);

    /// <summary>
    /// Imports parsed entries, overwriting same-named entries like a later SAGE load tier.
    /// </summary>
    /// <param name="images">The entries to import.</param>
    void ImportDefinitions(IEnumerable<MappedImageDefinition> images);

    /// <summary>
    /// Removes all indexed entries.
    /// </summary>
    void Clear();
}
