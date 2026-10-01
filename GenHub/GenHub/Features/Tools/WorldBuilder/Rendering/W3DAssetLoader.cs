// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Tools.WorldBuilder;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.WorldBuilder;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.WorldBuilder.Rendering;

/// <summary>
/// Loads compiled W3D models from Art/W3D through the game asset file system.
/// </summary>
/// <param name="fileSystem">The game asset file system.</param>
/// <param name="logger">The logger.</param>
public sealed class W3DAssetLoader(IGameAssetFileSystem fileSystem, ILogger<W3DAssetLoader> logger) : IW3DAssetLoader
{
    private readonly IGameAssetFileSystem _fileSystem = fileSystem;
    private readonly ILogger<W3DAssetLoader> _logger = logger;

    /// <inheritdoc />
    public async Task<OperationResult<W3DModel>> LoadAsync(string fileName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        var normalized = fileName.EndsWith(WorldBuilderConstants.W3D.FileExtension, StringComparison.OrdinalIgnoreCase)
            ? fileName
            : fileName + WorldBuilderConstants.W3D.FileExtension;
        var virtualPath = string.Concat(
            WorldBuilderDataConstants.Art.W3D,
            WorldBuilderDataConstants.Separators.Virtual,
            normalized);
        var bytes = await _fileSystem.ReadAllBytesAsync(virtualPath, cancellationToken).ConfigureAwait(false);
        if (!bytes.Success || bytes.Data == null)
        {
            return OperationResult<W3DModel>.CreateFailure($"W3D file '{normalized}' was not found.");
        }

        var chunks = W3dChunkReader.ReadTopLevel(bytes.Data);
        if (!chunks.Success || chunks.Data == null)
        {
            _logger.LogWarning("Failed to parse W3D file {File}: {Error}", normalized, chunks.FirstError);
            return OperationResult<W3DModel>.CreateFailure($"W3D file '{normalized}' is not a chunk stream.");
        }

        var model = W3dModelExtractor.ExtractModel(normalized, chunks.Data);
        if (model.Meshes.Count == 0 && model.Hlods.Count == 0)
        {
            _logger.LogWarning("W3D file {File} contains no meshes or HLOD data", normalized);
        }

        return OperationResult<W3DModel>.CreateSuccess(model);
    }
}
