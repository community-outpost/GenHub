// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.TextureEditor;

namespace GenHub.Core.Interfaces.Tools.WorldBuilder;

/// <summary>
/// Texture cache over Art\Textures: resolves a texture name through the game asset
/// file system, decodes it once via the SAGE texture codec, and serves cached
/// portable RGBA pixels for terrain swatches and icons.
/// </summary>
public interface ITextureCache
{
    /// <summary>
    /// Gets the number of cached textures.
    /// </summary>
    int Count { get; }

    /// <summary>
    /// Gets the decoded pixels for a texture name, decoding and caching on first use.
    /// A bare name tries the DDS sibling before TGA like the engine; an explicit
    /// extension tries the exact file first, then the swapped sibling.
    /// </summary>
    /// <param name="textureName">The texture name from the INI (with or without extension).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The decoded texture, or a failure when no candidate resolves.</returns>
    Task<OperationResult<DecodedTexture>> GetAsync(string textureName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Evicts all cached textures.
    /// </summary>
    void Clear();
}
