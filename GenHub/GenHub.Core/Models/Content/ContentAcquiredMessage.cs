using GenHub.Core.Models.Manifest;

namespace GenHub.Core.Models.Content;

/// <summary>
/// Message sent when content has been successfully acquired and added to the manifest pool.
/// </summary>
/// <param name="Manifest">The acquired content manifest.</param>
public record ContentAcquiredMessage(ContentManifest Manifest);
