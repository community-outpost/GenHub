using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameReplays;
using GenHub.Core.Models.Results;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GenHub.Core.Interfaces.GameReplays;

/// <summary>
/// Main service interface for GameReplays integration.
/// Provides access to tournaments, authentication, and comments.
/// </summary>
public interface IGameReplaysService
{
    /// <summary>
    /// Gets all tournaments from tournament board topic.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result containing categorized tournaments.</returns>
    Task<OperationResult<GameReplaysTournaments>> GetTournamentsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets tournaments by status category.
    /// </summary>
    /// <param name="status">The tournament status to filter by.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result containing filtered tournaments.</returns>
    Task<OperationResult<IEnumerable<Tournament>>> GetTournamentsByStatusAsync(
        TournamentStatus status,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a specific tournament by topic ID.
    /// </summary>
    /// <param name="topicId">The tournament topic ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result containing tournament details.</returns>
    Task<OperationResult<Tournament>> GetTournamentAsync(
        string topicId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets forum posts for a specific topic.
    /// </summary>
    /// <param name="topicId">The topic ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result containing forum posts.</returns>
    Task<OperationResult<IEnumerable<ForumPost>>> GetTopicPostsAsync(
        string topicId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if the user is authenticated with GameReplays.
    /// </summary>
    /// <returns>True if authenticated, false otherwise.</returns>
    bool IsAuthenticated();

    /// <summary>
    /// Gets the current authenticated user info.
    /// </summary>
    /// <returns>Result containing user info or error if not authenticated.</returns>
    OperationResult<OAuthUserInfo> GetCurrentUser();

    /// <summary>
    /// Initiates OAuth 2.0 login flow.
    /// </summary>
    /// <returns>Result containing authorization URL to open in browser.</returns>
    Task<OperationResult<string>> InitiateLoginAsync();

    /// <summary>
    /// Handles OAuth 2.0 callback from authorization server.
    /// </summary>
    /// <param name="code">The authorization code from callback.</param>
    /// <param name="state">The state parameter for CSRF protection.</param>
    /// <returns>Result indicating success or failure.</returns>
    Task<OperationResult<bool>> HandleCallbackAsync(
        string code,
        string state);

    /// <summary>
    /// Logs out the current user.
    /// </summary>
    /// <returns>Result indicating success or failure.</returns>
    Task<OperationResult<bool>> LogoutAsync();

    /// <summary>
    /// Posts a comment to a tournament topic.
    /// </summary>
    /// <param name="topicId">The tournament topic ID.</param>
    /// <param name="comment">The comment content.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result indicating success or failure.</returns>
    Task<OperationResult<bool>> PostTournamentCommentAsync(
        string topicId,
        string comment,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets comments for a tournament topic.
    /// TODO: Implement when GameReplays provides comments API.
    /// </summary>
    /// <param name="topicId">The tournament topic ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result containing comment collection.</returns>
    Task<OperationResult<IEnumerable<Comment>>> GetTournamentCommentsAsync(
        string topicId,
        CancellationToken cancellationToken = default);
}
