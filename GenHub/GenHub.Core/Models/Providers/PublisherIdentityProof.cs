using System.Text.Json.Serialization;

namespace GenHub.Core.Models.Providers;

/// <summary>
/// The publisher's public key and a signature that proves the definition at
/// <see cref="PublisherDefinition.DefinitionUrl"/> was published by the holder of the matching private key.
/// </summary>
/// <remarks>
/// The signature covers the UTF-8 bytes of four lines joined by a line feed, with no trailing line feed:
/// <see cref="Constants.PublisherKeyConstants.IdentityProofDomain"/>, the publisher ID, the definition URL,
/// and <see cref="SignedAt"/>, each exactly as written in the definition.
/// </remarks>
public class PublisherIdentityProof
{
    /// <summary>
    /// Gets or sets the PEM-encoded RSA or ECDSA public key.
    /// </summary>
    [JsonPropertyName("publicKey")]
    public string? PublicKey { get; set; }

    /// <summary>
    /// Gets or sets when the proof was signed, as an ISO 8601 timestamp with an offset.
    /// </summary>
    [JsonPropertyName("signedAt")]
    public string? SignedAt { get; set; }

    /// <summary>
    /// Gets or sets the base64-encoded SHA-256 signature over the proof payload.
    /// </summary>
    [JsonPropertyName("signature")]
    public string? Signature { get; set; }
}
