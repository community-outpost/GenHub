// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using GenHub.Core.Models.Results;

namespace GenHub.Core.Interfaces.Tools.WorldBuilder;

/// <summary>
/// Detects, decompresses, and compresses map file envelopes.
/// Mirrors Libraries/Compression/CompressionManager: 4-byte magic plus i32
/// uncompressed length header, then the codec payload.
/// </summary>
public interface IMapCompressionService
{
    /// <summary>
    /// Detects the compression envelope of a file buffer.
    /// </summary>
    /// <param name="data">The file bytes.</param>
    /// <returns>The detected envelope.</returns>
    MapCompression Detect(ReadOnlySpan<byte> data);

    /// <summary>
    /// Reads the declared uncompressed length from the envelope header.
    /// </summary>
    /// <param name="data">The file bytes.</param>
    /// <returns>The declared length, or the buffer length when uncompressed.</returns>
    OperationResult<int> GetUncompressedSize(ReadOnlySpan<byte> data);

    /// <summary>
    /// Maps the detected envelope to the engine CompressionType intent id
    /// (None, RefPack, or ZLib1..ZLib9) used by the world dictionary.
    /// </summary>
    /// <param name="data">The file bytes.</param>
    /// <returns>The intent id matching the envelope.</returns>
    OperationResult<int> GetCompressionIntent(ReadOnlySpan<byte> data);

    /// <summary>
    /// Decompresses a file buffer to raw chunk bytes.
    /// </summary>
    /// <param name="data">The file bytes.</param>
    /// <returns>The raw chunk bytes.</returns>
    OperationResult<byte[]> Decompress(ReadOnlySpan<byte> data);

    /// <summary>
    /// Compresses raw chunk bytes with a ZLib envelope at the given level (1..9).
    /// </summary>
    /// <param name="raw">The raw chunk bytes.</param>
    /// <param name="level">The ZLib level, written into the envelope magic.</param>
    /// <returns>The enveloped bytes.</returns>
    OperationResult<byte[]> CompressZLib(ReadOnlySpan<byte> raw, int level);
}
