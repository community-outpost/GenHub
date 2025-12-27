using GenHub.Core.Models.Enums;

namespace GenHub.Core.Exceptions;

/// <summary>
/// Exception thrown when a GitHub operation fails.
/// </summary>
public class GitHubOperationException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GitHubOperationException"/> class.
    /// </summary>
    public GitHubOperationException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GitHubOperationException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public GitHubOperationException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GitHubOperationException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="inner">The exception that is the cause of the current exception.</param>
    public GitHubOperationException(string message, Exception? inner)
        : base(message, inner)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GitHubOperationException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="inner">The exception that is the cause of the current exception.</param>
    /// <param name="owner">The repository owner.</param>
    /// <param name="repository">The repository name.</param>
    /// <param name="operationType">The type of operation that failed.</param>
    public GitHubOperationException(
        string message,
        Exception? inner,
        string owner,
        string repository,
        GitHubOperationType operationType)
        : base(message, inner)
    {
        Owner = owner;
        Repository = repository;
        OperationType = operationType;
    }

    /// <summary>
    /// Gets the repository owner.
    /// </summary>
    public string Owner { get; } = string.Empty;

    /// <summary>
    /// Gets the repository name.
    /// </summary>
    public string Repository { get; } = string.Empty;

    /// <summary>
    /// Gets the type of operation that failed.
    /// </summary>
    public GitHubOperationType OperationType { get; }
}
