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
    /// Gets or sets the version of the content.
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the filename of the artifact.
    /// </summary>
    public string FileName { get; set; } = string.Empty;
}
