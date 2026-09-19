using GenHub.Core.Constants;
using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Content.Services.GenLauncher;

/// <summary>
/// Validates file checksums against expected ETags for engine extensions.
/// </summary>
public static partial class GenLauncherChecksumValidator
{
    /// <summary>
    /// Checks if a file requires MD5 checksum validation based on its extension.
    /// </summary>
    /// <param name="filePath">The file path or name.</param>
    /// <returns>True if the file extension is one of the engine critical extensions; otherwise false.</returns>
    public static bool RequiresValidation(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        var ext = Path.GetExtension(filePath);
        if (string.IsNullOrEmpty(ext))
        {
            return false;
        }

        return Array.Exists(GenLauncherConstants.ChecksumExtensions, checksumExt => ext.Equals(checksumExt, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Cleans an S3 ETag by removing leading/trailing quotes and HTML entities.
    /// </summary>
    /// <param name="etag">The raw ETag string.</param>
    /// <returns>The unquoted lowercase MD5 hex string.</returns>
    public static string CleanETag(string? etag)
    {
        if (string.IsNullOrWhiteSpace(etag))
        {
            return string.Empty;
        }

        var cleaned = etag.Trim();
        if (cleaned.Length >= 12 && cleaned.StartsWith("&quot;", StringComparison.OrdinalIgnoreCase) && cleaned.EndsWith("&quot;", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = cleaned[6..^6];
        }

        cleaned = cleaned.Trim('"', '\'').Trim();
        return cleaned.ToLowerInvariant();
    }

    /// <summary>
    /// Checks if an ETag represents an S3 multipart upload (format: {hash}-{partCount}).
    /// </summary>
    /// <param name="etag">The raw or cleaned ETag.</param>
    /// <returns>True if the ETag is a multipart upload hash; otherwise false.</returns>
    public static bool IsMultipartETag(string? etag)
    {
        if (string.IsNullOrWhiteSpace(etag))
        {
            return false;
        }

        var cleaned = CleanETag(etag);
        return MultipartETagRegex().IsMatch(cleaned);
    }

    /// <summary>
    /// Computes MD5 hex hash of a file on disk.
    /// </summary>
    /// <param name="filePath">The local file path.</param>
    /// <returns>Lowercase hex MD5 hash.</returns>
    [SuppressMessage("Security", "S4790:Make sure that hashing data is safe here.", Justification = "MD5 is required by S3/MinIO ETag specification and legacy GenLauncher mod catalogs.")]
    public static string ComputeMd5Hex(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var md5 = MD5.Create();
        var hashBytes = md5.ComputeHash(stream);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// Computes MD5 hex hash of a file on disk asynchronously.
    /// </summary>
    /// <param name="filePath">The local file path.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task returning the lowercase hex MD5 hash.</returns>
    [SuppressMessage("Security", "S4790:Make sure that hashing data is safe here.", Justification = "MD5 is required by S3/MinIO ETag specification and legacy GenLauncher mod catalogs.")]
    public static async Task<string> ComputeMd5HexAsync(string filePath, CancellationToken cancellationToken = default)
    {
        using var stream = File.OpenRead(filePath);
        var hashBytes = await MD5.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// Validates a local file against an expected MD5 hash if the file requires validation.
    /// </summary>
    /// <param name="filePath">The local file path.</param>
    /// <param name="expectedMd5">The expected MD5 hash (ETag).</param>
    /// <returns>True if valid or validation not required; false if validation failed.</returns>
    public static bool ValidateFile(string filePath, string? expectedMd5)
    {
        if (!RequiresValidation(filePath))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(expectedMd5))
        {
            return true;
        }

        if (IsMultipartETag(expectedMd5))
        {
            return true;
        }

        if (!File.Exists(filePath))
        {
            return false;
        }

        var actualMd5 = ComputeMd5Hex(filePath);
        var expectedClean = CleanETag(expectedMd5);
        return string.Equals(actualMd5, expectedClean, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Validates a local file against an expected MD5 hash asynchronously if the file requires validation.
    /// </summary>
    /// <param name="filePath">The local file path.</param>
    /// <param name="expectedMd5">The expected MD5 hash (ETag).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task returning true if valid or validation not required; false if validation failed.</returns>
    public static async Task<bool> ValidateFileAsync(string filePath, string? expectedMd5, CancellationToken cancellationToken = default)
    {
        if (!RequiresValidation(filePath))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(expectedMd5))
        {
            return true;
        }

        if (IsMultipartETag(expectedMd5))
        {
            return true;
        }

        if (!File.Exists(filePath))
        {
            return false;
        }

        var actualMd5 = await ComputeMd5HexAsync(filePath, cancellationToken).ConfigureAwait(false);
        var expectedClean = CleanETag(expectedMd5);
        return string.Equals(actualMd5, expectedClean, StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex("^[a-fA-F0-9]{32}-[1-9][0-9]*$", RegexOptions.None, 1000)]
    private static partial Regex MultipartETagRegex();
}
