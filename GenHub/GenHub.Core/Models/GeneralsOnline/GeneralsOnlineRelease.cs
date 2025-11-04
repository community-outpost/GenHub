using System;

namespace GenHub.Core.Models.GeneralsOnline;

/// <summary>
/// Represents a Generals Online release with version information and download URLs.
/// This model represents release information from the Generals Online CDN.
/// Currently uses hardcoded patterns and placeholder sizes until manifest.json API is available.
/// </summary>
public class GeneralsOnlineRelease
{
    /// <summary>
    /// Gets version string in format: MMDDYY_QFE# (e.g., "101525_QFE5").
    /// </summary>
    required public string Version { get; init; }

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
    required public string PortableUrl { get; init; }

    /// <summary>
    /// Gets size of portable ZIP in bytes.
    /// NOTE: Currently a placeholder constant until manifest.json API is available.
    /// </summary>
    public long PortableSize { get; init; }

    /// <summary>
    /// Gets release changelog/notes.
    /// </summary>
    public string? Changelog { get; init; }
}
