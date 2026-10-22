using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;

namespace GenHub.Core.Services.Security;

/// <summary>
/// Verifies RSA publisher signatures: PKCS#1 v1.5 padding over SHA-256.
/// PKCS#1 v1.5 is chosen over PSS because it is deterministic and is the default of
/// common signing tools such as <c>openssl dgst -sha256 -sign</c>.
/// </summary>
/// <param name="logger">The logger.</param>
public sealed class RsaPublicKeyVerifier(ILogger<RsaPublicKeyVerifier> logger)
    : PublicKeyVerifierBase<RSA>(logger)
{
    /// <inheritdoc />
    public override PublicKeyAlgorithm Algorithm => PublicKeyAlgorithm.Rsa;

    /// <inheritdoc />
    protected override int MinimumKeySizeBits => PublisherKeyConstants.MinimumRsaKeySizeBits;

    /// <inheritdoc />
    protected override RSA CreateKey() => RSA.Create();

    /// <inheritdoc />
    protected override bool VerifyData(RSA key, byte[] payload, byte[] signature)
    {
        return key.VerifyData(payload, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    }

    /// <inheritdoc />
    protected override bool TryImportAlternateFormat(RSA key, string label, byte[] der, out int bytesRead)
    {
        if (!string.Equals(label, PublisherKeyConstants.RsaPublicKeyPemLabel, StringComparison.Ordinal))
        {
            bytesRead = 0;
            return false;
        }

        key.ImportRSAPublicKey(der, out bytesRead);
        return true;
    }
}
