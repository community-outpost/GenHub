using GenHub.Core.Models.Enums;

namespace GenHub.Core.Models.Security;

/// <summary>
/// An imported publisher public key, normalised to X.509 SubjectPublicKeyInfo.
/// </summary>
/// <param name="Algorithm">The key algorithm.</param>
/// <param name="SubjectPublicKeyInfo">The base64-encoded DER SubjectPublicKeyInfo.</param>
/// <param name="Fingerprint">The lowercase hex SHA-256 of the SubjectPublicKeyInfo bytes.</param>
public sealed record PublisherPublicKey(
    PublicKeyAlgorithm Algorithm,
    string SubjectPublicKeyInfo,
    string Fingerprint);
