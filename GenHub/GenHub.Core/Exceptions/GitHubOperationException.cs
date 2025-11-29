using GenHub.Core.Models.Enums;

namespace GenHub.Core.Exceptions;

/// <summary>
/// Exception thrown when a GitHub operation fails.
/// </summary>
public class GitHubOperationException(
    string message,
    Exception? inner,
    string owner,
    string repository,
    GitHubOperationType operationType)
    : Exception(message, inner)
{
    /// <summary>
    /// Gets the repository owner.
    /// </summary>
    public string Owner { get; } = owner;

    /// <summary>
    /// Gets the repository name.
    /// </summary>
    public string Repository { get; } = repository;

    /// <summary>
    /// Gets the type of operation that failed.
    /// </summary>
    public GitHubOperationType OperationType { get; } = operationType;
}
