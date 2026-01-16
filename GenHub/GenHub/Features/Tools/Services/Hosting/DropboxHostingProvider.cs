using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Models.Publishers;
using GenHub.Core.Models.Results;
using GenHub.Features.Tools.Interfaces;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.Tools.Services.Hosting;

/// <summary>
/// Hosting provider for Dropbox.
/// Enables publishers to host catalogs and artifacts on Dropbox.
/// </summary>
/// <remarks>
/// Dropbox is a good option for publishers who:
/// - Already use Dropbox for file storage
/// - Want simple, reliable hosting with direct download links
/// - Need more storage than GitHub gists allow
/// </remarks>
public class DropboxHostingProvider : IHostingProvider, IDisposable
{
    private const string DropboxApiUrl = "https://api.dropboxapi.com/2";
    private const string DropboxContentUrl = "https://content.dropboxapi.com/2";
    private const string PublisherFolderPath = "/GenHub_Publisher";

    private readonly ILogger<DropboxHostingProvider> _logger;
    private readonly HttpClient _httpClient;
    private string? _accessToken;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="DropboxHostingProvider"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public DropboxHostingProvider(ILogger<DropboxHostingProvider> logger)
    {
        _logger = logger;
        _httpClient = new HttpClient();
    }

    /// <inheritdoc/>
    public string ProviderId => "dropbox";

    /// <inheritdoc/>
    public string DisplayName => "Dropbox";

    /// <inheritdoc/>
    public string Description => "Host your catalogs and artifacts on Dropbox. Simple and reliable with direct download links.";

    /// <inheritdoc/>
    public string IconName => "Dropbox";

    /// <inheritdoc/>
    public bool RequiresAuthentication => true;

    /// <inheritdoc/>
    public bool IsAuthenticated => !string.IsNullOrEmpty(_accessToken);

    /// <inheritdoc/>
    public bool SupportsCatalogHosting => true;

    /// <inheritdoc/>
    public bool SupportsArtifactHosting => true;

    /// <inheritdoc/>
    public bool SupportsUpdate => true;

    /// <inheritdoc/>
    public Task<OperationResult<bool>> AuthenticateAsync(CancellationToken cancellationToken = default)
    {
        // Dropbox uses OAuth2 - for now, we'll use access token authentication
        _logger.LogInformation("Dropbox authentication requires an access token.");
        return Task.FromResult(OperationResult<bool>.CreateFailure(
            "Please use the access token authentication. Get a token from the Dropbox App Console."));
    }

    /// <summary>
    /// Authenticates using a Dropbox access token.
    /// </summary>
    /// <param name="accessToken">The Dropbox access token.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Operation result indicating success.</returns>
    public async Task<OperationResult<bool>> AuthenticateWithTokenAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return OperationResult<bool>.CreateFailure("Access token is required");
        }

        try
        {
            // Verify the token by getting current account info
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{DropboxApiUrl}/users/get_current_account");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Content = new StringContent("null", Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                _accessToken = accessToken;
                _logger.LogInformation("Successfully authenticated with Dropbox");
                return OperationResult<bool>.CreateSuccess(true);
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Dropbox authentication failed: {Error}", errorContent);
                return OperationResult<bool>.CreateFailure("Invalid access token");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dropbox authentication failed");
            return OperationResult<bool>.CreateFailure($"Authentication failed: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public Task SignOutAsync()
    {
        _accessToken = null;
        _logger.LogInformation("Signed out from Dropbox");
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task<OperationResult<string>> GetOrCreatePublisherFolderAsync(CancellationToken cancellationToken = default)
    {
        if (!IsAuthenticated)
        {
            return OperationResult<string>.CreateFailure("Not authenticated with Dropbox");
        }

        try
        {
            // Try to create the folder (will succeed if it doesn't exist)
            var createRequest = new
            {
                path = PublisherFolderPath,
                autorename = false
            };

            var response = await _httpClient.PostAsync(
                $"{DropboxApiUrl}/files/create_folder_v2",
                new StringContent(JsonSerializer.Serialize(createRequest), Encoding.UTF8, "application/json"),
                cancellationToken);

            if (response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                // Folder exists or was created
                _logger.LogInformation("Dropbox publisher folder ready: {Path}", PublisherFolderPath);
                return OperationResult<string>.CreateSuccess(PublisherFolderPath);
            }

            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            return OperationResult<string>.CreateFailure($"Failed to create folder: {error}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get or create Dropbox publisher folder");
            return OperationResult<string>.CreateFailure($"Folder operation failed: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<HostingUploadResult>> UploadFileAsync(
        Stream fileStream,
        string fileName,
        string? folderPath = null,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsAuthenticated)
        {
            return OperationResult<HostingUploadResult>.CreateFailure("Not authenticated with Dropbox");
        }

        try
        {
            // Ensure folder exists
            var folderResult = await GetOrCreatePublisherFolderAsync(cancellationToken);
            if (!folderResult.Success)
            {
                return OperationResult<HostingUploadResult>.CreateFailure(folderResult);
            }

            progress?.Report(10);

            var filePath = $"{PublisherFolderPath}/{fileName}";

            // Read file content
            using var memoryStream = new MemoryStream();
            await fileStream.CopyToAsync(memoryStream, cancellationToken);
            var fileBytes = memoryStream.ToArray();

            progress?.Report(30);

            // Upload file
            var uploadArgs = new
            {
                path = filePath,
                mode = "overwrite",
                autorename = false,
                mute = true
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{DropboxContentUrl}/files/upload");
            request.Headers.Add("Dropbox-API-Arg", JsonSerializer.Serialize(uploadArgs));
            request.Content = new ByteArrayContent(fileBytes);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

            var response = await _httpClient.SendAsync(request, cancellationToken);

            progress?.Report(70);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                return OperationResult<HostingUploadResult>.CreateFailure($"Upload failed: {error}");
            }

            // Create shared link
            var shareResult = await CreateSharedLinkAsync(filePath, cancellationToken);
            if (!shareResult.Success)
            {
                return OperationResult<HostingUploadResult>.CreateFailure(shareResult);
            }

            progress?.Report(100);

            var result = new HostingUploadResult
            {
                PublicUrl = shareResult.Data!,
                DirectDownloadUrl = ConvertToDirectDownloadUrl(shareResult.Data!),
                FileId = filePath,
                FileSize = fileBytes.Length
            };

            _logger.LogInformation("Uploaded file to Dropbox: {Path}", filePath);
            return OperationResult<HostingUploadResult>.CreateSuccess(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload file to Dropbox");
            return OperationResult<HostingUploadResult>.CreateFailure($"Upload failed: {ex.Message}");
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
        // For Dropbox, updating is the same as uploading with overwrite mode
        return await UploadFileAsync(fileStream, fileName, null, progress, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<OperationResult<HostingUploadResult>> UploadCatalogAsync(
        string catalogJson,
        string publisherId,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(catalogJson));
        var fileName = $"catalog-{publisherId}.json";
        return await UploadFileAsync(stream, fileName, null, progress, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<OperationResult<HostingState?>> RecoverHostingStateAsync(CancellationToken cancellationToken = default)
    {
        // TODO: Implement recovery by listing files in the publisher folder
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

        return url.Contains("dropbox.com", StringComparison.OrdinalIgnoreCase) ||
               url.Contains("dl.dropboxusercontent.com", StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    public string GetDirectDownloadUrl(string shareUrl)
    {
        return ConvertToDirectDownloadUrl(shareUrl);
    }

    private async Task<OperationResult<string>> CreateSharedLinkAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            // First try to get existing shared link
            var listRequest = new { path };
            var listResponse = await _httpClient.PostAsync(
                $"{DropboxApiUrl}/sharing/list_shared_links",
                new StringContent(JsonSerializer.Serialize(listRequest), Encoding.UTF8, "application/json"),
                cancellationToken);

            if (listResponse.IsSuccessStatusCode)
            {
                var listContent = await listResponse.Content.ReadAsStringAsync(cancellationToken);
                var listResult = JsonSerializer.Deserialize<JsonElement>(listContent);
                if (listResult.TryGetProperty("links", out var links) && links.GetArrayLength() > 0)
                {
                    var url = links[0].GetProperty("url").GetString();
                    return OperationResult<string>.CreateSuccess(url!);
                }
            }

            // Create new shared link
            var createRequest = new
            {
                path,
                settings = new
                {
                    requested_visibility = "public",
                    audience = "public",
                    access = "viewer"
                }
            };

            var response = await _httpClient.PostAsync(
                $"{DropboxApiUrl}/sharing/create_shared_link_with_settings",
                new StringContent(JsonSerializer.Serialize(createRequest), Encoding.UTF8, "application/json"),
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                var result = JsonSerializer.Deserialize<JsonElement>(content);
                var url = result.GetProperty("url").GetString();
                return OperationResult<string>.CreateSuccess(url!);
            }

            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            return OperationResult<string>.CreateFailure($"Failed to create shared link: {error}");
        }
        catch (Exception ex)
        {
            return OperationResult<string>.CreateFailure($"Shared link error: {ex.Message}");
        }
    }

    private static string ConvertToDirectDownloadUrl(string shareUrl)
    {
        // Convert Dropbox share URL to direct download URL
        // From: https://www.dropbox.com/s/xxxxx/filename?dl=0
        // To: https://dl.dropboxusercontent.com/s/xxxxx/filename
        if (shareUrl.Contains("dropbox.com"))
        {
            return shareUrl
                .Replace("www.dropbox.com", "dl.dropboxusercontent.com")
                .Replace("?dl=0", "")
                .Replace("?dl=1", "");
        }

        return shareUrl;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes the managed resources.
    /// </summary>
    /// <param name="disposing">True if disposing from Dispose method.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            _httpClient.Dispose();
        }

        _disposed = true;
    }
}
