namespace GenHub.Core.Models.Publishers;

/// <summary>
/// Hosting info for an uploaded artifact.
/// </summary>
public class ArtifactHostingInfo : HostedFileInfo
{
    /// <summary>
    /// Gets or sets the content ID this artifact belongs to.
    /// </summary>
    public string ContentId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the content name.
    /// </summary>
    public string ContentName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the version of the content.
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the SHA256 checksum of the artifact.
    /// </summary>
    public string? Sha256 { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this artifact is hosted on an external CDN.
    /// </summary>
    public bool IsExternalCdn { get; set; }
}
