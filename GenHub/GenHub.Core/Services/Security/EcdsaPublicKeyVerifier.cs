using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;

namespace GenHub.Core.Services.Security;

/// <summary>
/// Verifies ECDSA publisher signatures over SHA-256. Signatures are DER-encoded
/// (RFC 3279), the format produced by <c>openssl dgst -sha256 -sign</c>.
/// </summary>
/// <param name="logger">The logger.</param>
public sealed class EcdsaPublicKeyVerifier(ILogger<EcdsaPublicKeyVerifier> logger)
    : PublicKeyVerifierBase<ECDsa>(logger)
{
    /// <inheritdoc />
    public override PublicKeyAlgorithm Algorithm => PublicKeyAlgorithm.Ecdsa;

    /// <inheritdoc />
    protected override int MinimumKeySizeBits => PublisherKeyConstants.MinimumEcdsaKeySizeBits;

    /// <inheritdoc />
    protected override ECDsa CreateKey() => ECDsa.Create();

    /// <inheritdoc />
    protected override bool VerifyData(ECDsa key, byte[] payload, byte[] signature)
    {
        return key.VerifyData(payload, signature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
    }
}
