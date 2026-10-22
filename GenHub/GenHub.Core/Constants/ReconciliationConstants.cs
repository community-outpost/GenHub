using GenHub.Core.Models.Content;

namespace GenHub.Core.Constants;

/// <summary>
/// Constants for reconciliation operations.
/// </summary>
public static class ReconciliationConstants
{
    /// <summary>
    /// Length of operation ID (shortened GUID).
    /// </summary>
    public const int OperationIdLength = 8;

    /// <summary>
    /// Number of days to look back for audit history.
    /// </summary>
    public const int DefaultAuditLookbackDays = 7;

    /// <summary>
    /// Default audit log retention period in days.
    /// </summary>
    public const int DefaultAuditRetentionDays = 30;

    /// <summary>
    /// Maximum number of audit entries to return from history queries.
    /// </summary>
    public const int DefaultMaxAuditHistoryEntries = 50;

    /// <summary>
    /// Maximum number of audit entries to return for profile history.
    /// </summary>
    public const int DefaultMaxProfileHistoryEntries = 20;

    /// <summary>
    /// Maximum number of audit entries to return for manifest history.
    /// </summary>
    public const int DefaultMaxManifestHistoryEntries = 20;

    /// <summary>
    /// Maximum number of recent entries to load for filtering.
    /// </summary>
    public const int MaxFilterEntries = 500;

    /// <summary>
    /// Default timeout for garbage collection operations in seconds.
    /// </summary>
    public const int DefaultGcTimeoutSeconds = 300;

    /// <summary>
    /// Display names for reconciliation operation types.
    /// </summary>
    public static class OperationTypeDisplayNames
    {
        /// <summary>Display name for manifest replacement operations.</summary>
        public const string ManifestReplacement = "Manifest Replacement";

        /// <summary>Display name for manifest removal operations.</summary>
        public const string ManifestRemoval = "Manifest Removal";

        /// <summary>Display name for profile update operations.</summary>
        public const string ProfileUpdate = "Profile Update";

        /// <summary>Display name for workspace cleanup operations.</summary>
        public const string WorkspaceCleanup = "Workspace Cleanup";

        /// <summary>Display name for CAS untrack operations.</summary>
        public const string CasUntrack = "CAS Untrack";

        /// <summary>Display name for garbage collection operations.</summary>
        public const string GarbageCollection = "Garbage Collection";

        /// <summary>Display name for local content update operations.</summary>
        public const string LocalContentUpdate = "Local Content Update";

        /// <summary>Display name for GeneralsOnline update operations.</summary>
        public const string GeneralsOnlineUpdate = "GeneralsOnline Update";
    }

    /// <summary>
    /// Gets the display name for a reconciliation operation type.
    /// </summary>
    /// <param name="operationType">The operation type.</param>
    /// <returns>The display name.</returns>
    public static string GetDisplayName(ReconciliationOperationType operationType)
    {
        return operationType switch
        {
            ReconciliationOperationType.ManifestReplacement => OperationTypeDisplayNames.ManifestReplacement,
            ReconciliationOperationType.ManifestRemoval => OperationTypeDisplayNames.ManifestRemoval,
            ReconciliationOperationType.ProfileUpdate => OperationTypeDisplayNames.ProfileUpdate,
            ReconciliationOperationType.WorkspaceCleanup => OperationTypeDisplayNames.WorkspaceCleanup,
            ReconciliationOperationType.CasUntrack => OperationTypeDisplayNames.CasUntrack,
            ReconciliationOperationType.GarbageCollection => OperationTypeDisplayNames.GarbageCollection,
            ReconciliationOperationType.LocalContentUpdate => OperationTypeDisplayNames.LocalContentUpdate,
            ReconciliationOperationType.GeneralsOnlineUpdate => OperationTypeDisplayNames.GeneralsOnlineUpdate,
            _ => operationType.ToString(),
        };
    }
}
