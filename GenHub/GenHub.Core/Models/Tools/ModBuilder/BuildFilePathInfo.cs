using System.Collections.Generic;
using MessagePack;

namespace GenHub.Core.Models.Tools.ModBuilder;

/// <summary>
/// Represents file metadata for change detection.
/// Serializable dataclass for build state persistence.
/// </summary>
[MessagePackObject]
public sealed class BuildFilePathInfo
{
    /// <summary>
    /// Gets or sets the file path.
    /// </summary>
    [Key(0)]
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the file modification time (Unix timestamp).
    /// </summary>
    [Key(1)]
    public double ModifiedTime { get; set; }

    /// <summary>
    /// Gets or sets the MD5 hash of the file.
    /// </summary>
    [Key(2)]
    public string Md5 { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the build parameters associated with this file.
    /// </summary>
    [Key(3)]
    public Dictionary<string, object>? Params { get; set; }

    /// <summary>
    /// Checks if this file info matches another based on MD5 and params.
    /// </summary>
    /// <param name="other">The other file info to compare with.</param>
    /// <returns>True if the file info matches; otherwise, false.</returns>
    public bool Matches(BuildFilePathInfo? other)
    {
        if (other == null)
            return false;

        if (Md5 != other.Md5)
            return false;

        // Compare params dictionaries
        if (Params == null && other.Params == null)
            return true;

        if (Params == null || other.Params == null)
            return false;

        if (Params.Count != other.Params.Count)
            return false;

        foreach (var kvp in Params)
        {
            if (!other.Params.TryGetValue(kvp.Key, out var otherValue))
                return false;

            if (!Equals(kvp.Value, otherValue))
                return false;
        }

        return true;
    }
}
