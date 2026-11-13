namespace GenHub.Core.Models.Content;

/// <summary>
/// Event raised when a reconciliation operation starts.
/// </summary>
public record ReconciliationStartedEvent(
    string OperationId,
    string OperationType,
    int ExpectedProfilesAffected,
    int ExpectedManifestsAffected);
