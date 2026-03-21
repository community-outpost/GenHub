using System.Collections.Generic;

namespace GenHub.Core.Interfaces.Content;

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
