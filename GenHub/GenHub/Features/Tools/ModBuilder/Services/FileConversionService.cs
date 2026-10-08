using System.Globalization;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Tools.ModBuilder;
using GenHub.Core.Models.Results.ModBuilder;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.ModBuilder.Services;

/// <summary>
/// Service for coordinating file conversions across different formats.
/// </summary>
public sealed class FileConversionService(
    IImageConversionService imageConversionService,
    IStringTableConversionService stringTableConversionService,
    ITextProcessingService textProcessingService,
    IExternalToolService externalToolService,
    ILogger<FileConversionService> logger) : IFileConversionService
{
    /// <inheritdoc />
    public async Task<ConversionOperationResult> ConvertFileAsync(
        string sourcePath,
        string destinationPath,
        string? conversionType = null,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(0.0);

        try
        {
            logger.LogInformation("Converting file: {Source} -> {Destination}", sourcePath, destinationPath);

            if (!File.Exists(sourcePath))
            {
                return ConversionOperationResult.CreateFailure($"Source file not found: {sourcePath}");
            }

            // Determine conversion type from file extensions if not provided
            var sourceExt = Path.GetExtension(sourcePath).ToLowerInvariant();
            var targetExt = Path.GetExtension(destinationPath).ToLowerInvariant();

            // Route to appropriate conversion service based on file type
            ConversionOperationResult result;

            if (IsImageSource(sourceExt) && IsImageTarget(targetExt))
            {
                result = await ConvertImageAsync(sourcePath, destinationPath, progress, cancellationToken)
                    .ConfigureAwait(false);
            }
            else if ((sourceExt == ModBuilderConstants.FileExtensions.Str && targetExt == ModBuilderConstants.FileExtensions.Csf) ||
                     (sourceExt == ModBuilderConstants.FileExtensions.Csf && targetExt == ModBuilderConstants.FileExtensions.Str))
            {
                result = await ConvertStringTableAsync(sourcePath, destinationPath, progress, cancellationToken)
                    .ConfigureAwait(false);
            }
            else if (sourceExt == ModBuilderConstants.FileExtensions.Blend)
            {
                result = await ExecuteBlenderConversionAsync(sourcePath, destinationPath, progress, cancellationToken)
                    .ConfigureAwait(false);
            }
            else if (sourceExt is ModBuilderConstants.FileExtensions.Ini or ModBuilderConstants.FileExtensions.Txt)
            {
                result = await ProcessTextFileAsync(sourcePath, destinationPath, progress, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                // Direct copy for same extension or unsupported conversions
                result = await CopyFileAsync(sourcePath, destinationPath, progress, cancellationToken)
                    .ConfigureAwait(false);
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "File conversion failed");
            return ConversionOperationResult.CreateFailure(ex.Message);
        }
    }

    /// <summary>
    /// Checks if the source extension is a supported image format.
    /// </summary>
    private static bool IsImageSource(string extension)
    {
        return extension is ModBuilderConstants.FileExtensions.Psd
            or ModBuilderConstants.FileExtensions.Tga
            or ModBuilderConstants.FileExtensions.Tiff
            or ModBuilderConstants.FileExtensions.Tif
            or ModBuilderConstants.FileExtensions.Dds
            or ModBuilderConstants.FileExtensions.Bmp;
    }

    /// <summary>
    /// Checks if the target extension is an image format.
    /// </summary>
    private static bool IsImageTarget(string extension)
    {
        return extension is ModBuilderConstants.FileExtensions.Dds
            or ModBuilderConstants.FileExtensions.Tga
            or ModBuilderConstants.FileExtensions.Bmp
            or ModBuilderConstants.FileExtensions.Tiff
            or ModBuilderConstants.FileExtensions.Tif
            or ModBuilderConstants.FileExtensions.Png
            or ModBuilderConstants.FileExtensions.Jpg
            or ModBuilderConstants.FileExtensions.Jpeg;
    }

    /// <summary>
    /// Converts an image file using the image conversion service.
    /// </summary>
    private async Task<ConversionOperationResult> ConvertImageAsync(
        string sourcePath,
        string destinationPath,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(0.1);

        var success = await imageConversionService.ConvertImageAsync(
            sourcePath,
            destinationPath,
            parameters: null,
            cancellationToken)
            .ConfigureAwait(false);

        progress?.Report(1.0);

        return success
            ? ConversionOperationResult.CreateSuccess()
            : ConversionOperationResult.CreateFailure("Image conversion failed");
    }

    /// <summary>
    /// Converts a string table file using the string table conversion service.
    /// </summary>
    private async Task<ConversionOperationResult> ConvertStringTableAsync(
        string sourcePath,
        string destinationPath,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(0.1);

        var sourceExt = Path.GetExtension(sourcePath).ToLowerInvariant();
        var result = sourceExt == ".str"
            ? await stringTableConversionService.ConvertStrToCsfAsync(
                sourcePath,
                destinationPath,
                cancellationToken: cancellationToken)
                .ConfigureAwait(false)
            : await stringTableConversionService.ConvertCsfToStrAsync(
                sourcePath,
                destinationPath,
                cancellationToken: cancellationToken)
                .ConfigureAwait(false);

        progress?.Report(1.0);

        return result.Success
            ? ConversionOperationResult.CreateSuccess()
            : ConversionOperationResult.CreateFailure(result.FirstError ?? "String table conversion failed");
    }

    /// <summary>
    /// Executes Blender conversion using the external tool service.
    /// </summary>
    private async Task<ConversionOperationResult> ExecuteBlenderConversionAsync(
        string sourcePath,
        string destinationPath,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(0.1);

        logger.LogInformation("Executing Blender conversion: {Source} -> {Destination}", sourcePath, destinationPath);

        var blenderPath = ModBuilderConstants.BlenderExecutable;
        var arguments = string.Format(
            CultureInfo.InvariantCulture,
            ModBuilderConstants.BlenderExportArgumentFormat,
            sourcePath,
            destinationPath);

        var toolProgress = new Progress<string>(msg =>
        {
            logger.LogDebug("Blender: {Message}", msg);
        });

        var result = await externalToolService.ExecuteToolAsync(
            blenderPath,
            arguments,
            workingDirectory: Path.GetDirectoryName(sourcePath),
            progress: toolProgress,
            cancellationToken)
            .ConfigureAwait(false);

        progress?.Report(1.0);

        if (!result.Success)
        {
            return ConversionOperationResult.CreateFailure(result.Errors);
        }

        if (!File.Exists(destinationPath))
        {
            return ConversionOperationResult.CreateFailure(
                $"Blender conversion completed without producing the expected output file: \"{destinationPath}\"");
        }

        return ConversionOperationResult.CreateSuccess();
    }

    /// <summary>
    /// Processes a text file with optimizations.
    /// </summary>
    private async Task<ConversionOperationResult> ProcessTextFileAsync(
        string sourcePath,
        string destinationPath,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(0.1);

        try
        {
            // Read source file
            var content = await File.ReadAllTextAsync(sourcePath, cancellationToken)
                .ConfigureAwait(false);

            progress?.Report(0.3);

            // Process based on file type
            var sourceExt = Path.GetExtension(sourcePath).ToLowerInvariant();
            var processedContent = sourceExt == ".ini"
                ? await textProcessingService.OptimizeIniFileAsync(content, cancellationToken).ConfigureAwait(false)
                : await textProcessingService.NormalizeLineEndingsAsync(content, LineEndingType.CRLF, cancellationToken).ConfigureAwait(false);

            progress?.Report(0.7);

            // Ensure target directory exists
            var targetDir = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            // Write processed content
            await File.WriteAllTextAsync(destinationPath, processedContent, cancellationToken)
                .ConfigureAwait(false);

            progress?.Report(1.0);

            return ConversionOperationResult.CreateSuccess();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Text file processing failed");
            return ConversionOperationResult.CreateFailure(ex.Message);
        }
    }

    /// <summary>
    /// Copies a file directly without conversion.
    /// </summary>
    private static async Task<ConversionOperationResult> CopyFileAsync(
        string sourcePath,
        string destinationPath,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(0.1);

        if (string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(destinationPath), StringComparison.OrdinalIgnoreCase))
        {
            progress?.Report(1.0);
            return ConversionOperationResult.CreateSuccess();
        }

        // Ensure target directory exists
        var targetDir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
        {
            Directory.CreateDirectory(targetDir);
        }

        // Use async file copy with buffering for better performance
        await using var sourceStream = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            ModBuilderConstants.BuildFileBufferSize,
            useAsync: true);

        await using var destStream = new FileStream(
            destinationPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            ModBuilderConstants.BuildFileBufferSize,
            useAsync: true);

        await sourceStream.CopyToAsync(destStream, ModBuilderConstants.BuildFileBufferSize, cancellationToken)
            .ConfigureAwait(false);

        progress?.Report(1.0);

        return ConversionOperationResult.CreateSuccess();
    }

    /// <inheritdoc />
    public Task<ConversionOperationResult<bool>> ValidateConversionAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Check if source file exists
            if (!File.Exists(sourcePath))
            {
                return Task.FromResult(ConversionOperationResult<bool>.CreateFailure($"Source file not found: {sourcePath}"));
            }

            // Check if conversion is supported
            var sourceExt = Path.GetExtension(sourcePath).ToLowerInvariant();
            var targetExt = Path.GetExtension(destinationPath).ToLowerInvariant();

            var isSupported = sourceExt switch
            {
                _ when IsImageSource(sourceExt) => IsImageTarget(targetExt),
                ModBuilderConstants.FileExtensions.Str => targetExt == ModBuilderConstants.FileExtensions.Csf,
                ModBuilderConstants.FileExtensions.Csf => targetExt == ModBuilderConstants.FileExtensions.Str,
                ModBuilderConstants.FileExtensions.Blend => targetExt is ModBuilderConstants.FileExtensions.W3d or ModBuilderConstants.FileExtensions.Blend,
                _ => string.Equals(sourceExt, targetExt, StringComparison.OrdinalIgnoreCase)
            };

            return Task.FromResult(ConversionOperationResult<bool>.CreateSuccess(isSupported));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Validation failed");
            return Task.FromResult(ConversionOperationResult<bool>.CreateFailure(ex.Message));
        }
    }
}
