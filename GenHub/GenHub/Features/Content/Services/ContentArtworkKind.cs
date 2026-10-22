namespace GenHub.Features.Content.Services;

/// <summary>
/// Artwork slots persisted per stored manifest.
/// </summary>
public enum ContentArtworkKind
{
    /// <summary>
    /// Small square artwork shown on cards and headers.
    /// </summary>
    Icon,

    /// <summary>
    /// Wide banner artwork shown on cards and detail headers.
    /// </summary>
    Cover,
}
