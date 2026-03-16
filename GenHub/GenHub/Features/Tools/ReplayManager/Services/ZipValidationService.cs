using GenHub.Core.Constants;
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using GenHub.Core.Interfaces.Tools.ReplayManager;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Tools.ReplayManager.Services;

/// <summary>
/// Implementation of <see cref="IZipValidationService"/> for validating ZIP archives.
/// </summary>
public sealed class ZipValidationService(ILogger<ZipValidationService> logger) : IZipValidationService
{
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
            if (archive.Entries.Count == 0)
            {
                return (false, "ZIP archive is empty.");
            }

            foreach (var entry in archive.Entries)
            {
                // Check for directories (Name is empty for directory entries)
                if (string.IsNullOrEmpty(entry.Name))
                {
                    return (false, "ZIP contains directories. Only a single layer of files is allowed.");
                }

                // Check for nested files (FullName should equal Name for root files)
                // Normalize slashes just in case
                var normalizedFullName = entry.FullName.Replace('\\', '/');
                if (normalizedFullName != entry.Name)
                {
                    return (false, $"ZIP contains nested files ({entry.FullName}). Only a single layer of files is allowed.");
                }

                // Check extension
                if (!entry.Name.EndsWith(".rep", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, $"ZIP contains non-replay file: {entry.Name}. Only .rep files are allowed.");
                }

                // Check size
                if (entry.Length > ReplayManagerConstants.MaxReplaySizeBytes)
                {
                    return (false, $"File {entry.Name} in ZIP exceeds 1 MB limit.");
                }
            }

            return (true, null);
        }
        catch (InvalidDataException)
        {
            return (false, "The file is not a valid ZIP archive.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error validating ZIP: {Path}", zipPath);
            return (false, $"Validation error: {ex.Message}");
        }
    }
}