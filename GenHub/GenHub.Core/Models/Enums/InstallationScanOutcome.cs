namespace GenHub.Core.Models.Enums;

/// <summary>
/// Records the result of the most recent completed game installation scan.
/// </summary>
public enum InstallationScanOutcome
{
    /// <summary>No scan has completed yet.</summary>
    NotScanned = 0,

    /// <summary>The scan found at least one game installation.</summary>
    FoundInstallations = 1,

    /// <summary>The scan completed and found no game installation.</summary>
    FoundNothing = 2,
}
