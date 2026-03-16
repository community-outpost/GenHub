using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Tools.ReplayManager;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Tools.ReplayManager;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Tools.ReplayManager.Services;

/// <summary>
/// Service for saving replay files with metadata-based naming.
/// </summary>
/// <param name="directoryService">The directory service.</param>
/// <param name="parserService">The parser service.</param>
/// <param name="logger">The logger instance.</param>
public sealed class ReplaySaveService(
    IReplayDirectoryService directoryService,
    ReplayParserService parserService,
    ILogger<ReplaySaveService> logger)
{
    /// <summary>
    /// Saves a replay file to the Saved directory with metadata-based naming.
    /// </summary>
    /// <param name="sourceFilePath">The source replay file path.</param>
    /// <param name="gameType">The game type.</param>
    /// <returns>A tuple of the saved file path and parsed metadata, or nulls if save failed.</returns>
    public async Task<(string? SavedFilePath, ReplayMetadata? Metadata)> SaveReplayAsync(string sourceFilePath, GameType gameType)
    {
        try
        {
            if (!File.Exists(sourceFilePath))
            {
                logger.LogWarning("Source replay file not found: {FilePath}", sourceFilePath);
                return (null, null);
            }

            var fileInfo = new FileInfo(sourceFilePath);
            if (fileInfo.Length < ReplayManagerConstants.MinimumReplayFileSizeBytes)
            {
                logger.LogWarning("Replay file too small to save: {FilePath} ({Size} bytes)", sourceFilePath, fileInfo.Length);
                return (null, null);
            }

            var metadata = await parserService.ParseReplayAsync(sourceFilePath, gameType);

            var replayDirectory = directoryService.GetReplayDirectory(gameType);
            var savedDirectory = Path.Combine(replayDirectory, ReplayManagerConstants.SavedReplaysDirectoryName);

            Directory.CreateDirectory(savedDirectory);

            var fileName = GenerateFileName(metadata);
            var destinationPath = Path.Combine(savedDirectory, fileName);

            destinationPath = CopyWithRetry(sourceFilePath, destinationPath);

            logger.LogInformation("Saved replay: {Source} -> {Destination}", sourceFilePath, destinationPath);

            return (destinationPath, metadata);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error saving replay file: {FilePath}", sourceFilePath);
            return (null, null);
        }
    }

    private static string GenerateFileName(ReplayMetadata metadata)
    {
        var timestamp = metadata.GameDate ?? DateTime.Now;
        var dateString = timestamp.ToString(ReplayManagerConstants.SavedReplayDateFormat, CultureInfo.InvariantCulture);

        var components = new List<string> { dateString };

        if (metadata.IsParsed)
        {
            if (!string.IsNullOrWhiteSpace(metadata.MapName))
            {
                var sanitizedMapName = SanitizeFileName(metadata.MapName);
                if (!string.IsNullOrEmpty(sanitizedMapName))
                {
                    components.Add(sanitizedMapName);
                }
            }

            if (metadata.Players != null && metadata.Players.Count > 0)
            {
                var sanitizedNames = metadata.Players
                    .Take(ReplayManagerConstants.MaxPlayersInSavedFileName)
                    .Select(p => SanitizeFileName(p.Name))
                    .Where(name => !string.IsNullOrEmpty(name))
                    .ToList();
                var playerNames = string.Join("-", sanitizedNames);
                if (!string.IsNullOrWhiteSpace(playerNames))
                {
                    components.Add(playerNames);
                }
            }
        }

        var fileName = string.Join("_", components) + ".rep";

        // Reserve space for potential _counter suffix (e.g., _999 = 4 chars)
        var maxCounterLength = $"_{ReplayManagerConstants.MaxUniquePathAttempts}".Length;
        var maxBaseLength = ReplayManagerConstants.MaxFileNameLength - 4 - maxCounterLength; // 4 for ".rep"

        if (fileName.Length > ReplayManagerConstants.MaxFileNameLength)
        {
            var baseWithoutExtension = fileName[..^4]; // Remove ".rep"
            if (baseWithoutExtension.Length > maxBaseLength)
            {
                baseWithoutExtension = baseWithoutExtension[..maxBaseLength].TrimEnd('_', '-');
            }

            fileName = baseWithoutExtension + ".rep";
        }

        return fileName;
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = string.Concat(fileName.Where(c => !invalidChars.Contains(c)));

        sanitized = sanitized.Replace(' ', '_');

        while (sanitized.Contains("__", StringComparison.Ordinal))
        {
            sanitized = sanitized.Replace("__", "_");
        }

        return sanitized.Trim('_');
    }

    /// <summary>
    /// Copies a file to a unique destination path with retry logic to handle TOCTOU races.
    /// Uses atomic FileMode.CreateNew to avoid race conditions.
    /// </summary>
    private static string CopyWithRetry(string sourceFilePath, string destinationPath)
    {
        // Open source file once outside retry loop - if it fails, propagate immediately
        using var sourceStream = new FileStream(sourceFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);

        for (var attempt = 0; attempt < ReplayManagerConstants.SaveRetryMaxAttempts; attempt++)
        {
            var candidatePath = GetUniqueFilePath(destinationPath);
            try
            {
                // Use FileMode.CreateNew for atomic file creation (fails if file exists)
                using var destStream = new FileStream(candidatePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                sourceStream.Position = 0; // Reset stream position for retry attempts
                sourceStream.CopyTo(destStream);
                return candidatePath;
            }
            catch (IOException) when (attempt < ReplayManagerConstants.SaveRetryMaxAttempts - 1)
            {
                // Destination collision — retry with a new unique path
            }
            catch (IOException) when (attempt >= ReplayManagerConstants.SaveRetryMaxAttempts - 1)
            {
                throw new IOException(
                    $"Failed to copy replay file after {ReplayManagerConstants.SaveRetryMaxAttempts} attempts - destination path collision");
            }
            catch
            {
                // Clean up partially created destination file on non-IOException failures
                try
                {
                    if (File.Exists(candidatePath))
                    {
                        File.Delete(candidatePath);
                    }
                }
                catch
                {
                    // Ignore cleanup failures
                }

                throw;
            }
        }

        throw new InvalidOperationException("Retry loop exited unexpectedly");
    }

    private static string GetUniqueFilePath(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return filePath;
        }

        var directory = Path.GetDirectoryName(filePath) ?? string.Empty;
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);
        var extension = Path.GetExtension(filePath);

        var counter = 1;
        string uniquePath;

        do
        {
            uniquePath = Path.Combine(directory, $"{fileNameWithoutExtension}_{counter}{extension}");
            counter++;
        }
        while (File.Exists(uniquePath) && counter <= ReplayManagerConstants.MaxUniquePathAttempts);

        return uniquePath;
    }
}
