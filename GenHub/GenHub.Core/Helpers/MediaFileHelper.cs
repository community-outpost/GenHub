using System;
using System.Collections.Generic;
using System.IO;

namespace GenHub.Core.Helpers;

/// <summary>
/// Classifies dropped or linked files as preview images or videos by extension.
/// </summary>
public static class MediaFileHelper
{
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
        Span<byte> header = stackalloc byte[12];
        int read;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            read = stream.Read(header);
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
            ".BMP" => HasPrefix(header, read, [0x42, 0x4D]),
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
}
