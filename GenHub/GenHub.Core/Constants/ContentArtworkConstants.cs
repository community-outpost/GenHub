namespace GenHub.Core.Constants;

/// <summary>
/// Constants for locally persisted content artwork (icons and covers).
/// </summary>
public static class ContentArtworkConstants
{
    /// <summary>
    /// Gets the application-data subdirectory holding per-manifest artwork.
    /// </summary>
    public const string ArtworkDirectoryName = "Artwork";

    /// <summary>
    /// Gets the bundled fallback cover for Generals game clients without artwork.
    /// </summary>
    public const string GeneralsCoverSource = "/Assets/Covers/generals-cover.png";

    /// <summary>
    /// Gets the bundled fallback cover for Zero Hour game clients without artwork.
    /// </summary>
    public const string ZeroHourCoverSource = "/Assets/Covers/zerohour-cover.png";

    /// <summary>
    /// Gets the filename prefix used for in-flight temporary artwork files before atomic commit.
    /// </summary>
    public const string TempFilePrefix = ".tmp_";
}
