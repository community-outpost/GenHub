using System;
using System.Collections.Generic;

namespace GenHub.Core.Models.Content;

/// <summary>
/// Represents an audit log entry for a reconciliation operation.
/// </summary>
public record ReconciliationAuditEntry
{
    /// <summary>
    /// Gets unique identifier for the operation.
    /// </summary>
    public required string OperationId { get; init; }

    /// <summary>
    /// Gets type of reconciliation operation.
    /// </summary>
    public required ReconciliationOperationType OperationType { get; init; }

    /// <summary>
    /// Gets timestamp when the operation occurred.
    /// </summary>
    public required DateTime Timestamp { get; init; }

    /// <summary>
    /// Gets source that triggered the operation (e.g., "GeneralsOnline", "LocalEdit", "UserAction").
    /// </summary>
    public string? Source { get; init; }

    /// <summary>
    /// Gets profile IDs affected by the operation.
    /// </summary>
    public IReadOnlyList<string> AffectedProfileIds { get; init; } = [];

    /// <summary>
    /// Gets manifest IDs affected by the operation.
    /// </summary>
    public IReadOnlyList<string> AffectedManifestIds { get; init; } = [];

    /// <summary>
    /// Gets mapping of old manifest IDs to new manifest IDs (for replacement operations).
    /// </summary>
    public IReadOnlyDictionary<string, string>? ManifestMapping { get; init; }

    /// <summary>
    /// Gets a value indicating whether the operation completed successfully.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Gets error message if the operation failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Gets duration of the operation.
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// Gets additional metadata about the operation.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}
