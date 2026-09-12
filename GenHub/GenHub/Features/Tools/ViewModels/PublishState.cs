namespace GenHub.Features.Tools.ViewModels;

/// <summary>
/// Represents the current state of the publish workflow.
/// </summary>
public enum PublishState
{
    /// <summary>
    /// Initial state - not yet validated or published.
    /// </summary>
    Draft,

    /// <summary>
    /// Currently validating catalog structure and content.
    /// </summary>
    Validating,

    /// <summary>
    /// Validation passed, ready to publish.
    /// </summary>
    ReadyToPublish,

    /// <summary>
    /// Currently uploading files to hosting provider.
    /// </summary>
    Uploading,

    /// <summary>
    /// Successfully published and available.
    /// </summary>
    Published,

    /// <summary>
    /// Validation or upload failed.
    /// </summary>
    Error,
}
