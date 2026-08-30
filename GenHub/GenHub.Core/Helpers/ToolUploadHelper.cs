using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Models.Tools.MapManager;
using GenHub.Core.Models.Tools.ReplayManager;

namespace GenHub.Core.Helpers;

/// <summary>
/// Shared helper methods for upload workflows in tools (MapManager, ReplayManager).
/// </summary>
public static class ToolUploadHelper
{
    /// <summary>
    /// Formats the upload stage message based on progress percentage and archive mode.
    /// The percentage itself is rendered separately next to the progress bar.
    /// </summary>
    /// <param name="entityName">The entity name (e.g., "maps" or "replays").</param>
    /// <param name="isZip">Whether the upload is a single zip file.</param>
    /// <param name="percent">The completion percentage.</param>
    /// <returns>A formatted status string.</returns>
    public static string FormatUploadStageMessage(string entityName, bool isZip, int percent)
    {
        if (!isZip && percent < ToolConstants.UploadStageCompressionThresholdPercent)
        {
            return $"Compressing {entityName}...";
        }

        if (percent < ToolConstants.UploadStageCloudThresholdPercent)
        {
            return "Uploading to cloud...";
        }

        if (percent < ToolConstants.UploadStageCompletePercent)
        {
            return "Finalizing cloud upload...";
        }

        return "Upload complete!";
    }

    /// <summary>
    /// Formats the error message when upload rate limit is exceeded.
    /// </summary>
    /// <param name="totalSizeBytes">Total bytes of file being uploaded.</param>
    /// <param name="usedBytes">Used bytes in period.</param>
    /// <param name="limitBytes">Total allowed limit bytes.</param>
    /// <returns>A human-readable error description.</returns>
    public static string FormatUploadLimitExceededMessage(long totalSizeBytes, long usedBytes, long limitBytes)
    {
        var bytesPerMb = (double)ConversionConstants.BytesPerMegabyte;
        var remainingMb = Math.Max(0, (limitBytes - usedBytes) / bytesPerMb);
        var fileMb = totalSizeBytes / bytesPerMb;
        var limitMb = limitBytes / bytesPerMb;

        return $"Upload limit exceeded. You have {remainingMb:F1} MB remaining of your {limitMb:F0} MB limit. This file requires {fileMb:F1} MB.";
    }

    /// <summary>
    /// Computes the lowercase SHA256 hex string of a file if it exists.
    /// </summary>
    /// <param name="filePath">The absolute path to the file.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The lowercase hex string if successful; otherwise <see langword="null"/>.</returns>
    public static async Task<string?> ComputeFileSha256Async(string filePath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(filePath);
            var hashBytes = await System.Security.Cryptography.SHA256.HashDataAsync(stream, ct);
            return Convert.ToHexString(hashBytes).ToLowerInvariant();
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Verifies if a share URL is accessible and returns HTTP success.
    /// </summary>
    /// <param name="url">The URL to test.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><see langword="true"/> if the URL returns a success status code; otherwise <see langword="false"/>.</returns>
    public static async Task<bool> VerifyShareUrlAliveAsync(string url, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        try
        {
            using var httpClient = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Head, url);
            using var response = await httpClient.SendAsync(request, ct);
            return response.IsSuccessStatusCode;
        }
        catch (System.Net.Http.HttpRequestException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// Calculates the total size in bytes of a collection of map files including their directory assets.
    /// </summary>
    /// <param name="maps">The map files.</param>
    /// <returns>Total size in bytes.</returns>
    public static long CalculateMapsSize(IEnumerable<MapFile> maps)
    {
        long total = 0;
        foreach (var map in maps)
        {
            total += GetMapFileSize(map);
        }

        return total;
    }

    /// <summary>
    /// Calculates the total size in bytes of a collection of replay files.
    /// </summary>
    /// <param name="replays">The replay files.</param>
    /// <returns>Total size in bytes.</returns>
    public static long CalculateReplaysSize(IEnumerable<ReplayFile> replays)
    {
        long total = 0;
        foreach (var replay in replays.Where(r => File.Exists(r.FullPath)))
        {
            try
            {
                total += new FileInfo(replay.FullPath).Length;
            }
            catch (IOException)
            {
                // Ignore missing or inaccessible files in size estimate
            }
            catch (UnauthorizedAccessException)
            {
                // Ignore missing or inaccessible files in size estimate
            }
        }

        return total;
    }

    private static long GetMapFileSize(MapFile map)
    {
        long mapSize = 0;
        try
        {
            if (File.Exists(map.FullPath))
            {
                mapSize += new FileInfo(map.FullPath).Length;
            }

            if (map.IsDirectory && map.AssetFiles != null)
            {
                foreach (var asset in map.AssetFiles.Where(File.Exists))
                {
                    mapSize += new FileInfo(asset).Length;
                }
            }
        }
        catch (IOException)
        {
            // Ignore missing or inaccessible files in size estimate
        }
        catch (UnauthorizedAccessException)
        {
            // Ignore missing or inaccessible files in size estimate
        }

        return mapSize;
    }
}
