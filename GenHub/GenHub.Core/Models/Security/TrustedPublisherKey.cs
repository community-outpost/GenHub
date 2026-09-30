namespace GenHub.Core.Models.Security;

/// <summary>
/// A publisher public key the user trusts for a given publisher ID.
/// </summary>
/// <param name="PublisherId">The publisher identifier the key belongs to.</param>
/// <param name="PublicKey">The trusted public key.</param>
/// <param name="TrustedAt">When the key was first trusted, in UTC.</param>
public sealed record TrustedPublisherKey(
    string PublisherId,
    PublisherPublicKey PublicKey,
    DateTimeOffset TrustedAt);
