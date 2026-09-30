using System.Text.Json.Serialization;

namespace GenHub.Core.Models.Enums;

/// <summary>
/// Public key algorithms accepted for publisher signature verification.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PublicKeyAlgorithm
{
    /// <summary>
    /// RSA with PKCS#1 v1.5 padding over SHA-256.
    /// </summary>
    Rsa = 0,

    /// <summary>
    /// ECDSA over SHA-256 with DER-encoded signatures.
    /// </summary>
    Ecdsa = 1,
}
