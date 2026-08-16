using System;
using System.Collections.Generic;
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
public sealed class FileHashRegistryService : IFileHashRegistryService
{
    private readonly Dictionary<string, string> _hashRegistry = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<FileHashRegistryService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileHashRegistryService"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    public FileHashRegistryService(ILogger<FileHashRegistryService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task LoadRegistryAsync(string csvPath, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(csvPath))
            {
                _logger.LogDebug("Hash registry file not found at {CsvPath}", csvPath);
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

            _logger.LogInformation("Loaded {Count} hash entries from registry", _hashRegistry.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load hash registry from {CsvPath}", csvPath);
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
