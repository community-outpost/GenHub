using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Models.Publishers;
using GenHub.Core.Models.Results;

namespace GenHub.Features.Tools.Interfaces;

/// <summary>
/// Represents a hosting provider that can upload files and catalogs for decentralized distribution.
/// Publishers use hosting providers to make their catalogs and artifacts accessible to users.
/// </summary>
public interface IHostingProvider
{
    /// <summary>
    /// Gets the unique identifier for this hosting provider.
    /// </summary>
    string ProviderId { get; }

    /// <summary>
    /// Gets the display name of the hosting provider.
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Gets a description of the hosting provider.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Gets the icon name for the hosting provider (Material Icons).
    /// </summary>
    string IconName { get; }

    /// <summary>
    /// Gets a value indicating whether this provider requires authentication.
    /// </summary>
    bool RequiresAuthentication { get; }

    /// <summary>
    /// Gets a value indicating whether the user is currently authenticated.
    /// </summary>
    bool IsAuthenticated { get; }

    /// <summary>
    /// Gets a value indicating whether this provider supports catalog hosting.
    /// </summary>
    bool SupportsCatalogHosting { get; }

    /// <summary>
    /// Gets a value indicating whether this provider supports artifact hosting.
    /// </summary>
    bool SupportsArtifactHosting { get; }

    /// <summary>
    /// Gets a value indicating whether this provider supports updating existing files in-place.
    /// </summary>
    bool SupportsUpdate { get; }

    /// <summary>
    /// Authenticates the user with the hosting provider.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Operation result indicating success or failure.</returns>
    Task<OperationResult<bool>> AuthenticateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Signs out the user from the hosting provider.
    /// </summary>
    /// <returns>Task representing the operation.</returns>
    Task SignOutAsync();

    /// <summary>
    /// Uploads a file to the hosting provider.
    /// </summary>
    /// <param name="fileStream">The file stream to upload.</param>
    /// <param name="fileName">The name of the file.</param>
    /// <param name="folderPath">Optional folder path within the hosting service.</param>
    /// <param name="progress">Optional progress callback (0-100).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Operation result containing the public URL of the uploaded file.</returns>
    Task<OperationResult<HostingUploadResult>> UploadFileAsync(
        Stream fileStream,
        string fileName,
        string? folderPath = null,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Uploads a catalog JSON file to the hosting provider.
    /// </summary>
    /// <param name="catalogJson">The catalog JSON content.</param>
    /// <param name="publisherId">The publisher ID (used for naming/organizing).</param>
    /// <param name="progress">Optional progress callback (0-100).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Operation result containing the public URL of the catalog.</returns>
    Task<OperationResult<HostingUploadResult>> UploadCatalogAsync(
        string catalogJson,
        string publisherId,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing file on the hosting provider, keeping the same URL.
    /// </summary>
    Task<OperationResult<HostingUploadResult>> UpdateFileAsync(
        string fileId,
        Stream fileStream,
        string fileName,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets or creates the publisher folder on the hosting provider.
    /// </summary>
    Task<OperationResult<string>> GetOrCreatePublisherFolderAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to recover hosting state from the provider by scanning for existing files.
    /// </summary>
    Task<OperationResult<HostingState?>> RecoverHostingStateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the subscription link for a hosted catalog.
    /// </summary>
    /// <param name="catalogUrl">The public URL of the catalog.</param>
    /// <returns>The GenHub subscription link.</returns>
    string GetSubscriptionLink(string catalogUrl);

    /// <summary>
    /// Validates that a URL is a valid hosting URL from this provider.
    /// </summary>
    /// <param name="url">The URL to validate.</param>
    /// <returns>True if the URL is valid for this provider.</returns>
    bool IsValidHostingUrl(string url);

    /// <summary>
    /// Gets the direct download URL for a hosted file.
    /// Some providers require URL transformation for direct downloads.
    /// </summary>
    /// <param name="shareUrl">The share URL from the provider.</param>
    /// <returns>The direct download URL.</returns>
    string GetDirectDownloadUrl(string shareUrl);
}

/// <summary>
/// Result of a file upload to a hosting provider.
/// </summary>
public class HostingUploadResult
{
    /// <summary>
    /// Gets or sets the public URL where the file can be accessed.
    /// </summary>
    public string PublicUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the direct download URL (may differ from public URL).
    /// </summary>
    public string DirectDownloadUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the file ID within the hosting provider (for management).
    /// </summary>
    public string FileId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the file size in bytes.
    /// </summary>
    public long FileSize { get; set; }

    /// <summary>
    /// Gets or sets the SHA256 hash of the uploaded file.
    /// </summary>
    public string? Sha256Hash { get; set; }
}
