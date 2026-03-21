using System.Collections.Generic;

namespace GenHub.Core.Interfaces.Content;

/// <summary>
/// Result of GenLauncher file detection.
/// </summary>
public class GenLauncherDetectionResult
{
    /// <summary>
    /// Whether any GenLauncher files were detected.
    /// </summary>
    public bool HasGenLauncherFiles { get; set; }

    /// <summary>
    /// List of .gib files found.
    /// </summary>
    public List<string> GibFiles { get; set; } = [];

    /// <summary>
    /// List of files with .GLR suffix.
    /// </summary>
    public List<string> GlrFiles { get; set; } = [];

    /// <summary>
    /// List of files with .GOF suffix.
    /// </summary>
    public List<string> GofFiles { get; set; } = [];

    /// <summary>
    /// List of files with .GLTC suffix.
    /// </summary>
    public List<string> GltcFiles { get; set; } = [];

    /// <summary>
    /// List of symbolic links detected.
    /// </summary>
    public List<string> SymbolicLinks { get; set; } = [];

    /// <summary>
    /// Total count of affected files.
    /// </summary>
    public int TotalAffectedFiles =>
        GibFiles.Count + GlrFiles.Count + GofFiles.Count + GltcFiles.Count + SymbolicLinks.Count;

    /// <summary>
    /// Gets a user-friendly summary of detected files.
    /// </summary>
    /// <returns>Summary string.</returns>
    public string GetSummary()
    {
        var parts = new List<string>();
        if (GibFiles.Count > 0)
        {
            parts.Add($"{GibFiles.Count} .gib file(s)");
        }

        if (GlrFiles.Count > 0)
        {
            parts.Add($"{GlrFiles.Count} .GLR file(s)");
        }

        if (GofFiles.Count > 0)
        {
            parts.Add($"{GofFiles.Count} .GOF file(s)");
        }

        if (GltcFiles.Count > 0)
        {
            parts.Add($"{GltcFiles.Count} .GLTC file(s)");
        }

        if (SymbolicLinks.Count > 0)
        {
            parts.Add($"{SymbolicLinks.Count} symbolic link(s)");
        }

        return parts.Count > 0 ? string.Join(", ", parts) : "No GenLauncher files detected";
    }
}
