using GenHub.Core.Constants;
using GenHub.Core.Models.GameProfile;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Helpers;

/// <summary>
/// Provides compression, encoding, and sanitization utilities for shared profile payloads.
/// </summary>
public static class ProfileSharingCompressionHelper
{
    /// <summary>
    /// Warning message when dangerous shell characters are stripped from launch arguments.
    /// </summary>
    public const string ShellCharactersWarning = "Disallowed special command characters (| & ; > < ` $ ^ ! ( )) were removed from launch arguments.";

    /// <summary>
    /// Warning message when quote or percent characters are stripped from launch arguments.
    /// </summary>
    public const string QuotesOrPercentWarning = "Quote and percent characters were removed from launch arguments. Check arguments if paths with spaces were used.";

    /// <summary>
    /// Warning message when control characters are stripped from launch arguments.
    /// </summary>
    public const string ControlCharactersWarning = "Control characters were removed from launch arguments.";

    private static readonly char[] DangerousArgumentCharacters = ['|', '&', ';', '>', '<', '`', '$', '^', '!', '(', ')'];

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
    /// <exception cref="ArgumentException">Thrown when <paramref name="base64Url"/> is null or whitespace.</exception>
    /// <exception cref="FormatException">Thrown when <paramref name="base64Url"/> is not a valid base64url string.</exception>
    /// <exception cref="InvalidDataException">Thrown when decompressed data exceeds the maximum allowed size limit or is corrupt.</exception>
    public static string DecodeAndDecompress(string base64Url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(base64Url);

        byte[] compressedBytes = ConvertFromBase64Url(base64Url.Trim());
        using var inputStream = new MemoryStream(compressedBytes);
        using var brotliStream = new BrotliStream(inputStream, CompressionMode.Decompress);
        using var outputStream = new MemoryStream();

        byte[] buffer = new byte[8192];
        int bytesRead = 0;
        long totalBytes = 0;
        while ((bytesRead = brotliStream.Read(buffer, 0, buffer.Length)) > 0)
        {
            totalBytes += bytesRead;
            if (totalBytes > ProfileSharingConstants.MaxDecompressedPayloadBytes)
            {
                throw new InvalidDataException("Decompressed profile package exceeds maximum allowed size.");
            }

            outputStream.Write(buffer, 0, bytesRead);
        }

        return Encoding.UTF8.GetString(outputStream.ToArray());
    }

    /// <summary>
    /// Decodes a URL-safe Base64 string and decompresses the Brotli payload back into a JSON string.
    /// </summary>
    /// <param name="base64Url">The URL-safe Base64 payload string.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The decompressed JSON string.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="base64Url"/> is null or whitespace.</exception>
    /// <exception cref="FormatException">Thrown when <paramref name="base64Url"/> is not a valid base64url string.</exception>
    /// <exception cref="InvalidDataException">Thrown when decompressed data exceeds the maximum allowed size limit or is corrupt.</exception>
    public static async Task<string> DecodeAndDecompressAsync(string base64Url, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(base64Url);

        byte[] compressedBytes = ConvertFromBase64Url(base64Url.Trim());
        using var inputStream = new MemoryStream(compressedBytes);
        using var brotliStream = new BrotliStream(inputStream, CompressionMode.Decompress);
        using var outputStream = new MemoryStream();

        byte[] buffer = new byte[8192];
        int bytesRead = 0;
        long totalBytes = 0;
        while ((bytesRead = await brotliStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
        {
            totalBytes += bytesRead;
            if (totalBytes > ProfileSharingConstants.MaxDecompressedPayloadBytes)
            {
                throw new InvalidDataException("Decompressed profile package exceeds maximum allowed size.");
            }

            await outputStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
        }

        return Encoding.UTF8.GetString(outputStream.ToArray());
    }

    /// <summary>
    /// Sanitizes command-line arguments to prevent command injection, environment variable
    /// expansion, argument splitting via control characters, and quote-based flag injection.
    /// </summary>
    /// <param name="arguments">The raw command line arguments string from the shared package.</param>
    /// <param name="warnings">Output list of warnings if potentially unsafe characters were sanitized.</param>
    /// <param name="warningCodes">Output list of warning codes if potentially unsafe characters were sanitized.</param>
    /// <returns>The sanitized arguments string.</returns>
    public static string SanitizeCommandLineArguments(
        string? arguments,
        out List<string> warnings,
        out List<ProfileSecurityWarningCode> warningCodes)
    {
        warnings = [];
        warningCodes = [];
        if (string.IsNullOrWhiteSpace(arguments))
        {
            return string.Empty;
        }

        bool removedShellCharacters = false;
        bool removedQuotesOrPercent = false;
        bool removedControlCharacters = false;

        char[] buffer = new char[arguments.Length];
        int length = 0;

        foreach (char current in arguments)
        {
            if (DangerousArgumentCharacters.Contains(current))
            {
                removedShellCharacters = true;
                continue;
            }

            if (current is '"' or '\'' or '%')
            {
                removedQuotesOrPercent = true;
                continue;
            }

            if (char.IsControl(current))
            {
                removedControlCharacters = true;
                continue;
            }

            buffer[length++] = current;
        }

        if (removedShellCharacters)
        {
            warnings.Add(ShellCharactersWarning);
            warningCodes.Add(ProfileSecurityWarningCode.SanitizedShellCharacters);
        }

        if (removedQuotesOrPercent)
        {
            warnings.Add(QuotesOrPercentWarning);
            warningCodes.Add(ProfileSecurityWarningCode.SanitizedQuotesOrPercent);
        }

        if (removedControlCharacters)
        {
            warnings.Add(ControlCharactersWarning);
            warningCodes.Add(ProfileSecurityWarningCode.SanitizedControlCharacters);
        }

        return new string(buffer, 0, length).Trim();
    }

    /// <summary>
    /// Sanitizes command-line arguments to prevent command injection, environment variable
    /// expansion, argument splitting via control characters, and quote-based flag injection.
    /// </summary>
    /// <param name="arguments">The raw command line arguments string from the shared package.</param>
    /// <param name="warnings">Output list of warnings if potentially unsafe characters were sanitized.</param>
    /// <returns>The sanitized arguments string.</returns>
    public static string SanitizeCommandLineArguments(string? arguments, out List<string> warnings)
    {
        return SanitizeCommandLineArguments(arguments, out warnings, out _);
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

        string incoming = (base64Url.Length % 4) switch
        {
            2 => base64Url.Replace('-', '+').Replace('_', '/') + "==",
            3 => base64Url.Replace('-', '+').Replace('_', '/') + "=",
            0 => base64Url.Replace('-', '+').Replace('_', '/'),
            _ => throw new FormatException("Invalid Base64Url string length."),
        };

        return Convert.FromBase64String(incoming);
    }
}
