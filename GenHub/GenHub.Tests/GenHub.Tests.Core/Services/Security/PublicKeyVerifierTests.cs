using GenHub.Core.Interfaces.Security;
using GenHub.Core.Models.Enums;
using GenHub.Core.Services.Security;
using System.Security.Cryptography;
using System.Text;

namespace GenHub.Tests.Core.Services.Security;

/// <summary>
/// Tests for <see cref="RsaPublicKeyVerifier"/> and <see cref="EcdsaPublicKeyVerifier"/>.
/// </summary>
public sealed class PublicKeyVerifierTests
{
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

        Assert.All(rsaPrivatePems, pem => Assert.False(CreateRsaVerifier().ImportPublicKey(pem).Success));
        Assert.All(ecdsaPrivatePems, pem => Assert.False(CreateEcdsaVerifier().ImportPublicKey(pem).Success));
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

        Assert.False(CreateRsaVerifier().ImportPublicKey(pem).Success);
    }

    /// <summary>
    /// RSA keys below the minimum size are rejected.
    /// </summary>
    [Fact]
    public void ImportPublicKey_WeakRsaKey_Fails()
    {
        using var rsa = RSA.Create(1024);

        Assert.False(CreateRsaVerifier().ImportPublicKey(rsa.ExportSubjectPublicKeyInfoPem()).Success);
    }

    /// <summary>
    /// A key of the other algorithm is rejected at import.
    /// </summary>
    [Fact]
    public void ImportPublicKey_WrongAlgorithm_Fails()
    {
        using var rsa = RSA.Create(2048);
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        Assert.False(CreateRsaVerifier().ImportPublicKey(ecdsa.ExportSubjectPublicKeyInfoPem()).Success);
        Assert.False(CreateEcdsaVerifier().ImportPublicKey(rsa.ExportSubjectPublicKeyInfoPem()).Success);
        Assert.False(CreateEcdsaVerifier().ImportPublicKey(rsa.ExportRSAPublicKeyPem()).Success);
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

        Assert.False(rsaVerifier.Verify(Tamper(Payload), rsaSignature, rsaKey).Success);
        Assert.False(rsaVerifier.Verify(Payload, Tamper(rsaSignature), rsaKey).Success);
        Assert.False(ecdsaVerifier.Verify(Tamper(Payload), ecdsaSignature, ecdsaKey).Success);
        Assert.False(ecdsaVerifier.Verify(Payload, Tamper(ecdsaSignature), ecdsaKey).Success);
        Assert.False(ecdsaVerifier.Verify(Payload, [], ecdsaKey).Success);
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

        Assert.False(verifier.Verify(Payload, signature, key).Success);
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

        Assert.False(verifier.Verify(Payload, pssSignature, key).Success);
        Assert.False(verifier.Verify(Payload, sha512Signature, key).Success);
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

        Assert.False(CreateRsaVerifier().Verify(Payload, signature, ecdsaKey).Success);
        Assert.False(CreateRsaVerifier().Verify(Payload, signature, relabelled).Success);
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

        Assert.False(verifier.Verify(Payload, signature, key with { Fingerprint = new string('0', 64) }).Success);
        Assert.False(verifier.Verify(Payload, signature, key with { SubjectPublicKeyInfo = "@@not-base64@@" }).Success);
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
