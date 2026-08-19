using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Helpers;

/// <summary>
/// Provides compression, encoding, and sanitization utilities for shared profile payloads.
/// </summary>
public static class ProfileSharingCompressionHelper
{
    private static readonly char[] DangerousArgumentCharacters = ['|', '&', ';', '>', '<', '`', '$'];

    /// <summary>
    /// Compresses a JSON string with Brotli and encodes the result as a URL-safe Base64 string.
    /// </summary>
    /// <param name="json">The JSON payload string.</param>
    /// <returns>The URL-safe Base64 string representation of the compressed payload.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="json"/> is null.</exception>
    public static string CompressAndEncode(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        byte[] inputBytes = Encoding.UTF8.GetBytes(json);
        using var outputStream = new MemoryStream();
        using (var brotliStream = new BrotliStream(outputStream, CompressionLevel.Optimal, leaveOpen: true))
        {
            brotliStream.Write(inputBytes, 0, inputBytes.Length);
        }

        byte[] compressedBytes = outputStream.ToArray();
        return ConvertToBase64Url(compressedBytes);
    }

    /// <summary>
    /// Decodes a URL-safe Base64 string and decompresses the Brotli payload back into a JSON string synchronously.
    /// </summary>
    /// <param name="base64Url">The URL-safe Base64 payload string.</param>
    /// <returns>The decompressed JSON string.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="base64Url"/> is null or empty.</exception>
    public static string DecodeAndDecompress(string base64Url)
    {
        if (string.IsNullOrWhiteSpace(base64Url))
        {
            throw new ArgumentNullException(nameof(base64Url));
        }

        byte[] compressedBytes = ConvertFromBase64Url(base64Url.Trim());
        using var inputStream = new MemoryStream(compressedBytes);
        using var brotliStream = new BrotliStream(inputStream, CompressionMode.Decompress);
        using var reader = new StreamReader(brotliStream, Encoding.UTF8);

        return reader.ReadToEnd();
    }

    /// <summary>
    /// Decodes a URL-safe Base64 string and decompresses the Brotli payload back into a JSON string.
    /// </summary>
    /// <param name="base64Url">The URL-safe Base64 payload string.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The decompressed JSON string.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="base64Url"/> is null or empty.</exception>
    public static async Task<string> DecodeAndDecompressAsync(string base64Url, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(base64Url))
        {
            throw new ArgumentNullException(nameof(base64Url));
        }

        byte[] compressedBytes = ConvertFromBase64Url(base64Url.Trim());
        using var inputStream = new MemoryStream(compressedBytes);
        using var brotliStream = new BrotliStream(inputStream, CompressionMode.Decompress);
        using var reader = new StreamReader(brotliStream, Encoding.UTF8);

        return await reader.ReadToEndAsync(cancellationToken);
    }

    /// <summary>
    /// Sanitizes command-line arguments to prevent command injection and unauthorized flags.
    /// </summary>
    /// <param name="arguments">The raw command line arguments string from the shared package.</param>
    /// <param name="warnings">Output list of warnings if potentially unsafe characters were sanitized.</param>
    /// <returns>The sanitized arguments string.</returns>
    public static string SanitizeCommandLineArguments(string? arguments, out List<string> warnings)
    {
        warnings = [];
        if (string.IsNullOrWhiteSpace(arguments))
        {
            return string.Empty;
        }

        string sanitized = arguments;
        bool hasDangerousChars = false;

        foreach (char dangerousChar in DangerousArgumentCharacters)
        {
            if (sanitized.Contains(dangerousChar))
            {
                hasDangerousChars = true;
                sanitized = sanitized.Replace(dangerousChar.ToString(), string.Empty);
            }
        }

        if (hasDangerousChars)
        {
            warnings.Add("Disallowed special command characters (| & ; > < ` $) were removed from launch arguments.");
        }

        return sanitized.Trim();
    }

    /// <summary>
    /// Converts a byte array to a URL-safe Base64 string (without padding).
    /// </summary>
    /// <param name="bytes">The byte array to encode.</param>
    /// <returns>The URL-safe Base64 encoded string.</returns>
    public static string ConvertToBase64Url(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        string base64 = Convert.ToBase64String(bytes);
        return base64
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    /// <summary>
    /// Converts a URL-safe Base64 string back into a byte array.
    /// </summary>
    /// <param name="base64Url">The URL-safe Base64 string to decode.</param>
    /// <returns>The decoded byte array.</returns>
    public static byte[] ConvertFromBase64Url(string base64Url)
    {
        ArgumentNullException.ThrowIfNull(base64Url);

        string incoming = base64Url.Replace('-', '+').Replace('_', '/');
        switch (incoming.Length % 4)
        {
            case 2:
                incoming += "==";
                break;
            case 3:
                incoming += "=";
                break;
        }

        return Convert.FromBase64String(incoming);
    }
}
