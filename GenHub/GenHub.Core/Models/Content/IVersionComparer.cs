namespace GenHub.Core.Models.Content;

/// <summary>
/// Interface for comparing versions to determine if one is newer than another.
/// Implementations can handle different version formats (semantic, date-based, etc.).
/// </summary>
public interface IVersionComparer
{
    /// <summary>
    /// Compares two version strings.
    /// </summary>
    /// <param name="version1">The first version.</param>
    /// <param name="version2">The second version.</param>
    /// <returns>
    /// A value indicating the relative order of the versions:
    /// Less than zero: version1 is older than version2.
    /// Zero: versions are equal.
    /// Greater than zero: version1 is newer than version2.
    /// </returns>
    int Compare(string version1, string version2);

    /// <summary>
    /// Gets a value indicating whether this comparer can handle the given version format.
    /// </summary>
    /// <param name="version">The version to check.</param>
    /// <returns>True if this comparer can handle the version; otherwise, false.</returns>
    bool CanParse(string version);
}
