using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Models.Publishers;
using GenHub.Core.Models.Results;
using GenHub.Features.Tools.Interfaces;
using Microsoft.Extensions.Logging;
using Octokit;

namespace GenHub.Features.Tools.Services.Hosting;

/// <summary>
/// Hosting provider for GitHub Releases.
/// Enables publishers to upload artifacts to GitHub Releases and catalogs to GitHub Pages or Gists.
/// </summary>
/// <remarks>
/// GitHub is the recommended hosting option for GenHub publishers because:
/// - Free for public repositories
/// - Supports large files (up to 2GB per release asset)
/// - Built-in versioning via releases
/// - High availability and CDN distribution
/// - Easy setup with GitHub Pages for catalog hosting.
/// </remarks>
public class GitHubHostingProvider : IHostingProvider
{
    private readonly ILogger<GitHubHostingProvider> _logger;
    private GitHubClient? _client;
    private string? _authenticatedUsername;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitHubHostingProvider"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public GitHubHostingProvider(ILogger<GitHubHostingProvider> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public string ProviderId => "github";

    /// <inheritdoc/>
    public string DisplayName => "GitHub Gists";

    /// <inheritdoc/>
    public string Description => "Host catalogs and definitions via GitHub Gists. Artifacts should be hosted with direct URLs or cloud storage.";

    /// <inheritdoc/>
    public string IconName => "Github";

    /// <inheritdoc/>
    public bool RequiresAuthentication => true;

    /// <inheritdoc/>
    public bool IsAuthenticated => _client != null && !string.IsNullOrEmpty(_authenticatedUsername);

    /// <inheritdoc/>
    public bool SupportsCatalogHosting => true;

    /// <inheritdoc/>
    public bool SupportsArtifactHosting => false;

    /// <inheritdoc/>
    public bool SupportsUpdate => true;

    /// <inheritdoc/>
    public Task<OperationResult<bool>> AuthenticateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // For now, use device flow or personal access token
            // In production, this would use OAuth device flow
            _logger.LogInformation("Starting GitHub authentication...");

            // Create client with product header
            _client = new GitHubClient(new ProductHeaderValue("GenHub"));

            // TODO: Implement proper OAuth device flow
            // For now, we'll use a placeholder that requires manual PAT entry
            // The UI should prompt for a Personal Access Token
            return Task.FromResult(OperationResult<bool>.CreateFailure("GitHub authentication not yet implemented. Please use a Personal Access Token."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GitHub authentication failed");
            return Task.FromResult(OperationResult<bool>.CreateFailure($"Authentication failed: {ex.Message}"));
        }
    }

    /// <summary>
    /// Authenticates using a Personal Access Token.
    /// </summary>
    /// <param name="personalAccessToken">The GitHub PAT.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Operation result indicating success.</returns>
    public async Task<OperationResult<bool>> AuthenticateWithTokenAsync(string personalAccessToken, CancellationToken cancellationToken = default)
    {
        try
        {
            _client = new GitHubClient(new ProductHeaderValue("GenHub"))
            {
                Credentials = new Credentials(personalAccessToken),
            };

            // Verify the token by getting the current user
            var user = await _client.User.Current();
            _authenticatedUsername = user.Login;

            _logger.LogInformation("Authenticated with GitHub as {Username}", _authenticatedUsername);
            return OperationResult<bool>.CreateSuccess(true);
        }
        catch (AuthorizationException)
        {
            _logger.LogWarning("Invalid GitHub token provided");
            return OperationResult<bool>.CreateFailure("Invalid GitHub Personal Access Token");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GitHub token authentication failed");
            return OperationResult<bool>.CreateFailure($"Authentication failed: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public Task SignOutAsync()
    {
        _client = null;
        _authenticatedUsername = null;
        _logger.LogInformation("Signed out from GitHub");
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task<OperationResult<HostingUploadResult>> UploadFileAsync(
        Stream fileStream,
        string fileName,
        string? folderPath = null,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (_client == null || string.IsNullOrEmpty(_authenticatedUsername))
        {
            return OperationResult<HostingUploadResult>.CreateFailure("Not authenticated with GitHub");
        }

        try
        {
            // When folderPath is omitted, upload as a Gist (e.g. definitions or catalogs)
            if (string.IsNullOrEmpty(folderPath))
            {
                progress?.Report(20);
                using var streamReader = new StreamReader(fileStream);
                var jsonContent = await streamReader.ReadToEndAsync(cancellationToken);

                var newGist = new NewGist
                {
                    Description = $"GenHub Content - {fileName}",
                    Public = true,
                };
                newGist.Files.Add(fileName, jsonContent);

                var gist = await _client.Gist.Create(newGist);
                progress?.Report(100);

                var file = gist.Files.TryGetValue(fileName, out var gistFile) ? gistFile : gist.Files.Values.FirstOrDefault();
                var downloadUrl = file?.RawUrl ?? gist.HtmlUrl;

                return OperationResult<HostingUploadResult>.CreateSuccess(new HostingUploadResult
                {
                    FileId = gist.Id,
                    DirectDownloadUrl = downloadUrl,
                    PublicUrl = gist.HtmlUrl,
                    FileSize = Encoding.UTF8.GetByteCount(jsonContent),
                });
            }

            var parts = folderPath.Split('/');
            if (parts.Length < 3)
            {
                return OperationResult<HostingUploadResult>.CreateFailure("Invalid folder path. Expected: owner/repo/release-tag");
            }

            var owner = parts[0];
            var repo = parts[1];
            var releaseTag = parts[2];

            // Get or create the release
            Release release;
            try
            {
                release = await _client.Repository.Release.Get(owner, repo, releaseTag);
            }
            catch (NotFoundException)
            {
                // Create a new release
                var newRelease = new NewRelease(releaseTag)
                {
                    Name = releaseTag,
                    Body = $"Release created by GenHub Publisher Studio",
                    Draft = false,
                    Prerelease = false,
                };
                release = await _client.Repository.Release.Create(owner, repo, newRelease);
            }

            progress?.Report(20);

            // Read the stream into memory for upload
            using var memoryStream = new MemoryStream();
            await fileStream.CopyToAsync(memoryStream, cancellationToken);
            var fileData = memoryStream.ToArray();

            progress?.Report(50);

            // Upload as release asset
            var assetUpload = new ReleaseAssetUpload(
                fileName,
                "application/octet-stream",
                new MemoryStream(fileData),
                TimeSpan.FromMinutes(10));

            var asset = await _client.Repository.Release.UploadAsset(release, assetUpload, cancellationToken);

            progress?.Report(100);

            var result = new HostingUploadResult
            {
                PublicUrl = asset.BrowserDownloadUrl,
                DirectDownloadUrl = asset.BrowserDownloadUrl,
                FileId = asset.Id.ToString(),
                FileSize = asset.Size,
            };

            _logger.LogInformation("Uploaded {FileName} to GitHub release {ReleaseTag}", fileName, releaseTag);
            return OperationResult<HostingUploadResult>.CreateSuccess(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload file to GitHub");
            return OperationResult<HostingUploadResult>.CreateFailure($"Upload failed: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<HostingUploadResult>> UploadCatalogAsync(
        string catalogJson,
        string publisherId,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (_client == null || string.IsNullOrEmpty(_authenticatedUsername))
        {
            return OperationResult<HostingUploadResult>.CreateFailure("Not authenticated with GitHub");
        }

        try
        {
            // For catalogs, create/update a Gist
            // Gists provide a simple way to host and update JSON files
            var gistName = $"genhub-catalog-{publisherId}.json";
            _logger.LogInformation("Uploading catalog to GitHub Gist: {GistName}", gistName);

            progress?.Report(30);

            var newGist = new NewGist
            {
                Description = $"GenHub Publisher Catalog for {publisherId}",
                Public = true,
            };
            newGist.Files.Add(gistName, catalogJson);

            var gist = await _client.Gist.Create(newGist);

            progress?.Report(100);

            // Get the raw URL for the catalog file
            var file = gist.Files[gistName];
            var rawUrl = file.RawUrl;

            var result = new HostingUploadResult
            {
                PublicUrl = gist.HtmlUrl,
                DirectDownloadUrl = rawUrl,
                FileId = gist.Id,
                FileSize = Encoding.UTF8.GetByteCount(catalogJson),
            };

            _logger.LogInformation("Created GitHub Gist {GistId} for catalog", gist.Id);
            return OperationResult<HostingUploadResult>.CreateSuccess(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload catalog to GitHub Gist");
            return OperationResult<HostingUploadResult>.CreateFailure($"Catalog upload failed: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<HostingUploadResult>> UpdateFileAsync(
        string fileId,
        Stream fileStream,
        string fileName,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (_client == null || string.IsNullOrEmpty(_authenticatedUsername))
        {
            return OperationResult<HostingUploadResult>.CreateFailure("Not authenticated with GitHub");
        }

        try
        {
            // fileId is the Gist ID
            _logger.LogInformation("Updating GitHub Gist: {GistId}", fileId);

            progress?.Report(30);

            using var reader = new StreamReader(fileStream);
            var content = await reader.ReadToEndAsync(cancellationToken);

            var existingGist = await _client.Gist.Get(fileId);
            var targetKey = existingGist.Files.ContainsKey(fileName)
                ? fileName
                : (existingGist.Files.Keys.FirstOrDefault() ?? fileName);

            var gistUpdate = new GistUpdate
            {
                Description = $"Updated via GenHub Publisher Studio at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC",
            };

            // Update or add the file with target key
            gistUpdate.Files[targetKey] = new GistFileUpdate
            {
                Content = content,
                NewFileName = fileName,
            };

            var updatedGist = await _client.Gist.Edit(fileId, gistUpdate);

            progress?.Report(100);

            // Get the raw URL for the updated file
            var updatedFile = updatedGist.Files.TryGetValue(fileName, out var fileObj)
                ? fileObj
                : (updatedGist.Files.TryGetValue(targetKey, out var altObj) ? altObj : updatedGist.Files.Values.FirstOrDefault());
            var rawUrl = updatedFile?.RawUrl ?? updatedGist.HtmlUrl;

            var result = new HostingUploadResult
            {
                PublicUrl = updatedGist.HtmlUrl,
                DirectDownloadUrl = rawUrl,
                FileId = updatedGist.Id,
                FileSize = Encoding.UTF8.GetByteCount(content),
            };

            _logger.LogInformation("Updated file {FileName} in GitHub Gist {GistId}", fileName, fileId);
            return OperationResult<HostingUploadResult>.CreateSuccess(result);
        }
        catch (NotFoundException ex)
        {
            _logger.LogWarning(ex, "Gist not found for update: {FileId}", fileId);
            return OperationResult<HostingUploadResult>.CreateFailure($"Gist not found: {fileId}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update file in GitHub Gist");
            return OperationResult<HostingUploadResult>.CreateFailure($"Update failed: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public Task<OperationResult<string>> GetOrCreatePublisherFolderAsync(CancellationToken cancellationToken = default)
    {
        // GitHub doesn't use traditional folders - repositories are created by users
        // Return success with empty string to indicate folder is not applicable
        return Task.FromResult(OperationResult<string>.CreateSuccess(string.Empty));
    }

    /// <inheritdoc/>
    public Task<OperationResult<HostingState?>> RecoverHostingStateAsync(CancellationToken cancellationToken = default)
    {
        // Hosting state recovery is not implemented for GitHub yet
        // This would require scanning the user's repositories/gists for GenHub catalogs
        return Task.FromResult(OperationResult<HostingState?>.CreateSuccess(null));
    }

    /// <inheritdoc/>
    public string GetSubscriptionLink(string catalogUrl)
    {
        return $"genhub://subscribe?url={Uri.EscapeDataString(catalogUrl)}";
    }

    /// <inheritdoc/>
    public bool IsValidHostingUrl(string url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return false;
        }

        return url.Contains("github.com", StringComparison.OrdinalIgnoreCase) ||
               url.Contains("raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase) ||
               url.Contains("gist.github.com", StringComparison.OrdinalIgnoreCase) ||
               url.Contains("gist.githubusercontent.com", StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    public string GetDirectDownloadUrl(string shareUrl)
    {
        // GitHub release assets already have direct download URLs
        // Gist raw URLs are also direct
        return shareUrl;
    }
}
