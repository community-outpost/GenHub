using GenHub.Core.Constants;
using System;
using System.IO;
using System.Text;

namespace GenHub.Core.Utilities;

/// <summary>
/// Pure managed inspection of Flatpak single-file bundles (no flatpak/ostree tooling).
/// <para>
/// A bundle embeds its ostree ref near the header in plaintext
/// (<c>app/&lt;id&gt;/&lt;arch&gt;/&lt;branch&gt;</c>), so the application ID needed for
/// <c>flatpak install --bundle</c> followed by <c>flatpak run &lt;id&gt;</c> is readable
/// on any host. A missing or malformed ref yields <c>null</c>, never an exception.
/// </para>
/// </summary>
public static class FlatpakBundleHelper
{
    private static readonly byte[] RefPrefix = Encoding.ASCII.GetBytes(ContentFormatConstants.FlatpakRefPrefix);

    /// <summary>
    /// Extracts the Flatpak application ID from a bundle file.
    /// </summary>
    /// <param name="bundlePath">Absolute path of the <c>.flatpak</c> file.</param>
    /// <returns>The application ID, or <c>null</c> when it cannot be determined.</returns>
    public static string? TryExtractAppId(string bundlePath)
    {
        if (string.IsNullOrWhiteSpace(bundlePath) || !File.Exists(bundlePath))
        {
            return null;
        }

        try
        {
            using var stream = new FileStream(bundlePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var size = (int)Math.Min(ContentFormatConstants.FlatpakHeaderScanSize, stream.Length);
            if (size <= RefPrefix.Length)
            {
                return null;
            }

            var header = new byte[size];
            var read = stream.Read(header, 0, size);
            return TryExtractAppId(header.AsSpan(0, read));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// Extracts the Flatpak application ID from bundle header bytes.
    /// </summary>
    /// <param name="header">The first bytes of the bundle file.</param>
    /// <returns>The application ID, or <c>null</c> when no well-formed ref is present.</returns>
    public static string? TryExtractAppId(ReadOnlySpan<byte> header)
    {
        var searchFrom = 0;
        while (searchFrom <= header.Length - RefPrefix.Length)
        {
            var relative = header[searchFrom..].IndexOf(RefPrefix);
            if (relative < 0)
            {
                return null;
            }

            var idStart = searchFrom + relative + RefPrefix.Length;
            if (TryReadSegment(header, idStart, out var idEnd)
                && idEnd < header.Length && header[idEnd] == (byte)'/'
                && TryReadSegment(header, idEnd + 1, out var archEnd)
                && archEnd < header.Length && header[archEnd] == (byte)'/'
                && TryReadSegment(header, archEnd + 1, out _))
            {
                var candidate = Encoding.ASCII.GetString(header.Slice(idStart, idEnd - idStart));
                if (IsPlausibleAppId(candidate))
                {
                    return candidate;
                }
            }

            searchFrom = idStart;
        }

        return null;
    }

    private static bool TryReadSegment(ReadOnlySpan<byte> header, int start, out int end)
    {
        end = start;
        while (end < header.Length && IsRefByte(header[end]))
        {
            end++;
        }

        return end > start;
    }

    private static bool IsRefByte(byte value)
    {
        return (value >= (byte)'A' && value <= (byte)'Z')
            || (value >= (byte)'a' && value <= (byte)'z')
            || (value >= (byte)'0' && value <= (byte)'9')
            || value is (byte)'.' or (byte)'_' or (byte)'-' or (byte)'+';
    }

    private static bool IsPlausibleAppId(string candidate)
    {
        return candidate.Contains('.', StringComparison.Ordinal)
            && candidate.Length <= ContentFormatConstants.FlatpakMaxAppIdLength;
    }
}
