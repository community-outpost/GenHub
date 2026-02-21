using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameReplays;
using GenHub.Core.Models.Results;
using System.Collections.Generic;

namespace GenHub.Core.Interfaces.GameReplays;

/// <summary>
/// Parser interface for GameReplays HTML content.
/// Extracts tournament and forum post data from HTML.
/// </summary>
public interface IGameReplaysParser
{
    /// <summary>
    /// Parses tournament board HTML and extracts categorized tournaments.
    /// </summary>
    /// <param name="html">The HTML content to parse.</param>
    /// <returns>Result containing categorized tournaments.</returns>
    OperationResult<GameReplaysTournaments> ParseTournamentBoard(string html);

    /// <summary>
    /// Parses a tournament topic page and extracts tournament details.
    /// </summary>
    /// <param name="html">The HTML content to parse.</param>
    /// <returns>Result containing tournament details.</returns>
    OperationResult<Tournament> ParseTournamentTopic(string html);

    /// <summary>
    /// Parses forum posts from HTML.
    /// </summary>
    /// <param name="html">The HTML content.</param>
    /// <returns>Result containing collection of forum posts.</returns>
    OperationResult<IEnumerable<ForumPost>> ParseForumPosts(string html);

    /// <summary>
    /// Parses OAuth token response from JSON.
    /// </summary>
    /// <param name="json">The JSON content to parse.</param>
    /// <returns>Result containing OAuth token response.</returns>
    OperationResult<OAuthTokenResponse> ParseOAuthTokenResponse(string json);

    /// <summary>
    /// Parses OAuth user info from JSON.
    /// </summary>
    /// <param name="json">The JSON content to parse.</param>
    /// <returns>Result containing OAuth user info.</returns>
    OperationResult<OAuthUserInfo> ParseOAuthUserInfo(string json);

    /// <summary>
    /// Extracts tournament status from HTML content.
    /// </summary>
    /// <param name="html">The HTML content containing tournament info.</param>
    /// <returns>The tournament status.</returns>
    TournamentStatus ExtractTournamentStatus(string html);

    /// <summary>
    /// Extracts tournament dates from HTML content.
    /// </summary>
    /// <param name="html">The HTML content containing tournament info.</param>
    /// <returns>Tuple of (signupsCloseDate, startDate) or null if not found.</returns>
    (DateTime? SignupsCloseDate, DateTime? StartDate) ExtractTournamentDates(string html);

    /// <summary>
    /// Formats HTML content for display in GenHub.
    /// Removes unwanted elements and formats for clean display.
    /// </summary>
    /// <param name="html">The HTML content to format.</param>
    /// <returns>Formatted HTML string.</returns>
    string FormatHtmlForDisplay(string html);

    /// <summary>
    /// Extracts plain text from HTML content.
    /// </summary>
    /// <param name="html">The HTML content to extract text from.</param>
    /// <returns>Plain text string.</returns>
    string ExtractPlainText(string html);
}
