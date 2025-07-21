namespace GenHub.Core.Models.Validation;

/// <summary>
/// Represents progress information for validation operations.
/// </summary>
public class ValidationProgress
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ValidationProgress"/> class.
    /// </summary>
    /// <param name="processed">Number of files processed.</param>
    /// <param name="total">Total number of files.</param>
    /// <param name="currentFile">Current file being processed.</param>
    public ValidationProgress(int processed, int total, string? currentFile)
    {
        Processed = processed;
        Total = total;
        CurrentFile = currentFile;
    }

    /// <summary>
    /// Gets the percentage of completion (0-100).
    /// </summary>
    public double PercentComplete => Total > 0 ? (double)Processed / Total * 100 : 0;

    /// <summary>
    /// Gets the number of files processed.
    /// </summary>
    public int Processed { get; }

    /// <summary>
    /// Gets the total number of files.
    /// </summary>
    public int Total { get; }

    /// <summary>
    /// Gets the current file being processed.
    /// </summary>
    public string? CurrentFile { get; }
}
