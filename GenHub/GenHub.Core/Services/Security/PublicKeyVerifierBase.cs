using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Security;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Security;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;

namespace GenHub.Core.Services.Security;

/// <summary>
/// Shared PEM import and verification flow for <see cref="IPublicKeyVerifier"/> implementations.
/// Key material is never logged or echoed into failure messages.
/// </summary>
/// <typeparam name="TKey">The .NET key type for the algorithm.</typeparam>
/// <param name="logger">The logger.</param>
public abstract class PublicKeyVerifierBase<TKey>(ILogger logger) : IPublicKeyVerifier
    where TKey : AsymmetricAlgorithm
{
    /// <inheritdoc />
    public abstract PublicKeyAlgorithm Algorithm { get; }

    /// <summary>
    /// Gets the smallest accepted key size in bits.
    /// </summary>
    protected abstract int MinimumKeySizeBits { get; }

    /// <inheritdoc />
    public OperationResult<PublisherPublicKey> ImportPublicKey(string pem)
    {
        if (string.IsNullOrWhiteSpace(pem))
        {
            return RejectImport("The public key PEM is empty.");
        }

        var block = PublicKeyPem.ReadSingleBlock(pem);
        if (block.Failed)
        {
            return RejectImport(block.FirstError!);
        }

        var (label, der) = block.Data;
        try
        {
            using var key = CreateKey();
            var imported = ImportBlock(key, label, der);
            if (imported.Failed)
            {
                return RejectImport(imported.FirstError!);
            }

            var subjectPublicKeyInfo = key.ExportSubjectPublicKeyInfo();
            var publicKey = new PublisherPublicKey(
                Algorithm,
                Convert.ToBase64String(subjectPublicKeyInfo),
                ComputeFingerprint(subjectPublicKeyInfo));
            logger.LogDebug("Imported {Algorithm} publisher public key", Algorithm);
            return OperationResult<PublisherPublicKey>.CreateSuccess(publicKey);
        }
        catch (CryptographicException)
        {
            return RejectImport($"The PEM does not hold a valid {Algorithm} public key.");
        }
        catch (PlatformNotSupportedException)
        {
            return RejectImport($"The {Algorithm} public key is not supported on this platform.");
        }
    }

    /// <inheritdoc />
    public OperationResult Verify(byte[] payload, byte[] signature, PublisherPublicKey publicKey)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(signature);
        ArgumentNullException.ThrowIfNull(publicKey);

        if (publicKey.Algorithm != Algorithm)
        {
            return RejectVerification($"The key is a {publicKey.Algorithm} key, but this verifier handles {Algorithm}.");
        }

        var subjectPublicKeyInfo = DecodeSubjectPublicKeyInfo(publicKey);
        if (subjectPublicKeyInfo is null)
        {
            return RejectVerification("The stored public key is corrupt.");
        }

        var allowed = ValidateSubjectPublicKeyInfo(subjectPublicKeyInfo);
        if (allowed.Failed)
        {
            return RejectVerification(allowed.FirstError!);
        }

        try
        {
            using var key = CreateKey();
            key.ImportSubjectPublicKeyInfo(subjectPublicKeyInfo, out var bytesRead);
            var validated = ValidateKey(key, bytesRead, subjectPublicKeyInfo.Length);
            if (validated.Failed)
            {
                return RejectVerification(validated.FirstError!);
            }

            if (!VerifyData(key, payload, signature))
            {
                return RejectVerification("The signature does not match the payload and public key.");
            }

            return OperationResult.CreateSuccess();
        }
        catch (CryptographicException)
        {
            return RejectVerification($"The stored public key is not a valid {Algorithm} key.");
        }
        catch (PlatformNotSupportedException)
        {
            return RejectVerification($"The stored {Algorithm} public key is not supported on this platform.");
        }
    }

    /// <summary>
    /// Creates an empty key of the verifier's algorithm.
    /// </summary>
    /// <returns>The key instance.</returns>
    protected abstract TKey CreateKey();

    /// <summary>
    /// Verifies a SHA-256 signature with the imported key.
    /// </summary>
    /// <param name="key">The imported public key.</param>
    /// <param name="payload">The signed bytes.</param>
    /// <param name="signature">The signature bytes.</param>
    /// <returns>True when the signature is valid.</returns>
    protected abstract bool VerifyData(TKey key, byte[] payload, byte[] signature);

    /// <summary>
    /// Checks algorithm-specific policy on a SubjectPublicKeyInfo before it is imported,
    /// so acceptance does not depend on what the platform crypto library supports.
    /// Overrides must return a failure for malformed or unsupported input instead of throwing
    /// parsing exceptions; both import and verification rely on this non-throwing contract.
    /// </summary>
    /// <param name="subjectPublicKeyInfo">The DER SubjectPublicKeyInfo.</param>
    /// <returns>A success when the key is acceptable.</returns>
    protected virtual OperationResult ValidateSubjectPublicKeyInfo(byte[] subjectPublicKeyInfo)
    {
        return OperationResult.CreateSuccess();
    }

    /// <summary>
    /// Imports a key encoding other than SubjectPublicKeyInfo that the algorithm supports.
    /// </summary>
    /// <param name="key">The key to import into.</param>
    /// <param name="label">The PEM label.</param>
    /// <param name="der">The DER bytes.</param>
    /// <param name="bytesRead">The number of DER bytes consumed.</param>
    /// <returns>True when the label is supported and was imported.</returns>
    protected virtual bool TryImportAlternateFormat(TKey key, string label, byte[] der, out int bytesRead)
    {
        bytesRead = 0;
        return false;
    }

    private static string ComputeFingerprint(byte[] subjectPublicKeyInfo)
    {
        return Convert.ToHexString(SHA256.HashData(subjectPublicKeyInfo)).ToLowerInvariant();
    }

    private static byte[]? DecodeSubjectPublicKeyInfo(PublisherPublicKey publicKey)
    {
        if (string.IsNullOrEmpty(publicKey.SubjectPublicKeyInfo))
        {
            return null;
        }

        var subjectPublicKeyInfo = new byte[publicKey.SubjectPublicKeyInfo.Length];
        if (!Convert.TryFromBase64String(publicKey.SubjectPublicKeyInfo, subjectPublicKeyInfo, out var written))
        {
            return null;
        }

        subjectPublicKeyInfo = subjectPublicKeyInfo[..written];

        var fingerprint = ComputeFingerprint(subjectPublicKeyInfo);
        return string.Equals(fingerprint, publicKey.Fingerprint, StringComparison.OrdinalIgnoreCase)
            ? subjectPublicKeyInfo
            : null;
    }

    private OperationResult ImportBlock(TKey key, string label, byte[] der)
    {
        var bytesRead = 0;
        if (string.Equals(label, PublisherKeyConstants.SubjectPublicKeyInfoPemLabel, StringComparison.Ordinal))
        {
            var allowed = ValidateSubjectPublicKeyInfo(der);
            if (allowed.Failed)
            {
                return allowed;
            }

            key.ImportSubjectPublicKeyInfo(der, out bytesRead);
        }
        else if (!TryImportAlternateFormat(key, label, der, out bytesRead))
        {
            // The label is caller-controlled and may carry key material, so it is never echoed.
            return OperationResult.CreateFailure($"The PEM block type is not a supported {Algorithm} public key.");
        }

        return ValidateKey(key, bytesRead, der.Length);
    }

    private OperationResult ValidateKey(TKey key, int bytesRead, int length)
    {
        if (bytesRead != length)
        {
            return OperationResult.CreateFailure("The key data has trailing bytes.");
        }

        if (key.KeySize < MinimumKeySizeBits)
        {
            return OperationResult.CreateFailure(
                $"The {Algorithm} key is {key.KeySize} bits; at least {MinimumKeySizeBits} bits are required.");
        }

        return OperationResult.CreateSuccess();
    }

    private OperationResult<PublisherPublicKey> RejectImport(string reason)
    {
        logger.LogWarning("Rejected {Algorithm} publisher public key: {Reason}", Algorithm, reason);
        return OperationResult<PublisherPublicKey>.CreateFailure(reason);
    }

    private OperationResult RejectVerification(string reason)
    {
        logger.LogWarning("{Algorithm} publisher signature verification failed: {Reason}", Algorithm, reason);
        return OperationResult.CreateFailure(reason);
    }
}
