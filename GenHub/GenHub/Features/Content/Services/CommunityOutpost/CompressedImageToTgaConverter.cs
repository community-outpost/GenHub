using GenHub.Infrastructure.Imaging;
using ImageMagick;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Content.Services.CommunityOutpost;

/// <summary>
/// Converts compressed image files (AVIF, WebP) to TGA format for use with Command &amp; Conquer Generals/Zero Hour.
/// The game requires TGA textures, but GenPatcher dat archives contain AVIF and WebP files for compression.
/// GenPatcher's ConvertCompressedImageToTGA handles both .webp and .avif (see Util.ahk:206-269).
/// </summary>
public class CompressedImageToTgaConverter(ILogger<CompressedImageToTgaConverter> logger)
{
    private const string AvifExtension = ".avif";

    private static readonly string[] SupportedExtensions = [AvifExtension, ".webp"];

    /// <summary>
    /// Converts all supported compressed image files (AVIF, WebP) in a directory to TGA format.
    /// The original files are replaced with TGA files using the same base filename.
    /// </summary>
    /// <param name="directory">The directory containing image files.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of files converted.</returns>
    public async Task<int> ConvertDirectoryAsync(string directory, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(directory))
        {
            logger.LogWarning("Directory does not exist: {Directory}", directory);
            return 0;
        }

        try
        {
            var imageFiles = Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories)
                .Where(f => SupportedExtensions.Contains(
                    Path.GetExtension(f),
                    StringComparer.OrdinalIgnoreCase));

            int converted = 0;
            int totalFound = 0;

            foreach (var imageFile in imageFiles)
            {
                totalFound++;
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                try
                {
                    var tgaFile = Path.ChangeExtension(imageFile, ".tga");
                    await ConvertFileAsync(imageFile, tgaFile, cancellationToken);

                    // Delete the original file only if TGA exists and has content
                    var tgaInfo = new FileInfo(tgaFile);
                    if (tgaInfo.Exists && tgaInfo.Length > 0)
                    {
                        File.Delete(imageFile);
                        converted++;
                        logger.LogDebug("Converted {SourceFile} to {TgaFile}", imageFile, tgaFile);
                    }
                    else
                    {
                        logger.LogWarning("Conversion produced no output for {SourceFile}", imageFile);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to convert {SourceFile}", imageFile);
                }
            }

            logger.LogInformation(
                "Successfully converted {Converted} of {Total} compressed image files to TGA in {Directory}",
                converted,
                totalFound,
                directory);

            return converted;
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogError(ex, "Access denied to directory or subdirectories: {Directory}", directory);
            return 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to enumerate files in directory: {Directory}", directory);
            return 0;
        }
    }

    /// <summary>
    /// Converts a single compressed image file (AVIF or WebP) to TGA format.
    /// The decoder is chosen from the file extension, so a <c>.avif</c> path must hold AVIF data
    /// and any other path is decoded as WebP.
    /// </summary>
    /// <param name="sourcePath">The path to the source image file.</param>
    /// <param name="destinationPath">The path for the output TGA file.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task ConvertFileAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default)
    {
        await Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                var readSettings = new MagickReadSettings
                {
                    Format = IsAvif(sourcePath) ? MagickFormat.Avif : MagickFormat.WebP,
                };

                using var image = new MagickImage(sourcePath, readSettings);

                var destDir = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                {
                    Directory.CreateDirectory(destDir);
                }

                cancellationToken.ThrowIfCancellationRequested();

                // GenPatcher writes the same uncompressed 32-bit TGA via nconvert.exe -c 1.
                TgaWriter.Write32BitUncompressed(image, destinationPath);
            },
            cancellationToken);
    }

    private static bool IsAvif(string path) =>
        Path.GetExtension(path).Equals(AvifExtension, StringComparison.OrdinalIgnoreCase);
}
