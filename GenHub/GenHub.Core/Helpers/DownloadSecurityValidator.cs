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
        private uint _cbStruct;
        [MarshalAs(UnmanagedType.LPWStr)]
        private string _pszFilePath;
        private IntPtr _hFile;
        private IntPtr _pgKnownSubject;

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
        private uint _cbStruct;
        private IntPtr _pPolicyCallbackData;
        private IntPtr _pSIPClientData;
        private uint _dwUIChoice;
        private uint _fdwRevocationChecks;
        private uint _dwUnionChoice;
        private IntPtr _pFile;
        private uint _dwStateAction;
        private IntPtr _hWVTStateData;
        [MarshalAs(UnmanagedType.LPWStr)]
        private string? _pwszURLReference;
        private uint _dwProvFlags;
        private uint _dwUIContext;
        private IntPtr _pSignatureSettings;

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
        if (!trustResult.Success && !allowExpiredCertificates)
        {
            return trustResult;
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
        catch (Exception ex)
        {
            return OperationResult<bool>.CreateFailure($"Authenticode certificate verification failed: {ex.Message}");
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
        if (hasHashCheck)
        {
            var actualHash = await ComputeSha256Async(filePath, ct);
            hashMatched = allowedSha256Hashes!.Any(h => string.Equals(h, actualHash, StringComparison.OrdinalIgnoreCase));
            if (!hashMatched && !hasPublisherCheck)
            {
                return OperationResult<bool>.CreateFailure(
                    $"SHA-256 hash mismatch for '{Path.GetFileName(filePath)}'. Computed hash: '{actualHash}'. Expected one of: [{string.Join(", ", allowedSha256Hashes!)}].");
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
    /// Opens a file in read-only shared mode, sets the file as read-only, validates its SHA-256 and/or Authenticode signature,
    /// and returns the open <see cref="FileStream"/>. Holding this stream prevents TOCTOU modification until execution/use.
    /// </summary>
    /// <param name="filePath">Path to the file to validate and lock.</param>
    /// <param name="allowedSha256Hashes">Optional list of allowed SHA-256 hashes.</param>
    /// <param name="expectedAuthenticodePublisher">Optional expected Authenticode publisher substring.</param>
    /// <param name="allowExpiredCertificates">Whether to accept legacy certificates with expired timestamps.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>An operation result containing the locked stream on success, or an error description on failure.</returns>
    public static async Task<OperationResult<FileStream>> ValidateAndLockFileAsync(
        string filePath,
        IReadOnlyList<string>? allowedSha256Hashes = null,
        string? expectedAuthenticodePublisher = null,
        bool allowExpiredCertificates = false,
        CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
        {
            return OperationResult<FileStream>.CreateFailure($"File '{filePath}' does not exist for validation.");
        }

        try
        {
            File.SetAttributes(filePath, File.GetAttributes(filePath) | FileAttributes.ReadOnly);
        }
        catch (IOException)
        {
            // Non-critical if filesystem does not support read-only attribute
        }
        catch (UnauthorizedAccessException)
        {
            // Non-critical if filesystem does not support read-only attribute
        }

        FileStream? stream = null;
        try
        {
            stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 81920, true);

            bool hasHashCheck = allowedSha256Hashes is { Count: > 0 };
            bool hasPublisherCheck = !string.IsNullOrWhiteSpace(expectedAuthenticodePublisher);

            if (!hasHashCheck && !hasPublisherCheck)
            {
                stream.Dispose();
                return OperationResult<FileStream>.CreateFailure("No validation criteria (hash or publisher) specified.");
            }

            bool hashMatched = false;
            if (hasHashCheck)
            {
                var actualHash = await ComputeSha256Async(stream, ct);
                stream.Position = 0;
                hashMatched = allowedSha256Hashes!.Any(h => string.Equals(h, actualHash, StringComparison.OrdinalIgnoreCase));
                if (!hashMatched && !hasPublisherCheck)
                {
                    stream.Dispose();
                    return OperationResult<FileStream>.CreateFailure(
                        $"SHA-256 hash mismatch for '{Path.GetFileName(filePath)}'. Computed hash: '{actualHash}'. Expected one of: [{string.Join(", ", allowedSha256Hashes!)}].");
                }
            }

            if (hasPublisherCheck)
            {
                var authResult = ValidateAuthenticodeSignature(filePath, expectedAuthenticodePublisher, allowExpiredCertificates);
                if (!authResult.Success)
                {
                    if (hasHashCheck && hashMatched)
                    {
                        return OperationResult<FileStream>.CreateSuccess(stream);
                    }

                    stream.Dispose();
                    return OperationResult<FileStream>.CreateFailure(authResult.Errors);
                }
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

    private static OperationResult<bool> VerifyWindowsAuthenticodeTrust(string filePath)
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
            if (result != 0)
            {
                return OperationResult<bool>.CreateFailure(
                    $"Authenticode trust verification failed for '{Path.GetFileName(filePath)}' with error code 0x{result:X8}.");
            }

            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (Exception ex)
        {
            return OperationResult<bool>.CreateFailure($"WinVerifyTrust exception: {ex.Message}");
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
