using System;
using System.Security.Cryptography;
using System.Text;

namespace GenHub.Core.Helpers;

/// <summary>
/// Helper utilities for ModDB URLs and content identifiers.
/// </summary>
public static class ModDbHelper
{
    /// <summary>
    /// Extracts a ModDB identifier from a given URL, falling back to a deterministic SHA256 hex digest.
    /// </summary>
    /// <param name="url">The ModDB URL.</param>
    /// <returns>The extracted identifier segment, or a lowercase SHA256 hex string fallback.</returns>
    public static string ExtractModDbIdFromUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length > 0)
            {
                return segments[^1];
            }
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))).ToLowerInvariant();
    }
}
