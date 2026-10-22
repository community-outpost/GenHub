using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.TextureEditor;

namespace GenHub.Core.Interfaces.Tools.TextureEditor;

/// <summary>
/// Decodes and encodes SAGE texture formats into portable RGBA pixels.
/// </summary>
public interface ISageTextureCodec
{
    /// <summary>
    /// Gets a value indicating whether the file extension is a decodable texture format.
    /// </summary>
    /// <param name="extension">The file extension including the leading dot.</param>
    /// <returns>True when the codec can decode the extension.</returns>
    bool SupportsExtension(string extension);

    /// <summary>
    /// Decodes texture bytes selected by file extension.
    /// </summary>
    /// <param name="data">The encoded file bytes.</param>
    /// <param name="extension">The file extension including the leading dot.</param>
    /// <param name="sourceName">The optional source name used in error messages.</param>
    /// <returns>The decoded texture, or a failure describing the problem.</returns>
    OperationResult<DecodedTexture> Decode(byte[] data, string extension, string? sourceName = null);

    /// <summary>
    /// Decodes a texture file selected by file extension.
    /// </summary>
    /// <param name="path">The texture file path.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The decoded texture, or a failure describing the problem.</returns>
    Task<OperationResult<DecodedTexture>> DecodeFileAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Encodes pixels as an uncompressed 32-bit TGA file.
    /// </summary>
    /// <param name="texture">The texture to encode.</param>
    /// <returns>The TGA file bytes, or a failure describing the problem.</returns>
    OperationResult<byte[]> EncodeTga(DecodedTexture texture);
}
