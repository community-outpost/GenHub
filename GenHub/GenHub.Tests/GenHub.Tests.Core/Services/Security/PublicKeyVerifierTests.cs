using GenHub.Core.Interfaces.Security;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Security;
using GenHub.Core.Services.Security;
using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Text;

namespace GenHub.Tests.Core.Services.Security;

/// <summary>
/// Tests for <see cref="RsaPublicKeyVerifier"/> and <see cref="EcdsaPublicKeyVerifier"/>.
/// </summary>
public sealed class PublicKeyVerifierTests
{
    private const string SignatureMismatch = "signature does not match";

    private const string EcPublicKeyOid = "1.2.840.10045.2.1";

    private static readonly byte[] Payload = Encoding.UTF8.GetBytes("{\"publisher\":{\"id\":\"test-publisher\"}}");

    private readonly CapturingLogger<RsaPublicKeyVerifier> _rsaLogger = new();
    private readonly CapturingLogger<EcdsaPublicKeyVerifier> _ecdsaLogger = new();

    /// <summary>
    /// An RSA SubjectPublicKeyInfo PEM imports and records the algorithm and a SHA-256 fingerprint.
    /// </summary>
    [Fact]
    public void ImportPublicKey_RsaSubjectPublicKeyInfoPem_Succeeds()
    {
        using var rsa = RSA.Create(2048);

        var result = CreateRsaVerifier().ImportPublicKey(rsa.ExportSubjectPublicKeyInfoPem());

        Assert.True(result.Success, result.FirstError);
        Assert.Equal(PublicKeyAlgorithm.Rsa, result.Data!.Algorithm);
        Assert.Equal(Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo()), result.Data.SubjectPublicKeyInfo);
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(rsa.ExportSubjectPublicKeyInfo())).ToLowerInvariant(),
            result.Data.Fingerprint);
    }

    /// <summary>
    /// A PKCS#1 RSA PUBLIC KEY PEM imports and is normalised to SubjectPublicKeyInfo.
    /// </summary>
    [Fact]
    public void ImportPublicKey_RsaPkcs1PublicKeyPem_NormalisesToSubjectPublicKeyInfo()
    {
        using var rsa = RSA.Create(2048);

        var result = CreateRsaVerifier().ImportPublicKey(rsa.ExportRSAPublicKeyPem());

        Assert.True(result.Success, result.FirstError);
        Assert.Equal(Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo()), result.Data!.SubjectPublicKeyInfo);
    }

    /// <summary>
    /// An EC SubjectPublicKeyInfo PEM imports through the ECDSA verifier.
    /// </summary>
    [Fact]
    public void ImportPublicKey_EcdsaSubjectPublicKeyInfoPem_Succeeds()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var result = CreateEcdsaVerifier().ImportPublicKey(ecdsa.ExportSubjectPublicKeyInfoPem());

        Assert.True(result.Success, result.FirstError);
        Assert.Equal(PublicKeyAlgorithm.Ecdsa, result.Data!.Algorithm);
        Assert.Equal(Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo()), result.Data.SubjectPublicKeyInfo);
    }

    /// <summary>
    /// Malformed or empty PEM input is rejected through the result, never an exception.
    /// </summary>
    /// <param name="pem">The malformed input.</param>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a pem at all")]
    [InlineData("-----BEGIN PUBLIC KEY-----\n!!!!notbase64!!!!\n-----END PUBLIC KEY-----")]
    [InlineData("-----BEGIN PUBLIC KEY-----\nAAAA\n-----END PUBLIC KEY-----")]
    [InlineData("-----BEGIN CERTIFICATE-----\nAAAA\n-----END CERTIFICATE-----")]
    public void ImportPublicKey_MalformedPem_Fails(string pem)
    {
        Assert.False(CreateRsaVerifier().ImportPublicKey(pem).Success);
        Assert.False(CreateEcdsaVerifier().ImportPublicKey(pem).Success);
    }

    /// <summary>
    /// Private-key PEM is rejected in every encoding, so a publisher cannot leak a signing key into the store.
    /// </summary>
    [Fact]
    public void ImportPublicKey_PrivateKeyPem_Fails()
    {
        using var rsa = RSA.Create(2048);
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var password = "password"u8.ToArray();
        var pbe = new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 1000);

        string[] rsaPrivatePems =
        [
            rsa.ExportPkcs8PrivateKeyPem(),
            rsa.ExportRSAPrivateKeyPem(),
            rsa.ExportEncryptedPkcs8PrivateKeyPem(password, pbe),
            rsa.ExportSubjectPublicKeyInfoPem() + "\n" + rsa.ExportPkcs8PrivateKeyPem(),
        ];
        string[] ecdsaPrivatePems =
        [
            ecdsa.ExportPkcs8PrivateKeyPem(),
            ecdsa.ExportECPrivateKeyPem(),
            ecdsa.ExportSubjectPublicKeyInfoPem() + "\n" + ecdsa.ExportECPrivateKeyPem(),
        ];

        Assert.All(rsaPrivatePems, pem => AssertFailsWith(CreateRsaVerifier().ImportPublicKey(pem), "Private keys"));
        Assert.All(ecdsaPrivatePems, pem => AssertFailsWith(CreateEcdsaVerifier().ImportPublicKey(pem), "Private keys"));
    }

    /// <summary>
    /// A PEM holding two public keys is ambiguous and rejected.
    /// </summary>
    [Fact]
    public void ImportPublicKey_MultiplePublicKeys_Fails()
    {
        using var first = RSA.Create(2048);
        using var second = RSA.Create(2048);

        var pem = first.ExportSubjectPublicKeyInfoPem() + "\n" + second.ExportSubjectPublicKeyInfoPem();

        AssertFailsWith(CreateRsaVerifier().ImportPublicKey(pem), "more than one");
    }

    /// <summary>
    /// RSA keys below the minimum size are rejected.
    /// </summary>
    [Fact]
    public void ImportPublicKey_WeakRsaKey_Fails()
    {
        using var rsa = RSA.Create(1024);

        AssertFailsWith(CreateRsaVerifier().ImportPublicKey(rsa.ExportSubjectPublicKeyInfoPem()), "at least 2048 bits");
    }

    /// <summary>
    /// A key of the other algorithm is rejected at import.
    /// </summary>
    [Fact]
    public void ImportPublicKey_WrongAlgorithm_Fails()
    {
        using var rsa = RSA.Create(2048);
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        AssertFailsWith(CreateRsaVerifier().ImportPublicKey(ecdsa.ExportSubjectPublicKeyInfoPem()), "does not hold a valid Rsa public key");
        AssertFailsWith(CreateEcdsaVerifier().ImportPublicKey(rsa.ExportSubjectPublicKeyInfoPem()), "not an EC public key");
        AssertFailsWith(CreateEcdsaVerifier().ImportPublicKey(rsa.ExportRSAPublicKeyPem()), "PEM block type is not a supported Ecdsa public key");
    }

    /// <summary>
    /// An RSA PKCS#1 v1.5 SHA-256 signature produced in-test verifies.
    /// </summary>
    [Fact]
    public void Verify_RsaSignature_Succeeds()
    {
        using var rsa = RSA.Create(2048);
        var verifier = CreateRsaVerifier();
        var key = verifier.ImportPublicKey(rsa.ExportSubjectPublicKeyInfoPem()).Data!;
        var signature = rsa.SignData(Payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        var result = verifier.Verify(Payload, signature, key);

        Assert.True(result.Success, result.FirstError);
    }

    /// <summary>
    /// An ECDSA SHA-256 DER signature produced in-test verifies.
    /// </summary>
    [Fact]
    public void Verify_EcdsaSignature_Succeeds()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var verifier = CreateEcdsaVerifier();
        var key = verifier.ImportPublicKey(ecdsa.ExportSubjectPublicKeyInfoPem()).Data!;
        var signature = ecdsa.SignData(Payload, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);

        var result = verifier.Verify(Payload, signature, key);

        Assert.True(result.Success, result.FirstError);
    }

    /// <summary>
    /// A tampered payload or signature fails verification for both algorithms.
    /// </summary>
    [Fact]
    public void Verify_TamperedPayloadOrSignature_Fails()
    {
        using var rsa = RSA.Create(2048);
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var rsaVerifier = CreateRsaVerifier();
        var ecdsaVerifier = CreateEcdsaVerifier();
        var rsaKey = rsaVerifier.ImportPublicKey(rsa.ExportSubjectPublicKeyInfoPem()).Data!;
        var ecdsaKey = ecdsaVerifier.ImportPublicKey(ecdsa.ExportSubjectPublicKeyInfoPem()).Data!;
        var rsaSignature = rsa.SignData(Payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var ecdsaSignature = ecdsa.SignData(Payload, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);

        AssertFailsWith(rsaVerifier.Verify(Tamper(Payload), rsaSignature, rsaKey), SignatureMismatch);
        AssertFailsWith(rsaVerifier.Verify(Payload, Tamper(rsaSignature), rsaKey), SignatureMismatch);
        AssertFailsWith(ecdsaVerifier.Verify(Tamper(Payload), ecdsaSignature, ecdsaKey), SignatureMismatch);
        AssertFailsWith(ecdsaVerifier.Verify(Payload, Tamper(ecdsaSignature), ecdsaKey), SignatureMismatch);
        AssertFailsWith(ecdsaVerifier.Verify(Payload, [], ecdsaKey), SignatureMismatch);
    }

    /// <summary>
    /// A signature from a different key fails verification.
    /// </summary>
    [Fact]
    public void Verify_SignatureFromOtherKey_Fails()
    {
        using var signer = RSA.Create(2048);
        using var trusted = RSA.Create(2048);
        var verifier = CreateRsaVerifier();
        var key = verifier.ImportPublicKey(trusted.ExportSubjectPublicKeyInfoPem()).Data!;
        var signature = signer.SignData(Payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        AssertFailsWith(verifier.Verify(Payload, signature, key), SignatureMismatch);
    }

    /// <summary>
    /// A signature made with a different scheme or hash than the verifier contract fails.
    /// </summary>
    [Fact]
    public void Verify_WrongSignatureScheme_Fails()
    {
        using var rsa = RSA.Create(2048);
        var verifier = CreateRsaVerifier();
        var key = verifier.ImportPublicKey(rsa.ExportSubjectPublicKeyInfoPem()).Data!;

        var pssSignature = rsa.SignData(Payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
        var sha512Signature = rsa.SignData(Payload, HashAlgorithmName.SHA512, RSASignaturePadding.Pkcs1);

        AssertFailsWith(verifier.Verify(Payload, pssSignature, key), SignatureMismatch);
        AssertFailsWith(verifier.Verify(Payload, sha512Signature, key), SignatureMismatch);
    }

    /// <summary>
    /// A key record of the other algorithm is rejected by the verifier.
    /// </summary>
    [Fact]
    public void Verify_KeyOfOtherAlgorithm_Fails()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var ecdsaKey = CreateEcdsaVerifier().ImportPublicKey(ecdsa.ExportSubjectPublicKeyInfoPem()).Data!;
        var signature = ecdsa.SignData(Payload, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        var relabelled = ecdsaKey with { Algorithm = PublicKeyAlgorithm.Rsa };

        AssertFailsWith(CreateRsaVerifier().Verify(Payload, signature, ecdsaKey), "this verifier handles Rsa");
        AssertFailsWith(CreateRsaVerifier().Verify(Payload, signature, relabelled), "not a valid Rsa key");
    }

    /// <summary>
    /// A key record whose stored bytes no longer match its fingerprint, or are not base64, fails.
    /// </summary>
    [Fact]
    public void Verify_CorruptKeyRecord_Fails()
    {
        using var rsa = RSA.Create(2048);
        var verifier = CreateRsaVerifier();
        var key = verifier.ImportPublicKey(rsa.ExportSubjectPublicKeyInfoPem()).Data!;
        var signature = rsa.SignData(Payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        AssertFailsWith(verifier.Verify(Payload, signature, key with { Fingerprint = new string('0', 64) }), "corrupt");
        AssertFailsWith(verifier.Verify(Payload, signature, key with { SubjectPublicKeyInfo = "@@not-base64@@" }), "corrupt");
    }

    /// <summary>
    /// Keys on each allowed NIST curve import and verify SHA-256 signatures.
    /// </summary>
    /// <param name="curveName">The curve friendly name.</param>
    [Theory]
    [InlineData("nistP256")]
    [InlineData("nistP384")]
    [InlineData("nistP521")]
    public void ImportAndVerify_AllowedCurves_Succeed(string curveName)
    {
        using var ecdsa = ECDsa.Create(ECCurve.CreateFromFriendlyName(curveName));
        var verifier = CreateEcdsaVerifier();
        var imported = verifier.ImportPublicKey(ecdsa.ExportSubjectPublicKeyInfoPem());
        var signature = ecdsa.SignData(Payload, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);

        Assert.True(imported.Success, imported.FirstError);
        var verified = verifier.Verify(Payload, signature, imported.Data!);
        Assert.True(verified.Success, verified.FirstError);
    }

    /// <summary>
    /// A hand-built SubjectPublicKeyInfo naming P-256 imports, which proves the builder used by the
    /// rejection tests produces valid keys.
    /// </summary>
    [Fact]
    public void ImportPublicKey_BuiltP256SubjectPublicKeyInfo_Succeeds()
    {
        var result = CreateEcdsaVerifier().ImportPublicKey(ToPem(BuildEcSubjectPublicKeyInfo("1.2.840.10045.3.1.7")));

        Assert.True(result.Success, result.FirstError);
    }

    /// <summary>
    /// Curves outside P-256, P-384 and P-521 are rejected at import on every platform, through
    /// the result rather than a platform exception. This covers secp256k1, which OpenSSL imports
    /// and macOS rejects, and the 224-bit P-224, all rejected by the named-curve allowlist.
    /// </summary>
    /// <param name="curveOid">The named curve OID.</param>
    [Theory]
    [InlineData("1.3.132.0.10")]
    [InlineData("1.3.132.0.33")]
    [InlineData("1.3.36.3.3.2.8.1.1.7")]
    public void ImportPublicKey_DisallowedCurve_FailsOnEveryPlatform(string curveOid)
    {
        var result = CreateEcdsaVerifier().ImportPublicKey(ToPem(BuildEcSubjectPublicKeyInfo(curveOid)));

        AssertFailsWith(result, "curve is not supported");
    }

    /// <summary>
    /// Trailing ASN.1 values are rejected by the policy gate before platform key import.
    /// </summary>
    /// <param name="location">The sequence containing an unexpected value.</param>
    [Theory]
    [InlineData("algorithm")]
    [InlineData("subject")]
    [InlineData("outer")]
    public void ImportAndVerify_TrailingEcData_FailsPolicyValidation(string location)
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence())
        {
            using (writer.PushSequence())
            {
                writer.WriteObjectIdentifier(EcPublicKeyOid);
                writer.WriteObjectIdentifier("1.2.840.10045.3.1.7");
                if (location == "algorithm")
                {
                    writer.WriteNull();
                }
            }

            writer.WriteBitString(CreateP256Point());
            if (location == "subject")
            {
                writer.WriteNull();
            }
        }

        if (location == "outer")
        {
            writer.WriteNull();
        }

        var spki = writer.Encode();
        var verifier = CreateEcdsaVerifier();
        AssertFailsWith(verifier.ImportPublicKey(ToPem(spki)), "not a valid EC SubjectPublicKeyInfo");
        AssertFailsWith(verifier.Verify(Payload, [1, 2, 3], CreateRecord(PublicKeyAlgorithm.Ecdsa, spki)), "not a valid EC SubjectPublicKeyInfo");
    }

    /// <summary>
    /// Explicit curve parameters are rejected: only named curves are accepted.
    /// </summary>
    [Fact]
    public void ImportPublicKey_ExplicitCurveParameters_Fails()
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence())
        {
            using (writer.PushSequence())
            {
                writer.WriteObjectIdentifier(EcPublicKeyOid);
                using (writer.PushSequence())
                {
                    writer.WriteInteger(1);
                }
            }

            writer.WriteBitString(CreateP256Point());
        }

        AssertFailsWith(CreateEcdsaVerifier().ImportPublicKey(ToPem(writer.Encode())), "named curve");
    }

    /// <summary>
    /// A stored key record on a disallowed curve fails verification even when its fingerprint matches.
    /// </summary>
    [Fact]
    public void Verify_DisallowedCurveRecord_Fails()
    {
        var spki = BuildEcSubjectPublicKeyInfo("1.3.132.0.10");

        AssertFailsWith(CreateEcdsaVerifier().Verify(Payload, [1, 2, 3], CreateRecord(PublicKeyAlgorithm.Ecdsa, spki)), "curve is not supported");
    }

    /// <summary>
    /// The PEM label of an unsupported block is never echoed, because a caller can place key material in it.
    /// </summary>
    [Fact]
    public void ImportPublicKey_UnsupportedLabel_IsNotEchoed()
    {
        const string label = "MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEA";
        var pem = $"-----BEGIN {label}-----\nAAAA\n-----END {label}-----";

        var result = CreateRsaVerifier().ImportPublicKey(pem);

        AssertFailsWith(result, "PEM block type is not a supported Rsa public key");
        Assert.DoesNotContain(label, string.Join("\n", result.Errors.Concat(_rsaLogger.Entries)), StringComparison.Ordinal);
    }

    /// <summary>
    /// Stored key records that pass the fingerprint check but are otherwise invalid fail at verify time.
    /// </summary>
    [Fact]
    public void Verify_InvalidRecordsWithMatchingFingerprint_Fail()
    {
        using var rsa = RSA.Create(2048);
        using var weak = RSA.Create(1024);
        var verifier = CreateRsaVerifier();
        var signature = rsa.SignData(Payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        byte[] trailing = [.. rsa.ExportSubjectPublicKeyInfo(), 0x00];

        AssertFailsWith(verifier.Verify(Payload, signature, CreateRecord(PublicKeyAlgorithm.Rsa, trailing)), "trailing bytes");
        AssertFailsWith(verifier.Verify(Payload, signature, CreateRecord(PublicKeyAlgorithm.Rsa, weak.ExportSubjectPublicKeyInfo())), "at least 2048 bits");
        AssertFailsWith(verifier.Verify(Payload, signature, CreateRecord(PublicKeyAlgorithm.Rsa, [0x30, 0x03, 0x01, 0x02, 0x03])), "not a valid Rsa key");
        AssertFailsWith(verifier.Verify(Payload, signature, CreateRecord(PublicKeyAlgorithm.Rsa, [])), "corrupt");
    }

    /// <summary>
    /// Key material never reaches log output or failure messages, for public or rejected private keys.
    /// </summary>
    [Fact]
    public void ImportAndVerify_NeverLogKeyMaterial()
    {
        using var rsa = RSA.Create(2048);
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var rsaVerifier = CreateRsaVerifier();
        var ecdsaVerifier = CreateEcdsaVerifier();
        var rsaPublicPem = rsa.ExportSubjectPublicKeyInfoPem();
        var rsaPrivatePem = rsa.ExportPkcs8PrivateKeyPem();
        var ecPrivatePem = ecdsa.ExportECPrivateKeyPem();

        List<string> errors = [];
        var rsaKey = rsaVerifier.ImportPublicKey(rsaPublicPem).Data!;
        errors.AddRange(rsaVerifier.ImportPublicKey(rsaPrivatePem).Errors);
        errors.AddRange(ecdsaVerifier.ImportPublicKey(ecPrivatePem).Errors);
        errors.AddRange(ecdsaVerifier.ImportPublicKey(rsaPublicPem).Errors);
        errors.AddRange(rsaVerifier.Verify(Payload, [1, 2, 3], rsaKey).Errors);
        errors.AddRange(rsaVerifier.Verify(Payload, [1, 2, 3], rsaKey with { Fingerprint = "x" }).Errors);

        var output = string.Join("\n", _rsaLogger.Entries.Concat(_ecdsaLogger.Entries).Concat(errors));
        Assert.NotEmpty(errors);
        Assert.NotEmpty(_rsaLogger.Entries);
        foreach (var secret in new[] { rsaPublicPem, rsaPrivatePem, ecPrivatePem })
        {
            Assert.DoesNotContain(PemBodyFragment(secret), output, StringComparison.Ordinal);
        }

        Assert.DoesNotContain(rsaKey.SubjectPublicKeyInfo[..24], output, StringComparison.Ordinal);
    }

    private static void AssertFailsWith(ResultBase result, string expectedReason)
    {
        Assert.False(result.Success);
        Assert.Contains(expectedReason, result.FirstError, StringComparison.Ordinal);
    }

    private static PublisherPublicKey CreateRecord(PublicKeyAlgorithm algorithm, byte[] subjectPublicKeyInfo)
    {
        return new PublisherPublicKey(
            algorithm,
            Convert.ToBase64String(subjectPublicKeyInfo),
            Convert.ToHexString(SHA256.HashData(subjectPublicKeyInfo)).ToLowerInvariant());
    }

    private static byte[] CreateP256Point()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parameters = ecdsa.ExportParameters(false);
        return [0x04, .. parameters.Q.X!, .. parameters.Q.Y!];
    }

    private static byte[] BuildEcSubjectPublicKeyInfo(string curveOid)
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence())
        {
            using (writer.PushSequence())
            {
                writer.WriteObjectIdentifier(EcPublicKeyOid);
                writer.WriteObjectIdentifier(curveOid);
            }

            writer.WriteBitString(CreateP256Point());
        }

        return writer.Encode();
    }

    private static string ToPem(byte[] subjectPublicKeyInfo)
    {
        return new string(PemEncoding.Write("PUBLIC KEY", subjectPublicKeyInfo));
    }

    private static byte[] Tamper(byte[] source)
    {
        var copy = (byte[])source.Clone();
        copy[copy.Length / 2] ^= 0x01;
        return copy;
    }

    private static string PemBodyFragment(string pem)
    {
        var body = pem.Split('\n')[1];
        return body[..24];
    }

    private IPublicKeyVerifier CreateRsaVerifier() => new RsaPublicKeyVerifier(_rsaLogger);

    private IPublicKeyVerifier CreateEcdsaVerifier() => new EcdsaPublicKeyVerifier(_ecdsaLogger);
}
