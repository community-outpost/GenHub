namespace GenHub.Core.Models.Validation;

/// <summary>
/// Represents progress information for validation operations.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="ValidationProgress"/> class.
/// </remarks>
/// <param name="processed">Number of files processed.</param>
/// <param name="total">Total number of files.</param>
/// <param name="currentFile">Current file being processed.</param>
public class ValidationProgress(int processed, int total, string? currentFile)
{
    /// <summary>
    /// Gets the percentage of completion (0-100).
    /// </summary>
    public double PercentComplete => Total > 0 ? (double)Processed / Total * 100 : 0;

    /// <summary>
    /// Gets the number of files processed.
    /// </summary>
    public int Processed { get; } = processed;

    /// <summary>
    /// Gets the total number of files.
    /// </summary>
    public int Total { get; } = total;

    /// <summary>
    /// Gets the current file being processed.
    /// </summary>
    public string? CurrentFile { get; } = currentFile;

    /// <summary>
    /// Gets a value indicating whether the current operation is computing file hashes.
    /// This is an explicit flag to replace fragile heuristics based on file counts.
    /// </summary>
    public bool IsHashComputation { get; init; }
}