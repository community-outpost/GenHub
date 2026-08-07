using System.Collections.Generic;

namespace GenHub.Core.Interfaces.Content;

/// <summary>
/// Result of GenLauncher file normalization.
/// </summary>
public class GenLauncherNormalizationResult
{
    /// <summary>
    /// Gets or sets the number of files successfully normalized.
    /// </summary>
    public int NormalizedCount { get; set; }

    /// <summary>
    /// Gets or sets the number of symbolic links removed.
    /// </summary>
    public int SymbolicLinksRemoved { get; set; }

    /// <summary>
    /// Gets or sets the list of files that failed to normalize.
    /// </summary>
    public List<string> FailedFiles { get; set; } = [];

    /// <summary>
    /// Gets a value indicating whether normalization was fully successful.
    /// </summary>
    public bool IsFullySuccessful => FailedFiles.Count == 0;
}
