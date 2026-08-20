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
    /// <returns>Operation result indicating success or failure.</returns>
    public static OperationResult<bool> ValidateAuthenticodeSignature(string filePath, string? expectedPublisher = null)
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

        // 1. Verify Authenticode publisher / trust if specified
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
