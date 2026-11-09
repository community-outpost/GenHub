using System;
using System.Collections.Generic;
using System.IO;

namespace GenHub.Core.Constants;

/// <summary>
/// Policy constants for live content folders.
/// </summary>
public static class LiveContentConstants
{
    /// <summary>
    /// Debounce interval applied after a live folder event before a rescan starts.
    /// </summary>
    public const int DebounceMilliseconds = 500;

    /// <summary>
    /// FileSystemWatcher internal buffer size for each live root.
    /// </summary>
    public const int WatcherBufferSize = 64 * 1024;

    /// <summary>
    /// Extensions the game or its tools commonly write. These entries are copied into the
    /// workspace instead of linked so a write cannot reach the developer's source folder.
    /// </summary>
    public static readonly IReadOnlySet<string> ProtectedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".ini", ".cfg", ".txt", ".xml", ".json", ".log", ".rep", ".sav",
    };

    /// <summary>
    /// Gets whether a workspace-relative path must be copied instead of linked.
    /// </summary>
    /// <param name="relativePath">The path relative to the live root.</param>
    /// <returns><c>true</c> when the file is treated as game-writable.</returns>
    public static bool IsProtected(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        return ProtectedExtensions.Contains(Path.GetExtension(relativePath));
    }
}
