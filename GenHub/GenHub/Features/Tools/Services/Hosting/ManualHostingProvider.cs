using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Models.Publishers;
using GenHub.Core.Models.Results;
using GenHub.Features.Tools.Interfaces;

namespace GenHub.Features.Tools.Services.Hosting;

/// <summary>
/// Manual hosting provider for publishers who host files themselves.
/// This provider doesn't upload files but helps publishers with URL entry
/// and generates proper subscription links.
/// </summary>
/// <remarks>
/// This is the most flexible option for publishers who:
/// - Already have their own hosting (personal website, CDN, etc.)
/// - Want to use hosting services not directly integrated with GenHub
/// - Prefer manual control over their file distribution.
/// </remarks>
public class ManualHostingProvider : IHostingProvider
{
    /// <inheritdoc/>
    public string ProviderId => "manual";

    /// <inheritdoc/>
    public string DisplayName => "Custom URL (Manual)";

    /// <inheritdoc/>
    public string Description => "Use your own hosting. Enter download URLs manually for complete control.";

    /// <inheritdoc/>
    public string IconName => "Link";

    /// <inheritdoc/>
    public bool RequiresAuthentication => false;

    /// <inheritdoc/>
    public bool IsAuthenticated => true; // Always "authenticated" for manual

    /// <inheritdoc/>
    public bool SupportsCatalogHosting => true;

    /// <inheritdoc/>
    public bool SupportsArtifactHosting => true;

    /// <inheritdoc/>
    public bool SupportsUpdate => false;

    /// <inheritdoc/>
    public Task<OperationResult<bool>> AuthenticateAsync(CancellationToken cancellationToken = default)
    {
        // No authentication needed for manual hosting
        return Task.FromResult(OperationResult<bool>.CreateSuccess(true));
    }

    /// <inheritdoc/>
    public Task SignOutAsync()
    {
        // Nothing to sign out from
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<OperationResult<HostingUploadResult>> UploadFileAsync(
        Stream fileStream,
        string fileName,
        string? folderPath = null,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // Manual hosting doesn't actually upload files
        // The publisher is responsible for uploading to their own hosting
        return Task.FromResult(
            OperationResult<HostingUploadResult>.CreateFailure(
                "Manual hosting requires you to upload files yourself. Use the 'Enter URL' option to specify where you've hosted the file."));
    }

    /// <inheritdoc/>
    public Task<OperationResult<HostingUploadResult>> UploadCatalogAsync(
        string catalogJson,
        string publisherId,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // Manual hosting doesn't actually upload the catalog
        // The publisher needs to host it themselves
        return Task.FromResult(
            OperationResult<HostingUploadResult>.CreateFailure(
                "For manual hosting, export your catalog.json and upload it to your hosting service. Then enter the URL in Publisher Studio."));
    }

    /// <inheritdoc/>
    public Task<OperationResult<HostingUploadResult>> UpdateFileAsync(
        string fileId,
        Stream fileStream,
        string fileName,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // Manual hosting does not support in-place updates
        // Publishers must manually re-upload files to their hosting service
        return Task.FromResult(
            OperationResult<HostingUploadResult>.CreateFailure(
                "Manual hosting does not support in-place updates. Please re-upload the file to your hosting service and update the URL in Publisher Studio."));
    }

    /// <inheritdoc/>
    public Task<OperationResult<string>> GetOrCreatePublisherFolderAsync(CancellationToken cancellationToken = default)
    {
        // Manual hosting doesn't use folders - publishers manage their own file organization
        return Task.FromResult(OperationResult<string>.CreateSuccess(string.Empty));
    }

    /// <inheritdoc/>
    public Task<OperationResult<HostingState?>> RecoverHostingStateAsync(CancellationToken cancellationToken = default)
    {
        // Cannot recover state from manual hosting as it has no API to query
        return Task.FromResult(OperationResult<HostingState?>.CreateSuccess(null));
    }

    /// <inheritdoc/>
    public string GetSubscriptionLink(string catalogUrl)
    {
        if (string.IsNullOrEmpty(catalogUrl))
        {
            return string.Empty;
        }

        return $"genhub://subscribe?url={Uri.EscapeDataString(catalogUrl)}";
    }

    /// <inheritdoc/>
    public bool IsValidHostingUrl(string url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return false;
        }

        // Accept any valid HTTPS URL
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
    }

    /// <inheritdoc/>
    public string GetDirectDownloadUrl(string shareUrl)
    {
        // For manual hosting, we assume the URL is already a direct download URL
        return shareUrl;
    }
}
