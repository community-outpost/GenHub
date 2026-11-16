namespace GenHub.Core.Models.Tools.ModBuilder;

/// <summary>
/// Represents the change detection status of a file in the build system.
/// </summary>
public enum BuildFileStatus
{
    /// <summary>
    /// Status has not been determined yet.
    /// </summary>
    Unknown,

    /// <summary>
    /// File is marked as irrelevant by the file hash registry.
    /// </summary>
    Irrelevant,

    /// <summary>
    /// File exists and has not changed since the last build.
    /// </summary>
    Unchanged,

    /// <summary>
    /// File was removed from the source.
    /// </summary>
    Removed,

    /// <summary>
    /// File is expected but missing from the source.
    /// </summary>
    Missing,

    /// <summary>
    /// File is new and was not present in the previous build.
    /// </summary>
    Added,

    /// <summary>
    /// File exists but has been modified since the last build.
    /// </summary>
    Changed,
}
