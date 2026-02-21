using GenHub.Core.Constants;
using GenHub.Core.Interfaces.GameReplays;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameReplays;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Services.GameReplays;

/// <summary>
/// Service for posting comments to GameReplays forum topics.
/// </summary>
public class GameReplaysCommentService(
    IGameReplaysHttpClient httpClient,
    IGameReplaysParser parser,
    ILogger<GameReplaysCommentService> logger) : IGameReplaysCommentService
{
    /// <inheritdoc/>
    public async Task<OperationResult<bool>> PostCommentAsync(
        string topicId,
        string comment,
        CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogDebug("Posting comment to topic: {TopicId}", topicId);

            // Check if authenticated
            var authCookie = httpClient.GetAuthCookie();
            if (string.IsNullOrEmpty(authCookie))
            {
                return OperationResult<bool>.CreateFailure("Not authenticated. Please log in first.");
            }

            // Build form data for posting comment
            var formData = new Dictionary<string, string>
            {
                { "act", "Post" },
                { "CODE", "03" },
                { "f", GameReplaysConstants.ZeroHourGeneralDiscussionForumId },
                { "t", topicId },
                { "Post", comment },
                { "enableemo", "yes" },
                { "enablesig", "yes" },
                { "fast_reply_used", "1" },
            };

            var response = await httpClient.PostFormAsync(
                GameReplaysConstants.BaseUrl + "/community/index.php",
                formData,
                cancellationToken);

            if (!response.Success)
            {
                return OperationResult<bool>.CreateFailure(response.FirstError ?? "Failed to post comment");
            }

            logger.LogDebug("Successfully posted comment to topic: {TopicId}", topicId);

            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error posting comment to topic: {TopicId}", topicId);
            return OperationResult<bool>.CreateFailure($"Error: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<IEnumerable<Comment>>> GetCommentsAsync(
        string topicId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogDebug("Fetching comments for topic: {TopicId}", topicId);

            var url = $"{GameReplaysConstants.BaseUrl}/community/index.php?showtopic={topicId}";
            var htmlResult = await httpClient.GetHtmlAsync(url, cancellationToken);

            if (!htmlResult.Success)
            {
                return OperationResult<IEnumerable<Comment>>.CreateFailure(htmlResult.FirstError ?? "Failed to fetch topic pages");
            }

            var parseResult = parser.ParseForumPosts(htmlResult.Data);

            if (!parseResult.Success)
            {
                 return OperationResult<IEnumerable<Comment>>.CreateFailure(parseResult.FirstError ?? "Failed to parse topic posts");
            }

            var comments = new List<Comment>();

            foreach (var post in parseResult.Data)
            {
                comments.Add(new Comment
                {
                    Id = post.PostId,
                    AuthorId = post.Author,
                    AuthorName = post.AuthorDisplayName,
                    Content = post.ContentHtml,
                    PostedAt = post.PostedAt,
                    IsEdited = post.IsEdited,
                    EditedAt = post.EditedAt,
                });
            }

            return OperationResult<IEnumerable<Comment>>.CreateSuccess(comments);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching comments for topic: {TopicId}", topicId);
            return OperationResult<IEnumerable<Comment>>.CreateFailure($"Error: {ex.Message}");
        }
    }
}
