using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Models.Results;

namespace GenHub.Core.Interfaces.Content;

/// <summary>
/// Service for detecting and normalizing GenLauncher file modifications.
/// </summary>
public interface IGenLauncherNormalizationService
{
    /// <summary>
    /// Detects GenLauncher files (.gib, .GLR, .GOF, .GLTC) in the specified directory.
    /// </summary>
    /// <param name="directoryPath">The directory to scan.</param>
    /// <returns>Detection result with list of affected files.</returns>
    Task<GenLauncherDetectionResult> DetectGenLauncherFilesAsync(string directoryPath);

    /// <summary>
    /// Normalizes GenLauncher files in the specified directory.
    /// Converts .gib to .big and removes .GLR, .GOF, .GLTC suffixes.
    /// </summary>
    /// <param name="directoryPath">The directory containing files to normalize.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Operation result with normalization details.</returns>
    Task<OperationResult<GenLauncherNormalizationResult>> NormalizeFilesAsync(
        string directoryPath,
        CancellationToken cancellationToken = default);
}

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

/// <summary>
/// Result of GenLauncher file normalization.
/// </summary>
public class GenLauncherNormalizationResult
{
    /// <summary>
    /// Number of files successfully normalized.
    /// </summary>
    public int NormalizedCount { get; set; }

    /// <summary>
    /// Number of symbolic links removed.
    /// </summary>
    public int SymbolicLinksRemoved { get; set; }

    /// <summary>
    /// List of files that failed to normalize.
    /// </summary>
    public List<string> FailedFiles { get; set; } = [];

    /// <summary>
    /// Whether normalization was fully successful.
    /// </summary>
    public bool IsFullySuccessful => FailedFiles.Count == 0;
}
