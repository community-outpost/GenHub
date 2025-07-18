namespace GenHub.Core.Models.Manifest;

/// <summary>
/// File permission specifications for cross-platform compatibility.
/// </summary>
public class FilePermissions
{
    /// <summary>
    /// Gets or sets a value indicating whether the file is read-only.
    /// </summary>
    public bool IsReadOnly { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the file requires elevation.
    /// </summary>
    public bool RequiresElevation { get; set; }

    /// <summary>
    /// Gets or sets the Unix file permissions string.
    /// </summary>
    public string UnixPermissions { get; set; } = "644";
}
