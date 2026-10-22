using System;

namespace GenHub.Core.Models.Content;

/// <summary>
/// Event raised when a reconciliation operation completes.
/// </summary>
public record ReconciliationCompletedEvent(
    string OperationId,
    string OperationType,
    int ProfilesAffected,
    int ManifestsAffected,
    bool Success,
    string? ErrorMessage,
    TimeSpan Duration);
