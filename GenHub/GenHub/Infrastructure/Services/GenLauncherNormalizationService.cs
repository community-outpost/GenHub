using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;

namespace GenHub.Infrastructure.Services;

/// <summary>
/// Service for detecting and normalizing GenLauncher file modifications.
/// </summary>
/// <param name="logger">Logger instance.</param>
public class GenLauncherNormalizationService(ILogger<GenLauncherNormalizationService> logger) : IGenLauncherNormalizationService
{
    private static readonly string[] GibExtensions = [".gib"];
    private static readonly string[] SuffixesToRemove = [".GLR", ".GOF", ".GLTC"];

    /// <inheritdoc/>
    public async Task<GenLauncherDetectionResult> DetectGenLauncherFilesAsync(string directoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath, nameof(directoryPath));

        logger.LogInformation("Detecting GenLauncher files in directory: {DirectoryPath}", directoryPath);

        var result = new GenLauncherDetectionResult();

        if (!Directory.Exists(directoryPath))
        {
            logger.LogWarning("Directory does not exist: {DirectoryPath}", directoryPath);
            return result;
        }

        try
        {
            await Task.Run(() =>
            {
                var files = Directory.GetFiles(directoryPath, "*.*", SearchOption.AllDirectories);

                foreach (var file in files)
                {
                    var fileInfo = new FileInfo(file);

                    // Check for symbolic links
                    if (fileInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    {
                        result.SymbolicLinks.Add(file);
                        logger.LogDebug("Detected symbolic link: {FilePath}", file);
                        continue;
                    }

                    var fileName = fileInfo.Name;
                    var extension = fileInfo.Extension.ToLowerInvariant();

                    // Check for .gib files
                    if (GibExtensions.Contains(extension))
                    {
                        result.GibFiles.Add(file);
                        logger.LogDebug("Detected .gib file: {FilePath}", file);
                    }

                    // Check for suffix files
                    if (fileName.EndsWith(".GLR", StringComparison.OrdinalIgnoreCase))
                    {
                        result.GlrFiles.Add(file);
                        logger.LogDebug("Detected .GLR file: {FilePath}", file);
                    }
                    else if (fileName.EndsWith(".GOF", StringComparison.OrdinalIgnoreCase))
                    {
                        result.GofFiles.Add(file);
                        logger.LogDebug("Detected .GOF file: {FilePath}", file);
                    }
                    else if (fileName.EndsWith(".GLTC", StringComparison.OrdinalIgnoreCase))
                    {
                        result.GltcFiles.Add(file);
                        logger.LogDebug("Detected .GLTC file: {FilePath}", file);
                    }
                }

                result.HasGenLauncherFiles = result.TotalAffectedFiles > 0;
            }).ConfigureAwait(false);

            logger.LogInformation(
                "Detection complete. Found {TotalCount} GenLauncher files: {Summary}",
                result.TotalAffectedFiles,
                result.GetSummary());

            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error detecting GenLauncher files in directory: {DirectoryPath}", directoryPath);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<GenLauncherNormalizationResult>> NormalizeFilesAsync(
        string directoryPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath, nameof(directoryPath));

        logger.LogInformation("Starting GenLauncher file normalization in directory: {DirectoryPath}", directoryPath);

        if (!Directory.Exists(directoryPath))
        {
            logger.LogError("Directory does not exist: {DirectoryPath}", directoryPath);
            return OperationResult<GenLauncherNormalizationResult>.CreateFailure($"Directory does not exist: {directoryPath}");
        }

        var result = new GenLauncherNormalizationResult();

        try
        {
            // First, detect all files that need normalization
            var detection = await DetectGenLauncherFilesAsync(directoryPath).ConfigureAwait(false);

            if (!detection.HasGenLauncherFiles)
            {
                logger.LogInformation("No GenLauncher files detected. Nothing to normalize.");
                return OperationResult<GenLauncherNormalizationResult>.CreateSuccess(result);
            }

            // Remove symbolic links
            foreach (var symlink in detection.SymbolicLinks)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    File.Delete(symlink);
                    result.SymbolicLinksRemoved++;
                    logger.LogInformation("Removed symbolic link: {FilePath}", symlink);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to remove symbolic link: {FilePath}", symlink);
                    result.FailedFiles.Add(symlink);
                }
            }

            // Convert .gib files to .big
            foreach (var gibFile in detection.GibFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var bigFile = Path.ChangeExtension(gibFile, ".big");
                    File.Move(gibFile, bigFile, overwrite: true);
                    result.NormalizedCount++;
                    logger.LogInformation("Converted {GibFile} to {BigFile}", gibFile, bigFile);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to convert .gib file: {FilePath}", gibFile);
                    result.FailedFiles.Add(gibFile);
                }
            }

            // Remove suffixes from .GLR, .GOF, .GLTC files
            var suffixFiles = detection.GlrFiles
                .Concat(detection.GofFiles)
                .Concat(detection.GltcFiles)
                .ToList();

            foreach (var suffixFile in suffixFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var normalizedName = RemoveSuffix(suffixFile);
                    if (normalizedName != suffixFile)
                    {
                        File.Move(suffixFile, normalizedName, overwrite: true);
                        result.NormalizedCount++;
                        logger.LogInformation("Normalized {OriginalFile} to {NormalizedFile}", suffixFile, normalizedName);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to normalize file: {FilePath}", suffixFile);
                    result.FailedFiles.Add(suffixFile);
                }
            }

            logger.LogInformation(
                "Normalization complete. Normalized: {NormalizedCount}, Symlinks removed: {SymlinksRemoved}, Failed: {FailedCount}",
                result.NormalizedCount,
                result.SymbolicLinksRemoved,
                result.FailedFiles.Count);

            return OperationResult<GenLauncherNormalizationResult>.CreateSuccess(result);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Normalization operation was cancelled.");
            return OperationResult<GenLauncherNormalizationResult>.CreateFailure("Operation was cancelled.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during GenLauncher file normalization in directory: {DirectoryPath}", directoryPath);
            return OperationResult<GenLauncherNormalizationResult>.CreateFailure($"Normalization failed: {ex.Message}");
        }
    }

    private static string RemoveSuffix(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        var directory = Path.GetDirectoryName(filePath) ?? string.Empty;

        foreach (var suffix in SuffixesToRemove)
        {
            if (fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                var newFileName = fileName[..^suffix.Length];
                return Path.Combine(directory, newFileName);
            }
        }

        return filePath;
    }
}
