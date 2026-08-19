namespace GenHub.Core.Helpers;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Models.Results;

/// <summary>
/// Provides security validation for downloaded executables and packages, including SHA-256 and Authenticode checks.
/// </summary>
public static class DownloadSecurityValidator
{
    /// <summary>
    /// Computes the SHA-256 hash of a file as a lowercase hexadecimal string.
    /// </summary>
    /// <param name="filePath">Path to the file to hash.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>Lowercase hex SHA-256 string.</returns>
    public static async Task<string> ComputeSha256Async(string filePath, CancellationToken ct = default)
    {
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        using var sha256 = SHA256.Create();
        var hashBytes = await sha256.ComputeHashAsync(stream, ct);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// Validates the Authenticode signature publisher of a file.
    /// </summary>
    /// <param name="filePath">Path to the executable or library file.</param>
    /// <param name="expectedPublisher">Expected publisher subject or issuer substring (e.g. "Microsoft Corporation").</param>
    /// <returns>Operation result indicating success or failure.</returns>
    public static OperationResult<bool> ValidateAuthenticodeSignature(string filePath, string expectedPublisher)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return OperationResult<bool>.CreateFailure("File to validate does not exist.");
        }

        if (string.IsNullOrWhiteSpace(expectedPublisher))
        {
            return OperationResult<bool>.CreateSuccess(true);
        }

        try
        {
            using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(filePath));
            var subject = cert.Subject;
            var issuer = cert.Issuer;

            if (subject.Contains(expectedPublisher, StringComparison.OrdinalIgnoreCase) ||
                issuer.Contains(expectedPublisher, StringComparison.OrdinalIgnoreCase))
            {
                return OperationResult<bool>.CreateSuccess(true);
            }

            return OperationResult<bool>.CreateFailure(
                $"Authenticode signature publisher mismatch. Expected publisher containing '{expectedPublisher}', but found subject '{subject}' and issuer '{issuer}'.");
        }
        catch (Exception ex)
        {
            return OperationResult<bool>.CreateFailure($"Authenticode signature verification failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Validates a downloaded file against pinned SHA-256 hashes and/or Authenticode publisher signatures.
    /// Fails closed if any specified check fails.
    /// </summary>
    /// <param name="filePath">Path to the file to validate.</param>
    /// <param name="allowedSha256Hashes">Optional list of allowed SHA-256 hashes.</param>
    /// <param name="expectedAuthenticodePublisher">Optional expected Authenticode publisher substring.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>Operation result indicating validation success or failure.</returns>
    public static async Task<OperationResult<bool>> ValidateFileAsync(
        string filePath,
        IReadOnlyList<string>? allowedSha256Hashes = null,
        string? expectedAuthenticodePublisher = null,
        CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
        {
            return OperationResult<bool>.CreateFailure($"File '{filePath}' does not exist for validation.");
        }

        // 1. Verify Authenticode publisher if specified
        if (!string.IsNullOrWhiteSpace(expectedAuthenticodePublisher))
        {
            var authResult = ValidateAuthenticodeSignature(filePath, expectedAuthenticodePublisher);
            if (!authResult.Success)
            {
                return authResult;
            }
        }

        // 2. Verify SHA-256 hash if specified
        if (allowedSha256Hashes is { Count: > 0 })
        {
            var actualHash = await ComputeSha256Async(filePath, ct);
            bool matched = allowedSha256Hashes.Any(h => string.Equals(h, actualHash, StringComparison.OrdinalIgnoreCase));
            if (!matched)
            {
                return OperationResult<bool>.CreateFailure(
                    $"SHA-256 hash mismatch for '{Path.GetFileName(filePath)}'. Computed hash: '{actualHash}'. Expected one of: [{string.Join(", ", allowedSha256Hashes)}].");
            }
        }

        return OperationResult<bool>.CreateSuccess(true);
    }
}
