using GenHub.Core.Models.GeneralsOnline;
using GenHub.Core.Models.Results;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Interfaces.GeneralsOnline;

/// <summary>
/// Typed client for the official Generals Online backend REST API.
/// </summary>
public interface IGeneralsOnlineApiClient
{
    /// <summary>
    /// Sets the session token provider for authenticated API calls.
    /// This breaks the circular dependency between the API client and the auth service.
    /// </summary>
    /// <param name="tokenProvider">Function returning the current session token, or null when signed out.</param>
    void SetTokenProvider(System.Func<Task<string?>> tokenProvider);

    /// <summary>
    /// Issues a browser-login code from the backend.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The issued login code.</returns>
    Task<OperationResult<string>> GetLoginCodeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Polls the backend for browser-login completion of the given game code.
    /// </summary>
    /// <param name="gameCode">The game code issued by <see cref="GetLoginCodeAsync"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The login result carrying the pending state or fresh tokens.</returns>
    Task<OperationResult<LoginResult>> CheckLoginAsync(string gameCode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Exchanges a stored refresh token for a fresh session without browser interaction.
    /// </summary>
    /// <param name="refreshToken">The stored refresh token.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The login result carrying the fresh session.</returns>
    Task<OperationResult<LoginResult>> LoginWithTokenAsync(string refreshToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all active multiplayer lobbies.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The lobby list with latency hints.</returns>
    Task<OperationResult<GeneralsOnlineLobbiesResult>> GetLobbiesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches the network rooms. The room carrying the show-all flag views
    /// lobbies from every room and is required for launcher lobby browsing.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The network rooms.</returns>
    Task<OperationResult<IReadOnlyList<GeneralsOnlineRoom>>> GetRoomsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches one lobby with its current member list.
    /// </summary>
    /// <param name="lobbyId">The lobby id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The lobby detail, or null data when the lobby no longer exists.</returns>
    Task<OperationResult<GeneralsOnlineLobby?>> GetLobbyDetailsAsync(long lobbyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches the total online players and lobbies without authentication.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The public counts.</returns>
    Task<OperationResult<GeneralsOnlinePublicCounts>> GetPublicCountsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches the service start time and uptime without authentication.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The service uptime.</returns>
    Task<OperationResult<GeneralsOnlineServiceUptime>> GetServiceUptimeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches today's per-faction match and win totals.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The global statistics.</returns>
    Task<OperationResult<GeneralsOnlineDailyStats>> GetGlobalStatsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches one player's career statistics.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The player statistics.</returns>
    Task<OperationResult<GeneralsOnlinePlayerStats>> GetPlayerStatsAsync(long userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches the community message of the day.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The message text.</returns>
    Task<OperationResult<string>> GetMotdAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches friends and pending requests for the signed-in user.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The friends result.</returns>
    Task<OperationResult<GeneralsOnlineFriendsResult>> GetFriendsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches blocked users for the signed-in user.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The blocked result.</returns>
    Task<OperationResult<GeneralsOnlineBlockedResult>> GetBlockedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches active users visible to launcher sessions.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The active users result.</returns>
    Task<OperationResult<GeneralsOnlineActiveUsersResult>> GetActiveUsersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches the signed-in user's id and display name.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The signed-in user.</returns>
    Task<OperationResult<GeneralsOnlineMe>> GetMeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a friend request to a user.
    /// </summary>
    /// <param name="targetUserId">The target user id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success when the request was sent.</returns>
    Task<OperationResult<bool>> SendFriendRequestAsync(long targetUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Accepts a pending friend request.
    /// </summary>
    /// <param name="targetUserId">The requester user id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success when the request was accepted.</returns>
    Task<OperationResult<bool>> AcceptFriendRequestAsync(long targetUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rejects a pending friend request.
    /// </summary>
    /// <param name="targetUserId">The requester user id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success when the request was rejected.</returns>
    Task<OperationResult<bool>> RejectFriendRequestAsync(long targetUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a friend.
    /// </summary>
    /// <param name="targetUserId">The friend user id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success when the friend was removed.</returns>
    Task<OperationResult<bool>> RemoveFriendAsync(long targetUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Blocks a user.
    /// </summary>
    /// <param name="targetUserId">The target user id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success when the user was blocked.</returns>
    Task<OperationResult<bool>> BlockUserAsync(long targetUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Unblocks a user.
    /// </summary>
    /// <param name="targetUserId">The target user id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success when the user was unblocked.</returns>
    Task<OperationResult<bool>> UnblockUserAsync(long targetUserId, CancellationToken cancellationToken = default);
}
