using System.Collections.Generic;
using System.Linq;

namespace GenHub.Core.Models.Online;

/// <summary>
/// Aggregate mesh connectivity check over the joined roster.
/// </summary>
public sealed record OnlineMeshCheckResult
{
    /// <summary>
    /// Gets the per-peer probe outcomes.
    /// </summary>
    public required IReadOnlyList<OnlineMeshPeerResult> Peers { get; init; }

    /// <summary>
    /// Gets a value indicating whether every probed peer answered.
    /// Vacuously true when no member publishes a direct endpoint.
    /// </summary>
    public bool AllReachable => Peers.All(p => p.Reachable);

    /// <summary>
    /// Gets the number of probed peers that did not answer.
    /// </summary>
    public int UnreachableCount => Peers.Count(p => !p.Reachable);
}
