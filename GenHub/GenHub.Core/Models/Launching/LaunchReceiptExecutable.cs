namespace GenHub.Core.Models.Launching;

/// <summary>
/// Fingerprint of the executable a launch started.
/// </summary>
public class LaunchReceiptExecutable
{
    /// <summary>Gets or sets the executable path.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Gets or sets the executable size in bytes.</summary>
    public long SizeBytes { get; set; }

    /// <summary>Gets or sets the executable's last write time, in UTC.</summary>
    public DateTime LastWriteUtc { get; set; }

    /// <summary>Gets or sets the SHA-256 hash of the executable as a lowercase hex string.</summary>
    public string Sha256 { get; set; } = string.Empty;
}
