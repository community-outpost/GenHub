using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Security;
using GenHub.Core.Services.Security;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Cryptography;

namespace GenHub.Tests.Core.Services.Security;

/// <summary>
/// Builds signed publisher definitions for identity tests.
/// </summary>
internal static class PublisherIdentityTestData
{
    /// <summary>
    /// Creates an identity service over the given key store with the real RSA and ECDSA verifiers.
    /// </summary>
    /// <param name="keyStore">The key store.</param>
    /// <returns>The identity service.</returns>
    public static PublisherIdentityService CreateService(InMemoryPublisherKeyStore keyStore) =>
        new(
            keyStore,
            [
                new RsaPublicKeyVerifier(NullLogger<RsaPublicKeyVerifier>.Instance),
                new EcdsaPublicKeyVerifier(NullLogger<EcdsaPublicKeyVerifier>.Instance),
            ],
            NullLogger<PublisherIdentityService>.Instance);

    /// <summary>
    /// Imports the public half of an RSA key as a trusted key for the publisher.
    /// </summary>
    /// <param name="publisherId">The publisher ID.</param>
    /// <param name="rsa">The RSA key.</param>
    /// <returns>The trusted key record.</returns>
    public static TrustedPublisherKey Trust(string publisherId, RSA rsa)
    {
        var imported = new RsaPublicKeyVerifier(NullLogger<RsaPublicKeyVerifier>.Instance).ImportPublicKey(rsa.ExportSubjectPublicKeyInfoPem());
        return new TrustedPublisherKey(publisherId, imported.Data!, DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Imports the public half of an ECDSA key as a trusted key for the publisher.
    /// </summary>
    /// <param name="publisherId">The publisher ID.</param>
    /// <param name="ecdsa">The ECDSA key.</param>
    /// <returns>The trusted key record.</returns>
    public static TrustedPublisherKey Trust(string publisherId, ECDsa ecdsa)
    {
        var imported = new EcdsaPublicKeyVerifier(NullLogger<EcdsaPublicKeyVerifier>.Instance).ImportPublicKey(ecdsa.ExportSubjectPublicKeyInfoPem());
        return new TrustedPublisherKey(publisherId, imported.Data!, DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Creates a definition for the publisher hosted at the given URL, without an identity proof.
    /// </summary>
    /// <param name="publisherId">The publisher ID.</param>
    /// <param name="definitionUrl">The definition URL written in the definition.</param>
    /// <param name="catalogUrl">The catalog URL.</param>
    /// <returns>The definition.</returns>
    public static PublisherDefinition CreateDefinition(string publisherId, string definitionUrl, string catalogUrl) =>
        new()
        {
            Publisher = new PublisherProfile { Id = publisherId, Name = "Test Publisher" },
            Catalogs = [new CatalogEntry { Id = "main", Name = "Main", Url = catalogUrl }],
            DefinitionUrl = definitionUrl,
        };

    /// <summary>
    /// Signs an identity proof for the definition with an RSA key.
    /// </summary>
    /// <param name="definition">The definition to sign.</param>
    /// <param name="rsa">The RSA key.</param>
    /// <param name="signedAt">The signing time text.</param>
    /// <returns>The same definition with its identity set.</returns>
    public static PublisherDefinition SignWith(PublisherDefinition definition, RSA rsa, string signedAt)
    {
        var payload = PublisherIdentityService.BuildProofPayload(definition.Publisher.Id, definition.DefinitionUrl, signedAt)!;
        definition.Identity = new PublisherIdentityProof
        {
            PublicKey = rsa.ExportSubjectPublicKeyInfoPem(),
            SignedAt = signedAt,
            Signature = Convert.ToBase64String(rsa.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)),
        };
        return definition;
    }

    /// <summary>
    /// Signs an identity proof for the definition with an ECDSA key.
    /// </summary>
    /// <param name="definition">The definition to sign.</param>
    /// <param name="ecdsa">The ECDSA key.</param>
    /// <param name="signedAt">The signing time text.</param>
    /// <returns>The same definition with its identity set.</returns>
    public static PublisherDefinition SignWith(PublisherDefinition definition, ECDsa ecdsa, string signedAt)
    {
        var payload = PublisherIdentityService.BuildProofPayload(definition.Publisher.Id, definition.DefinitionUrl, signedAt)!;
        definition.Identity = new PublisherIdentityProof
        {
            PublicKey = ecdsa.ExportSubjectPublicKeyInfoPem(),
            SignedAt = signedAt,
            Signature = Convert.ToBase64String(ecdsa.SignData(payload, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence)),
        };
        return definition;
    }
}
