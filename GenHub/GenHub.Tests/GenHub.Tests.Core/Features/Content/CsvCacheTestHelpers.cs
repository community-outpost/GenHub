using System;
using System.IO;
using GenHub.Core.Constants;

namespace GenHub.Tests.Core.Features.Content;

/// <summary>
/// Helpers for exercising stale CSV catalog cache behavior.
/// </summary>
internal static class CsvCacheTestHelpers
{
    /// <summary>
    /// Marks every CSV cache entry under an application data path as stale.
    /// </summary>
    /// <param name="applicationDataPath">Application data path containing the cache.</param>
    internal static void MakeEntriesStale(string applicationDataPath)
    {
        foreach (var cacheFile in Directory.EnumerateFiles(
            applicationDataPath,
            $"*{CsvConstants.CacheFileExtension}",
            SearchOption.AllDirectories))
        {
            File.SetLastWriteTimeUtc(cacheFile, DateTime.UtcNow.AddDays(-2));
        }
    }
}
