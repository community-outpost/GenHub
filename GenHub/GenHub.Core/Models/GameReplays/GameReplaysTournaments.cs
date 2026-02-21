using System.Collections.Generic;
using System.Linq;

namespace GenHub.Core.Models.GameReplays;

/// <summary>
/// Container for categorized tournaments.
/// </summary>
public class GameReplaysTournaments
{
    /// <summary>Gets or sets tournaments with open signups.</summary>
    public IEnumerable<Tournament> SignupsOpen { get; set; } = [];

    /// <summary>Gets or sets upcoming tournaments.</summary>
    public IEnumerable<Tournament> Upcoming { get; set; } = [];

    /// <summary>Gets or sets active tournaments.</summary>
    public IEnumerable<Tournament> Active { get; set; } = [];

    /// <summary>Gets or sets finished/previous tournaments.</summary>
    public IEnumerable<Tournament> Finished { get; set; } = [];

    /// <summary>
    /// Gets all tournaments across all categories.
    /// </summary>
    public IEnumerable<Tournament> All =>
        SignupsOpen.Concat(Upcoming).Concat(Active).Concat(Finished);
}
