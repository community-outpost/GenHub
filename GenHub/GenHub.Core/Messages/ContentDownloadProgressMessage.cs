using GenHub.Core.Models.Results.Content;

namespace GenHub.Core.Messages;

/// <summary>
/// Message broadcast when an in-flight content download reports progress.
/// </summary>
public sealed record ContentDownloadProgressMessage(
    string ContentKey,
    string? ContentId,
    string? ProviderName,
    string? ContentName,
    double ProgressPercentage,
    string StatusMessage,
    string? ParentContentId = null)
{
    /// <summary>
    /// Checks whether this download message matches the specified content item.
    /// </summary>
    /// <param name="item">The search result to match.</param>
    /// <returns>True if the message matches the item; otherwise false.</returns>
    public bool Matches(ContentSearchResult? item) =>
        DownloadMessageMatchHelper.Matches(ContentKey, ContentId, ProviderName, ContentName, item);
}
