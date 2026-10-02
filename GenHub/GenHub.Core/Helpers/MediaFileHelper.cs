using System;
using System.Collections.Generic;
using System.IO;

namespace GenHub.Core.Helpers;

/// <summary>
/// Classifies dropped or linked files as preview images or videos by extension.
/// </summary>
public static class MediaFileHelper
{
    private const int HeaderReadSize = 24;

    /// <summary>
    /// Gets the collection of supported preview image file extensions.
    /// </summary>
    public static IReadOnlyCollection<string> ImageExtensions { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif", ".ico",
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".webm", ".mkv", ".mov", ".avi", ".m4v", ".ogv",
    };

    private static readonly HashSet<string> ImageExtensionsLookup = new(ImageExtensions, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Determines whether the path points to a preview image file.
    /// </summary>
    /// <param name="path">The file path or URL to check.</param>
    /// <returns>True when the extension is a known image extension.</returns>
    public static bool IsImageFile(string? path)
    {
        return !string.IsNullOrWhiteSpace(path) && ImageExtensionsLookup.Contains(GetExtension(path));
    }

    /// <summary>
    /// Determines whether the path points to a preview video file.
    /// </summary>
    /// <param name="path">The file path or URL to check.</param>
    /// <returns>True when the extension is a known video extension.</returns>
    public static bool IsVideoFile(string? path)
    {
        return !string.IsNullOrWhiteSpace(path) && VideoExtensions.Contains(GetExtension(path));
    }

    /// <summary>
    /// Determines whether the value is a remote http/https URL that subscribers can open.
    /// Publisher-local references (file:// URIs, absolute paths, relative paths) return false.
    /// </summary>
    /// <param name="value">The media URL or path to check.</param>
    /// <returns>True when the value is an absolute http or https URL.</returns>
    public static bool IsRemoteHttpUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    /// <summary>
    /// Resolves a catalog media reference to an existing local file.
    /// Accepts project-relative paths (kept inside the project directory), file:// URIs,
    /// and absolute paths. Remote http/https URLs and unresolvable values return null.
    /// </summary>
    /// <param name="projectDirectory">The studio project directory for relative references.</param>
    /// <param name="value">The media URL or path to resolve.</param>
    /// <returns>The full local file path, or null when it cannot be resolved.</returns>
    public static string? TryResolveLocalMediaPath(string? projectDirectory, string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || IsRemoteHttpUrl(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) && uri.IsFile)
        {
            return File.Exists(uri.LocalPath) ? uri.LocalPath : null;
        }

        if (Path.IsPathRooted(trimmed))
        {
            var fullPath = TryGetFullPath(trimmed);
            return fullPath != null && File.Exists(fullPath) ? fullPath : null;
        }

        if (string.IsNullOrEmpty(projectDirectory))
        {
            return null;
        }

        var projectRoot = TryGetFullPath(projectDirectory);
        if (projectRoot == null)
        {
            return null;
        }

        var root = projectRoot + Path.DirectorySeparatorChar;
        var combined = TryGetFullPath(Path.Combine(projectDirectory, trimmed));
        return combined != null && combined.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(combined)
            ? combined
            : null;
    }

    /// <summary>
    /// Determines whether a local file has image binary content matching its extension.
    /// Reads only the file header so renamed text or archive files are rejected.
    /// </summary>
    /// <param name="path">The local file path to inspect.</param>
    /// <returns>True when the header matches the extension's image format.</returns>
    public static bool HasImageContent(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        var extension = GetExtension(path);
        Span<byte> header = stackalloc byte[HeaderReadSize];
        var read = 0;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            int chunk = 0;
            while (read < header.Length && (chunk = stream.Read(header[read..])) > 0)
            {
                read += chunk;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        return extension.ToUpperInvariant() switch
        {
            ".PNG" => HasPrefix(header, read, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]),
            ".JPG" or ".JPEG" => HasPrefix(header, read, [0xFF, 0xD8, 0xFF]),
            ".GIF" => HasPrefix(header, read, [0x47, 0x49, 0x46, 0x38]),
            ".BMP" => HasPrefix(header, read, [0x42, 0x4D]) && HasValidBmpDibSize(header, read),
            ".ICO" => HasPrefix(header, read, [0x00, 0x00, 0x01, 0x00]),
            ".WEBP" => HasPrefixAt(header, read, 0, [0x52, 0x49, 0x46, 0x46]) &&
                HasPrefixAt(header, read, 8, [0x57, 0x45, 0x42, 0x50]),
            _ => false,
        };
    }

    private static bool HasPrefix(ReadOnlySpan<byte> header, int read, ReadOnlySpan<byte> prefix)
    {
        return HasPrefixAt(header, read, 0, prefix);
    }

    private static bool HasPrefixAt(ReadOnlySpan<byte> header, int read, int offset, ReadOnlySpan<byte> prefix)
    {
        return read >= offset + prefix.Length && header.Slice(offset, prefix.Length).SequenceEqual(prefix);
    }

    private static bool HasValidBmpDibSize(ReadOnlySpan<byte> header, int read)
    {
        // The DIB header size is a little-endian 32-bit value at offset 14.
        if (read < 18)
        {
            return false;
        }

        var dibSize = header[14] | (header[15] << 8) | (header[16] << 16) | (header[17] << 24);
        return dibSize is 12 or 40 or 52 or 56 or 64 or 108 or 124;
    }

    private static string GetExtension(string path)
    {
        if (Uri.TryCreate(path, UriKind.Absolute, out var uri) && !uri.IsFile)
        {
            return Path.GetExtension(uri.AbsolutePath);
        }

        try
        {
            return Path.GetExtension(path);
        }
        catch (ArgumentException)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Gets the full path for a file system path, or null when the path is invalid.
    /// </summary>
    /// <param name="path">The path to normalize.</param>
    /// <returns>The full path, or null when it cannot be resolved.</returns>
    private static string? TryGetFullPath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
        catch (PathTooLongException)
        {
            return null;
        }
    }
}
