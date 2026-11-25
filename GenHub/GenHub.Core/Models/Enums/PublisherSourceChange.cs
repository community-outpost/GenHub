namespace GenHub.Core.Models.Enums;

/// <summary>
/// How a subscription candidate relates to an existing subscription with the same publisher ID.
/// </summary>
public enum PublisherSourceChange
{
    /// <summary>
    /// No subscription exists for the publisher ID, or the candidate uses the same source.
    /// </summary>
    None = 0,

    /// <summary>
    /// The candidate uses a new source and proves it holds the key already trusted for the publisher ID.
    /// </summary>
    VerifiedMove = 1,

    /// <summary>
    /// The candidate uses a new source and no key is trusted for the publisher ID, so neither source can be confirmed.
    /// </summary>
    Collision = 2,

    /// <summary>
    /// The candidate uses a new source but does not prove it holds the key trusted for the publisher ID.
    /// </summary>
    IdentityMismatch = 3,
}
