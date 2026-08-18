using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using HeyRed.ImageSharp.Heif.Formats.Avif;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Tga;

namespace GenHub.Features.Content.Services.CommunityOutpost;

/// <summary>
/// Converts compressed image files (AVIF, WebP) to TGA format for use with Command &amp; Conquer Generals/Zero Hour.
/// The game requires TGA textures, but GenPatcher dat archives contain AVIF and WebP files for compression.
/// GenPatcher's ConvertCompressedImageToTGA handles both .webp and .avif (see Util.ahk:206-269).
/// </summary>
public class CompressedImageToTgaConverter(ILogger<CompressedImageToTgaConverter> logger)
{
    private const string AvifExtension = ".avif";
    private const int AvifCapabilityUnknown = 0;
    private const int AvifCapabilityAvailable = 1;
    private const int AvifCapabilityUnavailable = 2;

    private static readonly string[] SupportedExtensions = [AvifExtension, ".webp"];
    private static readonly SemaphoreSlim _avifCapabilityGate = new(1, 1);

    /// <summary>
    /// Remembers the availability discovered by the first AVIF decode.
    /// <para>
    /// AVIF decoding P/Invokes into libheif, supplied by LibHeif.Native. That package
    /// ships native assets for win-x64 and linux-x64 only (see the note on its
    /// PackageReference in GenHub.csproj); elsewhere it restores as an empty
    /// placeholder. Nothing detectable happens until the first decode, which then
    /// throws <see cref="DllNotFoundException"/>: constructing
    /// <see cref="AvifConfigurationModule"/> succeeds on every platform, so there is
    /// no meaningful way to probe up front. Attempting the operation and remembering
    /// the failure is both simpler and accurate — it also means a machine that
    /// happens to have libheif installed keeps working, which a hardcoded RID
    /// allowlist would wrongly deny.
    /// </para>
    /// <para>
    /// WebP is decoded by ImageSharp itself and needs no native library, so it keeps
    /// working everywhere. Only AVIF degrades.
    /// </para>
    /// </summary>
    private static int _avifCapabilityState;

    // Configure ImageSharp to support AVIF decoding (WebP is supported natively).
    private readonly Configuration _avifConfig = new(new AvifConfigurationModule());

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

            int skippedAvif = 0;

            foreach (var imageFile in imageFiles)
            {
                totalFound++;
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                if (IsAvif(imageFile) && IsAvifUnavailable())
                {
                    // A previous file already proved libheif is missing. Leaving the
                    // .avif in place is deliberate: the game cannot read it, but
                    // deleting it would destroy content the user could still convert
                    // on a platform that has the native library.
                    skippedAvif++;
                    continue;
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
                catch (PlatformNotSupportedException)
                {
                    // libheif is missing. The shared capability state is now unavailable,
                    // so every remaining .avif takes the skip path above instead of throwing.
                    skippedAvif++;
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

            if (skippedAvif > 0)
            {
                logger.LogWarning(
                    "Skipped {SkippedAvif} AVIF file(s) in {Directory}: AVIF decoding is unavailable on {Platform}. "
                    + "The textures were left in place and the content will be missing them in-game.",
                    skippedAvif,
                    directory,
                    RuntimeInformation.RuntimeIdentifier);
            }

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

                // AVIF requires a special configuration module; WebP is natively supported.
                var isAvif = IsAvif(sourcePath);
                if (isAvif && IsAvifUnavailable())
                {
                    throw AvifUnsupported(sourcePath);
                }

                var ownsCapabilityProbe = false;
                if (isAvif && Volatile.Read(ref _avifCapabilityState) == AvifCapabilityUnknown)
                {
                    _avifCapabilityGate.Wait(cancellationToken);
                    ownsCapabilityProbe = true;

                    if (IsAvifUnavailable())
                    {
                        _avifCapabilityGate.Release();
                        ownsCapabilityProbe = false;
                        throw AvifUnsupported(sourcePath);
                    }

                    if (Volatile.Read(ref _avifCapabilityState) == AvifCapabilityAvailable)
                    {
                        _avifCapabilityGate.Release();
                        ownsCapabilityProbe = false;
                    }
                }

                Image? image = null;
                try
                {
                    using var inputStream = File.OpenRead(sourcePath);
                    var decoderOptions = new DecoderOptions
                    {
                        Configuration = isAvif ? _avifConfig : Configuration.Default,
                    };

                    cancellationToken.ThrowIfCancellationRequested();
                    image = Image.Load(decoderOptions, inputStream);
                    if (isAvif)
                    {
                        Volatile.Write(ref _avifCapabilityState, AvifCapabilityAvailable);
                    }
                }
                catch (DllNotFoundException)
                {
                    // libheif is not present for this runtime. Remember it so the rest
                    // of the run skips AVIF instead of repeating the failure per file.
                    Volatile.Write(ref _avifCapabilityState, AvifCapabilityUnavailable);
                    throw AvifUnsupported(sourcePath);
                }
                finally
                {
                    if (ownsCapabilityProbe)
                    {
                        _avifCapabilityGate.Release();
                    }
                }

                using (image)
                {
                    // Create directory for output if it doesn't exist
                    var destDir = Path.GetDirectoryName(destinationPath);
                    if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                    {
                        Directory.CreateDirectory(destDir);
                    }

                    cancellationToken.ThrowIfCancellationRequested();

                    // Save as TGA with appropriate settings for Generals
                    // The game expects 32-bit BGRA TGA files without compression (TGA type 2)
                    // GenPatcher uses uncompressed TGA via nconvert.exe -c 1
                    var encoder = new TgaEncoder
                    {
                        BitsPerPixel = TgaBitsPerPixel.Pixel32,
                        Compression = TgaCompression.None,
                    };

                    image.SaveAsTga(destinationPath, encoder);
                }
            },
            cancellationToken);
    }

    private static bool IsAvif(string path) =>
        Path.GetExtension(path).Equals(AvifExtension, StringComparison.OrdinalIgnoreCase);

    private static bool IsAvifUnavailable() =>
        Volatile.Read(ref _avifCapabilityState) == AvifCapabilityUnavailable;

    private static PlatformNotSupportedException AvifUnsupported(string sourcePath) =>
        new($"Cannot convert '{sourcePath}': AVIF decoding needs the libheif native library, "
            + $"which is not available for {RuntimeInformation.RuntimeIdentifier}. "
            + "WebP conversion is unaffected.");
}
