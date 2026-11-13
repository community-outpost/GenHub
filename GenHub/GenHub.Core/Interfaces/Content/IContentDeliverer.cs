using GenHub.Core.Models.Content;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;

namespace GenHub.Core.Interfaces.Content;

/// <summary>
/// Delivers actual content files based on a ContentManifest.
/// This is pure content delivery - downloading, copying, extracting files.
/// </summary>
public interface IContentDeliverer : IContentSource
{
    /// <summary>
    /// Determines if this deliverer can handle the given manifest.
    /// </summary>
    /// <param name="manifest">The manifest to check for deliverability.</param>
    /// <returns>True if the deliverer can handle the manifest; otherwise, false.</returns>
    bool CanDeliver(ContentManifest manifest);

    /// <summary>
    /// Delivers content files to the specified directory based on the manifest.
    /// Transforms package-level operations into file-level operations.
    /// </summary>
    /// <param name="packageManifest">The manifest describing the package to deliver.</param>
    /// <param name="targetDirectory">The directory to deliver content files to.</param>
    /// <param name="progress">Optional progress reporter for content acquisition.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A result containing the delivered manifest or error information.</returns>
    Task<OperationResult<ContentManifest>> DeliverContentAsync(
        ContentManifest packageManifest,
        string targetDirectory,
        IProgress<ContentAcquisitionProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates that content can be delivered from this source.
    /// </summary>
    /// <param name="manifest">The manifest to validate for delivery.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A result indicating whether the content can be delivered.</returns>
    Task<OperationResult<bool>> ValidateContentAsync(
        ContentManifest manifest,
        CancellationToken cancellationToken = default);
}
