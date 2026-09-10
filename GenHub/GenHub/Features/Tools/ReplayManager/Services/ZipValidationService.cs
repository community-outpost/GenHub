using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Tools.ReplayManager;
using Microsoft.Extensions.Logging;
using SharpCompress.Archives;

namespace GenHub.Features.Tools.ReplayManager.Services;

/// <summary>
/// Implementation of <see cref="IZipValidationService"/> for validating ZIP and other archives for replays.
/// </summary>
public sealed class ZipValidationService(ILogger<ZipValidationService> logger) : IZipValidationService
{
    private static readonly char[] PathSeparators = ['/', '\\'];

    /// <inheritdoc />
    public (bool IsValid, string? ErrorMessage) ValidateZip(string zipPath)
    {
        try
        {
            if (!File.Exists(zipPath))
            {
                return (false, "ZIP file does not exist.");
            }

            using var archive = ZipFile.OpenRead(zipPath);
            var nonDirEntries = archive.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToList();
            if (nonDirEntries.Count == 0)
            {
                return (false, "ZIP archive is empty.");
            }

            if (nonDirEntries.Count > ReplayManagerConstants.MaxZipEntries)
            {
                return (false, $"ZIP contains too many entries ({nonDirEntries.Count} > {ReplayManagerConstants.MaxZipEntries}).");
            }

            long totalUncompressedBytes = 0;
            int replayCount = 0;

            foreach (var entry in nonDirEntries)
            {
                var segments = entry.FullName.Split(PathSeparators, StringSplitOptions.RemoveEmptyEntries);
                if (segments.Any(s => s == "." || s == ".." || s.Contains(':') || Path.IsPathRooted(s)))
                {
                    return (false, $"ZIP contains invalid path traversal segment in '{entry.FullName}'.");
                }

                var fileName = Path.GetFileName(entry.FullName);

                // Check extension
                if (!fileName.EndsWith(".rep", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, $"ZIP contains non-replay file: {fileName}. Only .rep files are allowed.");
                }

                replayCount++;

                // Check single entry size
                if (entry.Length > ReplayManagerConstants.MaxReplaySizeBytes)
                {
                    return (false, $"File {fileName} in ZIP exceeds {ReplayManagerConstants.MaxReplaySizeBytes / ConversionConstants.BytesPerMegabyte} MB limit.");
                }

                // Check compression ratio
                if (entry.CompressedLength > 0 &&
                    ((double)entry.Length / entry.CompressedLength) > ReplayManagerConstants.MaxCompressionRatio)
                {
                    return (false, $"File {fileName} exceeds maximum compression ratio (potential zip bomb).");
                }

                totalUncompressedBytes += entry.Length;
                if (totalUncompressedBytes > ReplayManagerConstants.MaxAggregateUncompressedBytes)
                {
                    return (false, $"ZIP aggregate uncompressed size exceeds maximum allowed limit ({totalUncompressedBytes} > {ReplayManagerConstants.MaxAggregateUncompressedBytes} bytes).");
                }
            }

            if (replayCount == 0)
            {
                return (false, "ZIP archive contains no .rep replay files.");
            }

            return (true, null);
        }
        catch (InvalidDataException)
        {
            return ValidateWithSharpCompress(zipPath);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error validating ZIP: {Path}", zipPath);
            return (false, $"Validation error: {ex.Message}");
        }
    }

    private (bool IsValid, string? ErrorMessage) ValidateWithSharpCompress(string archivePath)
    {
        try
        {
            using var archive = ArchiveFactory.OpenArchive(archivePath);
            var nonDirEntries = archive.Entries.Where(e => !e.IsDirectory && !string.IsNullOrEmpty(e.Key)).ToList();
            if (nonDirEntries.Count == 0)
            {
                return (false, "Archive is empty.");
            }

            if (nonDirEntries.Count > ReplayManagerConstants.MaxZipEntries)
            {
                return (false, $"Archive contains too many entries ({nonDirEntries.Count} > {ReplayManagerConstants.MaxZipEntries}).");
            }

            long totalUncompressedBytes = 0;
            int replayCount = 0;

            foreach (var entry in nonDirEntries)
            {
                var entryKey = entry.Key ?? string.Empty;
                var segments = entryKey.Split(PathSeparators, StringSplitOptions.RemoveEmptyEntries);
                if (segments.Any(s => s == "." || s == ".." || s.Contains(':') || Path.IsPathRooted(s)))
                {
                    return (false, $"Archive contains invalid path traversal segment in '{entryKey}'.");
                }

                var fileName = Path.GetFileName(entryKey);
                if (!fileName.EndsWith(".rep", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, $"Archive contains non-replay file: {fileName}. Only .rep files are allowed.");
                }

                replayCount++;

                if (entry.Size > ReplayManagerConstants.MaxReplaySizeBytes)
                {
                    return (false, $"File {fileName} in archive exceeds {ReplayManagerConstants.MaxReplaySizeBytes / ConversionConstants.BytesPerMegabyte} MB limit.");
                }

                totalUncompressedBytes += entry.Size;
                if (totalUncompressedBytes > ReplayManagerConstants.MaxAggregateUncompressedBytes)
                {
                    return (false, $"Archive aggregate uncompressed size exceeds maximum allowed limit ({totalUncompressedBytes} > {ReplayManagerConstants.MaxAggregateUncompressedBytes} bytes).");
                }
            }

            if (replayCount == 0)
            {
                return (false, "Archive contains no .rep replay files.");
            }

            return (true, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SharpCompress failed to validate archive: {Path}", archivePath);
            return (false, "The file is not a valid archive.");
        }
    }
}
