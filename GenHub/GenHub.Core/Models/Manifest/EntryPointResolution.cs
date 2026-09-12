using System.Collections.Generic;
using System.Linq;

namespace GenHub.Core.Models.Manifest;

/// <summary>
/// The outcome of resolving which file a manifest launches.
/// <para>
/// Failure carries the candidates that were considered. Resolution failing silently, or
/// failing with only "no executable found", leaves whoever hits it guessing at what the
/// manifest actually contained.
/// </para>
/// </summary>
public sealed class EntryPointResolution
{
    private EntryPointResolution(string? relativePath, string reason, IReadOnlyList<string> candidates)
    {
        RelativePath = relativePath;
        Reason = reason;
        Candidates = candidates;
    }

    /// <summary>Gets a value indicating whether an entry point was determined.</summary>
    public bool Success => RelativePath is not null;

    /// <summary>Gets the resolved relative path, or <c>null</c> when resolution failed.</summary>
    public string? RelativePath { get; }

    /// <summary>
    /// Gets a human-readable explanation: on success, how the entry point was chosen; on
    /// failure, why it could not be.
    /// </summary>
    public string Reason { get; }

    /// <summary>Gets the file paths considered, for diagnosing a failure.</summary>
    public IReadOnlyList<string> Candidates { get; }

    /// <summary>
    /// Creates a successful resolution.
    /// </summary>
    /// <param name="relativePath">The resolved entry point.</param>
    /// <param name="reason">How it was chosen.</param>
    /// <returns>A successful resolution.</returns>
    public static EntryPointResolution Resolved(string relativePath, string reason) =>
        new(relativePath, reason, []);

    /// <summary>
    /// Creates a failed resolution.
    /// </summary>
    /// <param name="reason">Why resolution failed.</param>
    /// <param name="candidates">The files that were considered.</param>
    /// <returns>A failed resolution.</returns>
    public static EntryPointResolution Failed(string reason, IEnumerable<ManifestFile> candidates) =>
        new(null, reason, candidates.Select(f => f.RelativePath).ToList());

    /// <summary>
    /// Builds a log-ready description including the candidates considered.
    /// </summary>
    /// <returns>A diagnostic string.</returns>
    public override string ToString()
    {
        if (Success)
        {
            return $"{RelativePath} ({Reason})";
        }

        if (Candidates.Count == 0)
        {
            return Reason;
        }

        return $"{Reason} Candidates: {string.Join(", ", Candidates)}";
    }
}
