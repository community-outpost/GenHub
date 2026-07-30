using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
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
    private static readonly HashSet<string> GibExtensions = [GenLauncherConstants.GibExtension];
    private static readonly HashSet<string> SuffixesToRemove =
    [
        GenLauncherConstants.ReplaceSuffix,
        GenLauncherConstants.OriginalFileSuffix,
        GenLauncherConstants.TempCopySuffix,
    ];

    /// <inheritdoc/>
    public async Task<GenLauncherDetectionResult> DetectGenLauncherFilesAsync(string directoryPath, CancellationToken cancellationToken = default)
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
                // Manual recursion to avoid following directory symlinks
                ScanDirectory(directoryPath, result, cancellationToken);

                result.HasGenLauncherFiles = result.TotalAffectedFiles > 0;
            }, cancellationToken).ConfigureAwait(false);

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
            var detection = await DetectGenLauncherFilesAsync(directoryPath, cancellationToken).ConfigureAwait(false);

            if (!detection.HasGenLauncherFiles)
            {
                logger.LogInformation("No GenLauncher files detected. Nothing to normalize.");
                return OperationResult<GenLauncherNormalizationResult>.CreateSuccess(result);
            }

            // Remove symbolic links (both files and directories, including dangling links)
            foreach (var symlink in detection.SymbolicLinks)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var attributes = File.GetAttributes(symlink);

                    // Check if it's a directory symlink using reparse-point metadata rather than target existence
                    if (attributes.HasFlag(FileAttributes.Directory))
                    {
                        Directory.Delete(symlink);
                        result.SymbolicLinksRemoved++;
                        logger.LogInformation("Removed directory symbolic link: {DirectoryPath}", symlink);
                    }
                    else
                    {
                        File.Delete(symlink);
                        result.SymbolicLinksRemoved++;
                        logger.LogInformation("Removed file symbolic link: {FilePath}", symlink);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to remove symbolic link: {FilePath}", symlink);
                    result.FailedFiles.Add(symlink);
                }
            }

            // Remove suffixes from .GLR, .GOF, .GLTC files and directories.
            var suffixItems = detection.GlrFiles
                .Concat(detection.GofFiles)
                .Concat(detection.GltcFiles)
                .ToList();

            // Process files first, then directories (deepest path first to handle nested suffix directories cleanly)
            var suffixFiles = suffixItems.Where(File.Exists).ToList();
            var suffixDirectories = suffixItems.Where(Directory.Exists).OrderByDescending(d => d.Length).ToList();

            foreach (var suffixFile in suffixFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var normalizedName = RemoveSuffix(suffixFile);
                    if (normalizedName != suffixFile)
                    {
                        // Check if destination exists (file or directory) - skip to avoid data loss
                        if (File.Exists(normalizedName) || Directory.Exists(normalizedName))
                        {
                            logger.LogWarning(
                                "Skipping suffix removal for {OriginalFile}: target {NormalizedFile} already exists. Manual resolution required.",
                                suffixFile,
                                normalizedName);
                            result.FailedFiles.Add(suffixFile);
                            continue;
                        }

                        File.Move(suffixFile, normalizedName);
                        result.NormalizedCount++;
                        logger.LogInformation("Normalized {OriginalFile} to {NormalizedFile}", suffixFile, normalizedName);

                        if (Path.GetExtension(normalizedName).Equals(GenLauncherConstants.GibExtension, StringComparison.OrdinalIgnoreCase))
                        {
                            TryConvertGibToBig(normalizedName, result);
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to normalize file: {FilePath}", suffixFile);
                    result.FailedFiles.Add(suffixFile);
                }
            }

            // Convert standalone .gib files to .big before directory moves so file paths remain valid during conversion
            foreach (var gibFile in detection.GibFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                TryConvertGibToBig(gibFile, result);
            }

            // Normalize matching directories after file conversions
            foreach (var suffixDir in suffixDirectories)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var normalizedName = RemoveSuffix(suffixDir);
                    if (normalizedName != suffixDir)
                    {
                        // Check if destination exists (directory or file) - skip to avoid data loss
                        if (Directory.Exists(normalizedName) || File.Exists(normalizedName))
                        {
                            logger.LogWarning(
                                "Skipping suffix removal for directory {OriginalDir}: target {NormalizedDir} already exists. Manual resolution required.",
                                suffixDir,
                                normalizedName);
                            result.FailedFiles.Add(suffixDir);
                            continue;
                        }

                        Directory.Move(suffixDir, normalizedName);
                        result.NormalizedCount++;
                        logger.LogInformation("Normalized directory {OriginalDir} to {NormalizedDir}", suffixDir, normalizedName);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to normalize directory: {DirectoryPath}", suffixDir);
                    result.FailedFiles.Add(suffixDir);
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

    private void ScanDirectory(string directoryPath, GenLauncherDetectionResult result, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Scan files in current directory only (non-recursive)
        foreach (var file in Directory.EnumerateFiles(directoryPath, "*.*", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();

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
            if (fileName.EndsWith(GenLauncherConstants.ReplaceSuffix, StringComparison.OrdinalIgnoreCase))
            {
                result.GlrFiles.Add(file);
                logger.LogDebug("Detected .GLR file: {FilePath}", file);
            }
            else if (fileName.EndsWith(GenLauncherConstants.OriginalFileSuffix, StringComparison.OrdinalIgnoreCase))
            {
                result.GofFiles.Add(file);
                logger.LogDebug("Detected .GOF file: {FilePath}", file);
            }
            else if (fileName.EndsWith(GenLauncherConstants.TempCopySuffix, StringComparison.OrdinalIgnoreCase))
            {
                result.GltcFiles.Add(file);
                logger.LogDebug("Detected .GLTC file: {FilePath}", file);
            }
        }

        // Scan subdirectories (non-recursive, manual control)
        foreach (var directory in Directory.EnumerateDirectories(directoryPath, "*", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var dirInfo = new DirectoryInfo(directory);

            // Check if it's a directory symlink
            if (dirInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                result.SymbolicLinks.Add(directory);
                logger.LogDebug("Detected directory symbolic link: {DirectoryPath}", directory);

                // Don't recurse into symlinked directories
                continue;
            }

            var dirName = dirInfo.Name;
            if (dirName.EndsWith(GenLauncherConstants.ReplaceSuffix, StringComparison.OrdinalIgnoreCase))
            {
                result.GlrFiles.Add(directory);
                logger.LogDebug("Detected .GLR directory: {DirectoryPath}", directory);
            }
            else if (dirName.EndsWith(GenLauncherConstants.OriginalFileSuffix, StringComparison.OrdinalIgnoreCase))
            {
                result.GofFiles.Add(directory);
                logger.LogDebug("Detected .GOF directory: {DirectoryPath}", directory);
            }
            else if (dirName.EndsWith(GenLauncherConstants.TempCopySuffix, StringComparison.OrdinalIgnoreCase))
            {
                result.GltcFiles.Add(directory);
                logger.LogDebug("Detected .GLTC directory: {DirectoryPath}", directory);
            }

            // Recurse into normal directories
            ScanDirectory(directory, result, cancellationToken);
        }
    }

    private void TryConvertGibToBig(string gibFile, GenLauncherNormalizationResult result)
    {
        if (!File.Exists(gibFile))
        {
            return;
        }

        try
        {
            var bigFile = Path.ChangeExtension(gibFile, GenLauncherConstants.BigExtension);

            // Check if destination exists - skip to avoid data loss
            if (File.Exists(bigFile))
            {
                logger.LogWarning(
                    "Skipping .gib → .big conversion for {GibFile}: target {BigFile} already exists. Manual resolution required.",
                    gibFile,
                    bigFile);
                result.FailedFiles.Add(gibFile);
                return;
            }

            File.Move(gibFile, bigFile);
            result.NormalizedCount++;
            logger.LogInformation("Converted {GibFile} to {BigFile}", gibFile, bigFile);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to convert .gib file: {FilePath}", gibFile);
            result.FailedFiles.Add(gibFile);
        }
    }
}
