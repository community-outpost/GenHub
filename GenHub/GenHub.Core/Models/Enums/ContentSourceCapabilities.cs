namespace GenHub.Core.Models.Enums;

/// <summary>
/// Defines the capabilities of a content source to clarify what operations it supports.
/// </summary>
[Flags]
public enum ContentSourceCapabilities
{
    /// <summary>The provider has no special capabilities.</summary>
    None = 0,

    /// <summary>The provider can be searched directly without a separate discovery step.</summary>
    DirectSearch = 1,

    /// <summary>The provider requires a separate discovery/resolution step before it can be used.</summary>
    RequiresDiscovery = 2,

    /// <summary>The provider supports streaming content downloads.</summary>
    SupportsStreaming = 4,

    /// <summary>The provider can acquire and process package-based content (e.g., ZIP files).</summary>
    SupportsPackageAcquisition = 8,

    /// <summary>The provider can generate a manifest on the fly.</summary>
    SupportsManifestGeneration = 16,

    /// <summary>The provider can deliver content files directly to the local file system.</summary>
    LocalFileDelivery = 32,
}