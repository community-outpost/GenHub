using System;
using System.Threading;

namespace GenHub.Core.Models.Content;

/// <summary>
/// Options for creating or updating local content manifests.
/// </summary>
public record LocalContentOptions
{
    /// <summary>
    /// Gets the original source path of the content if known.
    /// </summary>
    public string? SourcePath { get; init; }

    /// <summary>
    /// Gets the progress reporter for content storage operations.
    /// </summary>
    public IProgress<ContentStorageProgress>? Progress { get; init; }

    /// <summary>
    /// Gets the cancellation token for the operation.
    /// </summary>
    public CancellationToken CancellationToken { get; init; }

    /// <summary>
    /// Gets the relative entry point path for executable content types.
    /// </summary>
    public string? EntryPoint { get; init; }

    /// <summary>
    /// Gets a value indicating whether inactive mod archives (.gib, .ctr, etc.) should be normalized to .big.
    /// Defaults to true.
    /// </summary>
    public bool NormalizeInactiveArchives { get; init; } = true;
}
