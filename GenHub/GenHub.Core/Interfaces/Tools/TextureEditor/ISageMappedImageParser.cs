using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.TextureEditor;

namespace GenHub.Core.Interfaces.Tools.TextureEditor;

/// <summary>
/// Parses and serializes SAGE MappedImage INI blocks.
/// </summary>
public interface ISageMappedImageParser
{
    /// <summary>
    /// Parses MappedImage entries from INI text.
    /// </summary>
    /// <param name="content">The INI text content.</param>
    /// <param name="sourcePath">The optional source path recorded on each entry.</param>
    /// <returns>The parsed entries, or a failure describing malformed blocks.</returns>
    OperationResult<IReadOnlyList<MappedImageDefinition>> ParseText(string content, string? sourcePath = null);

    /// <summary>
    /// Parses MappedImage entries from an INI file.
    /// </summary>
    /// <param name="path">The INI file path.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The parsed entries, or a failure describing the problem.</returns>
    Task<OperationResult<IReadOnlyList<MappedImageDefinition>>> ParseFileAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Serializes MappedImage entries to SAGE INI text.
    /// </summary>
    /// <param name="images">The entries to serialize.</param>
    /// <param name="headerComment">The optional header comment lines without comment markers.</param>
    /// <returns>The serialized INI content.</returns>
    /// <exception cref="ArgumentException">Thrown when a name, texture, or status value contains line breaks or ';', which cannot round-trip through SAGE INI.</exception>
    string Serialize(IEnumerable<MappedImageDefinition> images, string? headerComment = null);
}
