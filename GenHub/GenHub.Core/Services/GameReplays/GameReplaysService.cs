using GenHub.Core.Constants;
using GenHub.Core.Interfaces.GameReplays;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameReplays;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Services.GameReplays;

/// <summary>
/// Main service implementation for GameReplays integration.
/// Provides access to tournaments, authentication, and comments.
/// </summary>
public class GameReplaysService(
    IGameReplaysHttpClient httpClient,
    IGameReplaysParser parser,
    IGameReplaysAuthService authService,
    IGameReplaysCommentService commentService,
    ILogger<GameReplaysService> logger) : IGameReplaysService
{
    private GameReplaysTournaments? _cachedTournaments;
    private System.DateTime _cacheExpiry;

    /// <inheritdoc/>
    public async Task<OperationResult<GameReplaysTournaments>> GetTournamentsAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogDebug("Fetching tournaments from tournament board");

            // Check cache
            if (_cachedTournaments != null && System.DateTime.UtcNow < _cacheExpiry)
            {
                logger.LogDebug("Returning cached tournaments");
                return OperationResult<GameReplaysTournaments>.CreateSuccess(_cachedTournaments);
            }

            // Fetch tournament board HTML
            var url = $"{GameReplaysConstants.BaseUrl}/community/index.php?showtopic={GameReplaysConstants.TournamentBoardTopicId}";
            var htmlResult = await httpClient.GetHtmlAsync(url, cancellationToken);

            if (!htmlResult.Success)
            {
                return OperationResult<GameReplaysTournaments>.CreateFailure(htmlResult.FirstError ?? "Failed to fetch tournament board");
            }

            // Parse tournaments
            var parseResult = parser.ParseTournamentBoard(htmlResult.Data);

            if (!parseResult.Success)
            {
                return OperationResult<GameReplaysTournaments>.CreateFailure(parseResult.FirstError ?? "Failed to parse tournament board");
            }

            // Update cache
            _cachedTournaments = parseResult.Data;
            _cacheExpiry = System.DateTime.UtcNow.AddMinutes(GameReplaysConstants.CacheDurationMinutes);

            logger.LogDebug(
                "Successfully fetched tournaments: {SignupsOpen} signups open, {Upcoming} upcoming, {Active} active, {Finished} finished",
                parseResult.Data.SignupsOpen.Count(),
                parseResult.Data.Upcoming.Count(),
                parseResult.Data.Active.Count(),
                parseResult.Data.Finished.Count());

            return OperationResult<GameReplaysTournaments>.CreateSuccess(parseResult.Data);
        }
        catch (System.Exception ex)
        {
            logger.LogError(ex, "Error fetching tournaments");
            return OperationResult<GameReplaysTournaments>.CreateFailure($"Error: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<IEnumerable<Tournament>>> GetTournamentsByStatusAsync(
        TournamentStatus status,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var tournamentsResult = await GetTournamentsAsync(cancellationToken);

            if (!tournamentsResult.Success)
            {
                return OperationResult<IEnumerable<Tournament>>.CreateFailure(tournamentsResult.FirstError ?? "Failed to fetch tournaments");
            }

            var tournaments = status switch
            {
                TournamentStatus.SignupsOpen => tournamentsResult.Data.SignupsOpen,
                TournamentStatus.Upcoming => tournamentsResult.Data.Upcoming,
                TournamentStatus.Active => tournamentsResult.Data.Active,
                TournamentStatus.Finished => tournamentsResult.Data.Finished,
                _ => tournamentsResult.Data.All,
            };

            return OperationResult<IEnumerable<Tournament>>.CreateSuccess(tournaments);
        }
        catch (System.Exception ex)
        {
            logger.LogError(ex, "Error fetching tournaments by status: {Status}", status);
            return OperationResult<IEnumerable<Tournament>>.CreateFailure($"Error: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<Tournament>> GetTournamentAsync(
        string topicId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogDebug("Fetching tournament details for topic: {TopicId}", topicId);

            var url = $"{GameReplaysConstants.BaseUrl}/community/index.php?showtopic={topicId}";
            var htmlResult = await httpClient.GetHtmlAsync(url, cancellationToken);

            if (!htmlResult.Success)
            {
                return OperationResult<Tournament>.CreateFailure(htmlResult.FirstError ?? "Failed to fetch tournament");
            }

            var parseResult = parser.ParseTournamentTopic(htmlResult.Data);

            if (!parseResult.Success)
            {
                return OperationResult<Tournament>.CreateFailure(parseResult.FirstError ?? "Failed to parse tournament");
            }

            return parseResult;
        }
        catch (System.Exception ex)
        {
            logger.LogError(ex, "Error fetching tournament: {TopicId}", topicId);
            return OperationResult<Tournament>.CreateFailure($"Error: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<IEnumerable<ForumPost>>> GetTopicPostsAsync(
        string topicId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogDebug("Fetching posts for topic: {TopicId}", topicId);

            var url = $"{GameReplaysConstants.BaseUrl}/community/index.php?showtopic={topicId}";
            var htmlResult = await httpClient.GetHtmlAsync(url, cancellationToken);

            if (!htmlResult.Success)
            {
                return OperationResult<IEnumerable<ForumPost>>.CreateFailure(htmlResult.FirstError ?? "Failed to fetch topic posts");
            }

            var parseResult = parser.ParseForumPosts(htmlResult.Data);

            if (!parseResult.Success)
            {
                return OperationResult<IEnumerable<ForumPost>>.CreateFailure(parseResult.FirstError ?? "Failed to parse topic posts");
            }

            // Set topic ID on posts
            foreach (var post in parseResult.Data)
            {
                post.TopicId = topicId;
            }

            return OperationResult<IEnumerable<ForumPost>>.CreateSuccess(parseResult.Data);
        }
        catch (System.Exception ex)
        {
            logger.LogError(ex, "Error fetching topic posts: {TopicId}", topicId);
            return OperationResult<IEnumerable<ForumPost>>.CreateFailure($"Error: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public bool IsAuthenticated()
    {
        return authService.GetAccessToken() != null;
    }

    /// <inheritdoc/>
    public OperationResult<OAuthUserInfo> GetCurrentUser()
    {
        var user = authService.GetCurrentUser();

        if (user == null)
        {
            return OperationResult<OAuthUserInfo>.CreateFailure("Not authenticated");
        }

        return OperationResult<OAuthUserInfo>.CreateSuccess(user);
    }

    /// <inheritdoc/>
    public Task<OperationResult<string>> InitiateLoginAsync()
    {
        try
        {
            logger.LogDebug("Initiating OAuth login flow");

            var authUrlResult = authService.GetAuthorizationUrl();

            if (!authUrlResult.Success)
            {
                return Task.FromResult(OperationResult<string>.CreateFailure(authUrlResult.FirstError ?? "Failed to generate authorization URL"));
            }

            return Task.FromResult(OperationResult<string>.CreateSuccess(authUrlResult.Data));
        }
        catch (System.Exception ex)
        {
            logger.LogError(ex, "Error initiating login");
            return Task.FromResult(OperationResult<string>.CreateFailure($"Error: {ex.Message}"));
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<bool>> HandleCallbackAsync(
        string code,
        string state)
    {
        try
        {
            logger.LogDebug("Handling OAuth callback");

            // Validate state
            if (!authService.ValidateState(state))
            {
                return OperationResult<bool>.CreateFailure("Invalid OAuth state");
            }

            // Exchange code for token
            var tokenResult = await authService.ExchangeCodeForTokenAsync(code);

            if (!tokenResult.Success)
            {
                return OperationResult<bool>.CreateFailure(tokenResult.FirstError ?? "Failed to exchange code for token");
            }

            // Fetch user info
            var userInfoResult = await authService.GetUserInfoAsync(tokenResult.Data.AccessToken);

            if (!userInfoResult.Success)
            {
                return OperationResult<bool>.CreateFailure(userInfoResult.FirstError ?? "Failed to fetch user info");
            }

            // Set current user
            authService.SetCurrentUser(userInfoResult.Data);

            logger.LogDebug("Successfully authenticated user: {UserId}", userInfoResult.Data.Id);

            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (System.Exception ex)
        {
            logger.LogError(ex, "Error handling OAuth callback");
            return OperationResult<bool>.CreateFailure($"Error: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<bool>> LogoutAsync()
    {
        try
        {
            logger.LogDebug("Logging out user");

            await authService.ClearTokenAsync();

            // Clear cache
            _cachedTournaments = null;

            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (System.Exception ex)
        {
            logger.LogError(ex, "Error logging out");
            return OperationResult<bool>.CreateFailure($"Error: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<bool>> PostTournamentCommentAsync(
        string topicId,
        string comment,
        CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogDebug("Posting comment to topic: {TopicId}", topicId);

            var result = await commentService.PostCommentAsync(topicId, comment, cancellationToken);

            if (!result.Success)
            {
                return OperationResult<bool>.CreateFailure(result.FirstError ?? "Failed to post comment");
            }

            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (System.Exception ex)
        {
            logger.LogError(ex, "Error posting comment");
            return OperationResult<bool>.CreateFailure($"Error: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<IEnumerable<Comment>>> GetTournamentCommentsAsync(
        string topicId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogDebug("Fetching comments for topic: {TopicId}", topicId);

            var result = await commentService.GetCommentsAsync(topicId, cancellationToken);

            if (!result.Success)
            {
                return OperationResult<IEnumerable<Comment>>.CreateFailure(result.FirstError ?? "Failed to fetch comments");
            }

            return OperationResult<IEnumerable<Comment>>.CreateSuccess(result.Data);
        }
        catch (System.Exception ex)
        {
            logger.LogError(ex, "Error fetching tournament comments: {TopicId}", topicId);
            return OperationResult<IEnumerable<Comment>>.CreateFailure($"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Clears the tournament cache.
    /// </summary>
    public void ClearCache()
    {
        _cachedTournaments = null;
        logger.LogDebug("Cleared tournament cache");
    }
}
