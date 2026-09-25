using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Tools.WndEditor;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.WndEditor;
using ImageMagick;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.WndEditor.Services;

/// <summary>
/// Imports texture files into a mod project and registers them as full-page mapped images.
/// </summary>
public sealed class WndTextureImportService(ILogger<WndTextureImportService> logger) : IWndTextureImportService
{
    private readonly SemaphoreSlim _upsertLock = new(1, 1);

    /// <inheritdoc />
    public async Task<OperationResult<WndTextureImportResult>> ImportTextureAsync(
        string sourceFilePath,
        string projectDirectory,
        string? mappedName = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceFilePath);
        ArgumentNullException.ThrowIfNull(projectDirectory);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            if (!File.Exists(sourceFilePath))
            {
                return OperationResult<WndTextureImportResult>.CreateFailure(
                    $"Texture file not found: {sourceFilePath}",
                    stopwatch.Elapsed);
            }

            var extension = Path.GetExtension(sourceFilePath);
            if (!WndConstants.AssetImport.SourceExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                return OperationResult<WndTextureImportResult>.CreateFailure(
                    $"Unsupported texture format '{extension}'. Supported formats: {string.Join(", ", WndConstants.AssetImport.SourceExtensions)}.",
                    stopwatch.Elapsed);
            }

            var name = SanitizeMappedName(string.IsNullOrWhiteSpace(mappedName) ? Path.GetFileNameWithoutExtension(sourceFilePath) : mappedName);
            var targetExtension = ResolveTargetExtension(extension);
            var textureFileName = string.Concat(name, targetExtension);
            var (textureDirectory, texturePath) = ResolveTextureDestination(projectDirectory, textureFileName);
            var definitionsPath = Path.Combine(
                projectDirectory,
                WndConstants.AssetImport.MappedImagesRelativeDirectory,
                WndConstants.AssetImport.ImportsFileName);

            await _upsertLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            var textureWritten = false;
            try
            {
                var (width, height) = await Task.Run(
                    () => WriteTexture(sourceFilePath, textureDirectory, texturePath, targetExtension, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
                textureWritten = true;

                UpsertDefinition(definitionsPath, name, textureFileName, width, height);

                logger.LogInformation("Imported texture {Name} ({Width}x{Height}) to {Path}", name, width, height, texturePath);
                return OperationResult<WndTextureImportResult>.CreateSuccess(
                    new WndTextureImportResult(name, textureFileName, width, height, texturePath, definitionsPath),
                    stopwatch.Elapsed);
            }
            catch
            {
                if (textureWritten && File.Exists(texturePath) && !string.Equals(Path.GetFullPath(sourceFilePath), Path.GetFullPath(texturePath), StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        File.Delete(texturePath);
                    }
                    catch (Exception cleanupEx)
                    {
                        logger.LogDebug(cleanupEx, "Failed to clean up texture file {Path} after failed import", texturePath);
                    }
                }

                throw;
            }
            finally
            {
                _upsertLock.Release();
            }
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Failed to import texture {Source}", sourceFilePath);
            return OperationResult<WndTextureImportResult>.CreateFailure(
                $"Failed to import texture: {ex.Message}",
                stopwatch.Elapsed);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Access denied importing texture {Source}", sourceFilePath);
            return OperationResult<WndTextureImportResult>.CreateFailure(
                $"Access denied importing texture: {ex.Message}",
                stopwatch.Elapsed);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Invalid texture format or dimensions for {Source}", sourceFilePath);
            return OperationResult<WndTextureImportResult>.CreateFailure(
                ex.Message,
                stopwatch.Elapsed);
        }
        catch (MagickException ex)
        {
            logger.LogWarning(ex, "Failed to decode texture {Source}", sourceFilePath);
            return OperationResult<WndTextureImportResult>.CreateFailure(
                $"Failed to decode texture '{Path.GetFileName(sourceFilePath)}': {ex.Message}",
                stopwatch.Elapsed);
        }
    }

    /// <summary>
    /// Sanitizes a mapped image name to characters safe for file stems and INI headers.
    /// </summary>
    /// <param name="raw">The raw name.</param>
    /// <returns>The sanitized name.</returns>
    internal static string SanitizeMappedName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return WndConstants.AssetImport.FallbackMappedName;
        }

        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var builder = new StringBuilder(raw.Length);
        var lastWasUnderscore = false;
        foreach (var ch in raw.Trim())
        {
            if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '-')
            {
                builder.Append(ch);
                lastWasUnderscore = false;
            }
            else if (!invalid.Contains(ch) && !lastWasUnderscore && builder.Length > 0)
            {
                builder.Append('_');
                lastWasUnderscore = true;
            }
        }

        var sanitized = builder.ToString().Trim('_');
        return sanitized.Length == 0 ? WndConstants.AssetImport.FallbackMappedName : sanitized;
    }

    /// <summary>
    /// Builds a full-page mapped image definition block.
    /// </summary>
    /// <param name="mappedName">The mapped image name.</param>
    /// <param name="textureFileName">The texture file name.</param>
    /// <param name="width">The texture width in pixels.</param>
    /// <param name="height">The texture height in pixels.</param>
    /// <returns>The INI block text.</returns>
    internal static string BuildDefinitionBlock(string mappedName, string textureFileName, int width, int height)
    {
        return string.Join(
            Environment.NewLine,
            string.Concat(WndConstants.MappedImages.BlockTag, " ", mappedName),
            string.Concat("  ", WndConstants.MappedImages.TextureField, " = ", textureFileName),
            string.Concat(
                "  ",
                WndConstants.MappedImages.CoordsField,
                " = ",
                WndConstants.MappedImages.LeftAttribute,
                ":0 ",
                WndConstants.MappedImages.TopAttribute,
                ":0 ",
                WndConstants.MappedImages.RightAttribute,
                ":",
                width,
                " ",
                WndConstants.MappedImages.BottomAttribute,
                ":",
                height),
            WndConstants.MappedImages.EndTag);
    }

    /// <summary>
    /// Replaces or appends a mapped image block in existing INI content.
    /// </summary>
    /// <param name="existingContent">The existing INI content, if any.</param>
    /// <param name="mappedName">The mapped image name identifying the block.</param>
    /// <param name="block">The replacement block text.</param>
    /// <returns>The updated INI content.</returns>
    internal static string UpsertDefinitionBlock(string? existingContent, string mappedName, string block)
    {
        if (string.IsNullOrEmpty(existingContent))
        {
            return block + Environment.NewLine;
        }

        var lines = existingContent.Split(["\r\n", "\n"], StringSplitOptions.None).ToList();
        RemoveExistingBlock(lines, mappedName);
        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
        {
            lines.RemoveAt(lines.Count - 1);
        }

        if (lines.Count > 0)
        {
            lines.Add(string.Empty);
        }

        lines.Add(block);
        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    private static string ResolveTargetExtension(string sourceExtension)
    {
        if (string.Equals(sourceExtension, WndConstants.MappedImages.TextureExtensionDds, StringComparison.OrdinalIgnoreCase))
        {
            return WndConstants.MappedImages.TextureExtensionDds;
        }

        if (string.Equals(sourceExtension, WndConstants.MappedImages.TextureExtensionTga, StringComparison.OrdinalIgnoreCase))
        {
            return WndConstants.MappedImages.TextureExtensionTga;
        }

        // The engine reads DDS and TGA texture pages. Normalize screenshots and web
        // formats to TGA so the imported art works both in-game and in previews.
        return WndConstants.MappedImages.TextureExtensionTga;
    }

    private static (string TextureDirectory, string TexturePath) ResolveTextureDestination(
        string projectDirectory,
        string textureFileName)
    {
        var defaultDir = Path.Combine(projectDirectory, WndConstants.AssetImport.TexturesRelativeDirectory);
        var defaultPath = Path.Combine(defaultDir, textureFileName);

        if (!Directory.Exists(projectDirectory))
        {
            return (defaultDir, defaultPath);
        }

        try
        {
            var sourceRoot = Path.Combine(projectDirectory, "GameFilesEdited");
            var searchDir = Directory.Exists(sourceRoot) ? sourceRoot : projectDirectory;

            var matches = Directory.EnumerateFiles(searchDir, textureFileName, SearchOption.AllDirectories)
                .Where(p => !p.Contains(".Build", StringComparison.OrdinalIgnoreCase) &&
                            !p.Contains(".Release", StringComparison.OrdinalIgnoreCase) &&
                            !p.Contains(".staging", StringComparison.OrdinalIgnoreCase) &&
                            !p.Contains(".git", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (matches.Count > 0)
            {
                var existingPath = matches[0];
                return (Path.GetDirectoryName(existingPath)!, existingPath);
            }
        }
        catch
        {
            // Fall back to default location
        }

        return (defaultDir, defaultPath);
    }

    private static (int Width, int Height) WriteTexture(
        string sourceFilePath,
        string textureDirectory,
        string texturePath,
        string targetExtension,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(textureDirectory);
        cancellationToken.ThrowIfCancellationRequested();

        var ping = new MagickImageInfo(sourceFilePath);
        if (ping.Width > WndConstants.Preview.MaxImportedTextureDimension || ping.Height > WndConstants.Preview.MaxImportedTextureDimension)
        {
            throw new InvalidOperationException(
                $"Texture dimensions ({ping.Width}x{ping.Height}) exceed maximum permitted dimension of {WndConstants.Preview.MaxImportedTextureDimension}px.");
        }

        if (string.Equals(Path.GetExtension(sourceFilePath), targetExtension, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(Path.GetFullPath(sourceFilePath), Path.GetFullPath(texturePath), StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(sourceFilePath, texturePath, overwrite: true);
        }
        else if (!string.Equals(Path.GetExtension(sourceFilePath), targetExtension, StringComparison.OrdinalIgnoreCase))
        {
            using var image = new MagickImage(sourceFilePath);
            image.Settings.Compression = CompressionMethod.NoCompression;
            image.ColorType = image.HasAlpha ? ColorType.TrueColorAlpha : ColorType.TrueColor;
            image.Format = MagickFormat.Tga;
            image.Write(texturePath);
        }

        using var info = new MagickImage(texturePath);
        return ((int)info.Width, (int)info.Height);
    }

    private static void UpsertDefinition(string definitionsPath, string mappedName, string textureFileName, int width, int height)
    {
        var directory = Path.GetDirectoryName(definitionsPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var existing = File.Exists(definitionsPath) ? File.ReadAllText(definitionsPath) : null;
        var block = BuildDefinitionBlock(mappedName, textureFileName, width, height);
        File.WriteAllText(definitionsPath, UpsertDefinitionBlock(existing, mappedName, block));
    }

    private static void RemoveExistingBlock(List<string> lines, string mappedName)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            if (!IsBlockStart(lines[i], mappedName))
            {
                continue;
            }

            var end = i;
            while (end < lines.Count && !string.Equals(lines[end].Trim(), WndConstants.MappedImages.EndTag, StringComparison.OrdinalIgnoreCase))
            {
                end++;
            }

            var count = Math.Min(end - i + 1, lines.Count - i);
            lines.RemoveRange(i, count);
            return;
        }
    }

    private static bool IsBlockStart(string line, string mappedName)
    {
        var trimmed = line.Trim();
        var tag = WndConstants.MappedImages.BlockTag;
        if (!trimmed.StartsWith(tag, StringComparison.OrdinalIgnoreCase) || trimmed.Length <= tag.Length)
        {
            return false;
        }

        if (!char.IsWhiteSpace(trimmed[tag.Length]))
        {
            return false;
        }

        return string.Equals(trimmed[tag.Length..].Trim(), mappedName, StringComparison.OrdinalIgnoreCase);
    }
}
