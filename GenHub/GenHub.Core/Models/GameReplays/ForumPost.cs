using System;
using System.Collections.Generic;

namespace GenHub.Core.Models.GameReplays;

/// <summary>
/// Represents a forum post from GameReplays.org.
/// </summary>
public class ForumPost
{
    /// <summary>
    /// Gets or sets the post ID.
    /// </summary>
    public string PostId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the topic ID.
    /// </summary>
    public string TopicId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the author's username.
    /// </summary>
    public string Author { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the author's display name.
    /// </summary>
    public string AuthorDisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the post timestamp.
    /// </summary>
    public DateTime PostedAt { get; set; }

    /// <summary>
    /// Gets or sets the post content in HTML format.
    /// </summary>
    public string ContentHtml { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the collection of comments on this post.
    /// </summary>
    public List<Comment> Comments { get; set; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether this post has been edited.
    /// </summary>
    public bool IsEdited { get; set; }

    /// <summary>
    /// Gets or sets the edit timestamp if the post was edited.
    /// </summary>
    public DateTime? EditedAt { get; set; }
}
