using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Tools.ModelViewer;
using GenHub.Core.Interfaces.Tools.TextureEditor;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.ModelViewer;
using GenHub.Core.Services.Tools.Checksum;
using GenHub.Core.Services.Tools.WndEditor;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.IniEditor.Services;

/// <summary>
/// Resolves model names to parsed .w3d models with decoded textures from layered game files.
/// </summary>
public sealed class W3dModelResolver(
    IW3dParser parser,
    ISageTextureCodec textureCodec,
    ILogger<W3dModelResolver> logger) : IW3dModelResolver
{
    private readonly Dictionary<string, SageVirtualFileSystem> _fileSystemCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> _fileSystemOrder = [];
    private readonly object _syncLock = new();

    /// <inheritdoc />
    public Task<OperationResult<W3dResolvedModel>> ResolveAsync(
        string modelName,
        string installationPath,
        bool isZeroHour,
        string? projectDirectory = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelName);
        ArgumentException.ThrowIfNullOrWhiteSpace(installationPath);
        long started = Stopwatch.GetTimestamp();
        cancellationToken.ThrowIfCancellationRequested();

        if (!Directory.Exists(installationPath))
        {
            return Task.FromResult(OperationResult<W3dResolvedModel>.CreateFailure(
                $"Installation directory not found: {installationPath}",
                Stopwatch.GetElapsedTime(started)));
        }

        try
        {
            var fileSystem = GetOrOpenFileSystem(installationPath, isZeroHour, projectDirectory, cancellationToken);
            return ResolveFromFileSystemAsync(modelName, fileSystem, cancellationToken);
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Failed to open game files at {Path}", installationPath);
            return Task.FromResult(OperationResult<W3dResolvedModel>.CreateFailure(
                $"Failed to open game files: {installationPath}",
                Stopwatch.GetElapsedTime(started)));
        }
    }

    /// <inheritdoc />
    public Task<OperationResult<W3dResolvedModel>> ResolveFromFileSystemAsync(
        string modelName,
        SageVirtualFileSystem fileSystem,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelName);
        ArgumentNullException.ThrowIfNull(fileSystem);
        long started = Stopwatch.GetTimestamp();
        cancellationToken.ThrowIfCancellationRequested();

        string cleanName = modelName.Trim();
        var modelBytes = FindModelBytes(cleanName, fileSystem, out string? sourceName);
        if (modelBytes == null)
        {
            return Task.FromResult(OperationResult<W3dResolvedModel>.CreateFailure(
                $"{W3dConstants.ModelNotFoundPrefix} {cleanName}",
                Stopwatch.GetElapsedTime(started)));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var parsed = parser.Parse(modelBytes, sourceName ?? cleanName);
        if (!parsed.Success || parsed.Data == null)
        {
            return Task.FromResult(OperationResult<W3dResolvedModel>.CreateFailure(
                parsed,
                Stopwatch.GetElapsedTime(started)));
        }

        var resolved = ResolveTextures(parsed.Data, fileSystem, cancellationToken);
        var model = new W3dResolvedModel(cleanName, sourceName ?? cleanName, parsed.Data, resolved.Textures, resolved.Missing);
        return Task.FromResult(OperationResult<W3dResolvedModel>.CreateSuccess(model, Stopwatch.GetElapsedTime(started)));
    }

    /// <inheritdoc />
    public void ClearCache()
    {
        lock (_syncLock)
        {
            _fileSystemCache.Clear();
            _fileSystemOrder.Clear();
        }
    }

    private static bool IsInvalidAssetPath(string name)
    {
        // Asset names are bare SAGE identifiers; both separator kinds are rejected
        // on every OS because backslash names arrive from Windows-authored files.
        return string.IsNullOrWhiteSpace(name) ||
            name.Contains("..", StringComparison.Ordinal) ||
            name.Contains('/') ||
            name.Contains('\\') ||
            Path.IsPathRooted(name);
    }

    private byte[]? FindModelBytes(string modelName, SageVirtualFileSystem fileSystem, out string? sourceName)
    {
        sourceName = null;
        if (IsInvalidAssetPath(modelName))
        {
            logger.LogWarning("Rejected unsafe model asset path {Model}", modelName);
            return null;
        }

        string fileName = modelName.EndsWith(W3dConstants.FileExtension, StringComparison.OrdinalIgnoreCase)
            ? modelName
            : modelName + W3dConstants.FileExtension;

        foreach (string candidate in new[] { fileName, $"{W3dConstants.ArtDirectory}/{fileName}" })
        {
            var bytes = fileSystem.Read(candidate);
            if (bytes != null)
            {
                sourceName = candidate;
                return bytes;
            }
        }

        var archived = fileSystem.TryReadArchiveFileByNameWithPath(fileName);
        if (archived.HasValue)
        {
            sourceName = archived.Value.Path;
            return archived.Value.Bytes;
        }

        return null;
    }

    private IReadOnlyList<string> TextureCandidates(string textureName)
    {
        string clean = textureName.Trim();
        if (IsInvalidAssetPath(clean))
        {
            logger.LogWarning("Rejected unsafe texture asset path {Texture}", textureName);
            return [];
        }

        // Shipped models often reference one image extension while the game
        // ships the other, so names carrying an extension also try the sibling
        // extension the same way the engine does.
        if (clean.EndsWith(ModBuilderConstants.FileExtensions.Dds, StringComparison.OrdinalIgnoreCase))
        {
            string stem = clean[..^ModBuilderConstants.FileExtensions.Dds.Length];
            return
            [
                clean,
                $"{W3dConstants.ArtDirectory}/{clean}",
                stem + ModBuilderConstants.FileExtensions.Tga,
                $"{W3dConstants.ArtDirectory}/{stem}{ModBuilderConstants.FileExtensions.Tga}",
            ];
        }

        if (clean.EndsWith(ModBuilderConstants.FileExtensions.Tga, StringComparison.OrdinalIgnoreCase))
        {
            string stem = clean[..^ModBuilderConstants.FileExtensions.Tga.Length];
            return
            [
                clean,
                $"{W3dConstants.ArtDirectory}/{clean}",
                stem + ModBuilderConstants.FileExtensions.Dds,
                $"{W3dConstants.ArtDirectory}/{stem}{ModBuilderConstants.FileExtensions.Dds}",
            ];
        }

        // Bare stems and foreign extensions attempt the literal name first so an
        // exact file is never skipped over in favor of an extension guess.
        return
        [
            clean,
            $"{W3dConstants.ArtDirectory}/{clean}",
            clean + ModBuilderConstants.FileExtensions.Dds,
            clean + ModBuilderConstants.FileExtensions.Tga,
            $"{W3dConstants.ArtDirectory}/{clean}{ModBuilderConstants.FileExtensions.Dds}",
            $"{W3dConstants.ArtDirectory}/{clean}{ModBuilderConstants.FileExtensions.Tga}",
        ];
    }

    private SageVirtualFileSystem GetOrOpenFileSystem(string installationPath, bool isZeroHour, string? projectDirectory, CancellationToken cancellationToken)
    {
        string key = WndGameFileSystem.BuildAssetCacheKey(installationPath, null, projectDirectory, null, isZeroHour);
        lock (_syncLock)
        {
            if (_fileSystemCache.TryGetValue(key, out var cached))
            {
                return cached;
            }
        }

        // Archive mounting performs multi-second I/O, so it runs outside the
        // lock; a lost insertion race simply drops the redundant file system.
        var fileSystem = WndGameFileSystem.Open(installationPath, null, projectDirectory, logger, null, isZeroHour, cancellationToken);
        lock (_syncLock)
        {
            if (_fileSystemCache.TryGetValue(key, out var raced))
            {
                return raced;
            }

            if (_fileSystemCache.Count >= IniConstants.Editor.MaxCachedFileSystems && _fileSystemOrder.First != null)
            {
                _fileSystemCache.Remove(_fileSystemOrder.First.Value);
                _fileSystemOrder.RemoveFirst();
            }

            _fileSystemCache[key] = fileSystem;
            _fileSystemOrder.AddLast(key);
            return fileSystem;
        }
    }

    private (IReadOnlyList<W3dResolvedTexture> Textures, IReadOnlyList<string> Missing) ResolveTextures(
        W3dModel model,
        SageVirtualFileSystem fileSystem,
        CancellationToken cancellationToken)
    {
        var names = new HashSet<string>(
            model.Meshes
                .SelectMany(mesh => mesh.Textures)
                .Where(texture => !string.IsNullOrWhiteSpace(texture.Name))
                .Select(texture => texture.Name.Trim()),
            StringComparer.OrdinalIgnoreCase);

        var textures = new List<W3dResolvedTexture>();
        var missing = new List<string>();
        foreach (string textureName in names.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var decoded = TryDecodeTexture(textureName, fileSystem, out string? source);
            if (decoded == null)
            {
                missing.Add(textureName);
            }
            else
            {
                textures.Add(new W3dResolvedTexture(textureName, decoded, source ?? textureName));
            }
        }

        return (textures, missing);
    }

    private Core.Models.Tools.TextureEditor.DecodedTexture? TryDecodeTexture(string textureName, SageVirtualFileSystem fileSystem, out string? source)
    {
        source = null;
        foreach (string candidate in TextureCandidates(textureName))
        {
            // Normalize separators before extracting the file name: Path.GetFileName
            // only treats backslash as a separator on Windows.
            var bytes = fileSystem.Read(candidate)
                ?? fileSystem.TryReadArchiveFileByName(Path.GetFileName(candidate.Replace('\\', '/')));
            if (bytes == null)
            {
                continue;
            }

            string extension = Path.GetExtension(candidate);
            if (!textureCodec.SupportsExtension(extension))
            {
                logger.LogWarning("Skipping texture {Texture} with unsupported extension {Extension}", candidate, extension);
                continue;
            }

            var decoded = textureCodec.Decode(bytes, extension, candidate);
            if (decoded.Success && decoded.Data != null)
            {
                source = candidate;
                return decoded.Data;
            }

            logger.LogWarning("Failed to decode texture {Texture}: {Error}", candidate, decoded.FirstError);
        }

        return null;
    }
}
