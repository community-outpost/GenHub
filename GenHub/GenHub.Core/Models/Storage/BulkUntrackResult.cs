using System.Collections.Generic;

namespace GenHub.Core.Models.Storage;

/// <summary>
/// Result of a bulk untracking operation.
/// </summary>
/// <param name="Untracked">Number of manifests successfully untracked.</param>
/// <param name="Total">Total number of manifests requested.</param>
/// <param name="Errors">List of errors encountered.</param>
public record BulkUntrackResult(int Untracked, int Total, IReadOnlyList<string> Errors)
{
    /// <summary>
    /// Gets a value indicating whether the balance of the operation was successful.
    /// </summary>
    public bool Success => Untracked == Total && (Errors?.Count ?? 0) == 0;
}
