using GenHub.Core.Models.Security;

namespace GenHub.Core.Services.Security;

/// <summary>
/// On-disk shape of the trusted publisher key store.
/// </summary>
internal sealed class PublisherKeyStoreDocument
{
    /// <summary>
    /// Gets or sets the store schema version.
    /// </summary>
    public int SchemaVersion { get; set; }

    /// <summary>
    /// Gets or sets the trusted keys.
    /// </summary>
    public List<TrustedPublisherKey>? Keys { get; set; }
}
