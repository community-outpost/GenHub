using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

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
    /// Determines whether the value is a video watch or embed page (YouTube, Vimeo)
    /// that must open in the system browser instead of the in-app player.
    /// </summary>
    /// <param name="value">The media URL to check.</param>
    /// <returns>True when the URL host is a known video embed platform.</returns>
    public static bool IsEmbedVideoPageUrl(string? value)
    {
        if (!IsRemoteHttpUrl(value))
        {
            return false;
        }

        if (!Uri.TryCreate(value!.Trim(), UriKind.Absolute, out var uri))
        {
            return false;
        }

        var host = uri.Host;
        return IsYouTubeHost(host)
            || host.Equals(Constants.ApiConstants.YouTubeShortHost, StringComparison.OrdinalIgnoreCase)
            || IsHostOrSubdomain(host, Constants.ApiConstants.VimeoHostSuffix);
    }

    /// <summary>
    /// Determines whether the value is a remote video file that the in-app player can stream.
    /// </summary>
    /// <param name="value">The media URL to check.</param>
    /// <returns>True when the value is a remote http/https URL with a video file extension.</returns>
    public static bool IsDirectVideoFileUrl(string? value)
    {
        if (!IsRemoteHttpUrl(value))
        {
            return false;
        }

        if (!Uri.TryCreate(value!.Trim(), UriKind.Absolute, out var uri))
        {
            return false;
        }

        var extension = Path.GetExtension(uri.AbsolutePath);
        return !string.IsNullOrEmpty(extension) && VideoExtensions.Contains(extension);
    }

    /// <summary>
    /// Determines whether the value is an extensionless hosted file URL from GenHub media
    /// hosting (Google Drive direct downloads, gateway uploads). These carry no file
    /// extension, so callers in a video context route them to the in-app player, which
    /// sniffs the content type, with a browser fallback on failure.
    /// </summary>
    /// <param name="value">The media URL to check.</param>
    /// <returns>True when the value is an extensionless file URL on a known media host.</returns>
    public static bool IsExtensionlessHostedFileUrl(string? value)
    {
        if (!IsRemoteHttpUrl(value))
        {
            return false;
        }

        if (!Uri.TryCreate(value!.Trim(), UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(Path.GetExtension(uri.AbsolutePath)))
        {
            return false;
        }

        if (uri.Host.Equals(
                Constants.HostingConstants.GoogleDriveDirectDownloadHost,
                StringComparison.OrdinalIgnoreCase))
        {
            return uri.AbsolutePath.Equals("/uc", StringComparison.OrdinalIgnoreCase);
        }

        return uri.Host.Equals(
            Constants.HostingConstants.UploadThingFileHost,
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Extracts the YouTube video ID from watch, embed, shorts, live, and shortened URLs.
    /// </summary>
    /// <param name="value">The media URL to inspect.</param>
    /// <returns>The video ID, or null when the URL is not a recognized YouTube video URL.</returns>
    public static string? TryGetYouTubeVideoId(string? value)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri))
        {
            return null;
        }

        var host = uri.Host;
        if (host.Equals(Constants.ApiConstants.YouTubeShortHost, StringComparison.OrdinalIgnoreCase))
        {
            return SanitizeYouTubeVideoId(uri.AbsolutePath.Trim('/'));
        }

        if (!IsYouTubeHost(host))
        {
            return null;
        }

        var queryId = GetQueryValue(uri.Query, "v");
        if (!string.IsNullOrEmpty(queryId))
        {
            return SanitizeYouTubeVideoId(queryId);
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 2 && IsYouTubePathPrefix(segments[0]))
        {
            return SanitizeYouTubeVideoId(segments[1]);
        }

        return null;
    }

    /// <summary>
    /// Resolves the official thumbnail image URL for a YouTube video URL.
    /// Non-YouTube URLs return null so callers render a placeholder instead of unrelated artwork.
    /// </summary>
    /// <param name="value">The media URL to inspect.</param>
    /// <returns>The thumbnail URL, or null when no official thumbnail exists.</returns>
    public static string? TryGetYouTubeThumbnailUrl(string? value)
    {
        var videoId = TryGetYouTubeVideoId(value);
        return videoId is null
            ? null
            : string.Format(CultureInfo.InvariantCulture, Constants.ApiConstants.YouTubeThumbnailUrlTemplate, videoId);
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
            return fullPath is not null && File.Exists(fullPath) ? fullPath : null;
        }

        if (string.IsNullOrEmpty(projectDirectory))
        {
            return null;
        }

        var projectRoot = TryGetFullPath(projectDirectory);
        if (projectRoot is null)
        {
            return null;
        }

        var combined = TryGetFullPath(Path.Combine(projectDirectory, trimmed));
        return combined is not null && IsWithinDirectory(projectRoot, combined) && File.Exists(combined)
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

    /// <summary>
    /// Determines whether a candidate path stays inside a root directory.
    /// Detects escapes via the relative path so the check stays sound on case-sensitive file systems.
    /// </summary>
    /// <param name="root">The containing directory.</param>
    /// <param name="candidate">The resolved path to test.</param>
    /// <returns>True when the candidate is inside the root.</returns>
    private static bool IsWithinDirectory(string root, string candidate)
    {
        var relative = Path.GetRelativePath(root, candidate);
        return !relative.Equals("..", StringComparison.Ordinal)
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !Path.IsPathRooted(relative);
    }

    private static bool IsYouTubeHost(string host)
    {
        return IsHostOrSubdomain(host, Constants.ApiConstants.YouTubeHostSuffix)
            || IsHostOrSubdomain(host, Constants.ApiConstants.YouTubeNoCookieHostSuffix);
    }

    private static bool IsHostOrSubdomain(string host, string suffix)
    {
        return host.Equals(suffix, StringComparison.OrdinalIgnoreCase)
            || host.EndsWith("." + suffix, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsYouTubePathPrefix(string segment)
    {
        return segment.Equals("embed", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("shorts", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("live", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("v", StringComparison.OrdinalIgnoreCase);
    }

    private static string? SanitizeYouTubeVideoId(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        var id = candidate.Trim();
        if (id.Any(c => !char.IsLetterOrDigit(c) && c != '-' && c != '_'))
        {
            return null;
        }

        return id.Length is >= 6 and <= 64 ? id : null;
    }

    private static string? GetQueryValue(string query, string key)
    {
        var pairs = query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
        foreach (var pair in pairs)
        {
            var separator = pair.IndexOf('=');
            var name = separator < 0 ? pair : pair[..separator];
            if (name.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                var encoded = separator < 0 ? string.Empty : pair[(separator + 1)..];
                return Uri.UnescapeDataString(encoded);
            }
        }

        return null;
    }
}
