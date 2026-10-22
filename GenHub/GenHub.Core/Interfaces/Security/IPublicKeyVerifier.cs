using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Security;

namespace GenHub.Core.Interfaces.Security;

/// <summary>
/// Imports PEM-encoded publisher public keys and verifies SHA-256 signatures made with them.
/// One implementation exists per <see cref="PublicKeyAlgorithm"/>.
/// </summary>
public interface IPublicKeyVerifier
{
    /// <summary>
    /// Gets the algorithm this verifier handles.
    /// </summary>
    PublicKeyAlgorithm Algorithm { get; }

    /// <summary>
    /// Imports a PEM-encoded public key. Private keys, multiple PEM blocks, keys of another
    /// algorithm, keys below the minimum size, and EC keys outside P-256, P-384 and P-521 are rejected.
    /// </summary>
    /// <param name="pem">The PEM text holding exactly one PEM block, a public key. Text outside the block is ignored.</param>
    /// <returns>The normalised public key, or a failure describing why it was rejected.</returns>
    OperationResult<PublisherPublicKey> ImportPublicKey(string pem);

    /// <summary>
    /// Verifies a SHA-256 signature over a payload.
    /// </summary>
    /// <param name="payload">The signed bytes.</param>
    /// <param name="signature">The signature bytes.</param>
    /// <param name="publicKey">The public key to verify with.</param>
    /// <returns>A success only when the signature is valid for the payload and key.</returns>
    OperationResult Verify(byte[] payload, byte[] signature, PublisherPublicKey publicKey);
}
