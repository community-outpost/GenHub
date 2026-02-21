using GenHub.Core.Models.Enums;
using System;

namespace GenHub.Core.Models.GameReplays;

/// <summary>
/// Represents a tournament from GameReplays.org.
/// </summary>
public class Tournament
{
    /// <summary>
    /// Gets or sets the topic ID for the tournament.
    /// </summary>
    public string TopicId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the tournament name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the tournament host.
    /// </summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the full URL to the tournament topic.
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the tournament status.
    /// </summary>
    public TournamentStatus Status { get; set; }

    /// <summary>
    /// Gets or sets the date when signups close.
    /// </summary>
    public DateTime? SignupsCloseDate { get; set; }

    /// <summary>
    /// Gets or sets the date when the tournament started.
    /// </summary>
    public DateTime? StartDate { get; set; }

    /// <summary>
    /// Gets or sets the tournament description.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the number of comments on the tournament topic.
    /// TODO: Implement comment counting when API is available.
    /// </summary>
    public int CommentCount { get; set; }
}
