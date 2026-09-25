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
            var definitionsPath = Path.Combine(
                projectDirectory,
                WndConstants.AssetImport.MappedImagesRelativeDirectory,
                WndConstants.AssetImport.ImportsFileName);

            await _upsertLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            var textureWritten = false;
            string? texturePath = null;
            try
            {
                var (textureDirectory, resolvedPath) = await Task.Run(
                    () => ResolveTextureDestination(projectDirectory, textureFileName),
                    cancellationToken).ConfigureAwait(false);
                texturePath = resolvedPath;

                var (imageWidth, imageHeight, potWidth, potHeight) = await Task.Run(
                    () => WriteTexture(sourceFilePath, textureDirectory, texturePath, targetExtension, logger, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
                textureWritten = true;

                UpsertDefinition(definitionsPath, name, textureFileName, imageWidth, imageHeight, potWidth, potHeight);

                logger.LogInformation("Imported texture {Name} ({Width}x{Height}, POT {PotW}x{PotH}) to {Path}", name, imageWidth, imageHeight, potWidth, potHeight, texturePath);
                return OperationResult<WndTextureImportResult>.CreateSuccess(
                    new WndTextureImportResult(name, textureFileName, imageWidth, imageHeight, texturePath, definitionsPath),
                    stopwatch.Elapsed);
            }
            catch
            {
                if (textureWritten && texturePath != null && File.Exists(texturePath) && !string.Equals(Path.GetFullPath(sourceFilePath), Path.GetFullPath(texturePath), StringComparison.OrdinalIgnoreCase))
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

    /// <inheritdoc />
    public async Task<OperationResult<WndTextureImportResult>> ImportTextureFromBytesAsync(
        byte[] imageBytes,
        string projectDirectory,
        string mappedName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);
        ArgumentNullException.ThrowIfNull(projectDirectory);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            if (imageBytes.Length == 0)
            {
                return OperationResult<WndTextureImportResult>.CreateFailure(
                    "Image bytes cannot be empty.",
                    stopwatch.Elapsed);
            }

            var name = SanitizeMappedName(mappedName);
            var targetExtension = WndConstants.MappedImages.TextureExtensionTga;
            var textureFileName = string.Concat(name, targetExtension);
            var definitionsPath = Path.Combine(
                projectDirectory,
                WndConstants.AssetImport.MappedImagesRelativeDirectory,
                WndConstants.AssetImport.ImportsFileName);

            await _upsertLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            var textureWritten = false;
            string? texturePath = null;
            try
            {
                var (textureDirectory, resolvedPath) = await Task.Run(
                    () => ResolveTextureDestination(projectDirectory, textureFileName),
                    cancellationToken).ConfigureAwait(false);
                texturePath = resolvedPath;

                var (imageWidth, imageHeight, potWidth, potHeight) = await Task.Run(
                    () => WriteTextureBytes(imageBytes, textureDirectory, texturePath, logger, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
                textureWritten = true;

                UpsertDefinition(definitionsPath, name, textureFileName, imageWidth, imageHeight, potWidth, potHeight);

                logger.LogInformation("Imported clipboard texture {Name} ({Width}x{Height}, POT {PotW}x{PotH}) to {Path}", name, imageWidth, imageHeight, potWidth, potHeight, texturePath);
                return OperationResult<WndTextureImportResult>.CreateSuccess(
                    new WndTextureImportResult(name, textureFileName, imageWidth, imageHeight, texturePath, definitionsPath),
                    stopwatch.Elapsed);
            }
            catch
            {
                if (textureWritten && texturePath != null && File.Exists(texturePath))
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
            logger.LogWarning(ex, "Failed to import clipboard texture as {Name}", mappedName);
            return OperationResult<WndTextureImportResult>.CreateFailure(
                $"Failed to import texture: {ex.Message}",
                stopwatch.Elapsed);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Access denied importing clipboard texture as {Name}", mappedName);
            return OperationResult<WndTextureImportResult>.CreateFailure(
                $"Access denied importing texture: {ex.Message}",
                stopwatch.Elapsed);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Invalid texture format or dimensions for clipboard image");
            return OperationResult<WndTextureImportResult>.CreateFailure(
                ex.Message,
                stopwatch.Elapsed);
        }
        catch (MagickException ex)
        {
            logger.LogWarning(ex, "Failed to decode clipboard image");
            return OperationResult<WndTextureImportResult>.CreateFailure(
                $"Failed to decode clipboard image: {ex.Message}",
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

        var result = builder.ToString().TrimEnd('_');
        return string.IsNullOrEmpty(result) ? WndConstants.AssetImport.FallbackMappedName : result;
    }

    /// <summary>
    /// Formats a complete MappedImage definition block with power-of-two texture dimensions and SAGE engine attributes.
    /// </summary>
    /// <param name="mappedName">The mapped image name.</param>
    /// <param name="textureFileName">The target texture file name.</param>
    /// <param name="imageWidth">Source image width.</param>
    /// <param name="imageHeight">Source image height.</param>
    /// <param name="textureWidth">Underlying texture surface width.</param>
    /// <param name="textureHeight">Underlying texture surface height.</param>
    /// <returns>Formatted INI block string.</returns>
    internal static string BuildDefinitionBlock(
        string mappedName,
        string textureFileName,
        int imageWidth,
        int imageHeight,
        int textureWidth,
        int textureHeight)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"{WndConstants.MappedImages.BlockTag} {mappedName}");
        builder.AppendLine($"  {WndConstants.MappedImages.TextureField} = {textureFileName}");
        builder.AppendLine($"  {WndConstants.MappedImages.TextureWidthField} = {textureWidth}");
        builder.AppendLine($"  {WndConstants.MappedImages.TextureHeightField} = {textureHeight}");
        builder.AppendLine($"  {WndConstants.MappedImages.CoordsField} = {WndConstants.MappedImages.LeftAttribute}:0 {WndConstants.MappedImages.TopAttribute}:0 {WndConstants.MappedImages.RightAttribute}:{imageWidth} {WndConstants.MappedImages.BottomAttribute}:{imageHeight}");
        builder.AppendLine($"  {WndConstants.MappedImages.StatusField} = {WndConstants.MappedImages.StatusNone}");
        builder.AppendLine(WndConstants.MappedImages.EndTag);
        return builder.ToString();
    }

    /// <summary>
    /// Formats a complete MappedImage definition block where texture surface matches image dimensions.
    /// </summary>
    /// <param name="mappedName">The target mapped image name to register.</param>
    /// <param name="textureFileName">The target texture file name on disk.</param>
    /// <param name="width">Source image width in pixels.</param>
    /// <param name="height">Source image height in pixels.</param>
    /// <returns>Formatted INI block string.</returns>
    internal static string BuildDefinitionBlock(string mappedName, string textureFileName, int width, int height)
        => BuildDefinitionBlock(mappedName, textureFileName, width, height, width, height);

    /// <summary>
    /// Inserts or replaces a MappedImage block in an existing INI content string.
    /// </summary>
    /// <param name="existingContent">The current content of the INI file, or null/empty.</param>
    /// <param name="mappedName">The mapped image name to upsert.</param>
    /// <param name="newBlock">The new block text to insert.</param>
    /// <returns>The updated file content.</returns>
    internal static string UpsertDefinitionBlock(string? existingContent, string mappedName, string newBlock)
    {
        if (string.IsNullOrWhiteSpace(existingContent))
        {
            return newBlock;
        }

        var lines = existingContent
            .Split(["\r\n", "\r", "\n"], StringSplitOptions.None)
            .ToList();

        RemoveExistingBlock(lines, mappedName);

        var trimmedEnd = lines.Count;
        while (trimmedEnd > 0 && string.IsNullOrWhiteSpace(lines[trimmedEnd - 1]))
        {
            trimmedEnd--;
        }

        lines.RemoveRange(trimmedEnd, lines.Count - trimmedEnd);

        var result = new StringBuilder();
        foreach (var line in lines)
        {
            result.AppendLine(line);
        }

        if (lines.Count > 0)
        {
            result.AppendLine();
        }

        result.Append(newBlock);
        return result.ToString();
    }

    private static string ResolveTargetExtension(string sourceExtension)
    {
        return string.Equals(sourceExtension, WndConstants.MappedImages.TextureExtensionDds, StringComparison.OrdinalIgnoreCase)
            ? WndConstants.MappedImages.TextureExtensionDds
            : WndConstants.MappedImages.TextureExtensionTga;
    }

    private static (string TextureDirectory, string TexturePath) ResolveTextureDestination(
        string projectDirectory,
        string textureFileName)
    {
        var defaultDir = Path.Combine(
            projectDirectory,
            ModBuilderConstants.GameFilesEditedDir,
            WndConstants.MappedImages.ArtFolder,
            WndConstants.MappedImages.TexturesFolder);
        var defaultPath = Path.Combine(defaultDir, textureFileName);

        if (!Directory.Exists(projectDirectory))
        {
            return (defaultDir, defaultPath);
        }

        try
        {
            var sourceRoot = Path.Combine(projectDirectory, ModBuilderConstants.GameFilesEditedDir);
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

            var englishTextures = Path.Combine(
                searchDir,
                WndConstants.MappedImages.DataFolder,
                WndConstants.MappedImages.EnglishFolder,
                WndConstants.MappedImages.ArtFolder,
                WndConstants.MappedImages.TexturesFolder);
            if (Directory.Exists(englishTextures))
            {
                return (englishTextures, Path.Combine(englishTextures, textureFileName));
            }

            var standardArtTextures = Path.Combine(
                searchDir,
                WndConstants.MappedImages.ArtFolder,
                WndConstants.MappedImages.TexturesFolder);
            if (Directory.Exists(standardArtTextures))
            {
                return (standardArtTextures, Path.Combine(standardArtTextures, textureFileName));
            }

            var candidateDirs = Directory.EnumerateDirectories(
                searchDir,
                WndConstants.MappedImages.TexturesFolder,
                SearchOption.AllDirectories)
                .Where(d => !d.Contains(".Build", StringComparison.OrdinalIgnoreCase) &&
                            !d.Contains(".Release", StringComparison.OrdinalIgnoreCase) &&
                            !d.Contains(".staging", StringComparison.OrdinalIgnoreCase) &&
                            !d.Contains(".git", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (candidateDirs.Count > 0)
            {
                var artTexturesSuffix = Path.Combine(
                    WndConstants.MappedImages.ArtFolder,
                    WndConstants.MappedImages.TexturesFolder);
                var artTexturesDir = candidateDirs.FirstOrDefault(d =>
                    d.EndsWith(artTexturesSuffix, StringComparison.OrdinalIgnoreCase));
                var targetDir = artTexturesDir ?? candidateDirs[0];
                return (targetDir, Path.Combine(targetDir, textureFileName));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Fall back to default location
        }

        return (defaultDir, defaultPath);
    }

    private static (int ImageWidth, int ImageHeight, int TextureWidth, int TextureHeight) WriteTexture(
        string sourceFilePath,
        string textureDirectory,
        string texturePath,
        string targetExtension,
        ILogger logger,
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

        var imageWidth = (int)ping.Width;
        var imageHeight = (int)ping.Height;
        var potWidth = RoundUpToPowerOfTwo(imageWidth);
        var potHeight = RoundUpToPowerOfTwo(imageHeight);

        if (string.Equals(Path.GetExtension(sourceFilePath), targetExtension, StringComparison.OrdinalIgnoreCase)
            && imageWidth == potWidth && imageHeight == potHeight
            && !string.Equals(Path.GetFullPath(sourceFilePath), Path.GetFullPath(texturePath), StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(sourceFilePath, texturePath, overwrite: true);
        }
        else
        {
            using var image = new MagickImage(sourceFilePath);
            if (image.Width != (uint)potWidth || image.Height != (uint)potHeight)
            {
                image.Extent((uint)potWidth, (uint)potHeight, Gravity.Northwest, MagickColors.Transparent);
            }

            image.Settings.Compression = CompressionMethod.NoCompression;
            image.ColorType = image.HasAlpha ? ColorType.TrueColorAlpha : ColorType.TrueColor;
            image.Format = string.Equals(targetExtension, WndConstants.MappedImages.TextureExtensionDds, StringComparison.OrdinalIgnoreCase)
                ? MagickFormat.Dds
                : MagickFormat.Tga;
            image.Write(texturePath);
        }

        SyncTextureToSiblingDirectories(textureDirectory, Path.GetFileName(texturePath), texturePath, logger);

        return (imageWidth, imageHeight, potWidth, potHeight);
    }

    private static (int ImageWidth, int ImageHeight, int TextureWidth, int TextureHeight) WriteTextureBytes(
        byte[] imageBytes,
        string textureDirectory,
        string texturePath,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(textureDirectory);
        cancellationToken.ThrowIfCancellationRequested();

        using var image = CreateMagickImageFromBytes(imageBytes);
        if (image.Width > WndConstants.Preview.MaxImportedTextureDimension || image.Height > WndConstants.Preview.MaxImportedTextureDimension)
        {
            throw new InvalidOperationException(
                $"Texture dimensions ({image.Width}x{image.Height}) exceed maximum permitted dimension of {WndConstants.Preview.MaxImportedTextureDimension}px.");
        }

        var imageWidth = (int)image.Width;
        var imageHeight = (int)image.Height;
        var potWidth = RoundUpToPowerOfTwo(imageWidth);
        var potHeight = RoundUpToPowerOfTwo(imageHeight);

        if (image.Width != (uint)potWidth || image.Height != (uint)potHeight)
        {
            image.Extent((uint)potWidth, (uint)potHeight, Gravity.Northwest, MagickColors.Transparent);
        }

        image.Settings.Compression = CompressionMethod.NoCompression;
        image.ColorType = image.HasAlpha ? ColorType.TrueColorAlpha : ColorType.TrueColor;
        image.Format = MagickFormat.Tga;
        image.Write(texturePath);

        SyncTextureToSiblingDirectories(textureDirectory, Path.GetFileName(texturePath), texturePath, logger);

        return (imageWidth, imageHeight, potWidth, potHeight);
    }

    private static int RoundUpToPowerOfTwo(int value)
    {
        if (value <= 1)
        {
            return 1;
        }

        var power = 1;
        while (power < value && power < WndConstants.Preview.MaxImportedTextureDimension)
        {
            power <<= 1;
        }

        return power;
    }

    private static void SyncTextureToSiblingDirectories(string textureDirectory, string fileName, string sourceFile, ILogger logger)
    {
        try
        {
            var gameFilesEdited = FindGameFilesEditedDirectory(textureDirectory);
            if (gameFilesEdited == null || !gameFilesEdited.Exists)
            {
                return;
            }

            var dataDir = Path.Combine(gameFilesEdited.FullName, WndConstants.MappedImages.DataFolder);
            if (Directory.Exists(dataDir))
            {
                foreach (var langDir in Directory.EnumerateDirectories(dataDir))
                {
                    var langArtTextures = Path.Combine(
                        langDir,
                        WndConstants.MappedImages.ArtFolder,
                        WndConstants.MappedImages.TexturesFolder);
                    if (Directory.Exists(langArtTextures) && !string.Equals(Path.GetFullPath(langArtTextures), Path.GetFullPath(textureDirectory), StringComparison.OrdinalIgnoreCase))
                    {
                        var destFile = Path.Combine(langArtTextures, fileName);
                        File.Copy(sourceFile, destFile, overwrite: true);
                    }
                }
            }

            var artTextures = Path.Combine(
                gameFilesEdited.FullName,
                WndConstants.MappedImages.ArtFolder,
                WndConstants.MappedImages.TexturesFolder);
            if (Directory.Exists(artTextures) && !string.Equals(Path.GetFullPath(artTextures), Path.GetFullPath(textureDirectory), StringComparison.OrdinalIgnoreCase))
            {
                var destFile = Path.Combine(artTextures, fileName);
                File.Copy(sourceFile, destFile, overwrite: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(ex, "Failed to synchronize texture {File} to sibling directories", fileName);
        }
    }

    private static DirectoryInfo? FindGameFilesEditedDirectory(string textureDirectory)
    {
        var current = new DirectoryInfo(textureDirectory);
        while (current != null)
        {
            if (current.Name.Equals(ModBuilderConstants.GameFilesEditedDir, StringComparison.OrdinalIgnoreCase))
            {
                return current;
            }

            var sub = Path.Combine(current.FullName, ModBuilderConstants.GameFilesEditedDir);
            if (Directory.Exists(sub))
            {
                return new DirectoryInfo(sub);
            }

            current = current.Parent;
        }

        return null;
    }

    private static MagickImage CreateMagickImageFromBytes(byte[] bytes)
    {
        try
        {
            return new MagickImage(bytes);
        }
        catch (MagickException)
        {
            if (bytes.Length > 40 && TryCreateBmpFromDib(bytes, out var bmpBytes))
            {
                return new MagickImage(bmpBytes);
            }

            throw;
        }
    }

    private static bool TryCreateBmpFromDib(byte[] dib, out byte[] bmpBytes)
    {
        bmpBytes = [];
        if (dib.Length < 40)
        {
            return false;
        }

        var headerSize = BitConverter.ToInt32(dib, 0);
        if (headerSize != 40 && headerSize != 108 && headerSize != 124)
        {
            return false;
        }

        var bitCount = (int)BitConverter.ToInt16(dib, 14);
        var clrUsed = BitConverter.ToInt32(dib, 32);
        int paletteEntries;
        if (clrUsed > 0)
        {
            paletteEntries = clrUsed;
        }
        else if (bitCount is > 0 and <= 8)
        {
            paletteEntries = 1 << bitCount;
        }
        else
        {
            paletteEntries = 0;
        }

        var offsetToPixels = 14 + headerSize + (paletteEntries * 4);
        var totalFileSize = 14 + dib.Length;
        var fullBmp = new byte[totalFileSize];
        fullBmp[0] = (byte)'B';
        fullBmp[1] = (byte)'M';
        BitConverter.GetBytes(totalFileSize).CopyTo(fullBmp, 2);
        BitConverter.GetBytes(offsetToPixels).CopyTo(fullBmp, 10);
        dib.CopyTo(fullBmp, 14);
        bmpBytes = fullBmp;
        return true;
    }

    private static void UpsertDefinition(
        string definitionsPath,
        string mappedName,
        string textureFileName,
        int imageWidth,
        int imageHeight,
        int textureWidth,
        int textureHeight)
    {
        var directory = Path.GetDirectoryName(definitionsPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var existing = File.Exists(definitionsPath) ? File.ReadAllText(definitionsPath) : null;
        var block = BuildDefinitionBlock(mappedName, textureFileName, imageWidth, imageHeight, textureWidth, textureHeight);
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
