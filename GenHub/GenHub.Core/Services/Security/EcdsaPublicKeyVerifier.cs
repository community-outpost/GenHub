using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;
using System.Formats.Asn1;
using System.Security.Cryptography;

namespace GenHub.Core.Services.Security;

/// <summary>
/// Verifies ECDSA publisher signatures over SHA-256. Signatures are DER-encoded
/// (RFC 3279), the format produced by <c>openssl dgst -sha256 -sign</c>.
/// Only the named curves P-256, P-384 and P-521 are accepted, checked by OID before import,
/// so the same keys are accepted on every platform.
/// </summary>
/// <param name="logger">The logger.</param>
public sealed class EcdsaPublicKeyVerifier(ILogger<EcdsaPublicKeyVerifier> logger)
    : PublicKeyVerifierBase<ECDsa>(logger)
{
    private static readonly HashSet<string> AllowedCurveOids =
    [
        PublisherKeyConstants.NistP256Oid,
        PublisherKeyConstants.NistP384Oid,
        PublisherKeyConstants.NistP521Oid,
    ];

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

    /// <inheritdoc />
    protected override OperationResult ValidateSubjectPublicKeyInfo(byte[] subjectPublicKeyInfo)
    {
        try
        {
            var reader = new AsnReader(subjectPublicKeyInfo, AsnEncodingRules.DER);
            var subjectPublicKey = reader.ReadSequence();
            reader.ThrowIfNotEmpty();
            var algorithmIdentifier = subjectPublicKey.ReadSequence();
            if (algorithmIdentifier.ReadObjectIdentifier() != PublisherKeyConstants.EcPublicKeyOid)
            {
                return OperationResult.CreateFailure("The key is not an EC public key.");
            }

            if (!algorithmIdentifier.PeekTag().HasSameClassAndValue(Asn1Tag.ObjectIdentifier))
            {
                return OperationResult.CreateFailure("The EC key must use a named curve.");
            }

            var curveOid = algorithmIdentifier.ReadObjectIdentifier();
            algorithmIdentifier.ThrowIfNotEmpty();
            subjectPublicKey.ReadBitString(out _);
            subjectPublicKey.ThrowIfNotEmpty();

            return AllowedCurveOids.Contains(curveOid)
                ? OperationResult.CreateSuccess()
                : OperationResult.CreateFailure("The EC curve is not supported. Use P-256, P-384 or P-521.");
        }
        catch (AsnContentException)
        {
            return OperationResult.CreateFailure("The key is not a valid EC SubjectPublicKeyInfo.");
        }
    }
}
