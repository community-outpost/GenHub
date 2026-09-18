namespace GenHub.Core.Models.GenLauncher;

/// <summary>
/// Represents an individual file listed in an S3 bucket for a GenLauncher modification.
/// </summary>
public class GenLauncherS3FileEntry
{
    /// <summary>Gets or sets the full S3 object key.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Gets or sets the relative file path within the game directory.</summary>
    public string RelativePath { get; set; } = string.Empty;

    /// <summary>Gets or sets the ETag (MD5 checksum) of the file.</summary>
    public string ETag { get; set; } = string.Empty;

    /// <summary>Gets or sets the file size in bytes.</summary>
    public long Size { get; set; }

    /// <summary>Gets or sets the direct HTTP download URL for the file.</summary>
    public string DownloadUrl { get; set; } = string.Empty;
}
