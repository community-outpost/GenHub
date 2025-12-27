using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Results;

namespace GenHub.Core.Models.GameProfile;

/// <summary>
/// Result of adding content to a profile, including information about swapped content.
/// </summary>
public class AddToProfileResult : ResultBase
{
    /// <summary>
    /// Gets or sets a value indicating whether existing content was swapped.
    /// </summary>
    public bool WasContentSwapped { get; set; }

    /// <summary>
    /// Gets or sets the manifest ID of the swapped content.
    /// </summary>
    public string? SwappedContentId { get; set; }

    /// <summary>
    /// Gets or sets the display name of the swapped content.
    /// </summary>
    public string? SwappedContentName { get; set; }

    /// <summary>
    /// Gets or sets the content type of the swapped content.
    /// </summary>
    public ContentType SwappedContentType { get; set; }

    /// <summary>
    /// Gets or sets the manifest ID of the newly added content.
    /// </summary>
    public string? AddedContentId { get; set; }

    /// <summary>
    /// Gets or sets the display name of the added content.
    /// </summary>
    public string? AddedContentName { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="AddToProfileResult"/> class for a successful operation.
    /// </summary>
    /// <param name="addedContentId">The added content ID.</param>
    /// <param name="addedContentName">The added content name.</param>
    /// <param name="elapsed">The elapsed time.</param>
    protected AddToProfileResult(string addedContentId, string? addedContentName, TimeSpan elapsed = default)
        : base(true, errors: null, elapsed: elapsed)
    {
        AddedContentId = addedContentId;
        AddedContentName = addedContentName;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AddToProfileResult"/> class for a failed operation.
    /// </summary>
    /// <param name="error">The error message.</param>
    /// <param name="elapsed">The elapsed time.</param>
    protected AddToProfileResult(string error, TimeSpan elapsed = default)
        : base(false, error, elapsed)
    {
    }

    /// <summary>
    /// Creates a successful result for adding content without swapping.
    /// </summary>
    /// <param name="addedContentId">The added content ID.</param>
    /// <param name="addedContentName">The added content name.</param>
    /// <param name="elapsed">The elapsed time.</param>
    /// <returns>A successful <see cref="AddToProfileResult"/>.</returns>
    public static AddToProfileResult CreateSuccess(string addedContentId, string? addedContentName, TimeSpan elapsed = default)
    {
        return new AddToProfileResult(addedContentId, addedContentName, elapsed);
    }

    /// <summary>
    /// Creates a successful result for adding content with a swap.
    /// </summary>
    /// <param name="addedContentId">The added content ID.</param>
    /// <param name="addedContentName">The added content name.</param>
    /// <param name="swappedContentId">The swapped content ID.</param>
    /// <param name="swappedContentName">The swapped content name.</param>
    /// <param name="swappedContentType">The swapped content type.</param>
    /// <param name="elapsed">The elapsed time.</param>
    /// <returns>A successful <see cref="AddToProfileResult"/> with swap details.</returns>
    public static AddToProfileResult CreateSuccessWithSwap(
        string addedContentId,
        string? addedContentName,
        string swappedContentId,
        string? swappedContentName,
        ContentType swappedContentType,
        TimeSpan elapsed = default)
    {
        return new AddToProfileResult(addedContentId, addedContentName, elapsed)
        {
            WasContentSwapped = true,
            SwappedContentId = swappedContentId,
            SwappedContentName = swappedContentName,
            SwappedContentType = swappedContentType,
        };
    }

    /// <summary>
    /// Creates a failed result.
    /// </summary>
    /// <param name="error">The error message.</param>
    /// <param name="elapsed">The elapsed time.</param>
    /// <returns>A failed <see cref="AddToProfileResult"/>.</returns>
    public static AddToProfileResult CreateFailure(string error, TimeSpan elapsed = default)
    {
        return new AddToProfileResult(error, elapsed);
    }
}