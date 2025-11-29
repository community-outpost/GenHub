namespace GenHub.Core.Models.Enums;

/// <summary>
/// Enumeration of GitHub operation types.
/// </summary>
public enum GitHubOperationType
{
    /// <summary>Repository discovery operation.</summary>
    Discovery,

    /// <summary>Release retrieval operation.</summary>
    ReleaseRetrieval,

    /// <summary>Workflow run retrieval operation.</summary>
    WorkflowRetrieval,

    /// <summary>Artifact download operation.</summary>
    Download,

    /// <summary>Authentication operation.</summary>
    Authentication,

    /// <summary>Rate limit handling operation.</summary>
    RateLimit,
}
