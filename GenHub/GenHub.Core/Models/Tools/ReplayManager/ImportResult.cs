namespace GenHub.Core.Models.Tools.ReplayManager;

/// <summary>
/// Result of an import operation.
/// </summary>
public sealed class ImportResult
{
    /// <summary>
    /// Gets a value indicating whether the import was successful.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Gets the number of files successfully imported.
    /// </summary>
    public required int FilesImported { get; init; }

    /// <summary>
    /// Gets the number of files skipped.
    /// </summary>
    public required int FilesSkipped { get; init; }

    /// <summary>
    /// Gets the list of error messages.
    /// </summary>
    public IReadOnlyList<string> Errors { get; init; } = [];

    /// <summary>
    /// Gets the list of imported file paths.
    /// </summary>
    public IReadOnlyList<string> ImportedFiles { get; init; } = [];
}