using GenHub.Core.Models.GameReplays;
using GenHub.Core.Models.Results;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GenHub.Core.Interfaces.GameReplays;

/// <summary>
/// Service for posting comments to GameReplays forum topics.
/// </summary>
public interface IGameReplaysCommentService
{
    /// <summary>
    /// Posts a comment to a forum topic.
    /// </summary>
    /// <param name="topicId">The tournament topic ID.</param>
    /// <param name="comment">The comment content.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result indicating success or failure.</returns>
    Task<OperationResult<bool>> PostCommentAsync(
        string topicId,
        string comment,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets comments for a forum topic.
    /// TODO: Implement when GameReplays provides comments API.
    /// </summary>
    /// <param name="topicId">The tournament topic ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result containing comment collection.</returns>
    Task<OperationResult<IEnumerable<Comment>>> GetCommentsAsync(
        string topicId,
        CancellationToken cancellationToken = default);
}
