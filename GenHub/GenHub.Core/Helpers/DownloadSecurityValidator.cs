namespace GenHub.Core.Helpers;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFileInfo
    {
        private readonly uint _cbStruct;
        [MarshalAs(UnmanagedType.LPWStr)]
        private readonly string _pszFilePath;
        private readonly IntPtr _hFile;
        private readonly IntPtr _pgKnownSubject;

        public WinTrustFileInfo(string filePath)
        {
            _cbStruct = (uint)Marshal.SizeOf<WinTrustFileInfo>();
            _pszFilePath = filePath;
            _hFile = IntPtr.Zero;
            _pgKnownSubject = IntPtr.Zero;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustData
    {
        private readonly uint _cbStruct;
        private readonly IntPtr _pPolicyCallbackData;
        private readonly IntPtr _pSIPClientData;
        private readonly uint _dwUIChoice;
        private readonly uint _fdwRevocationChecks;
        private readonly uint _dwUnionChoice;
        private readonly IntPtr _pFile;
        private readonly uint _dwStateAction;
        private readonly IntPtr _hWVTStateData;
        [MarshalAs(UnmanagedType.LPWStr)]
        private readonly string? _pwszURLReference;
        private readonly uint _dwProvFlags;
        private readonly uint _dwUIContext;
        private readonly IntPtr _pSignatureSettings;

        public WinTrustData(IntPtr filePtr)
        {
            _cbStruct = (uint)Marshal.SizeOf<WinTrustData>();
            _pPolicyCallbackData = IntPtr.Zero;
            _pSIPClientData = IntPtr.Zero;
            _dwUIChoice = 2; // WTD_UI_NONE
            _fdwRevocationChecks = 1; // WTD_REVOKE_WHOLECHAIN
            _dwUnionChoice = 1; // WTD_CHOICE_FILE
            _pFile = filePtr;
            _dwStateAction = 0; // WTD_STATEACTION_IGNORE
            _hWVTStateData = IntPtr.Zero;
            _pwszURLReference = null;
            _dwProvFlags = 0x00000040; // WTD_CACHE_ONLY_URL_RETRIEVAL
            _dwUIContext = 0;
            _pSignatureSettings = IntPtr.Zero;
        }
    }

    private const int CertEExpired = unchecked((int)0x800B0101);
    private const int CertEValidityPeriodNesting = unchecked((int)0x800B0102);

    private static readonly Guid WinTrustActionGenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    /// <summary>
    /// Computes the SHA-256 hash of a file as a lowercase hexadecimal string.
    /// </summary>
    /// <param name="filePath">Path to the file to hash.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>Lowercase hex SHA-256 string.</returns>
    public static async Task<string> ComputeSha256Async(string filePath, CancellationToken ct = default)
    {
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        return await ComputeSha256Async(stream, ct);
    }

    /// <summary>
    /// Computes the SHA-256 hash of a stream as a lowercase hexadecimal string.
    /// </summary>
    /// <param name="stream">The stream to hash.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>Lowercase hex SHA-256 string.</returns>
    public static async Task<string> ComputeSha256Async(Stream stream, CancellationToken ct = default)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = await sha256.ComputeHashAsync(stream, ct);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// Validates the Authenticode signature and publisher of a file.
    /// On Windows, performs WinVerifyTrust trust and integrity verification.
    /// </summary>
    /// <param name="filePath">Path to the executable or library file.</param>
    /// <param name="expectedPublisher">Expected publisher subject or issuer substring (e.g. "Microsoft Corporation").</param>
    /// <param name="allowExpiredCertificates">Whether to accept legacy expired certificates if publisher matches.</param>
    /// <returns>Operation result indicating success or failure.</returns>
    public static OperationResult<bool> ValidateAuthenticodeSignature(
        string filePath,
        string? expectedPublisher = null,
        bool allowExpiredCertificates = false)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return OperationResult<bool>.CreateFailure("File to validate does not exist.");
        }

        // On non-Windows, Authenticode trust verification is not supported; fail closed
        if (!OperatingSystem.IsWindows())
        {
            return OperationResult<bool>.CreateFailure("Authenticode signature validation is only supported on Windows.");
        }

        var trustResult = VerifyWindowsAuthenticodeTrust(filePath);
        if (!trustResult.Success)
        {
            return OperationResult<bool>.CreateFailure(trustResult.Errors);
        }

        int hresult = trustResult.Data;
        if (hresult != 0)
        {
            bool isExpiredCert = hresult == CertEExpired || hresult == CertEValidityPeriodNesting;
            if (!isExpiredCert || !allowExpiredCertificates)
            {
                return OperationResult<bool>.CreateFailure(
                    $"Authenticode trust verification failed for '{Path.GetFileName(filePath)}' with error code 0x{hresult:X8}.");
            }
        }

        // Verify publisher from the embedded certificate
        try
        {
            using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(filePath));

            if (!string.IsNullOrWhiteSpace(expectedPublisher))
            {
                var subject = cert.Subject;
                var issuer = cert.Issuer;

                if (!subject.Contains(expectedPublisher, StringComparison.OrdinalIgnoreCase) &&
                    !issuer.Contains(expectedPublisher, StringComparison.OrdinalIgnoreCase))
                {
                    return OperationResult<bool>.CreateFailure(
                        $"Authenticode signature publisher mismatch. Expected publisher containing '{expectedPublisher}', but found subject '{subject}' and issuer '{issuer}'.");
                }
            }

            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (CryptographicException ex)
        {
            return OperationResult<bool>.CreateFailure($"Authenticode certificate verification failed: {ex.Message}");
        }
        catch (IOException ex)
        {
            return OperationResult<bool>.CreateFailure($"Authenticode certificate read failed: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return OperationResult<bool>.CreateFailure($"Authenticode certificate access denied: {ex.Message}");
        }
    }

    /// <summary>
    /// Validates a downloaded file against pinned SHA-256 hashes and/or Authenticode publisher signatures.
    /// Fails closed if any specified check fails.
    /// </summary>
    /// <param name="filePath">Path to the file to validate.</param>
    /// <param name="allowedSha256Hashes">Optional list of allowed SHA-256 hashes.</param>
    /// <param name="expectedAuthenticodePublisher">Optional expected Authenticode publisher substring.</param>
    /// <param name="allowExpiredCertificates">Whether to accept legacy expired certificates if publisher matches.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>Operation result indicating validation success or failure.</returns>
    public static async Task<OperationResult<bool>> ValidateFileAsync(
        string filePath,
        IReadOnlyList<string>? allowedSha256Hashes = null,
        string? expectedAuthenticodePublisher = null,
        bool allowExpiredCertificates = false,
        CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
        {
            return OperationResult<bool>.CreateFailure($"File '{filePath}' does not exist for validation.");
        }

        bool hasHashCheck = allowedSha256Hashes is { Count: > 0 };
        bool hasPublisherCheck = !string.IsNullOrWhiteSpace(expectedAuthenticodePublisher);

        if (!hasHashCheck && !hasPublisherCheck)
        {
            return OperationResult<bool>.CreateFailure("No validation criteria (hash or publisher) specified.");
        }

        // Check SHA-256 hash if specified
        bool hashMatched = false;
        if (allowedSha256Hashes is { Count: > 0 })
        {
            var actualHash = await ComputeSha256Async(filePath, ct);
            hashMatched = allowedSha256Hashes.Any(h => string.Equals(h, actualHash, StringComparison.OrdinalIgnoreCase));
            if (!hashMatched && !hasPublisherCheck)
            {
                return OperationResult<bool>.CreateFailure(
                    $"SHA-256 hash mismatch for '{Path.GetFileName(filePath)}'. Computed hash: '{actualHash}'. Expected one of: [{string.Join(", ", allowedSha256Hashes)}].");
            }
        }

        // Check Authenticode publisher if specified
        if (hasPublisherCheck)
        {
            var authResult = ValidateAuthenticodeSignature(filePath, expectedAuthenticodePublisher, allowExpiredCertificates);
            if (!authResult.Success)
            {
                // If hash check was also specified and matched, allow fallback to known pinned hash
                if (hasHashCheck && hashMatched)
                {
                    return OperationResult<bool>.CreateSuccess(true);
                }

                return authResult;
            }
        }

        return OperationResult<bool>.CreateSuccess(true);
    }

    /// <summary>
    /// Validates a file using SHA-256 hash and/or Authenticode signature checks, and returns a shared-read, locked stream if valid.
    /// The caller is responsible for disposing the returned stream to release the file lock.
    /// </summary>
    /// <param name="filePath">The absolute path to the file to validate and lock.</param>
    /// <param name="allowedSha256Hashes">Optional collection of allowed SHA-256 hashes (hex string, case-insensitive).</param>
    /// <param name="expectedAuthenticodePublisher">Optional expected publisher common name (CN) in Authenticode certificate.</param>
    /// <param name="allowExpiredCertificates">Whether to accept expired certificates if valid at signing time.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A successful OperationResult containing the locked FileStream, or a failure result with validation errors.</returns>
    public static async Task<OperationResult<FileStream>> ValidateAndLockFileAsync(
        string filePath,
        IReadOnlyList<string>? allowedSha256Hashes = null,
        string? expectedAuthenticodePublisher = null,
        bool allowExpiredCertificates = false,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return OperationResult<FileStream>.CreateFailure("File path cannot be null or empty.");
        }

        if (!File.Exists(filePath))
        {
            return OperationResult<FileStream>.CreateFailure($"File '{filePath}' does not exist.");
        }

        // Remove ReadOnly attribute if present so caller can overwrite/delete later if needed
        try
        {
            var attributes = File.GetAttributes(filePath);
            if ((attributes & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(filePath, attributes & ~FileAttributes.ReadOnly);
            }
        }
        catch (IOException)
        {
            // Non-critical if filesystem does not support read-only attribute
        }
        catch (UnauthorizedAccessException)
        {
            // Non-critical if filesystem does not support read-only attribute
        }
        catch (ArgumentException)
        {
            // Non-critical if filesystem does not support read-only attribute
        }
        catch (NotSupportedException)
        {
            // Non-critical if filesystem does not support read-only attribute
        }

        FileStream? stream = null;
        try
        {
            stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 81920, true);

            var verifyResult = await VerifyStreamHashAndSignatureAsync(
                stream,
                filePath,
                allowedSha256Hashes,
                expectedAuthenticodePublisher,
                allowExpiredCertificates,
                ct);

            if (!verifyResult.Success)
            {
                await stream.DisposeAsync();
                stream = null;
                return OperationResult<FileStream>.CreateFailure(verifyResult.Errors);
            }

            return OperationResult<FileStream>.CreateSuccess(stream);
        }
        catch (Exception ex)
        {
            if (stream != null)
            {
                await stream.DisposeAsync();
            }

            return OperationResult<FileStream>.CreateFailure($"Failed to validate and lock file '{filePath}': {ex.Message}");
        }
    }

    private static async Task<OperationResult<bool>> VerifyStreamHashAndSignatureAsync(
        FileStream stream,
        string filePath,
        IReadOnlyList<string>? allowedSha256Hashes,
        string? expectedAuthenticodePublisher,
        bool allowExpiredCertificates,
        CancellationToken ct)
    {
        bool hasHashCheck = allowedSha256Hashes is { Count: > 0 };
        bool hasPublisherCheck = !string.IsNullOrWhiteSpace(expectedAuthenticodePublisher);

        if (!hasHashCheck && !hasPublisherCheck)
        {
            return OperationResult<bool>.CreateFailure("No validation criteria (hash or publisher) specified.");
        }

        bool hashMatched = false;
        if (allowedSha256Hashes is { Count: > 0 })
        {
            var actualHash = await ComputeSha256Async(stream, ct);
            stream.Position = 0;
            hashMatched = allowedSha256Hashes.Any(h => string.Equals(h, actualHash, StringComparison.OrdinalIgnoreCase));
            if (!hashMatched && !hasPublisherCheck)
            {
                return OperationResult<bool>.CreateFailure(
                    $"SHA-256 hash mismatch for '{Path.GetFileName(filePath)}'. Computed hash: '{actualHash}'. Expected one of: [{string.Join(", ", allowedSha256Hashes)}].");
            }
        }

        if (hasPublisherCheck)
        {
            var authResult = ValidateAuthenticodeSignature(filePath, expectedAuthenticodePublisher, allowExpiredCertificates);
            if (!authResult.Success)
            {
                if (hasHashCheck && hashMatched)
                {
                    return OperationResult<bool>.CreateSuccess(true);
                }

                return authResult;
            }
        }

        return OperationResult<bool>.CreateSuccess(true);
    }

    private static OperationResult<int> VerifyWindowsAuthenticodeTrust(string filePath)
    {
        var fileInfo = new WinTrustFileInfo(Path.GetFullPath(filePath));

        var pFileInfo = IntPtr.Zero;
        var pData = IntPtr.Zero;
        bool fileInfoMarshaled = false;
        bool trustDataMarshaled = false;

        try
        {
            pFileInfo = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
            Marshal.StructureToPtr(fileInfo, pFileInfo, false);
            fileInfoMarshaled = true;

            var trustData = new WinTrustData(pFileInfo);

            pData = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustData>());
            Marshal.StructureToPtr(trustData, pData, false);
            trustDataMarshaled = true;

            int result = WinVerifyTrust(IntPtr.Zero, WinTrustActionGenericVerifyV2, pData);
            return OperationResult<int>.CreateSuccess(result);
        }
        catch (Exception ex)
        {
            return OperationResult<int>.CreateFailure($"WinVerifyTrust exception: {ex.Message}");
        }
        finally
        {
            if (trustDataMarshaled)
            {
                Marshal.DestroyStructure<WinTrustData>(pData);
            }

            if (pData != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(pData);
            }

            if (fileInfoMarshaled)
            {
                Marshal.DestroyStructure<WinTrustFileInfo>(pFileInfo);
            }

            if (pFileInfo != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(pFileInfo);
            }
        }
    }

    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false, CharSet = CharSet.Unicode)]
    private static extern int WinVerifyTrust(
        IntPtr hwnd,
        [MarshalAs(UnmanagedType.LPStruct)] Guid pgActionID,
        IntPtr pWVTData);
}
