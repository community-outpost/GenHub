using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Security;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using Microsoft.Extensions.Logging;

namespace GenHub.Common.Services;

/// <summary>
/// Determines whether GenHub can create storage at a filesystem location by writing a probe file.
/// </summary>
public class StorageWritabilityProbe(ILogger<StorageWritabilityProbe> logger) : IStorageWritabilityProbe
{
    private readonly ConcurrentDictionary<string, bool> _results = new(PathHelper.PathComparer);

    /// <inheritdoc/>
    public bool CanCreateStorageAt(string storagePath)
    {
        if (string.IsNullOrWhiteSpace(storagePath))
        {
            return false;
        }

        string fullStoragePath;

        try
        {
            fullStoragePath = Path.GetFullPath(storagePath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or SecurityException or IOException)
        {
            logger.LogDebug(ex, "Storage path {StoragePath} could not be resolved", storagePath);
            return false;
        }

        return _results.GetOrAdd(fullStoragePath, Probe);
    }

    /// <inheritdoc/>
    public void Invalidate(string? storagePath = null)
    {
        if (string.IsNullOrWhiteSpace(storagePath))
        {
            _results.Clear();
            return;
        }

        try
        {
            _results.TryRemove(Path.GetFullPath(storagePath), out _);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or SecurityException or IOException)
        {
            logger.LogDebug(ex, "Storage path {StoragePath} could not be resolved for invalidation", storagePath);
        }
    }

    private bool Probe(string fullStoragePath)
    {
        string? probePath = null;
        var storageDirectoryExisted = Directory.Exists(fullStoragePath);
        var storageDirectoryCreated = false;
        var probeSucceeded = false;

        try
        {
            Directory.CreateDirectory(fullStoragePath);
            storageDirectoryCreated = !storageDirectoryExisted;

            probePath = Path.Combine(
                fullStoragePath,
                $"{StorageConstants.WriteProbeFilePrefix}{Guid.NewGuid():N}.tmp");
            using var probe = new FileStream(
                probePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1,
                FileOptions.DeleteOnClose);
            probe.WriteByte(0);
            probeSucceeded = true;
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or ArgumentException or NotSupportedException or SecurityException)
        {
            logger.LogDebug(ex, "Storage path {StoragePath} is not writable", fullStoragePath);
            return false;
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(probePath) && File.Exists(probePath))
            {
                try
                {
                    File.Delete(probePath);
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
                {
                    logger.LogDebug(ex, "Could not remove storage write probe {ProbePath}", probePath);
                }
            }

            if (!probeSucceeded && storageDirectoryCreated)
            {
                try
                {
                    if (Directory.Exists(fullStoragePath) &&
                        !Directory.EnumerateFileSystemEntries(fullStoragePath).Any())
                    {
                        Directory.Delete(fullStoragePath);
                    }
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or SecurityException)
                {
                    logger.LogDebug(ex, "Could not remove failed storage probe directory {StoragePath}", fullStoragePath);
                }
            }
        }
    }
}
