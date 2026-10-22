using GenHub.Core.Models.Enums;

namespace GenHub.Core.Models.Security;

/// <summary>
/// The result of comparing a subscription candidate with the existing subscription and trusted key.
/// </summary>
/// <param name="Change">How the candidate relates to the existing subscription.</param>
/// <param name="ProvenKey">The candidate's public key when its identity proof verifies with that key, otherwise null.</param>
/// <param name="ProofSignedAt">The signing time of a verified identity proof, otherwise null.</param>
public sealed record PublisherSourceAssessment(
    PublisherSourceChange Change,
    PublisherPublicKey? ProvenKey,
    DateTimeOffset? ProofSignedAt)
{
    /// <summary>
    /// Gets a value indicating whether the user must choose between replacing the existing publisher and skipping the candidate.
    /// </summary>
    public bool RequiresDecision => Change is PublisherSourceChange.Collision or PublisherSourceChange.IdentityMismatch;
}
