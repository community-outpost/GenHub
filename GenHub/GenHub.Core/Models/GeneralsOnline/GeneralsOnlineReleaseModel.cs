using GenHub.Core.Constants;

namespace GenHub.Core.Models.GeneralsOnline;

/// <summary>
/// Represents a Generals Online release with version information and download URLs.
/// This model represents release information from the Generals Online CDN.
/// Currently uses hardcoded patterns and placeholder sizes until manifest.json API is available.
/// </summary>
public class GeneralsOnlineReleaseModel
{
    /// <summary>
    /// Gets version string in format: MMDDYY_QFE# (e.g., "101525_QFE5").
    /// </summary>
    public required string Version { get; init; }

    /// <summary>
    /// Gets date encoded in version (e.g., 101525 = October 15, 2025).
    /// </summary>
    public DateTime VersionDate { get; init; }

    /// <summary>
    /// Gets actual release date.
    /// </summary>
    public DateTime ReleaseDate { get; init; }

    /// <summary>
    /// Gets URL to portable ZIP.
    /// This is the format GenHub uses for content delivery.
    /// </summary>
    public required string PortableUrl { get; init; }

    /// <summary>
    /// Gets size of portable ZIP in bytes.
    /// Null when size is unknown (e.g., from latest.txt API).
    /// </summary>
    public long? PortableSize { get; init; }

    /// <summary>
    /// Gets release changelog/notes.
    /// </summary>
    public string? Changelog { get; init; }
}
