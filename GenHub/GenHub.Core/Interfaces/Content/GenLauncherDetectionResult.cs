using System.Collections.Generic;
using GenHub.Core.Constants;

namespace GenHub.Core.Interfaces.Content;

/// <summary>
/// Result of GenLauncher file detection.
/// </summary>
public class GenLauncherDetectionResult
{
    /// <summary>
    /// Gets or sets a value indicating whether any GenLauncher files were detected.
    /// </summary>
    public bool HasGenLauncherFiles { get; set; }

    /// <summary>
    /// Gets or sets the list of .gib files found.
    /// </summary>
    public List<string> GibFiles { get; set; } = [];

    /// <summary>
    /// Gets or sets the list of files with .GLR suffix.
    /// </summary>
    public List<string> GlrFiles { get; set; } = [];

    /// <summary>
    /// Gets or sets the list of files with .GOF suffix.
    /// </summary>
    public List<string> GofFiles { get; set; } = [];

    /// <summary>
    /// Gets or sets the list of files with .GLTC suffix.
    /// </summary>
    public List<string> GltcFiles { get; set; } = [];

    /// <summary>
    /// Gets or sets the list of symbolic links detected.
    /// </summary>
    public List<string> SymbolicLinks { get; set; } = [];

    /// <summary>
    /// Gets the total count of affected files.
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
            parts.Add($"{GibFiles.Count} {GenLauncherConstants.GibExtension} file(s)");
        }

        if (GlrFiles.Count > 0)
        {
            parts.Add($"{GlrFiles.Count} {GenLauncherConstants.ReplaceSuffix} file(s)");
        }

        if (GofFiles.Count > 0)
        {
            parts.Add($"{GofFiles.Count} {GenLauncherConstants.OriginalFileSuffix} file(s)");
        }

        if (GltcFiles.Count > 0)
        {
            parts.Add($"{GltcFiles.Count} {GenLauncherConstants.TempCopySuffix} file(s)");
        }

        if (SymbolicLinks.Count > 0)
        {
            parts.Add($"{SymbolicLinks.Count} symbolic link(s)");
        }

        return parts.Count > 0 ? string.Join(", ", parts) : "No GenLauncher files detected";
    }
}
