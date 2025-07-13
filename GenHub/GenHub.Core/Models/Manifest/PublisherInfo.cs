namespace GenHub.Core.Models.Manifest;

/// <summary>
/// Publisher information for content attribution and support.
/// </summary>
public class PublisherInfo
{
    /// <summary>
    /// Gets or sets the publisher name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the publisher website.
    /// </summary>
    public string Website { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the publisher support URL.
    /// </summary>
    public string SupportUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the publisher contact email.
    /// </summary>
    public string ContactEmail { get; set; } = string.Empty;
}
