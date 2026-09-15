using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Tools.ModBuilder;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Tools.ModBuilder.Services;

/// <summary>
/// Manages file hash registry for skipping unchanged files.
/// Implements the FileHashRegistry optimization from Python ModBuilder (20-30% performance gain).
/// </summary>
public sealed class FileHashRegistryService(ILogger<FileHashRegistryService> logger) : IFileHashRegistryService
{
    private readonly ConcurrentDictionary<string, string> _hashRegistry = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public async Task LoadRegistryAsync(string csvPath, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(csvPath))
            {
                logger.LogDebug("Hash registry file not found at {CsvPath}", csvPath);
                return;
            }

            _hashRegistry.Clear();

            await using var stream = File.OpenRead(csvPath);
            using var reader = new StreamReader(stream);

            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var parts = line.Split(',');
                if (parts.Length >= 2)
                {
                    var fileName = parts[0].Trim().ToLowerInvariant();
                    var hash = parts[1].Trim().ToLowerInvariant();
                    _hashRegistry[fileName] = hash;
                }
            }

            logger.LogInformation("Loaded {Count} hash entries from registry", _hashRegistry.Count);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load hash registry from {CsvPath}", csvPath);
        }
    }

    /// <inheritdoc/>
    public bool IsFileIrrelevant(string filePath, string currentMd5)
    {
        if (_hashRegistry.Count == 0)
        {
            return false;
        }

        var normalizedPath = Path.GetFileName(filePath).ToLowerInvariant();
        return _hashRegistry.TryGetValue(normalizedPath, out var registryMd5)
            && registryMd5.Equals(currentMd5, StringComparison.OrdinalIgnoreCase);
    }
}
