// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Tools.TextureEditor;
using GenHub.Core.Interfaces.Tools.WorldBuilder;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.TextureEditor;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace GenHub.Core.Services.Tools.WorldBuilder;

/// <summary>
/// Texture cache over Art\Textures: resolves a texture name through the game asset
/// file system, decodes it once via the SAGE texture codec, and serves cached
/// portable RGBA pixels for terrain swatches and icons.
/// </summary>
public sealed class TextureCache(IGameAssetFileSystem fileSystem, ISageTextureCodec codec, ILogger<TextureCache> logger) : ITextureCache
{
    private readonly object _syncLock = new();
    private readonly Dictionary<string, DecodedTexture> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public int Count
    {
        get
        {
            lock (_syncLock)
            {
                return _cache.Count;
            }
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<DecodedTexture>> GetAsync(string textureName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(textureName);
        var started = Stopwatch.GetTimestamp();
        var key = textureName.Trim();
        lock (_syncLock)
        {
            if (_cache.TryGetValue(key, out var cached))
            {
                return OperationResult<DecodedTexture>.CreateSuccess(cached, Stopwatch.GetElapsedTime(started));
            }
        }

        foreach (var candidate in CandidatePaths(key))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!fileSystem.FileExists(candidate))
            {
                continue;
            }

            var decoded = await DecodeCandidateAsync(candidate, cancellationToken).ConfigureAwait(false);
            if (!decoded.Success || decoded.Data is null)
            {
                logger.LogDebug("Candidate {Candidate} failed to decode: {Error}", candidate, decoded.FirstError);
                continue;
            }

            lock (_syncLock)
            {
                _cache[key] = decoded.Data;
            }

            logger.LogDebug("Cached texture {Texture} from {Path}", key, candidate);
            return OperationResult<DecodedTexture>.CreateSuccess(decoded.Data, Stopwatch.GetElapsedTime(started));
        }

        return OperationResult<DecodedTexture>.CreateFailure($"Texture '{key}' was not found under {WorldBuilderDataConstants.Art.Textures}.", Stopwatch.GetElapsedTime(started));
    }

    /// <inheritdoc />
    public void Clear()
    {
        lock (_syncLock)
        {
            _cache.Clear();
        }
    }

    private static IReadOnlyList<string> CandidatePaths(string textureName)
    {
        var directory = WorldBuilderDataConstants.Art.Textures;
        var separator = WorldBuilderDataConstants.Separators.Virtual;
        var extension = Path.GetExtension(textureName);
        if (extension.Length == 0)
        {
            return
            [
                string.Concat(directory, separator, textureName, TextureEditorConstants.DdsExtension),
                string.Concat(directory, separator, textureName, TextureEditorConstants.TgaExtension),
            ];
        }

        var exact = string.Concat(directory, separator, textureName);
        var sibling = SiblingExtension(extension);
        if (sibling is null)
        {
            return [exact];
        }

        var stem = textureName[..^extension.Length];
        return [exact, string.Concat(directory, separator, stem, sibling)];
    }

    private static string? SiblingExtension(string extension)
    {
        if (extension.Equals(TextureEditorConstants.TgaExtension, StringComparison.OrdinalIgnoreCase))
        {
            return TextureEditorConstants.DdsExtension;
        }

        return extension.Equals(TextureEditorConstants.DdsExtension, StringComparison.OrdinalIgnoreCase)
            ? TextureEditorConstants.TgaExtension
            : null;
    }

    private async Task<OperationResult<DecodedTexture>> DecodeCandidateAsync(string candidate, CancellationToken cancellationToken)
    {
        var read = await fileSystem.ReadAllBytesAsync(candidate, cancellationToken).ConfigureAwait(false);
        if (!read.Success || read.Data is null)
        {
            return OperationResult<DecodedTexture>.CreateFailure(read);
        }

        return codec.Decode(read.Data, Path.GetExtension(candidate), candidate);
    }
}
