using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;

namespace GenHub.Core.Utilities;

/// <summary>
/// Screens archive entry names before they are turned into filesystem paths. Names come from
/// third-party archives, so a name the host cannot represent has to be refused up front rather
/// than left to fail somewhere inside the write: an empty name in particular collapses
/// <see cref="Path.Combine(string, string)"/> onto the extraction directory itself, which puts the
/// write on the directory instead of on a file inside it.
/// </summary>
public static class ArchiveEntryName
{
    private static readonly char[] SeparatorChars = ['/', '\\'];

    private static readonly char[] UnusableChars =
        ['\"', '<', '>', '|', ':', '*', '?', .. Enumerable.Range(0, 32).Select(value => (char)value)];

    private static readonly string[] ReservedDeviceNames =
    [
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    ];

    /// <summary>
    /// Determines whether an archive entry name can be combined with an extraction directory to
    /// name a file. A name that resolves to the directory itself is refused, as is one the
    /// strictest supported host cannot represent, so an archive behaves the same everywhere: that
    /// rules out reserved device names and the characters Windows forbids, including the colon that
    /// would otherwise open an NTFS alternate data stream. Traversal in the middle of a name is not
    /// judged here; that stays with the containment check that follows.
    /// </summary>
    /// <param name="entryName">The archive-relative entry name to screen.</param>
    /// <returns><see langword="true"/> when the name can be extracted; otherwise, <see langword="false"/>.</returns>
    public static bool IsExtractable([NotNullWhen(true)] string? entryName)
    {
        if (string.IsNullOrWhiteSpace(entryName))
        {
            return false;
        }

        if (entryName.EndsWith('/') || entryName.EndsWith('\\'))
        {
            return false;
        }

        var segments = entryName.Split(SeparatorChars, StringSplitOptions.RemoveEmptyEntries);

        return segments.Length > 0 &&
               segments[^1] is not ("." or "..") &&
               segments.All(IsExtractableSegment);
    }

    private static bool IsExtractableSegment(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
        {
            return false;
        }

        if (segment is not ("." or "..") && (segment.EndsWith('.') || segment.EndsWith(' ')))
        {
            return false;
        }

        if (segment.IndexOfAny(UnusableChars) >= 0)
        {
            return false;
        }

        var deviceName = segment.Split('.')[0];

        return !ReservedDeviceNames.Contains(deviceName, StringComparer.OrdinalIgnoreCase);
    }
}
